using System;
using System.IO;
using Xunit;

namespace GUIClient.Tests.Views;

/// <summary>
/// Guards the governance admin grids against the binding that showed a raw entity id.
///
/// The appetite grid's scope column was bound at <c>EntityId</c>, so it rendered "3" for an
/// entity-scoped appetite and nothing at all for the organization-wide row — whose <c>EntityId</c> is
/// null by definition. The "risks above appetite" grid had the same shape one step later: it bound
/// <c>EntityName</c>, which the server leaves null for the global bucket and for any entity with no
/// name property.
///
/// Both now bind a per-row display model that resolves the name and the localized "Global". This test
/// scans the source text, as the other tests in this folder do — GUIClient.Tests deliberately does not
/// reference GUIClient, because that would drag all of Avalonia into a headless run.
/// </summary>
public class GovernanceAdminGridBindingsTests
{
    private static string View() => File.ReadAllText(Path.Combine(
        GuiClientSourceRoot(), "Views/Admin/GovernanceAdminView.axaml"));

    [Fact]
    public void NoGridColumnOnTheGovernanceScreenBindsARawEntityIdOrName()
    {
        var view = View();

        Assert.DoesNotContain("Binding=\"{Binding EntityId}\"", view);
        Assert.DoesNotContain("Binding=\"{Binding EntityName}\"", view);
    }

    [Fact]
    public void TheAppetiteAndBreachGridsBindTheResolvedScopeLabel()
    {
        var view = View();

        // Two columns, one per grid. Counting them means a grid added later cannot quietly reintroduce
        // the id binding while this assertion still passes on the other one.
        var occurrences = 0;
        for (var at = view.IndexOf("Binding=\"{Binding EntityDisplay}\"", StringComparison.Ordinal);
             at >= 0;
             at = view.IndexOf("Binding=\"{Binding EntityDisplay}\"", at + 1, StringComparison.Ordinal))
            occurrences++;

        Assert.Equal(2, occurrences);
    }

    [Fact]
    public void TheRowModelsResolveTheScopeThroughEntityScopeLabel()
    {
        var rows = File.ReadAllText(Path.Combine(
            GuiClientSourceRoot(), "ViewModels/Admin/GovernanceAdminRows.cs"));

        // The label rule lives in one place; a row model that formatted its own scope would be free to
        // drift back to a bare id.
        Assert.Contains("EntityScopeLabel.Describe(appetite.EntityId, appetite.Entity?.DisplayName", rows);
        Assert.Contains("EntityScopeLabel.Describe(breach.EntityId, breach.EntityName", rows);
    }

    private static string GuiClientSourceRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory != null;
             directory = directory.Parent)
        {
            var project = Path.Combine(directory.FullName, "GUIClient", "GUIClient.csproj");

            if (File.Exists(project))
                return Path.GetDirectoryName(project)!;
        }

        throw new InvalidOperationException(
            $"Could not find GUIClient/GUIClient.csproj walking up from {AppContext.BaseDirectory}.");
    }
}
