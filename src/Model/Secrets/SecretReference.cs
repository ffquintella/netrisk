using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

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
/// <para><b>Wire format.</b> Two versions, chosen to be unambiguous against a real credential and
/// stable enough to be in the database for years:</para>
///
/// <code>
/// vault:v1:{connectionId}:{base64url(secretId)}[:{base64url(field)}]
/// vault:v2:{connectionId}:{base64url(secretId)}:{base64url(field)}:{base64url(options)}
/// </code>
///
/// The identifiers are base64url-encoded rather than written literally because a vault's secret id
/// is arbitrary text — Bastionvault allows <c>/</c> and <c>:</c> in a path — and a delimiter that can
/// appear inside a field is a parser that breaks on somebody's real data.
///
/// <para><b>v2 exists for <see cref="Options"/>, and is written only when there are any.</b>
/// <see cref="ToString"/> emits v1 whenever the options are empty, so every reference in every
/// installed database stays byte-identical and a field nobody re-picks never changes. A v1 parser
/// meeting a v2 string rejects it outright rather than mis-reading it — which is why v2 is a new
/// tag and not a fourth segment appended to v1, where the extra part would have landed inside the
/// decoded field and produced a silently wrong credential.</para>
/// </summary>
public sealed class SecretReference
{
    /// <summary>The original marker: a reference with no plugin-declared values. Still the common case.</summary>
    public const string Prefix = "vault:v1:";

    /// <summary>The marker for a reference that carries <see cref="Options"/>.</summary>
    public const string PrefixV2 = "vault:v2:";

    /// <summary>
    /// What an <see cref="Options"/> key may look like: lower-case, starting with a letter or digit,
    /// at most 32 characters. Mirrors the bound the host puts on
    /// <c>Contracts.Ui.PluginFieldSpec.Key</c> — a key is part of a stored reference, so it is a
    /// column name in all but name.
    /// </summary>
    public static readonly Regex OptionKeyPattern =
        new("^[a-z0-9][a-z0-9_.-]{0,31}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>The <c>secret_vault_connections</c> row that resolves this reference.</summary>
    public required int ConnectionId { get; init; }

    /// <summary>The vault's identifier for the secret, exactly as the vault reported it.</summary>
    public required string SecretId { get; init; }

    /// <summary>The field within a structured secret, or null for a single-value secret.</summary>
    public string? Field { get; init; }

    /// <summary>
    /// The values of the controls the plugin contributed to the picker, keyed by the plugin's field
    /// key — BastionVault's <c>environment</c> being the first of them.
    ///
    /// <para>This is the property that keeps <see cref="SecretId"/> opaque. Before it existed, a
    /// plugin with a concept the contract had not foreseen could only encode it inside the id, and
    /// the id is a string the host stores, prefix-scans and displays without knowing it had acquired
    /// a grammar. The host does not interpret what is in here either — but it does carry it in a
    /// place of its own, where a second plugin's keys cannot collide with a first plugin's
    /// punctuation.</para>
    ///
    /// <para>Empty for every reference stored before a plugin declared a field, which is nearly all
    /// of them.</para>
    /// </summary>
    public IReadOnlyDictionary<string, string> Options { get; init; } =
        new Dictionary<string, string>();

    /// <summary>
    /// A short, non-secret label for logs and for the UI — the reference without the connection id,
    /// which is the part a person recognises. Plugin values are appended, because "which environment
    /// did that read" is otherwise invisible in a log line.
    /// </summary>
    public string DisplayKey
    {
        get
        {
            var key = Field is null ? SecretId : SecretId + " / " + Field;

            return Options.Count == 0
                ? key
                : key + " (" + string.Join(", ", Options.OrderBy(o => o.Key, StringComparer.Ordinal)
                    .Select(o => o.Key + "=" + o.Value)) + ")";
        }
    }

    /// <summary>Whether <paramref name="value"/> is a vault reference rather than a literal secret.</summary>
    public static bool IsReference(string? value) =>
        value is not null
        && (value.StartsWith(Prefix, StringComparison.Ordinal)
            || value.StartsWith(PrefixV2, StringComparison.Ordinal));

    /// <summary>
    /// The stored-value prefixes that belong to one connection, for the <c>StartsWith</c> scan that
    /// answers "is this connection still in use".
    ///
    /// <b>Both versions, always.</b> Counting only v1 would make deleting a connection that a v2
    /// reference still points at allowed, and that field would then fail to resolve with a message
    /// naming a connection that no longer exists.
    /// </summary>
    public static string[] ConnectionPrefixes(int connectionId) =>
        [Prefix + connectionId + ":", PrefixV2 + connectionId + ":"];

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
        // v1 while there is nothing v1 cannot say. Every stored reference predates Options, so this
        // is what keeps them all byte-identical after the upgrade.
        if (Options.Count == 0)
        {
            var v1 = Prefix + ConnectionId + ":" + Encode(SecretId);
            return Field is null ? v1 : v1 + ":" + Encode(Field);
        }

        return PrefixV2 + ConnectionId + ":" + Encode(SecretId)
               + ":" + (Field is null ? string.Empty : Encode(Field))
               + ":" + Encode(EncodeOptions(Options));
    }

