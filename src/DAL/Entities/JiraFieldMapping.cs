using DAL.Enums;

namespace DAL.Entities;

/// <summary>
/// "Write NetRisk's <see cref="NetRiskField"/> into Jira's <see cref="JiraFieldId"/>"
/// (Track 4 milestone 4.6).
///
/// Milestone 4.2 could map severity onto the native priority field and nothing else, so a customer
/// whose Jira requires a "Security severity" custom field on every issue could not create one from
/// NetRisk at all. This is that mapping, and it is per connection because the custom field ids differ
/// per site — <c>customfield_10012</c> means nothing anywhere else.
/// </summary>
public class JiraFieldMapping
{
    public int Id { get; set; }

    public int ConnectionId { get; set; }

    public JiraFieldMappingDirection Direction { get; set; } = JiraFieldMappingDirection.Outbound;

    /// <summary>
    /// The NetRisk value, named with the same vocabulary as the title/description templates
    /// (<c>Severity</c>, <c>Cvss</c>, <c>Asset</c>, <c>Link</c>). Reusing the placeholder names means
    /// an operator learns one list, and the config screen can generate both pickers from it.
    /// </summary>
    public string NetRiskField { get; set; } = null!;

    /// <summary>Jira's field id — <c>priority</c>, <c>labels</c>, <c>customfield_10012</c>.</summary>
    public string JiraFieldId { get; set; } = null!;

    /// <summary>Cached label from <c>/rest/api/3/field</c>, so the grid is readable.</summary>
    public string? JiraFieldName { get; set; }

    /// <summary>
    /// Jira's schema type for the field (<c>string</c>, <c>option</c>, <c>array</c>, <c>number</c>).
    /// Kept because the JSON shape Jira demands depends on it: an option field wants
    /// <c>{"value":…}</c> and a string field wants a bare string, and posting the wrong one is a 400
    /// with a message about the field being unknown.
    /// </summary>
    public string? JiraFieldType { get; set; }

    public JiraAttributeTransform Transform { get; set; } = JiraAttributeTransform.None;

    /// <summary>
    /// Written instead of the NetRisk value when <see cref="NetRiskField"/> is empty — the "every
    /// issue from this connection carries team = Platform" case, which is otherwise a template hack.
    /// </summary>
    public string? ConstantValue { get; set; }

    public bool Enabled { get; set; } = true;

    public virtual IssueTrackerConnection? Connection { get; set; }
}
