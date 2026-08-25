using System.Text.Json;
using DAL.Entities;
using Model.Exceptions;
using Model.Integrations;
using Serilog;
using ServerServices.Interfaces;

namespace ServerServices.Integrations.SecurityScorecard;

/// <summary>
/// The SecurityScorecard v1 REST surface NetRisk uses (Track 4 milestone 4.5).
///
/// Two API details shape this client. Authentication is <c>Authorization: Token &lt;key&gt;</c> — not
/// <c>Bearer</c>, which is the mistake that produces a puzzling 401 against a valid key. And the
/// issues endpoints page with <c>limit</c>/<c>offset</c> under an <c>entries</c> array, unlike the
/// company and factor endpoints, which return a single object.
/// </summary>
public class SecurityScorecardClient(ILogger logger, IOutboundHttpClient http) : ISecurityScorecardClient
{
    private const int PageSize = 500;

    /// <summary>
    /// Cap on pages per endpoint. A domain with more than 25,000 active issues is beyond anything worth
    /// ingesting as individual findings, and the cap is logged rather than silently applied.
    /// </summary>
    private const int MaxPages = 50;

    public async Task<ConnectionTestResult> TestAsync(SecurityScorecardConnection connection, string? token,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            return ConnectionTestResult.Fail("No API token is configured for this connection.");

        var response = await GetAsync(connection, token,
            $"/companies/{Uri.EscapeDataString(connection.Domain)}", ct);

        if (response.IsSuccess)
        {
            var company = ParseCompany(connection.Domain, response.Body);

            return ConnectionTestResult.Ok(
                $"Read the scorecard for '{connection.Domain}'.",
                new Dictionary<string, string>
                {
                    ["Company"] = company?.Name ?? connection.Domain,
                    ["Score"] = company?.Score?.ToString() ?? "(none)",
                    ["Grade"] = company?.Grade ?? "(none)"
                });
        }

        return response.StatusCode switch
        {
            0 => ConnectionTestResult.Fail($"SecurityScorecard could not be reached: {response.TransportError}"),
            401 => ConnectionTestResult.Fail(
                "SecurityScorecard rejected the token (401). The header must be 'Authorization: Token "
                + "<key>' — NetRisk sends that, so check the key itself."),
            403 => ConnectionTestResult.Fail(
                "SecurityScorecard refused the request (403). The account may not be entitled to this "
                + "domain's scorecard."),
            404 => ConnectionTestResult.Fail(
                $"SecurityScorecard has no scorecard for '{connection.Domain}', or this account cannot "
                + "see it. Use the registered domain, not a subdomain or a URL."),
            429 => ConnectionTestResult.Fail("SecurityScorecard is rate-limiting this token (429)."),
            _ => ConnectionTestResult.Fail($"SecurityScorecard answered HTTP {response.StatusCode}.")
        };
    }

    public async Task<SecurityScorecardCompany?> GetCompanyAsync(SecurityScorecardConnection connection,
        string? token, CancellationToken ct = default)
    {
        var response = await GetAsync(connection, token,
            $"/companies/{Uri.EscapeDataString(connection.Domain)}", ct);

        if (!response.IsSuccess)
            throw new IntegrationRequestException("SecurityScorecard",
                response.StatusCode == 0
                    ? $"SecurityScorecard could not be reached: {response.TransportError}"
                    : $"SecurityScorecard answered HTTP {response.StatusCode} for the company endpoint.");

        return ParseCompany(connection.Domain, response.Body);
    }

    public async Task<List<SecurityScorecardFactorScore>> GetFactorsAsync(
        SecurityScorecardConnection connection, string? token, CancellationToken ct = default)
    {
        var factors = new List<SecurityScorecardFactorScore>();

        var response = await GetAsync(connection, token,
            $"/companies/{Uri.EscapeDataString(connection.Domain)}/factors", ct);

        if (!response.IsSuccess)
            throw new IntegrationRequestException("SecurityScorecard",
                $"SecurityScorecard answered HTTP {response.StatusCode} for the factors endpoint.");

        try
        {
            using var document = JsonDocument.Parse(response.Body!);

            if (!document.RootElement.TryGetProperty("entries", out var entries)
                || entries.ValueKind != JsonValueKind.Array)
                return factors;

            foreach (var entry in entries.EnumerateArray())
            {
                var name = String(entry, "name") ?? String(entry, "factor");
                if (string.IsNullOrWhiteSpace(name)) continue;

                factors.Add(new SecurityScorecardFactorScore
                {
                    Name = name.Trim().ToLowerInvariant(),
                    Score = (int)Math.Clamp(Number(entry, "score") ?? 0, 0, 100),
                    Grade = String(entry, "grade"),
                    IssueCount = (int?)Number(entry, "issue_count", "issueCount")
                });
            }
        }
        catch (JsonException ex)
        {
            throw new IntegrationRequestException("SecurityScorecard",
                $"The factors response was not valid JSON: {ex.Message}");
        }

        return factors;
    }

    public Task<List<SecurityScorecardIssue>> GetVulnerabilitiesAsync(SecurityScorecardConnection connection,
        string? token, CancellationToken ct = default) =>
        // Patching Cadence's CVE feed. A separate endpoint from the general issues list, and the only
        // one that carries CVE ids.
        CollectAsync(connection, token,
            $"/companies/{Uri.EscapeDataString(connection.Domain)}/issues/potentially_vulnerable",
            isVulnerability: true, ct);

    public Task<List<SecurityScorecardIssue>> GetIssuesAsync(SecurityScorecardConnection connection,
        string? token, CancellationToken ct = default) =>
        CollectAsync(connection, token,
            $"/companies/{Uri.EscapeDataString(connection.Domain)}/issues",
            isVulnerability: false, ct);

