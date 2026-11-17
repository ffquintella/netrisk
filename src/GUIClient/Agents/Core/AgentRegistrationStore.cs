using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GUIClient.Agents;

/// <summary>
/// Where approved registrations live between runs. The GUI implementation keeps the serialized list in the client's
/// encrypted configuration store (the same LiteDB file as the rest of the per-user settings); nothing in it is a secret,
/// because a registration carries only the key's SHA-256.
/// </summary>
public interface IAgentRegistrationStore
{
    IReadOnlyList<AgentRegistration> Load();

    void Save(IReadOnlyList<AgentRegistration> registrations);
}

/// <summary>A store that keeps the serialized form in memory — for tests, and so they can inspect exactly what is written.</summary>
public sealed class InMemoryAgentRegistrationStore : IAgentRegistrationStore
{
    public string? Serialized { get; private set; }

    public IReadOnlyList<AgentRegistration> Load() =>
        Serialized is null ? Array.Empty<AgentRegistration>() : AgentRegistrationSerializer.Deserialize(Serialized);

    public void Save(IReadOnlyList<AgentRegistration> registrations) =>
        Serialized = AgentRegistrationSerializer.Serialize(registrations);
}

/// <summary>
/// The persisted shape. A separate DTO rather than serializing <see cref="AgentRegistration"/> directly, so that adding
/// a property to the domain type is a deliberate decision about the store and not a side effect.
/// </summary>
public static class AgentRegistrationSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Serialize(IEnumerable<AgentRegistration> registrations) =>
        JsonSerializer.Serialize(registrations.Select(Stored.From).ToList(), Options);

    /// <summary>The registrations in <paramref name="json"/>, or none when it does not parse.</summary>
    public static IReadOnlyList<AgentRegistration> Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<AgentRegistration>();
        try
        {
            var stored = JsonSerializer.Deserialize<List<Stored>>(json, Options);
            return stored?.Where(s => !string.IsNullOrEmpty(s.Id) && s.KeyHash?.Length == 64)
                       .Select(s => s.ToRegistration()).ToList()
                   ?? (IReadOnlyList<AgentRegistration>)Array.Empty<AgentRegistration>();
        }
        catch (JsonException)
        {
            return Array.Empty<AgentRegistration>();
        }
    }

    private sealed class Stored
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("user_id")] public int UserId { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("description")] public string Description { get; set; } = "";
        [JsonPropertyName("client_name")] public string ClientName { get; set; } = "";
        [JsonPropertyName("client_version")] public string ClientVersion { get; set; } = "";
        [JsonPropertyName("key_sha256")] public string? KeyHash { get; set; }
        [JsonPropertyName("fingerprint")] public string Fingerprint { get; set; } = "";
        [JsonPropertyName("created_at")] public DateTimeOffset CreatedAt { get; set; }
        [JsonPropertyName("last_used_at")] public DateTimeOffset? LastUsedAt { get; set; }

        public static Stored From(AgentRegistration r) => new()
        {
            Id = r.Id,
            UserId = r.UserId,
            Name = r.Name,
            Description = r.Description,
            ClientName = r.ClientName,
            ClientVersion = r.ClientVersion,
            KeyHash = r.KeyHash,
            Fingerprint = r.Fingerprint,
            CreatedAt = r.CreatedAt,
            LastUsedAt = r.LastUsedAt
        };

        public AgentRegistration ToRegistration() => new()
        {
            Id = Id,
            UserId = UserId,
            Name = Name,
            Description = Description,
            ClientName = ClientName,
            ClientVersion = ClientVersion,
            KeyHash = KeyHash ?? "",
            Fingerprint = Fingerprint,
            CreatedAt = CreatedAt,
            LastUsedAt = LastUsedAt
        };
    }
}
