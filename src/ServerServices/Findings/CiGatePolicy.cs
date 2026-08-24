using System.Globalization;
using System.Text.Json;
using Contracts.Importers;
using DAL.Entities;
using Model.Exceptions;

namespace ServerServices.Findings;

/// <summary>
/// The exit-code gating policy for CI (Track 3 milestone 3.5.4).
///
/// A policy is a small expression evaluated against one import's counts —
/// <c>new-critical</c>, <c>any-high&gt;5</c>, <c>sla-breach</c>. It lives in ServerServices rather
/// than in the console command so the same evaluation can back a future server-side gate endpoint,
/// and so it is unit-testable without a process exit.
///
/// "New vs pre-existing" is what makes gating non-flaky, and that distinction comes from the dedup
/// engine: <c>new-*</c> reads the import's new-findings counts, so a build does not fail for a
/// vulnerability that was already known and accepted.
/// </summary>
public static class CiGatePolicy
{
    /// <summary>
    /// Parses and evaluates one policy expression against an import.
    ///
    /// Grammar, deliberately tiny:
    /// <list type="bullet">
    /// <item><c>new-critical</c> — fail if the import created any Critical finding.</item>
    /// <item><c>new-&lt;severity&gt;</c> — same for high/medium/low.</item>
    /// <item><c>new-&lt;severity&gt;&gt;N</c> — fail if it created more than N.</item>
    /// <item><c>any-&lt;severity&gt;&gt;N</c> — as above; <c>any-</c> is accepted as a synonym so the
    /// spec's <c>any-high&gt;5</c> reads naturally.</item>
    /// <item><c>sla-breach</c> — fail if the import created any finding already past its SLA.</item>
    /// <item><c>none</c> — never fail. Useful for a pipeline that only reports.</item>
    /// </list>
    /// </summary>
    public static GateResult Evaluate(string policy, ScanImport import, int slaBreachedCount = 0)
    {
        if (string.IsNullOrWhiteSpace(policy))
            throw new InvalidParameterException(nameof(policy), "A gate policy expression is required.");

        var expression = policy.Trim().ToLowerInvariant();

        // Checked before the "none" opt-out, and deliberately: an import that failed is a gate
        // failure regardless of the expression, because nobody can claim a build is clean when the
        // scan results never landed. Turning gating off says "do not stop me for findings", not
        // "do not stop me when the scan did not run".
        if (import.Status == (int)DAL.Enums.ScanImportStatus.Failed)
            return new GateResult(true, expression,
                $"The import failed: {import.ErrorMessage ?? "no reason recorded"}.", 0, 0);

        if (expression is "none" or "off")
            return new GateResult(false, expression, "Gating disabled by policy.", 0, 0);

        if (expression == "sla-breach")
            return new GateResult(slaBreachedCount > 0, expression,
                slaBreachedCount > 0
                    ? $"{slaBreachedCount} finding(s) from this import are already past their SLA deadline."
                    : "No finding from this import is past its SLA deadline.",
                slaBreachedCount, 0);

        var (severity, threshold) = ParseSeverityExpression(expression);
        var counts = ParseNewBySeverity(import.NewBySeverity);

        // "critical" means "critical and worse", which for the top band is itself. Counting at or
        // above the named severity is what a pipeline author means by "fail on new highs" — they do
        // not mean "unless it is critical".
        var actual = counts
            .Where(c => c.Key >= severity)
            .Sum(c => c.Value);

        var failed = actual > threshold;

        var message = failed
            ? $"This import created {actual} new {severity}-or-worse finding(s); the policy allows {threshold}."
            : $"This import created {actual} new {severity}-or-worse finding(s), within the allowed {threshold}.";

        return new GateResult(failed, expression, message, actual, threshold);
    }

    /// <summary>
    /// Splits <c>new-high&gt;5</c> into its severity and threshold. A bare <c>new-high</c> means a
    /// threshold of zero — "fail on any" — which is what a pipeline author almost always wants and
    /// so is the shorter form.
    /// </summary>
    private static (NormalizedSeverity Severity, int Threshold) ParseSeverityExpression(string expression)
    {
        var body = expression;
        var threshold = 0;

        var comparison = body.IndexOf('>');
        if (comparison > 0)
        {
            var rawThreshold = body.Substring(comparison + 1).Trim();
            if (!int.TryParse(rawThreshold, NumberStyles.Integer, CultureInfo.InvariantCulture, out threshold))
                throw new InvalidParameterException("policy",
                    $"'{rawThreshold}' is not a number. Expected a form like 'any-high>5'.");

            body = body.Substring(0, comparison).Trim();
        }

        foreach (var prefix in new[] { "new-", "any-" })
            if (body.StartsWith(prefix, StringComparison.Ordinal))
            {
                body = body.Substring(prefix.Length);
                break;
            }

        if (!Enum.TryParse<NormalizedSeverity>(body, ignoreCase: true, out var severity) ||
            severity == NormalizedSeverity.None)
            throw new InvalidParameterException("policy",
                $"'{body}' is not a severity. Expected one of: critical, high, medium, low — " +
                "for example 'new-critical' or 'any-high>5'. 'sla-breach' and 'none' are also accepted.");

        return (severity, threshold);
    }

    /// <summary>
    /// Reads the denormalized per-severity counts the ingestion pipeline stored. A malformed or
    /// absent value is treated as "no new findings" rather than an error: the gate's job is to
    /// decide, and refusing to decide because a JSON blob is odd fails builds for the wrong reason.
    /// </summary>
    internal static Dictionary<NormalizedSeverity, int> ParseNewBySeverity(string? json)
    {
        var counts = new Dictionary<NormalizedSeverity, int>();
        if (string.IsNullOrWhiteSpace(json)) return counts;

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return counts;

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!Enum.TryParse<NormalizedSeverity>(property.Name, ignoreCase: true, out var severity)) continue;
                if (property.Value.ValueKind != JsonValueKind.Number) continue;

                counts[severity] = property.Value.GetInt32();
            }
        }
        catch (JsonException)
        {
            return counts;
        }

        return counts;
    }
}

/// <summary>
/// A gate decision.
/// </summary>
/// <param name="Failed">True when the build should stop.</param>
/// <param name="Policy">The expression as evaluated, normalized.</param>
/// <param name="Message">A human-readable explanation, printed by the CLI.</param>
/// <param name="Actual">The count the policy measured.</param>
/// <param name="Threshold">The count it was allowed to be.</param>
public record GateResult(bool Failed, string Policy, string Message, int Actual, int Threshold);