    private async Task<List<SecurityScorecardIssue>> CollectAsync(SecurityScorecardConnection connection,
        string? token, string path, bool isVulnerability, CancellationToken ct)
    {
        var issues = new List<SecurityScorecardIssue>();

        for (var page = 0; page < MaxPages; page++)
        {
            var separator = path.Contains('?') ? "&" : "?";

            var response = await GetAsync(connection, token,
                $"{path}{separator}limit={PageSize}&offset={page * PageSize}", ct);

            // 404 on the issues endpoint means the domain has none of that kind, which is a good
            // outcome and not an error.
            if (response.StatusCode == 404) break;

            if (!response.IsSuccess)
                throw new IntegrationRequestException("SecurityScorecard",
                    response.StatusCode == 0
                        ? $"SecurityScorecard could not be reached: {response.TransportError}"
                        : $"SecurityScorecard answered HTTP {response.StatusCode} for {path}.");

            int parsed;

            try
            {
                parsed = ParseIssues(response.Body!, isVulnerability, issues);
            }
            catch (JsonException ex)
            {
                throw new IntegrationRequestException("SecurityScorecard",
                    $"The issues response was not valid JSON: {ex.Message}");
            }

            // A short page is the last page. Trusting a total count instead would keep paging past the
            // end whenever the count and the page contents disagree, which they do while a scan is
            // running.
            if (parsed < PageSize) break;

            if (page == MaxPages - 1)
                logger.Warning(
                    "Stopped reading SecurityScorecard {Path} after {Pages} pages ({Count} rows); the "
                    + "result set was truncated", path, MaxPages, issues.Count);
        }

        return issues;
    }

    internal static int ParseIssues(string body, bool isVulnerability, List<SecurityScorecardIssue> into)
    {
        using var document = JsonDocument.Parse(body);

        if (!document.RootElement.TryGetProperty("entries", out var entries)
            || entries.ValueKind != JsonValueKind.Array)
            return 0;

        var count = 0;

        foreach (var entry in entries.EnumerateArray())
        {
            count++;

            var type = String(entry, "type", "issue_type", "issueType");
            var cve = String(entry, "cve", "cve_id", "vulnerability_id");

            if (string.IsNullOrWhiteSpace(type) && string.IsNullOrWhiteSpace(cve)) continue;

            var issue = new SecurityScorecardIssue
            {
                Type = type ?? "vulnerability",
                Severity = String(entry, "severity", "issue_type_severity"),
                FactorName = String(entry, "factor", "group", "factor_name")?.ToLowerInvariant(),
                Target = String(entry, "hostname", "ip_address", "target", "url", "domain", "subdomain"),
                Description = String(entry, "description", "issue_type_title", "detail", "reason"),
                CveId = cve,
                CvssScore = Number(entry, "cvss", "cvss_score", "cvss_base_score"),
                Port = String(entry, "port", "observed_port"),
                IsVulnerability = isVulnerability || !string.IsNullOrWhiteSpace(cve)
            };

            var first = String(entry, "first_seen_time", "first_seen", "firstSeen");
            if (DateTime.TryParse(first, out var firstParsed)) issue.FirstSeen = firstParsed.ToUniversalTime();

            var last = String(entry, "last_seen_time", "last_seen", "lastSeen");
            if (DateTime.TryParse(last, out var lastParsed)) issue.LastSeen = lastParsed.ToUniversalTime();

            into.Add(issue);
        }

        return count;
    }

    internal static SecurityScorecardCompany? ParseCompany(string domain, string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            var company = new SecurityScorecardCompany
            {
                Domain = String(root, "domain") ?? domain,
                Name = String(root, "name"),
                Grade = String(root, "grade", "grade_letter"),
                Industry = String(root, "industry"),
                Size = (int?)Number(root, "size")
            };

            // "score" has appeared as both a number and a numeric string in this API.
            var score = Number(root, "score", "grade_score");
            if (score != null) company.Score = (int)Math.Clamp(Math.Round(score.Value), 0, 100);

            var lastSeen = String(root, "last_seen", "last_seen_time");
            if (DateTime.TryParse(lastSeen, out var parsed)) company.LastSeen = parsed.ToUniversalTime();

            return company;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private Task<OutboundHttpResponse> GetAsync(SecurityScorecardConnection connection, string? token,
        string path, CancellationToken ct) =>
        http.SendAsync(new OutboundHttpRequest
        {
            Method = "GET",
            Url = connection.BaseUrl.TrimEnd('/') + path,
            Headers =
            {
                // "Token", not "Bearer". SecurityScorecard rejects Bearer outright.
                ["Authorization"] = "Token " + token,
                ["Accept"] = "application/json"
            },
            Timeout = TimeSpan.FromSeconds(60)
        }, ct);

    private static string? String(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.ValueKind != JsonValueKind.Object) return null;
            if (!element.TryGetProperty(name, out var value)) continue;

            switch (value.ValueKind)
            {
                case JsonValueKind.String:
                    var text = value.GetString();
                    if (!string.IsNullOrWhiteSpace(text)) return text;
                    break;
                case JsonValueKind.Number:
                    return value.ToString();
                case JsonValueKind.Array:
                    var first = value.EnumerateArray().FirstOrDefault(e => e.ValueKind == JsonValueKind.String);
                    if (first.ValueKind == JsonValueKind.String) return first.GetString();
                    break;
            }
        }

        return null;
    }

    private static double? Number(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.ValueKind != JsonValueKind.Object) return null;
            if (!element.TryGetProperty(name, out var value)) continue;

            if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)) return number;

            if (value.ValueKind == JsonValueKind.String
                && double.TryParse(value.GetString(), System.Globalization.CultureInfo.InvariantCulture,
                    out var parsed))
                return parsed;
        }

        return null;
    }
}
