using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GUIClient.Agents;

/// <summary>
/// Which names read like a credential (T352). Normalised before matching: lower-cased, split into words at case changes,
/// underscores and dashes, and also joined with every separator removed, so <c>SSHKEY</c>, <c>privkey</c>,
/// <c>connectionString</c> and <c>chave_api</c> are caught as well as <c>apiKey</c>. Includes Portuguese terms.
/// </summary>
public static class AgentSecretNames
{
    // Matched anywhere in the joined, lower-cased name.
    private static readonly string[] Fragments =
    {
        "password", "passwd", "passphrase", "passcode", "secret", "token", "credential", "apikey", "privkey", "privatekey",
        "sshkey", "keyfile", "keystore", "keypair", "accesskey", "secretkey", "signingkey", "masterkey", "authkey", "enckey",
        "licensekey", "vault", "encrypted", "connectionstring", "connstr", "community",
        "senha", "segredo", "chave", "credencial", "palavrapasse", "contrasena"
    };

    // Ambiguous as fragments ("monkey", "hashtag", "spin"), so matched as whole words.
    private static readonly string[] Words =
        { "key", "keys", "hash", "salt", "pwd", "pw", "pass", "pin", "otp", "totp", "hotp", "mfa", "jwt", "bearer", "cookie", "dsn" };

    public static bool IsCredentialLike(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var joined = new string(name.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        if (Fragments.Any(f => joined.Contains(f, StringComparison.Ordinal))) return true;
        return SplitWords(name).Any(w => Words.Contains(w, StringComparer.Ordinal));
    }

    /// <summary>Splits PascalCase, camelCase, snake_case and kebab-case into lower-case words.</summary>
    public static IEnumerable<string> SplitWords(string name)
    {
        var word = new StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            var ch = name[i];
            if (!char.IsLetterOrDigit(ch))
            {
                if (word.Length > 0) yield return word.ToString().ToLowerInvariant();
                word.Clear();
                continue;
            }
            if (char.IsUpper(ch) && word.Length > 0 &&
                (!char.IsUpper(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1]))))
            {
                yield return word.ToString().ToLowerInvariant();
                word.Clear();
            }
            word.Append(ch);
        }
        if (word.Length > 0) yield return word.ToString().ToLowerInvariant();
    }
}

/// <summary>
/// The allowlist rule for tool results (T352), applied twice: to every registered tool's declared
/// <see cref="IAgentTool.ResultType"/> when the registry is built, and to every result a tool returns before it is sent
/// (<see cref="Conforms"/>), so no tool — built on <c>AgentReadTool</c> or not — can return a field its type does not name.
/// </summary>
public static class AgentResultShape
{
    private static readonly HashSet<Type> Scalars = new()
    {
        typeof(string), typeof(bool), typeof(int), typeof(long), typeof(short), typeof(double), typeof(float),
        typeof(decimal), typeof(DateTime), typeof(DateOnly), typeof(TimeSpan), typeof(DateTimeOffset)
    };

    // Entities, contracts and framework types are never a result shape: a tool maps them to its own record.
    private static readonly string[] ForeignNamespaces =
        { "DAL", "Model", "ClientServices", "System", "Microsoft", "RestSharp", "Newtonsoft" };

    /// <summary>Every way <paramref name="shape"/> could leak; empty when it is a clean allowlist record.</summary>
    public static IReadOnlyList<string> Problems(Type shape)
    {
        var problems = new List<string>();
        Visit(shape, shape.Name, new HashSet<Type>(), problems);
        return problems;
    }

    /// <summary>Whether <paramref name="node"/> is something <paramref name="shape"/> could have serialized to.</summary>
    public static bool Conforms(Type shape, JsonNode? node, out string? problem)
    {
        problem = Check(shape, node, shape.Name);
        return problem is null;
    }

    private static void Visit(Type type, string path, HashSet<Type> seen, List<string> problems)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (Scalars.Contains(type) || type.IsEnum) return;

        if (ListElement(type) is { } element)
        {
            Visit(element, path + "[]", seen, problems);
            return;
        }

        if (!IsRecord(type) || type.IsArray || type.IsInterface || type.IsAbstract ||
            ForeignNamespaces.Any(ns => type.Namespace == ns || type.Namespace?.StartsWith(ns + ".", StringComparison.Ordinal) == true))
        {
            problems.Add($"{path} is a {type.FullName}, not an allowlist record");
            return;
        }
        if (!seen.Add(type)) return;

        foreach (var property in Properties(type))
        {
            var memberPath = path + "." + property.Name;
            if (AgentSecretNames.IsCredentialLike(property.Name)) problems.Add(memberPath + " is named like a credential");
            Visit(property.PropertyType, memberPath, seen, problems);
        }
    }

    private static string? Check(Type type, JsonNode? node, string path)
    {
        if (node is null) return null;
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (Scalars.Contains(type) || type.IsEnum)
            return node is JsonValue ? null : $"{path} should be a value";

        if (ListElement(type) is { } element)
        {
            if (node is not JsonArray array) return $"{path} should be an array";
            for (var i = 0; i < array.Count; i++)
                if (Check(element, array[i], $"{path}[{i}]") is { } inner) return inner;
            return null;
        }

        if (node is not JsonObject obj) return $"{path} should be an object";
        var declared = Properties(type).ToDictionary(p => JsonNamingPolicy.SnakeCaseLower.ConvertName(p.Name),
            p => p.PropertyType, StringComparer.Ordinal);
        foreach (var (name, value) in obj)
        {
            if (!declared.TryGetValue(name, out var memberType)) return $"{path}.{name} is not declared by {type.Name}";
            if (Check(memberType, value, path + "." + name) is { } inner) return inner;
        }
        return null;
    }

    private static Type? ListElement(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyList<>) ? type.GetGenericArguments()[0] : null;

    private static bool IsRecord(Type type) =>
        type.IsClass && type.GetProperty("EqualityContract", BindingFlags.NonPublic | BindingFlags.Instance) is not null;

    private static IEnumerable<PropertyInfo> Properties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.Name != "EqualityContract");
}
