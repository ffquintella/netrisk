namespace DAL.Enums;

/// <summary>
/// How a mapped value is normalised on its way between Jira and NetRisk (Track 4 milestone 4.6),
/// persisted in <c>jira_object_attribute_mappings.transform</c> and
/// <c>jira_field_mappings.transform</c>.
///
/// A small closed enum, deliberately, and for the same reason milestone 4.2's templates are
/// <c>{{Placeholder}}</c> substitution rather than a template language: the values are third-party
/// text crossing between two systems, and an expression evaluator in that position is a server-side
/// injection surface bought for no benefit. Nobody needs a loop to trim a hostname.
/// </summary>
public enum JiraAttributeTransform
{
    /// <summary>Take the value as it arrives.</summary>
    None = 0,

    Trim = 1,

    Upper = 2,

    Lower = 3,

    /// <summary>
    /// Read the value as a boolean, accepting what a CMDB actually contains: <c>true</c>, <c>yes</c>,
    /// <c>y</c>, <c>1</c>, <c>active</c>, <c>enabled</c>, <c>in use</c>, <c>in service</c>, <c>on</c>.
    /// Anything else is false. String-matching only "True" would read every "Active" as inactive.
    /// </summary>
    TruthyBoolean = 4,

    /// <summary>
    /// Take the first entry of a multi-valued attribute. Assets reference attributes are lists even
    /// when the customer treats them as single-valued, and a joined "Alice, Bob" in an owner column
    /// matches no person.
    /// </summary>
    FirstOfList = 5,

    /// <summary>Parse as a date/time and store UTC.</summary>
    DateTime = 6,

    /// <summary>Parse as an integer, clamping to the target field's range rather than throwing.</summary>
    Integer = 7
}
