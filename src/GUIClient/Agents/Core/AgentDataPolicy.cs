using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace GUIClient.Agents;

/// <summary>Why a value was left out of a result. Counted per call and reported in the envelope.</summary>
public enum AgentWithheldKind
{
    /// <summary>Personal contact data (e-mail, phone…) while <see cref="AgentDataPolicy.WithholdPersonalContactData"/> holds.</summary>
    Personal,

    /// <summary>A name or a value that reads like a credential.</summary>
    Secret,

    /// <summary>An entity property the reviewed schema does not declare safe.</summary>
    Undeclared,

    /// <summary>
    /// A whole item — or a reference to one — whose data class the organisation withholds from agents (T362,
    /// <see cref="AgentExclusionPolicy"/>), or whose class could not be established.
    /// </summary>
    Classified
}

/// <summary>What a tool left out of its answer. The dispatcher reports the counts in the envelope; never the values.</summary>
public sealed class AgentWithholding
{
    private int _personal;
    private int _secret;
    private int _undeclared;
    private int _classified;

    public int Personal => _personal;
    public int Secret => _secret;
    public int Undeclared => _undeclared;
    public int Classified => _classified;

    public void Add(AgentWithheldKind kind)
    {
        switch (kind)
        {
            case AgentWithheldKind.Personal: System.Threading.Interlocked.Increment(ref _personal); break;
            case AgentWithheldKind.Secret: System.Threading.Interlocked.Increment(ref _secret); break;
            case AgentWithheldKind.Classified: System.Threading.Interlocked.Increment(ref _classified); break;
            default: System.Threading.Interlocked.Increment(ref _undeclared); break;
        }
    }
}

/// <summary>
/// What item data may reach an agent beyond what the result types allow (T352 and the M62 security review). Entity
/// properties come from a configurable schema, so they are allowlisted by key and their values screened; personal contact
/// data is withheld everywhere until the S52 data-class exclusion and the DPO review exist.
/// </summary>
public static class AgentDataPolicy
{
    /// <summary>
    /// THE SWITCH (pending the DPO decision, S55 risk table): while true, e-mail addresses, phone numbers and similar
    /// personal contact identifiers are withheld from every agent result; names that identify an item are kept.
    /// </summary>
    public static bool WithholdPersonalContactData { get; } = true;

    /// <summary>
    /// Entity property keys reviewed as safe to return: every property of <c>API/EntitiesConfiguration.yaml</c> (2.7) that
    /// is neither contact data nor a credential. <c>Definition(…)</c> properties hold entity ids. Anything else is withheld.
    /// </summary>
    public static readonly IReadOnlySet<string> SafeEntityProperties = new HashSet<string>(StringComparer.Ordinal)
    {
        "name", "description", "isActive", "active", "criticality", "securityClassification", "mainOrganization",
        "technology", "organizationUnit", "applications", "technicalOwner", "strategicObjectives", "sensitive", "processes",
        "parentOrganizationUnit", "parentApplication", "objective", "numberOfEmployees", "members", "leader",
        "isMainOrganization", "internetFacing", "horizon", "environment", "data", "responsible"
    };

    /// <summary>
    /// The properties of <c>API/EntitiesConfiguration.yaml</c> (2.7) typed <c>Definition(…)</c>: their value is the id of
    /// another entity. T362 withholds one that points at an entity the classification policy withholds.
    /// </summary>
    public static readonly IReadOnlySet<string> EntityReferenceProperties = new HashSet<string>(StringComparer.Ordinal)
    {
        "applications", "data", "leader", "mainOrganization", "members", "organizationUnit", "parentApplication",
        "parentOrganizationUnit", "processes", "responsible", "securityClassification", "strategicObjectives",
        "technicalOwner"
    };

    // Contact identifiers by name: the schema's own (email, phoneNumber) and the usual others, in English and Portuguese.
    private static readonly string[] ContactFragments =
    {
        "email", "mail", "phone", "mobile", "cellular", "telefone", "celular", "fax", "address", "endereco", "cpf", "rg",
        "birth", "nascimento", "whatsapp", "contact", "contato"
    };

    // Values that read like a secret whatever the property is called.
    private static readonly Regex[] SecretValues =
    {
        new(@"vault:v[0-9]+:", RegexOptions.CultureInvariant),
        new(@"-----BEGIN [A-Z0-9 ]*(PRIVATE KEY|CERTIFICATE|ENCRYPTED|OPENSSH)", RegexOptions.CultureInvariant),
        new(@"\bBearer\s+[A-Za-z0-9\-._~+/]+=*", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase),
        new(@"\beyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}", RegexOptions.CultureInvariant),
        new(@"\b(password|passwd|pwd|pass|secret|token|api[_-]?key|senha|chave)\s*[:=]\s*\S+",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase),
        // A long unbroken run of base64/hex alphabet holding both letters and digits: a key, a token, a hash.
        new(@"(?<![A-Za-z0-9+/=_-])(?=[A-Za-z0-9+/=_-]*[0-9])(?=[A-Za-z0-9+/=_-]*[A-Za-z])[A-Za-z0-9+/=_-]{32,}(?![A-Za-z0-9+/=_-])",
            RegexOptions.CultureInvariant)
    };

    public static bool LooksLikeSecretValue(string? value) =>
        !string.IsNullOrEmpty(value) && SecretValues.Any(pattern => pattern.IsMatch(value));

    public static bool IsPersonalContactName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var words = AgentSecretNames.SplitWords(name).ToList();
        var joined = string.Concat(words);
        return ContactFragments.Any(f => f.Length <= 3 ? words.Contains(f) : joined.Contains(f, StringComparison.Ordinal));
    }

    /// <summary>Whether an entity property is returned; null when it is, otherwise why it is withheld.</summary>
    public static AgentWithheldKind? Screen(string? key, string? value)
    {
        if (AgentSecretNames.IsCredentialLike(key) || LooksLikeSecretValue(value)) return AgentWithheldKind.Secret;
        if (IsPersonalContactName(key))
            return WithholdPersonalContactData ? AgentWithheldKind.Personal : null;
        return key is not null && SafeEntityProperties.Contains(key) ? null : AgentWithheldKind.Undeclared;
    }
}
