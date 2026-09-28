using System.Globalization;
using System.Text.Json;
using Contracts.Secrets;
using Contracts.Ui;
using DAL.Context;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Model.Exceptions;
using Model.Secrets;
using Serilog;
using ServerServices.Interfaces;
using ServerServices.Services;

namespace ServerServices.Secrets;

/// <summary>
/// Vault connections and reference resolution, on top of whatever secret-vault plugin is installed.
///
/// The interesting design choices are all about failure. Listing and testing report failures as
/// values, because an administrator is watching and the answer to "is this API key right" is a
/// message, not a stack trace. Resolution throws, because nobody is watching: it happens inside a
/// sync job or a notification send, and the only useful outcome is that the operation fails loudly
/// with the vault named. See <see cref="Model.Exceptions.SecretVaultResolutionException"/>.
/// </summary>
public class SecretVaultService(
    ILogger logger,
    IDalService dalService,
    ISecretProtector protector,
    IPluginsService pluginsService,
    IObfuscatedSecretCache cache,
    IPluginHttpClientFactory httpFactory,
    IVaultEndpointResolver endpoints)
    : ServiceBase(logger, dalService), ISecretVaultService
{
    /// <summary>
    /// Cache key prefix per connection, so rotating one connection's key can evict exactly its
    /// values (<see cref="IObfuscatedSecretCache.RemoveByPrefix"/>) and nobody else's.
    /// </summary>
    private static string CachePrefix(int connectionId) => $"vault:{connectionId}:";

    /// <summary>
    /// The cache key for one reference. Built from the reference's own canonical string rather than
    /// by concatenating its parts, so a secret named <c>a</c> with field <c>b</c> and one named
    /// <c>a:b</c> with no field cannot collide onto the same entry.
    /// </summary>
    private static string CacheKey(SecretReference reference) =>
        CachePrefix(reference.ConnectionId) + reference;

    // --- connections ------------------------------------------------------------------------

    public async Task<List<SecretVaultConnectionView>> GetConnectionsAsync(bool includeDisabled = true)
    {
        await using var db = DalService.GetContext();

        var connections = await db.SecretVaultConnections
            .Where(c => includeDisabled || c.Enabled)
            .OrderBy(c => c.Name)
            .ToListAsync();

        // The plugin set is read once for the whole list, not once per row: each read instantiates
        // every loaded plugin by reflection, and a page of ten connections would do that ten times.
        var plugins = await GetAvailablePluginsAsync();

        return connections.Select(c => ToView(c, plugins)).ToList();
    }

    public async Task<SecretVaultConnectionView> GetConnectionAsync(int id)
    {
        await using var db = DalService.GetContext();
        return ToView(await LoadAsync(db, id), await GetAvailablePluginsAsync());
    }

    public async Task<List<SecretVaultPluginInfo>> GetAvailablePluginsAsync()
    {
        try
        {
            var plugins = await pluginsService.GetEnabledPluginsAsync<INetriskSecretVaultPlugin>();

            return plugins.Select(p => new SecretVaultPluginInfo
            {
                PluginName = p.PluginName,
                VaultKind = p.VaultKind,
                Description = p.PluginDescription,
                Version = p.PluginVersion,
                RequiresMachineId = p.RequiresMachineId,
                RequiresAppId = p.RequiresAppId,
                SecretSelectorFields = DescribeScreen(p, PluginScreen.VaultSecretSelector),
                ConnectionEditorFields = DescribeScreen(p, PluginScreen.VaultConnectionEditor)
            }).ToList();
        }
        catch (Exception ex)
        {
            // A plugin directory that cannot be read must not take the connection editor down with
            // it. Empty is a truthful answer here — nothing is usable — and the log says why.
            Logger.Error(ex, "Could not enumerate the installed secret-vault plugins");
            return [];
        }
    }

    public async Task<SecretVaultConnectionView> CreateConnectionAsync(SecretVaultConnectionInput input,
        string? apiKey, int? userId = null)
    {
        Validate(input);
        await ValidateAgainstPluginAsync(input);

        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidParameterException(nameof(apiKey),
                "A vault API key is required to create a connection.");

        await using var db = DalService.GetContext();

        if (await db.SecretVaultConnections.AnyAsync(c => c.Name == input.Name))
            throw new InvalidParameterException(nameof(input.Name),
                $"A vault connection named '{input.Name}' already exists.");

        var stored = new SecretVaultConnection
        {
            CreatedAt = DateTime.UtcNow,
            CreatedById = userId
        };

        Copy(input, stored);
        stored.EncryptedApiKey = protector.Protect(apiKey);

        db.SecretVaultConnections.Add(stored);
        await db.SaveChangesAsync();

        Logger.Information("Secret vault connection {Name} created against {BaseUrl} via plugin {Plugin}",
            stored.Name, stored.BaseUrl, stored.PluginName);

        return ToView(stored, await GetAvailablePluginsAsync());
    }

    public async Task<SecretVaultConnectionView> UpdateConnectionAsync(SecretVaultConnectionInput input,
        string? apiKey)
    {
        Validate(input);
        await ValidateAgainstPluginAsync(input);

        await using var db = DalService.GetContext();

        var stored = await LoadAsync(db, input.Id);

        if (await db.SecretVaultConnections.AnyAsync(c => c.Name == input.Name && c.Id != input.Id))
            throw new InvalidParameterException(nameof(input.Name),
                $"A vault connection named '{input.Name}' already exists.");

        // Captured before the copy, because deciding afterwards means comparing the new value with
        // itself and never evicting anything. Compared against the *normalized* incoming values — the
        // same trimming Copy applies — so that re-saving a form unchanged does not read as a change and
        // throw away a warm cache on every keystroke-free save.
        var previousBaseUrl = stored.BaseUrl;
        var previousMachineId = stored.MachineId;
        var previousAppId = stored.AppId;
        var previousIgnoreSsl = stored.IgnoreSslErrors;

        Copy(input, stored);

        var addressChanged = !string.Equals(previousBaseUrl, stored.BaseUrl, StringComparison.Ordinal)
                             || !string.Equals(previousMachineId, stored.MachineId, StringComparison.Ordinal)
                             // The app id is part of who the vault thinks is calling, so changing it can
                             // change which secrets answer — and the TLS setting changes whether the
                             // health probes that pick a node can succeed at all. Both make a cached
                             // node choice and a cached value stale for the same reason a new address does.
                             || !string.Equals(previousAppId, stored.AppId, StringComparison.Ordinal)
                             || previousIgnoreSsl != stored.IgnoreSslErrors;

        stored.UpdatedAt = DateTime.UtcNow;

        if (apiKey != null) stored.EncryptedApiKey = protector.Protect(apiKey);

        await db.SaveChangesAsync();

        // Anything that changes *which* vault answers, or *whether* it answers, invalidates every
        // value cached through this connection. Serving a 15-minute-old secret from a vault whose
        // credential was just rotated is precisely the failure a short TTL is supposed to prevent.
        // The discovered node is cached separately from the secret values, and for a different
        // reason, so it needs its own eviction: an operator who repoints a connection at another
        // cluster and presses Test must not be told about the previous one.
        if (addressChanged) endpoints.Invalidate(stored.Id);

        if (apiKey != null || addressChanged || !stored.Enabled)
        {
            var evicted = cache.RemoveByPrefix(CachePrefix(stored.Id));
            if (evicted > 0)
                Logger.Information("Evicted {Count} cached secrets for vault connection {Name} after a change",
                    evicted, stored.Name);
        }

        return ToView(stored, await GetAvailablePluginsAsync());
    }

    public async Task DeleteConnectionAsync(int id)
    {
        await using var db = DalService.GetContext();

        var stored = await LoadAsync(db, id);

        var references = await CountReferencesAsync(db, id);
        if (references > 0)
            throw new InvalidParameterException(nameof(id),
                $"{references} credential field(s) still resolve through the vault connection "
                + $"'{stored.Name}'. Point them at another secret first — deleting this connection "
                + "would leave them unresolvable.");

        db.SecretVaultConnections.Remove(stored);
        await db.SaveChangesAsync();

        cache.RemoveByPrefix(CachePrefix(id));

        Logger.Information("Secret vault connection {Id} ({Name}) deleted", id, stored.Name);
    }

    // --- vault operations -------------------------------------------------------------------

    public async Task<SecretVaultTestResultView> TestConnectionAsync(int id)
    {
        await using var db = DalService.GetContext();

        var stored = await LoadAsync(db, id);

        SecretVaultTestResultView view;

        try
        {
            var (plugin, context, endpoint) = await OpenAsync(stored);
            var result = await plugin.TestConnectionAsync(context);

            view = new SecretVaultTestResultView
            {
                Success = result.Success,
                Message = result.Message,
                VisibleSecretCount = result.VisibleSecretCount,
                ResolvedEndpoint = endpoint.Discovered ? endpoint.BaseUrl : string.Empty,
                EndpointNote = endpoint.Note
            };
        }
        catch (Exception ex) when (ex is SecretVaultException or SecretProtectionException
                                       or SecretVaultResolutionException or InvalidParameterException)
        {
            view = new SecretVaultTestResultView { Success = false, Message = ex.Message };
        }
        catch (Exception ex)
        {
            // A plugin is third-party code and may throw anything. The operator gets a message; the
            // stack trace goes to the log, where it belongs.
            Logger.Error(ex, "Secret vault plugin {Plugin} threw while testing connection {Id}",
                stored.PluginName, id);
            view = new SecretVaultTestResultView
            {
                Success = false,
                Message = $"The '{stored.PluginName}' plugin failed while testing this connection: {ex.Message}"
            };
        }

        // Folded into the message rather than only carried in its own field, because the message is
        // what is persisted on the connection and what the grid shows the next time somebody opens
        // the screen. A note that only exists in the response to the test call is a note nobody
        // reads twice.
        if (!string.IsNullOrEmpty(view.ResolvedEndpoint))
            view.Message = $"{view.Message} (node {view.ResolvedEndpoint})";

        if (!string.IsNullOrWhiteSpace(view.EndpointNote))
            view.Message = $"{view.Message} {view.EndpointNote}";

        stored.LastTestAt = DateTime.UtcNow;
        stored.LastTestSucceeded = view.Success;
        stored.LastTestMessage = view.Message;
        await db.SaveChangesAsync();

        return view;
    }

    public async Task<List<VaultSecretSummary>> ListSecretsAsync(int connectionId)
    {
        await using var db = DalService.GetContext();

        var stored = await LoadAsync(db, connectionId);

        var (plugin, context, _) = await OpenAsync(stored);

        IReadOnlyList<VaultSecretDescriptor> descriptors;

        try
        {
            descriptors = await plugin.ListSecretsAsync(context);
        }
        catch (SecretVaultException ex)
        {
            throw new IntegrationRequestException(stored.PluginName,
                $"The vault '{stored.Name}' could not list its secrets: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Secret vault plugin {Plugin} threw while listing secrets", stored.PluginName);
            throw new IntegrationRequestException(stored.PluginName,
                $"The '{stored.PluginName}' plugin failed while listing the secrets of '{stored.Name}'.", ex);
        }

        return descriptors
            .Where(d => !string.IsNullOrWhiteSpace(d.Id))
            .Select(d => new VaultSecretSummary
            {
                Id = d.Id,
                Name = string.IsNullOrWhiteSpace(d.Name) ? d.Id : d.Name,
                Path = d.Path,
                Description = d.Description,
                Fields = d.Fields.Where(f => !string.IsNullOrWhiteSpace(f)).ToList(),
                Version = d.Version,
                UpdatedAt = d.UpdatedAt
            })
            .OrderBy(s => s.Path ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<string> ResolveAsync(SecretReference reference, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(reference);

        var cacheKey = CacheKey(reference);

        var cached = cache.Get(cacheKey);
        if (cached != null) return cached;

        await using var db = DalService.GetContext();

        var stored = await db.SecretVaultConnections
                         .FirstOrDefaultAsync(c => c.Id == reference.ConnectionId, ct)
                     ?? throw new SecretVaultResolutionException(
                         $"Vault connection {reference.ConnectionId} no longer exists, so the secret "
                         + $"'{reference.DisplayKey}' cannot be read.", reference.ToString());

        if (!stored.Enabled)
            throw new SecretVaultResolutionException(
                $"The vault connection '{stored.Name}' is disabled, so the secret "
                + $"'{reference.DisplayKey}' cannot be read.", reference.ToString());

        var (plugin, context, _) = await OpenAsync(stored, ct);

        VaultSecretValue value;

        var pluginReference = Normalize(plugin, new VaultSecretReference
        {
            SecretId = reference.SecretId,
            Field = reference.Field,
            Options = reference.Options
        });

        // The declared fields are checked here and not only in the picker, because the picker is not
        // the only way a credential column gets written and because a plugin may have started
        // requiring a field after the reference was stored. Normalizing first is what keeps a
        // reference in the plugin's older self-encoded form passing: by this point its values are in
        // Options where the declaration can see them.
        if (RequiredFieldMissing(plugin, pluginReference) is { } missing)
            throw new SecretVaultResolutionException(
                $"The secret '{reference.DisplayKey}' cannot be read from '{stored.Name}': {missing} "
                + "Re-select the secret on the field so the value can be chosen.",
                reference.ToString());

        try
        {
            value = await plugin.GetSecretAsync(context, pluginReference, ct);
        }
        catch (SecretVaultException ex)
        {
            throw new SecretVaultResolutionException(
                $"The vault '{stored.Name}' refused to read '{reference.DisplayKey}': {ex.Message}",
                reference.ToString(), ex);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Secret vault plugin {Plugin} threw while reading a secret", stored.PluginName);
            throw new SecretVaultResolutionException(
                $"The '{stored.PluginName}' plugin failed while reading '{reference.DisplayKey}' from "
                + $"'{stored.Name}'.", reference.ToString(), ex);
        }

        if (string.IsNullOrEmpty(value.Value))
            throw new SecretVaultResolutionException(
                $"The vault '{stored.Name}' returned an empty value for '{reference.DisplayKey}'. "
                + "An empty credential is not usable, so this is reported rather than sent.",
                reference.ToString());

        cache.Set(cacheKey, value.Value, EffectiveTtl(stored, value));

        return value.Value;
    }

    /// <summary>
    /// How long this value may be cached: the connection's configured TTL, clamped to the product
    /// bounds, and shortened when the vault itself asked for something shorter.
    ///
    /// The vault wins when it is stricter and loses when it is laxer. A vault that says "cache this
    /// for a day" does not get to override an installation's 15-minute policy; one that says "30
    /// seconds" knows something about that particular secret that the installation does not.
    /// </summary>
    private static TimeSpan EffectiveTtl(SecretVaultConnection connection, VaultSecretValue value)
    {
        var minutes = Math.Clamp(connection.CacheTtlMinutes,
            SecretVaultDefaults.MinCacheTtlMinutes, SecretVaultDefaults.MaxCacheTtlMinutes);

        var ttl = TimeSpan.FromMinutes(minutes);

        if (value.MaxCacheAge is { } vaultMax && vaultMax < ttl) ttl = vaultMax;

        return ttl;
    }

    public async Task<SecretReferenceView> DescribeAsync(string? storedValue)
    {
        if (!SecretReference.TryParse(storedValue, out var reference))
            return new SecretReferenceView { IsVaultReference = false, Resolvable = false };

        await using var db = DalService.GetContext();

        var connection = await db.SecretVaultConnections
            .FirstOrDefaultAsync(c => c.Id == reference.ConnectionId);

        // Read through the plugin's own eyes before it is shown. A reference in a form the plugin no
        // longer produces -- an environment encoded inside the secret id, say -- would otherwise be
        // displayed as the raw string an operator cannot act on, and the caption beside a credential
        // field is exactly where the environment it reads ought to be legible.
        var described = await NormalizeForDisplayAsync(connection, reference);

        var view = new SecretReferenceView
        {
            IsVaultReference = true,
            ConnectionId = reference.ConnectionId,
            SecretId = described.SecretId,
            Field = described.Field,
            Options = described.Options.ToDictionary(o => o.Key, o => o.Value, StringComparer.Ordinal),
            ConnectionName = connection?.Name ?? string.Empty,
            Resolvable = connection is { Enabled: true }
        };

        // Deliberately no vault call. This is asked once per secret field when a form opens, and a
        // form with six credential fields would otherwise be six network round trips before it
        // renders. Whether the *secret* still exists is answered by the connection test and by the
        // picker, both of which the operator invokes on purpose.
        var label = SecretReference.Create(reference.ConnectionId, described.SecretId, described.Field,
            described.Options).DisplayKey;

        view.DisplayName = connection is null
            ? $"{label} (vault connection {reference.ConnectionId} is missing)"
            : connection.Enabled
                ? $"{connection.Name}: {label}"
                : $"{connection.Name}: {label} (connection disabled)";

        return view;
    }

    /// <summary>
    /// The reference as its plugin reads it, for display only.
    ///
    /// Still no vault call — <c>NormalizeReference</c> is contracted to be offline — but it does
    /// instantiate the plugin, which is why it is skipped for a reference that already carries
    /// declared values and for a connection whose plugin is not installed. A form with six
    /// credential fields asks this six times, and the answer has to stay cheap enough that it does.
    /// </summary>
    private async Task<VaultSecretReference> NormalizeForDisplayAsync(SecretVaultConnection? connection,
        SecretReference reference)
    {
        var asStored = new VaultSecretReference
        {
            SecretId = reference.SecretId,
            Field = reference.Field,
            Options = reference.Options
        };

        if (connection is null || reference.Options.Count > 0) return asStored;

        var plugin = await pluginsService
            .GetPluginByNameAsync<INetriskSecretVaultPlugin>(connection.PluginName);

        return plugin is null ? asStored : Normalize(plugin, asStored);
    }

    public async Task<int> CountReferencesAsync(int connectionId)
    {
        await using var db = DalService.GetContext();
        return await CountReferencesAsync(db, connectionId);
    }

    public async Task<bool> IsAvailableAsync()
    {
        var plugins = await GetAvailablePluginsAsync();
        if (plugins.Count == 0) return false;

        await using var db = DalService.GetContext();

        var names = plugins.Select(p => p.PluginName).ToList();

        return await db.SecretVaultConnections.AnyAsync(c => c.Enabled && names.Contains(c.PluginName));
    }

    // --- internals --------------------------------------------------------------------------

    /// <summary>
    /// Resolves the plugin for a connection and builds the per-call context: credentials decrypted
    /// here and nowhere else, plus the host's HTTP seam.
    /// </summary>
    private async Task<(INetriskSecretVaultPlugin Plugin, SecretVaultContext Context,
        VaultEndpointSelection Endpoint)> OpenAsync(SecretVaultConnection connection,
        CancellationToken ct = default)
    {
        var plugin = await pluginsService.GetPluginByNameAsync<INetriskSecretVaultPlugin>(connection.PluginName);

        if (plugin is null)
            throw new SecretVaultResolutionException(
                $"The secret-vault plugin '{connection.PluginName}' that connection '{connection.Name}' "
                + "needs is not installed. Install it in the Plugins/"
                + SecretVaultDefaults.PluginDirectory + " directory and reload plugins.");

        if (!await pluginsService.PluginIsEnabledAsync(plugin.PluginName))
            throw new SecretVaultResolutionException(
                $"The secret-vault plugin '{plugin.PluginName}' is installed but disabled, so vault "
                + $"connection '{connection.Name}' cannot be used. Enable it under Admin, Plugins.");

        var apiKey = protector.Unprotect(connection.EncryptedApiKey);

        if (string.IsNullOrEmpty(apiKey))
            throw new SecretVaultResolutionException(
                $"Vault connection '{connection.Name}' has no API key stored. Re-enter it on the "
                + "connection.");

        // A vault connection's own key is the one credential in the product that cannot be a vault
        // reference: resolving it would need a connection to the vault, which is what this key is
        // for. The check is here rather than left to the plugin because ISecretProtector.Unprotect
        // hands a reference back verbatim by design -- so without it the literal string
        // "vault:v1:3:secret/..." is sent as the vault token, and the vault answers 403 "Permission
        // denied", which is indistinguishable from an expired token and sends the operator looking
        // in the wrong place.
        if (SecretReference.IsReference(apiKey))
            throw new SecretVaultResolutionException(
                $"The API key of vault connection '{connection.Name}' is stored as a vault reference, "
                + "which cannot work: reading it would require this very connection. Re-enter the key "
                + "itself on the connection.");

        if (plugin.RequiresMachineId && string.IsNullOrWhiteSpace(connection.MachineId))
            throw new SecretVaultResolutionException(
                $"The '{plugin.PluginName}' plugin binds credentials to a machine identity, and vault "
                + $"connection '{connection.Name}' has none. Enter the machine ID the vault issued for "
                + "this server.");

        if (plugin.RequiresAppId && string.IsNullOrWhiteSpace(connection.AppId))
            throw new SecretVaultResolutionException(
                $"The '{plugin.PluginName}' plugin authorizes by application identity, and vault "
                + $"connection '{connection.Name}' has none. Enter the app ID the vault knows this "
                + "installation by.");

        if (connection.IgnoreSslErrors)
            Logger.Warning(
                "Vault connection {Name} is configured to skip TLS certificate validation. The vault's "
                + "identity is not being verified on any call made through it",
                connection.Name);

        // The plugin is handed one node, never a cluster name: it was written against a base URL it
        // can concatenate a path onto, and teaching every plugin to do SRV discovery would put the
        // same DNS code — and the same SSRF question — in each of them. See VaultEndpointResolver.
        var selection = await endpoints.ResolveAsync(connection.Id, connection.BaseUrl,
            connection.IgnoreSslErrors, ct);

        if (selection.Discovered)
            Logger.Debug("Vault connection {Name} resolved '{Address}' to {Node} of {Count} candidates",
                connection.Name, connection.BaseUrl, selection.BaseUrl, selection.Candidates.Count);

        var context = new SecretVaultContext
        {
            Credentials = new SecretVaultCredentials
            {
                BaseUrl = selection.BaseUrl,
                ApiKey = apiKey,
                MachineId = string.IsNullOrWhiteSpace(connection.MachineId) ? null : connection.MachineId,
                AppId = string.IsNullOrWhiteSpace(connection.AppId) ? null : connection.AppId,
                Options = ReadExtraSettings(connection)
            },
            Http = httpFactory.Create(connection.IgnoreSslErrors)
        };

        return (plugin, context, selection);
    }

    private static async Task<SecretVaultConnection> LoadAsync(NRDbContext db, int id) =>
        await db.SecretVaultConnections.FirstOrDefaultAsync(c => c.Id == id)
        ?? throw new DataNotFoundException("SecretVaultConnection", id.ToString(),
            new Exception($"Vault connection {id} was not found."));

    /// <summary>
    /// What one plugin contributes to one screen, checked against the host's bounds.
    ///
    /// <para>Every part of this is defensive, because <c>DescribeScreen</c> is third-party code the
    /// host calls while building a form. It is contracted to be pure and it is called on a plugin
    /// instance the host just created, but "contracted" is not "guaranteed": a throw here would take
    /// down the connection editor and the picker for every vault, including the ones whose plugin
    /// behaved. So a plugin that throws contributes nothing and says so in the log, which is the
    /// same answer as a plugin that declares nothing.</para>
    ///
    /// <para>A declaration that breaks a structural rule is dropped whole by
    /// <see cref="PluginFieldSpecProjection"/> rather than partly rendered. See there for why.</para>
    /// </summary>
    private List<VaultFieldSpecView> DescribeScreen(INetriskSecretVaultPlugin plugin, PluginScreen screen)
    {
        IReadOnlyList<PluginFieldSpec> declared;

        try
        {
            declared = plugin.DescribeScreen(screen);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Secret vault plugin {Plugin} threw while describing the {Screen} screen; "
                             + "it contributes no controls to it", plugin.PluginName, screen);
            return [];
        }

        var projection = PluginFieldSpecProjection.Project(declared, CultureInfo.CurrentUICulture);

        if (projection.Problem is { } problem)
        {
            Logger.Warning(
                "Secret vault plugin {Plugin} declared controls for the {Screen} screen that this "
                + "host will not render, because {Problem}. The screen renders without them",
                plugin.PluginName, screen, problem);

            return [];
        }

        return projection.Fields;
    }

    /// <summary>
    /// The options for one declared choice field, fetched from the plugin with the connection's
    /// context.
    ///
    /// By call and never from the declaration, because the answer depends on the credential: an
    /// environment-scoped role may read the environments its scope names and no others, so two
    /// connections served by one plugin answer differently.
    /// </summary>
    public async Task<List<VaultFieldOptionView>> ListFieldOptionsAsync(int connectionId,
        VaultFieldOptionsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var db = DalService.GetContext();

        var stored = await LoadAsync(db, connectionId);

        var (plugin, context, _) = await OpenAsync(stored);

        var screen = (PluginScreen)(int)request.Screen;

        // Asked only for a field the plugin actually declared as a choice. Without this the endpoint
        // is a way to call an arbitrary plugin method with an arbitrary key, and the bounds the
        // projection applies would have been decoration.
        var declared = DescribeScreen(plugin, screen)
            .FirstOrDefault(f => string.Equals(f.Key, request.FieldKey, StringComparison.Ordinal));

        if (declared is null || declared.Kind != VaultFieldKind.Choice)
            throw new InvalidParameterException(nameof(request.FieldKey),
                $"The '{stored.PluginName}' plugin declares no choice field named "
                + $"'{request.FieldKey}' on that screen.");

        var query = new PluginFieldQuery
        {
            Screen = screen,
            FieldKey = declared.Key,
            SecretId = string.IsNullOrWhiteSpace(request.SecretId) ? null : request.SecretId,
            Values = request.Values ?? new Dictionary<string, string>()
        };

        IReadOnlyList<PluginFieldOption> options;

        try
        {
            options = await plugin.GetFieldOptionsAsync(context, query);
        }
        catch (SecretVaultException ex)
        {
            throw new IntegrationRequestException(stored.PluginName,
                $"The vault '{stored.Name}' could not list the values for '{declared.Label}': {ex.Message}",
                ex);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Secret vault plugin {Plugin} threw while listing the options of {Field}",
                stored.PluginName, declared.Key);

            throw new IntegrationRequestException(stored.PluginName,
                $"The '{stored.PluginName}' plugin failed while listing the values for "
                + $"'{declared.Label}'.", ex);
        }

        var projected = PluginFieldSpecProjection.ProjectOptions(options, CultureInfo.CurrentUICulture,
            out var truncated);

        if (truncated)
            Logger.Warning(
                "Secret vault plugin {Plugin} offered more than {Max} options for {Field}; the list "
                + "shown to the operator is cut short", stored.PluginName,
                SecretVaultDefaults.MaxFieldOptions, declared.Key);

        return projected;
    }

    /// <summary>
    /// The plugin's own reading of a stored reference, for a reference that carries no declared
    /// values.
    ///
    /// <para>This is how a plugin retires a grammar it once had to invent. BastionVault could not
    /// put an environment anywhere the contract named, so it wrote it into the secret id as
    /// <c>?env=hml</c>; only BastionVault knows that, and a host that learned it would be repeating
    /// the mistake this contract exists to end. So the host hands the reference back and uses
    /// whatever comes out.</para>
    ///
    /// <para>Skipped when the reference already carries values: those came from a declared control,
    /// so there is nothing legacy to read. Contracted to be pure and offline, and wrapped anyway —
    /// a plugin that throws here leaves the reference exactly as it was stored, which is the
    /// behaviour of every plugin that does not implement it.</para>
    /// </summary>
    /// <summary>
    /// The complaint about a reference whose declared fields are not all filled, or null when they
    /// are.
    ///
    /// The point of saying it here is that the alternative is the vault saying it: an
    /// environment-scoped BastionVault credential answers a read that names no environment with a
    /// flat permission-denied, which is indistinguishable from an expired token and sends the
    /// operator to the wrong place.
    /// </summary>
    private string? RequiredFieldMissing(INetriskSecretVaultPlugin plugin, VaultSecretReference reference)
    {
        var declared = DescribeScreen(plugin, PluginScreen.VaultSecretSelector);

        return declared.Count == 0
            ? null
            : PluginFieldSpecProjection.Validate(declared, reference.Options);
    }

    private VaultSecretReference Normalize(INetriskSecretVaultPlugin plugin, VaultSecretReference reference)
    {
        if (reference.Options.Count > 0) return reference;

        try
        {
            return plugin.NormalizeReference(reference) ?? reference;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Secret vault plugin {Plugin} threw while normalizing a stored reference; "
                             + "it is used as stored", plugin.PluginName);
            return reference;
        }
    }

    /// <summary>
    /// Counts the credential fields that resolve through a connection.
    ///
    /// <b>This is the registry to extend when a new credential column gains vault support.</b> There
    /// is no join table — a reference lives in the credential column itself, in the clear — so
    /// "who points at this connection" is this list of columns and nothing else. Enumerated
    /// explicitly rather than discovered from the model because the alternative is raw SQL over
    /// information_schema, which the in-memory provider the service tests run on cannot execute.
    ///
    /// A column missed here does not corrupt anything: it means deleting a connection that column
    /// still uses is allowed, and that field then fails to resolve with a message naming the missing
    /// connection. Bad, but loud.
    /// </summary>
    private static async Task<int> CountReferencesAsync(NRDbContext db, int connectionId)
    {
        var count = 0;

        // Both wire versions, in one loop. A v2 reference (one carrying plugin-declared values) that
        // this scan missed would make deleting the connection it points at allowed, and that field
        // would then fail to resolve naming a connection that no longer exists.
        foreach (var prefix in SecretReference.ConnectionPrefixes(connectionId))
        {
            count += await db.TrendMicroConnections
                .CountAsync(c => c.EncryptedApiKey != null && c.EncryptedApiKey.StartsWith(prefix));

            count += await db.SecurityScorecardConnections
                .CountAsync(c => c.EncryptedApiToken != null && c.EncryptedApiToken.StartsWith(prefix));

            count += await db.IssueTrackerConnections
                .CountAsync(c => c.EncryptedToken != null && c.EncryptedToken.StartsWith(prefix));

            count += await db.IssueTrackerConnections
                .CountAsync(c => c.EncryptedWebhookSecret != null
                                 && c.EncryptedWebhookSecret.StartsWith(prefix));

            count += await db.IdentityProviders
                .CountAsync(p => p.EncryptedClientSecret != null
                                 && p.EncryptedClientSecret.StartsWith(prefix));

            // Notification channel secrets live inside a JSON blob rather than in a column of their
            // own, so this is a containment test and not a prefix test. It over-counts nothing real:
            // the prefix includes the connection id and a colon, which does not occur in ordinary
            // configuration.
            count += await db.NotificationChannels
                .CountAsync(c => c.ConfigurationJson.Contains(prefix));
        }

        return count;
    }

    /// <summary>
    /// The half of validation that needs to know which plugin will service the connection.
    ///
    /// Separate from <see cref="Validate"/>, which is static and pure, because this one instantiates
    /// every loaded plugin by reflection to read its declarations — and because the two fail for
    /// different reasons: <see cref="Validate"/> rejects a malformed field, this rejects a
    /// well-formed connection that the chosen plugin cannot use.
    ///
    /// It exists because <c>OpenAsync</c>'s machine-ID check fires at *resolution* time, which is
    /// inside a sync job at 3am. A plugin that declares
    /// <see cref="INetriskSecretVaultPlugin.RequiresMachineId"/> and a connection saved without one
    /// is a configuration that can never work, and the place to say so is the form.
    /// </summary>
    private async Task ValidateAgainstPluginAsync(SecretVaultConnectionInput input)
    {
        var plugins = await GetAvailablePluginsAsync();

        var plugin = plugins.FirstOrDefault(p =>
            string.Equals(p.PluginName, input.PluginName.Trim(), StringComparison.Ordinal));

        // Not an error. A connection may legitimately be saved while its plugin is uninstalled or
        // disabled — that is how an operator prepares one before deploying the DLL, and the view
        // already reports PluginAvailable = false so the state is visible rather than silent.
        if (plugin is null) return;

        if (plugin.RequiresMachineId && string.IsNullOrWhiteSpace(input.MachineId))
            throw new InvalidParameterException(nameof(input.MachineId),
                $"The '{plugin.PluginName}' plugin binds credentials to a machine identity, so this "
                + "connection needs the machine ID the vault issued for this server.");

        // The same check the connection editor runs while the form is open, repeated because the
        // form is not the only caller of the API. Its messages name the plugin's own label, which is
        // the only name an operator has for a control the host did not invent.
        if (PluginFieldSpecProjection.Validate(plugin.ConnectionEditorFields, input.Options) is { } problem)
            throw new InvalidParameterException(nameof(input.Options), problem);

        if (plugin.RequiresAppId && string.IsNullOrWhiteSpace(input.AppId))
            throw new InvalidParameterException(nameof(input.AppId),
                $"The '{plugin.PluginName}' plugin authorizes by application identity, so this "
                + "connection needs the app ID the vault knows this installation by.");
    }

    private static void Validate(SecretVaultConnectionInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (string.IsNullOrWhiteSpace(input.Name))
            throw new InvalidParameterException(nameof(input.Name), "A vault connection name is required.");

        if (input.Name.Length > 255)
            throw new InvalidParameterException(nameof(input.Name),
                "A vault connection name may be at most 255 characters.");

        if (string.IsNullOrWhiteSpace(input.PluginName))
            throw new InvalidParameterException(nameof(input.PluginName),
                "A vault connection must name the plugin that services it.");

        // Parsed here as well as at call time, because an address that does not parse is a typo an
        // administrator can fix while looking at the form — rather than a sync failure a week later.
        // VaultAddress accepts a node URL and a cluster name to be discovered by SRV, and its
        // messages say which forms are legal.
        VaultAddress.Parse(input.BaseUrl, nameof(input.BaseUrl));

        if (input.MachineId is { Length: > 255 })
            throw new InvalidParameterException(nameof(input.MachineId),
                "A machine ID may be at most 255 characters.");

        if (input.AppId is { Length: > 255 })
            throw new InvalidParameterException(nameof(input.AppId),
                "An app ID may be at most 255 characters.");
    }

    private static void Copy(SecretVaultConnectionInput input, SecretVaultConnection target)
    {
        target.Name = input.Name.Trim();
        target.PluginName = input.PluginName.Trim();
        target.BaseUrl = input.BaseUrl.Trim().TrimEnd('/');
        target.MachineId = string.IsNullOrWhiteSpace(input.MachineId) ? null : input.MachineId.Trim();
        target.AppId = string.IsNullOrWhiteSpace(input.AppId) ? null : input.AppId.Trim();
        target.IgnoreSslErrors = input.IgnoreSslErrors;
        target.Enabled = input.Enabled;

        // Clamped rather than refused: a TTL outside the bounds is a value somebody typed, and
        // correcting it to the nearest legal one is both what they meant and what the resolver will
        // enforce anyway.
        target.CacheTtlMinutes = Math.Clamp(input.CacheTtlMinutes,
            SecretVaultDefaults.MinCacheTtlMinutes, SecretVaultDefaults.MaxCacheTtlMinutes);

        target.ExtraSettings = WriteExtraSettings(input.Options);
    }

    /// <summary>
    /// The connection's plugin-declared values, as the plugin will receive them.
    ///
    /// Null and unparseable both read back as empty rather than throwing. A row whose JSON somebody
    /// hand-edited into nonsense must not take every other connection's listing down with it, and an
    /// empty dictionary is what a plugin that declares nothing gets anyway — so the failure mode is
    /// "the vault says the namespace is missing", which names the problem, rather than a 500 on the
    /// connections page.
    /// </summary>
    private IReadOnlyDictionary<string, string> ReadExtraSettings(SecretVaultConnection connection)
    {
        if (string.IsNullOrWhiteSpace(connection.ExtraSettings)) return new Dictionary<string, string>();

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(connection.ExtraSettings)
                   ?? new Dictionary<string, string>();
        }
        catch (JsonException ex)
        {
            Logger.Warning(ex, "Vault connection {Name} has unreadable plugin settings; the plugin is "
                               + "called as though it had none", connection.Name);

            return new Dictionary<string, string>();
        }
    }

    /// <summary>
    /// The values worth storing, as JSON — or null when there are none, so a connection whose plugin
    /// declares nothing keeps a null column rather than an empty object.
    ///
    /// Blank values are dropped: an untouched control means "not set", and storing an empty string
    /// would make a plugin's <c>TryGetValue</c> succeed with nothing in it.
    /// </summary>
    private static string? WriteExtraSettings(Dictionary<string, string>? options)
    {
        if (options is null || options.Count == 0) return null;

        var kept = options
            .Where(o => !string.IsNullOrWhiteSpace(o.Key) && !string.IsNullOrWhiteSpace(o.Value))
            .ToDictionary(o => o.Key.Trim(), o => o.Value.Trim(), StringComparer.Ordinal);

        return kept.Count == 0 ? null : JsonSerializer.Serialize(kept);
    }

    private static SecretVaultConnectionView ToView(SecretVaultConnection connection,
        List<SecretVaultPluginInfo> plugins)
    {
        var plugin = plugins.FirstOrDefault(p =>
            string.Equals(p.PluginName, connection.PluginName, StringComparison.Ordinal));

        return new SecretVaultConnectionView
        {
            Id = connection.Id,
            Name = connection.Name,
            PluginName = connection.PluginName,
            VaultKind = plugin?.VaultKind ?? string.Empty,
            BaseUrl = connection.BaseUrl,
            HasApiKey = !string.IsNullOrEmpty(connection.EncryptedApiKey),
            MachineId = connection.MachineId,
            AppId = connection.AppId,
            IgnoreSslErrors = connection.IgnoreSslErrors,
            Enabled = connection.Enabled,
            CacheTtlMinutes = connection.CacheTtlMinutes,
            LastTestAt = connection.LastTestAt,
            LastTestSucceeded = connection.LastTestSucceeded,
            LastTestMessage = connection.LastTestMessage,
            PluginAvailable = plugin is not null,
            RequiresMachineId = plugin?.RequiresMachineId ?? false,
            RequiresAppId = plugin?.RequiresAppId ?? false,
            Options = ReadExtraSettingsForView(connection)
        };
    }

    /// <summary>
    /// <see cref="ReadExtraSettings"/> for the read-only path, where there is no instance to log
    /// through: <see cref="ToView"/> is static because it is called once per row of a list.
    /// </summary>
    private static Dictionary<string, string> ReadExtraSettingsForView(SecretVaultConnection connection)
    {
        if (string.IsNullOrWhiteSpace(connection.ExtraSettings)) return new Dictionary<string, string>();

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(connection.ExtraSettings)
                   ?? new Dictionary<string, string>();
        }
        catch (JsonException)
        {
            // Reported by ReadExtraSettings on the path that actually calls the vault. Here it would
            // be one log line per row per page load.
            return new Dictionary<string, string>();
        }
    }
}
