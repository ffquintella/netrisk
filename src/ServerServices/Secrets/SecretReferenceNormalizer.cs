using Contracts.Secrets;
using DAL.Context;
using Microsoft.EntityFrameworkCore;
using Model.Secrets;
using Serilog;
using ServerServices.Interfaces;
using ServerServices.Services;

namespace ServerServices.Secrets;

/// <summary>
/// Rewrites stored references that a plugin has since learned to express differently.
///
/// <para><b>Why this is a command and not a database migration.</b> The references this exists for
/// carry a plugin's own grammar inside the secret id — BastionVault's
/// <c>secret/trend/netrisk-dsv?env=hml</c> — and only that plugin knows what the grammar means. A
/// migration written in SQL, or in the host at all, would be the host learning one plugin's private
/// encoding, which is the exact mistake that plugin-contributed screens exist to end. So the host
/// does the walking and the plugin does the reading: every candidate goes through
/// <c>INetriskSecretVaultPlugin.NormalizeReference</c>, and what comes back is what gets stored.</para>
///
/// <para>Nothing here runs on its own. An operator invokes
/// <c>netrisk-console vault normalize-references</c>, sees the plan, and runs it again with
/// <c>--apply</c>. A rewrite that a plugin got wrong would repoint a credential field at a different
/// secret, so this is not something to do quietly during an upgrade.</para>
/// </summary>
public class SecretReferenceNormalizer(
    ILogger logger,
    IDalService dalService,
    IPluginsService pluginsService)
    : ServiceBase(logger, dalService), ISecretReferenceNormalizer
{
    public async Task<List<SecretReferenceRewrite>> NormalizeAsync(bool apply,
        CancellationToken ct = default)
    {
        await using var db = DalService.GetContext();

        var connections = await db.SecretVaultConnections.ToDictionaryAsync(c => c.Id, ct);

        // One instance per plugin name for the whole walk. Each lookup instantiates the plugin by
        // reflection, and a thousand references would otherwise be a thousand instantiations of the
        // same stateless object.
        var plugins = new Dictionary<string, INetriskSecretVaultPlugin?>(StringComparer.Ordinal);

        var rewrites = new List<SecretReferenceRewrite>();

        // The same registry as SecretVaultService.CountReferencesAsync, and it has to stay in step
        // with it: a column missing here is a column whose references never get normalized, which
        // shows up as one field still resolving through the plugin's compatibility path long after
        // everything else moved.
        await WalkAsync(db.TrendMicroConnections, c => c.EncryptedApiKey, (c, v) => c.EncryptedApiKey = v,
            c => $"trendmicro_connections[{c.Id}].encrypted_api_key");

        await WalkAsync(db.SecurityScorecardConnections, c => c.EncryptedApiToken,
            (c, v) => c.EncryptedApiToken = v,
            c => $"securityscorecard_connections[{c.Id}].encrypted_api_token");

        await WalkAsync(db.IssueTrackerConnections, c => c.EncryptedToken, (c, v) => c.EncryptedToken = v,
            c => $"issue_tracker_connections[{c.Id}].encrypted_token");

        await WalkAsync(db.IssueTrackerConnections, c => c.EncryptedWebhookSecret,
            (c, v) => c.EncryptedWebhookSecret = v,
            c => $"issue_tracker_connections[{c.Id}].encrypted_webhook_secret");

        await WalkAsync(db.IdentityProviders, p => p.EncryptedClientSecret,
            (p, v) => p.EncryptedClientSecret = v,
            p => $"identity_providers[{p.Id}].encrypted_client_secret");

        // The channel's secrets are inside a JSON document rather than in a column, so the rewrite
        // is a replacement of one exact reference string by another. Exact, and therefore safe: a
        // reference is base64url plus colons, so it cannot be a prefix of a different reference and
        // cannot occur inside ordinary configuration.
        await WalkJsonAsync(db.NotificationChannels, c => c.ConfigurationJson,
            (c, v) => c.ConfigurationJson = v, c => $"notification_channels[{c.Id}].configuration_json");

        if (apply && rewrites.Count > 0) await db.SaveChangesAsync(ct);

        return rewrites;

        async Task WalkAsync<T>(DbSet<T> set, Func<T, string?> read, Action<T, string> write,
            Func<T, string> describe) where T : class
        {
            foreach (var row in await set.ToListAsync(ct))
            {
                var stored = read(row);
                if (!SecretReference.IsReference(stored)) continue;

                if (Rewrite(stored!) is not { } rewritten) continue;

                rewrites.Add(new SecretReferenceRewrite
                {
                    Location = describe(row),
                    Before = stored!,
                    After = rewritten
                });

                if (apply) write(row, rewritten);
            }
        }

        async Task WalkJsonAsync<T>(DbSet<T> set, Func<T, string?> read, Action<T, string> write,
            Func<T, string> describe) where T : class
        {
            foreach (var row in await set.ToListAsync(ct))
            {
                var document = read(row);
                if (string.IsNullOrEmpty(document)) continue;

                var updated = document;

                foreach (var stored in ReferencesIn(document))
                {
                    if (Rewrite(stored) is not { } rewritten) continue;

                    rewrites.Add(new SecretReferenceRewrite
                    {
                        Location = describe(row),
                        Before = stored,
                        After = rewritten
                    });

                    updated = updated.Replace(stored, rewritten, StringComparison.Ordinal);
                }

                if (apply && !string.Equals(updated, document, StringComparison.Ordinal))
                    write(row, updated);
            }
        }

        string? Rewrite(string stored)
        {
            if (!SecretReference.TryParse(stored, out var reference)) return null;

            // A reference that already carries declared values came from a declared control. There
            // is no older form to read out of it, and asking the plugin would invite it to rewrite
            // something an operator chose.
            if (reference.Options.Count > 0) return null;

            if (!connections.TryGetValue(reference.ConnectionId, out var connection)) return null;

            var plugin = PluginFor(connection.PluginName);
            if (plugin is null) return null;

            VaultSecretReference normalized;

            try
            {
                normalized = plugin.NormalizeReference(new VaultSecretReference
                {
                    SecretId = reference.SecretId,
                    Field = reference.Field
                }) ?? throw new InvalidOperationException("the plugin returned no reference");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Secret vault plugin {Plugin} threw while normalizing a stored "
                                 + "reference; it is left as stored", connection.PluginName);
                return null;
            }

            var candidate = SecretReference.Create(reference.ConnectionId, normalized.SecretId,
                normalized.Field, normalized.Options).ToString();

            // Rebuilt through Create and compared as text, so a plugin that returned the same thing
            // in a different shape is not recorded as a change.
            return string.Equals(candidate, stored, StringComparison.Ordinal) ? null : candidate;
        }

        INetriskSecretVaultPlugin? PluginFor(string pluginName)
        {
            if (plugins.TryGetValue(pluginName, out var known)) return known;

            var plugin = pluginsService
                .GetPluginByNameAsync<INetriskSecretVaultPlugin>(pluginName)
                .GetAwaiter().GetResult();

            plugins[pluginName] = plugin;

            if (plugin is null)
                Logger.Warning("References resolve through the '{Plugin}' plugin, which is not "
                               + "installed; they are left as stored", pluginName);

            return plugin;
        }
    }

    /// <summary>
    /// Every vault reference embedded in a JSON configuration document.
    ///
    /// Scanned as text rather than parsed, because the document's shape is the channel type's and
    /// not this service's to know. A reference's alphabet — the marker, digits, colons and
    /// base64url — is what bounds each match, and anything that matched but does not parse is
    /// dropped by the rewrite step.
    /// </summary>
    private static IEnumerable<string> ReferencesIn(string document)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);

        foreach (var prefix in (string[])[SecretReference.Prefix, SecretReference.PrefixV2])
        {
            var at = 0;

            while ((at = document.IndexOf(prefix, at, StringComparison.Ordinal)) >= 0)
            {
                var end = at;

                while (end < document.Length && IsReferenceCharacter(document[end])) end++;

                var candidate = document[at..end];

                if (SecretReference.IsReference(candidate)) found.Add(candidate);

                at = end;
            }
        }

        return found;
    }

    private static bool IsReferenceCharacter(char c) =>
        char.IsAsciiLetterOrDigit(c) || c is ':' or '-' or '_';
}