    /// <summary>
    /// Parses a stored value. Returns false — rather than throwing — for anything that is not a
    /// reference, because the caller's next question is always "then treat it as a literal".
    /// </summary>
    public static bool TryParse(string? value, [NotNullWhen(true)] out SecretReference? reference)
    {
        reference = null;
        if (value is null) return false;

        return value.StartsWith(PrefixV2, StringComparison.Ordinal)
            ? TryParseV2(value, out reference)
            : TryParseV1(value, out reference);
    }

    private static bool TryParseV1(string value, [NotNullWhen(true)] out SecretReference? reference)
    {
        reference = null;
        if (!value.StartsWith(Prefix, StringComparison.Ordinal)) return false;

        // Split with a cap, not an unbounded split: a field that decoded to something containing a
        // colon would otherwise turn into extra parts and be rejected.
        var parts = value[Prefix.Length..].Split(':', 3);
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
    /// v2 is fixed-arity — four segments, the field one possibly empty — so "no field" and "no
    /// options" are told apart by position rather than by count.
    /// </summary>
    private static bool TryParseV2(string value, [NotNullWhen(true)] out SecretReference? reference)
    {
        reference = null;

        var parts = value[PrefixV2.Length..].Split(':');
        if (parts.Length != 4) return false;

        if (!int.TryParse(parts[0], out var connectionId) || connectionId <= 0) return false;

        if (!TryDecode(parts[1], out var secretId) || secretId.Length == 0) return false;

        string? field = null;
        if (parts[2].Length > 0)
        {
            if (!TryDecode(parts[2], out var decodedField) || decodedField.Length == 0) return false;
            field = decodedField;
        }

        if (!TryDecode(parts[3], out var encodedOptions)) return false;
        if (!TryDecodeOptions(encodedOptions, out var options)) return false;

        // A v2 string with no options is not something this type ever writes, and accepting it would
        // give one selection two canonical forms — which the cache key is built from.
        if (options.Count == 0) return false;

        reference = new SecretReference
        {
            ConnectionId = connectionId,
            SecretId = secretId,
            Field = field,
            Options = options
        };

        return true;
    }

    /// <summary>
    /// Builds a reference, rejecting the empty inputs that would produce one that cannot be parsed
    /// back. A whitespace-only field is treated as absent, which is what an untouched text box in the
    /// picker sends — and so is a whitespace-only plugin value, which is what an untouched declared
    /// control sends.
    /// </summary>
    public static SecretReference Create(int connectionId, string secretId, string? field = null,
        IReadOnlyDictionary<string, string>? options = null)
    {
        if (connectionId <= 0)
            throw new ArgumentOutOfRangeException(nameof(connectionId), "A vault connection id is required.");
        if (string.IsNullOrWhiteSpace(secretId))
            throw new ArgumentException("A vault secret id is required.", nameof(secretId));

        return new SecretReference
        {
            ConnectionId = connectionId,
            SecretId = secretId,
            Field = string.IsNullOrWhiteSpace(field) ? null : field,
            Options = Clean(options)
        };
    }

    /// <summary>
    /// The plugin values worth storing: a blank one is an untouched control and means "not set", and
    /// a key that is not a key at all is dropped rather than written into a format that would then
    /// fail to parse back.
    /// </summary>
    private static IReadOnlyDictionary<string, string> Clean(IReadOnlyDictionary<string, string>? options)
    {
        if (options is null || options.Count == 0) return new Dictionary<string, string>();

        var cleaned = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (key, value) in options)
        {
            if (key is null || value is null) continue;
            if (string.IsNullOrWhiteSpace(value)) continue;
            if (!OptionKeyPattern.IsMatch(key)) continue;

            cleaned[key] = value.Trim();
        }

        return cleaned;
    }

    /// <summary>
    /// The canonical text of the options: keys sorted ordinal, both halves percent-encoded, joined
    /// with <c>&amp;</c>.
    ///
    /// Sorted because the whole string is the cache key (<c>SecretVaultService.CacheKey</c>), and two
    /// spellings of one selection would be two cache entries and two vault reads. Percent-encoded
    /// because a value is operator-supplied text that may contain <c>=</c> or <c>&amp;</c>.
    /// </summary>
    private static string EncodeOptions(IReadOnlyDictionary<string, string> options) =>
        string.Join('&', options
            .OrderBy(o => o.Key, StringComparer.Ordinal)
            .Select(o => Uri.EscapeDataString(o.Key) + "=" + Uri.EscapeDataString(o.Value)));

    private static bool TryDecodeOptions(string encoded,
        [NotNullWhen(true)] out Dictionary<string, string>? options)
    {
        options = new Dictionary<string, string>(StringComparer.Ordinal);

        if (encoded.Length == 0) return true;

        foreach (var pair in encoded.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            if (separator <= 0) { options = null; return false; }

            string key, value;

            try
            {
                key = Uri.UnescapeDataString(pair[..separator]);
                value = Uri.UnescapeDataString(pair[(separator + 1)..]);
            }
            catch (UriFormatException)
            {
                options = null;
                return false;
            }

            if (!OptionKeyPattern.IsMatch(key) || value.Length == 0) { options = null; return false; }

            // A repeated key has no meaning and no obvious winner, so it is a malformed reference
            // rather than a last-one-wins.
            if (!options.TryAdd(key, value)) { options = null; return false; }
        }

        return true;
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
