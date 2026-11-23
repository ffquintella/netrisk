using Xunit;

namespace GUIClient.UiTests.Infrastructure;

/// <summary>
/// The harness tolerates "the child still sees the parent's view-model" only while the element has not
/// been given its own data context yet, and it can only do that if it can read the two types out of
/// Avalonia's message. Avalonia 12.2.0 reworded that message; without this, 53 Risk tests failed on a
/// view that was in fact correct.
/// </summary>
public class UiDiagnosticsCastErrorTest
{
    [UiTheory]
    [InlineData("Unable to cast object of type 'GUIClient.ViewModels.RiskViewModel' to type 'GUIClient.ViewModels.AttackTechniquesPanelViewModel'.")]
    [InlineData("Could not convert DataContext of type 'GUIClient.ViewModels.RiskViewModel' to 'GUIClient.ViewModels.AttackTechniquesPanelViewModel'.")]
    public void TestBothAvaloniaWordingsYieldTheParentAndChildTypes(string message)
    {
        Assert.True(UiDiagnostics.TryParseCastError(message, out var from, out var to));
        Assert.Equal("GUIClient.ViewModels.RiskViewModel", from);
        Assert.Equal("GUIClient.ViewModels.AttackTechniquesPanelViewModel", to);
    }

    [UiTheory]
    [InlineData("Value is null.")]
    [InlineData("Could not find a matching property accessor for 'Foo' on 'Bar'")]
    public void TestOtherMessagesAreNotTakenForACastError(string message)
    {
        Assert.False(UiDiagnostics.TryParseCastError(message, out _, out _));
    }
}
