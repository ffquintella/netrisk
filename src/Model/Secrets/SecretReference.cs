using System.Diagnostics.CodeAnalysis;

namespace Model.Secrets;

/// <summary>
/// A pointer to a secret held in an external vault, stored in the same column that would otherwise
/// hold the secret itself.
///
/// The point of the whole feature is that <c>trendmicro_connections.encrypted_api_key</c> stops
/// containing a Vision One API key and starts containing "secret <c>tm-prod</c> in vault connection
/// 3". Encoding that as a marked string in the existing column, rather than adding a
/// <c>vault_secret_id</c> column beside every credential column in the schema, is a deliberate
/// trade:
///
///  * every credential field in the product gains vault support at once, including the ones added
///    next year, because the resolver sits on the read path and not on the table;
///  * an integration that does not know about vaults still round-trips the value correctly, since to
///    it this is an opaque string;
///  * and "is a credential set" — which the API reports as <c>HasApiKey</c> — keeps its existing
///    meaning without a second nullable column to keep in sync.
///
/// What it costs: the value is not queryable as a foreign key, so deleting a vault connection cannot
/// cascade. That is handled by refusing the delete while references exist
/// (<c>SecretVaultService.DeleteConnectionAsync</c>) rather than by pretending the database can see
/// it.
///
/// Wire format, chosen to be unambiguous against a real credential and stable enough to be in the
/// database for years:
///
/// <code>vault:v1:{connectionId}:{base64url(secretId)}[:{base64url(field)}]</code>
///
/// The identifiers are base64url-encoded rather than written literally because a vault's secret id
/// is arbitrary text — Bastionvault allows <c>/</c> and <c>:</c> in a path — and a delimiter that can
/// appear inside a field is a parser that breaks on somebody's real data.
/// </summary>
public sealed class SecretReference
{
    /// <summary>The marker. Nothing else in a credential column may start with this.</summary>
    public const string Prefix = "vault:v1:";

    /// <summary>The <c>secret_vault_connections</c> row that resolves this reference.</summary>
    public required int ConnectionId { get; init; }

    /// <summary>The vault's identifier for the secret, exactly as the vault reported it.</summary>
    public required string SecretId { get; init; }

    /// <summary>The field within a structured secret, or null for a single-value secret.</summary>
    public string? Field { get; init; }

    /// <summary>
    /// A short, non-secret label for logs and for the UI — the reference without the connection id,
    /// which is the part a person recognises.
    /// </summary>
    public string DisplayKey => Field is null ? SecretId : SecretId + " / " + Field;

    /// <summary>Whether <paramref name="value"/> is a vault reference rather than a literal secret.</summary>
    public static bool IsReference(string? value) =>
        value is not null && value.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>
    /// The stored value when it is a vault reference, null when it is a literal credential or empty.
    ///
    /// This is what a connection view returns to a client beside its <c>HasApiKey</c> flag. Returning
    /// the reference is safe — it names a secret, it is not one — and it is the only way the desktop
    /// client can show which secret a field is bound to without the server handing out values.
    /// </summary>
    public static string? StoredReferenceOrNull(string? value) => IsReference(value) ? value : null;

    public override string ToString()
    {
        var encoded = Prefix + ConnectionId + ":" + Encode(SecretId);
        return Field is null ? encoded : encoded + ":" + Encode(Field);
    }

    /// <summary>
    /// Parses a stored value. Returns false — rather than throwing — for anything that is not a
    /// reference, because the caller's next question is always "then treat it as a literal".
    /// </summary>
    public static bool TryParse(string? value, [NotNullWhen(true)] out SecretReference? reference)
    {
        reference = null;
        if (!IsReference(value)) return false;

        // Split with a cap, not an unbounded split: a field that decoded to something containing a
        // colon would otherwise turn into extra parts and be rejected.
        var parts = value![Prefix.Length..].Split(':', 3);
        if (parts.Length is < 2 or > 3) return false;

        if (!int.TryParse(parts[0], out var connectionId) || connectionId <= 0) return false;

        if (!TryDecode(parts[1], out var secretId) || secretId.Length == 0) return false;

        string? field = null;
        if (parts.Length == 3)
        {
            if (!TryDecode(parts[2], out var decodedField) || decodedField.Length == 0) return false;
            field = decodedField;
        }

        reference = new SecretReference { ConnectionId = connectionId, SecretId = secretId, Field = field };
        return true;
    }

    /// <summary>
    /// Builds a reference, rejecting the empty inputs that would produce one that cannot be parsed
    /// back. A whitespace-only field is treated as absent, which is what an untouched text box in the
    /// picker sends.
    /// </summary>
    public static SecretReference Create(int connectionId, string secretId, string? field = null)
    {
        if (connectionId <= 0)
            throw new ArgumentOutOfRangeException(nameof(connectionId), "A vault connection id is required.");
        if (string.IsNullOrWhiteSpace(secretId))
            throw new ArgumentException("A vault secret id is required.", nameof(secretId));

        return new SecretReference
        {
            ConnectionId = connectionId,
            SecretId = secretId,
            Field = string.IsNullOrWhiteSpace(field) ? null : field
        };
    }

    private static string Encode(string value) =>
        Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(value))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static bool TryDecode(string value, out string decoded)
    {
        decoded = string.Empty;
        if (value.Length == 0) return false;

        var padded = value.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 0: break;
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
            // Length % 4 == 1 is not a length base64 can produce, so it is not a truncated
            // reference to be salvaged — it is not one at all.
            default: return false;
        }

        try
        {
            decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(padded));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
