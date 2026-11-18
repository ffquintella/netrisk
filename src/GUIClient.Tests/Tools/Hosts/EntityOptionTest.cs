using System.Collections.Generic;
using System.Linq;
using DAL.Entities;
using GUIClient.Tools.Hosts;
using JetBrains.Annotations;
using Xunit;

namespace GUIClient.Tests.Tools.Hosts;

/// <summary>The host dialog's entity picker (M58, T282, S39 D-20).</summary>
[TestSubject(typeof(EntityOption))]
public class EntityOptionTest
{
    private static Entity NewEntity(int id, string definition, string? name) => new()
    {
        Id = id,
        DefinitionName = definition,
        EntitiesProperties = name == null
            ? new List<EntitiesProperty>()
            : new List<EntitiesProperty> { new() { Type = "name", Value = name, Name = "name", OldValue = "" } }
    };

    [Fact]
    public void UnassignedComesFirstAndTheRestAreOrderedByName()
    {
        var options = EntityOption.Build(
            [NewEntity(7, "application", "Portal"), NewEntity(3, "organization", "Acme")], "Unassigned");

        Assert.Equal(new int?[] { null, 3, 7 }, options.Select(o => o.Id));
        Assert.Equal("Unassigned", options[0].Label);
        Assert.Equal("Acme (3)", options[1].Label);
    }

    [Fact]
    public void PeopleAreNotOfferedAsAHostsEntity()
    {
        var options = EntityOption.Build(
            [NewEntity(1, "person", "Alice"), NewEntity(2, "organizationUnit", "Finance")], "Unassigned");

        Assert.DoesNotContain(options, o => o.Id == 1);
        Assert.Contains(options, o => o.Id == 2);
    }

    [Fact]
    public void AnEntityWithoutANameIsLabelledByItsDefinition()
    {
        var options = EntityOption.Build([NewEntity(9, "application", null)], "Unassigned");

        Assert.Equal("application (9)", options[1].Label);
    }

    [Fact]
    public void TheDialogBindsThePickerAndSavesTheChoiceAsTheHostsEntity()
    {
        var dialog = HostsTestFiles.Read("Views/EditHostDialog.axaml");
        Assert.Contains(@"SelectedItem=""{Binding SelectedEntity}""", dialog);
        Assert.Contains(@"ItemsSource=""{Binding EntityOptions}""", dialog);

        var viewModel = HostsTestFiles.Read("ViewModels/EditHostDialogViewModel.cs");
        Assert.Contains("Host.EntityId = SelectedEntity != null ? SelectedEntity.Id : _hostEntityId;", viewModel);
    }
}
