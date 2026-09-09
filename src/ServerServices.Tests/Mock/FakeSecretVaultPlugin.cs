using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Contracts.Secrets;
using Serilog;

namespace ServerServices.Tests.Mock;

/// <summary>
/// A secret-vault plugin that answers from a dictionary.
///
/// It stands in for a real plugin so the server-side tests exercise the whole path — connection,
/// credentials, cache, error mapping — without a vault, a network or a loaded assembly. Loading a
/// real plugin in a test would need a signed DLL in a plugins directory and would test McMaster's
/// loader rather than NetRisk's behaviour.
///
/// It counts its calls, because several of the assertions are about how many times the vault was
/// asked: a cache that does not cache is invisible unless something is counting.
/// </summary>
public class FakeSecretVaultPlugin : INetriskSecretVaultPlugin
{
    public string PluginName { get; set; } = "FakeVaultPlugin";

    public string PluginVersion => "1.0.0";

    public string PluginDescription => "A vault that lives in a dictionary.";

    public string VaultKind { get; set; } = "fake";

    public bool RequiresMachineId { get; set; }

    /// <summary>Secret id → (field → value). A secret with the empty field name is single-valued.</summary>
    public Dictionary<string, Dictionary<string, string>> Secrets { get; } = new(StringComparer.Ordinal);

    /// <summary>Set to make every operation fail the way a refusing vault does.</summary>
    public string? FailWith { get; set; }

    /// <summary>Set to make the plugin throw something the host did not anticipate.</summary>
    public Exception? ThrowUnexpected { get; set; }

    /// <summary>The vault's own cache cap, when it reports one.</summary>
    public TimeSpan? MaxCacheAge { get; set; }

    public int GetCalls { get; private set; }

    public int ListCalls { get; private set; }

    public int TestCalls { get; private set; }

    /// <summary>The credentials of the most recent call, so a test can assert what the host passed.</summary>
    public SecretVaultCredentials? LastCredentials { get; private set; }

    public void Initialize(ILogger? logger) { }

    public void Dispose() { }

    /// <summary>Adds a single-valued secret.</summary>
    public FakeSecretVaultPlugin With(string id, string value)
    {
        Secrets[id] = new Dictionary<string, string>(StringComparer.Ordinal) { [""] = value };
        return this;
    }

    /// <summary>Adds a structured secret.</summary>
    public FakeSecretVaultPlugin With(string id, params (string Field, string Value)[] fields)
    {
        Secrets[id] = fields.ToDictionary(f => f.Field, f => f.Value, StringComparer.Ordinal);
        return this;
    }

    public Task<SecretVaultTestResult> TestConnectionAsync(SecretVaultContext context,
        CancellationToken ct = default)
    {
        TestCalls++;
        LastCredentials = context.Credentials;

        if (ThrowUnexpected != null) throw ThrowUnexpected;

        return Task.FromResult(FailWith is null
            ? SecretVaultTestResult.Ok($"Reached the fake vault ({Secrets.Count}).", Secrets.Count)
            : SecretVaultTestResult.Fail(FailWith));
    }

    public Task<IReadOnlyList<VaultSecretDescriptor>> ListSecretsAsync(SecretVaultContext context,
        CancellationToken ct = default)
    {
        ListCalls++;
        LastCredentials = context.Credentials;

        if (ThrowUnexpected != null) throw ThrowUnexpected;
        if (FailWith != null) throw new SecretVaultException(FailWith);

        IReadOnlyList<VaultSecretDescriptor> descriptors = Secrets
            .Select(s => new VaultSecretDescriptor
            {
                Id = s.Key,
                Name = s.Key,
                Fields = s.Value.Keys.Where(k => k.Length > 0).ToList()
            })
            .ToList();

        return Task.FromResult(descriptors);
    }

    public Task<VaultSecretValue> GetSecretAsync(SecretVaultContext context, VaultSecretReference reference,
        CancellationToken ct = default)
    {
        GetCalls++;
        LastCredentials = context.Credentials;

        if (ThrowUnexpected != null) throw ThrowUnexpected;
        if (FailWith != null) throw new SecretVaultException(FailWith);

        if (!Secrets.TryGetValue(reference.SecretId, out var fields))
            throw new SecretVaultException($"No secret '{reference.SecretId}'.");

        if (!fields.TryGetValue(reference.Field ?? "", out var value))
            throw new SecretVaultException(
                $"Secret '{reference.SecretId}' has no field '{reference.Field}'.");

        return Task.FromResult(new VaultSecretValue { Value = value, MaxCacheAge = MaxCacheAge });
    }
}
