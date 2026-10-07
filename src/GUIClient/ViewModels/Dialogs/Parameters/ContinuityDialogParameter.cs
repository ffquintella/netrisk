using System.Collections.Generic;
using Model.Continuity;

namespace GUIClient.ViewModels.Dialogs.Parameters;

/// <summary>
/// The business process or IT service a Stage 9.3 dialog works on (S43 §7): the BIA editor, the
/// dependency picker and the restoration-test list share it.
/// </summary>
public class ContinuityDialogParameter : NavigationParameterBase
{
    public int EntityId { get; set; }

    public string EntityName { get; set; } = string.Empty;

    /// <summary>The current BIA, for the editor to start from; null when the node has none.</summary>
    public BusinessImpactAnalysisDto? Bia { get; set; }

    /// <summary>The providers already declared, which the dependency picker leaves out.</summary>
    public IReadOnlyCollection<int> ExistingProviderIds { get; set; } = [];

    /// <summary>Whether the caller may record and void tests (permission and global scope, from the server).</summary>
    public bool CanRecordTests { get; set; }
}
