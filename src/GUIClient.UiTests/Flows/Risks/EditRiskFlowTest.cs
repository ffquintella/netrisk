using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;
using ClientServices.Interfaces;
using DAL.Entities;
using GUIClient.UiTests.Infrastructure;
using GUIClient.Views;
using NSubstitute;
using Xunit;
using static GUIClient.UiTests.Flows.Risks.RiskScreen;
using static GUIClient.UiTests.Flows.Risks.RiskServer;

namespace GUIClient.UiTests.Flows.Risks;

/// <summary>
/// Add and Edit on the risk screen open <see cref="EditRiskWindow"/>: the editor loads the reference lists (and, to
/// edit, the risk and its scoring), validates as the user fills it in, computes the score from likelihood × impact,
/// and saves through <see cref="IRisksService"/> — after which the register reloads and a new risk is selected with
/// the offer to plan its mitigation (IX-6).
/// </summary>
public class EditRiskFlowTest
{
    private const int UnitId = 60;
    private const string UnitName = "Diretoria Financeira";
    private const int CreatedId = 5;
    private const string NewSubject = "Dependência de fornecedor único de nuvem";

    [UiFact]
    public async Task TestAddingARiskSavesWhatWasTypedAndOffersToPlanItsMitigation()
    {
        var server = ArrangeEditor();
        Risk? created = null;
        server.Risks.CreateRisk(Arg.Any<Risk>()).Returns(call =>
        {
            // What the REST client does: post a copy and hand back the server's row, with its new id.
            var sent = call.Arg<Risk>();
            sent.Id = 0;
            created = new Risk
            {
                Id = CreatedId, Subject = sent.Subject, Status = sent.Status, Source = sent.Source,
                Category = sent.Category, Owner = sent.Owner, Manager = sent.Manager, Notes = sent.Notes,
                SubmissionDate = sent.SubmissionDate
            };
            server.Rows.Add(created);
            return created;
        });

        var screen = await OpenAsync(RiskUsers.Manager);
        UiInput.Click(screen.Bar("BtAddRisk"));
        var editor = await screen.WaitForDialogAsync<EditRiskWindow>();
        var save = editor.Find<Button>(b => b.Name == "BtSave");

        // The header says what the editor is doing, and an empty form cannot be saved.
        Assert.Contains(editor.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == Localized("Creation"));
        await UiHost.WaitUntilAsync(() => Field<ComboBox>(editor, Localized("Source") + ": ").ItemCount == 2,
            "the editor's reference lists to load");
        Assert.False(save.IsEffectivelyEnabled);
        Assert.Contains(Localized("RiskMustHaveASubjectMSG"), ToolTip.GetTip(save) as string);

        // The owner defaults to the signed-in user and the scales to their first level.
        Assert.Equal("Analista de Riscos", UiInput.TextOf(Field<ComboBox>(editor, Localized("Owner") + ":")));
        Assert.Equal("1,00", ScoreValue(editor));

        UiInput.Click(Field<TextBox>(editor, Localized("Subject") + ": "));
        UiInput.Type(editor, NewSubject);
        await UiInput.ChooseAsync(Field<ComboBox>(editor, Localized("Source") + ": "), "Autoavaliação");
        await UiInput.ChooseAsync(Field<ComboBox>(editor, Localized("Category") + ": "), "Operacional");
        await UiInput.ChooseAsync(Field<ComboBox>(editor, Localized("Manager") + ":"), "Gestora de Riscos");
        await UiInput.ChooseAsync(Field<ComboBox>(editor, Localized("Probability")), "Possível");
        await UiInput.ChooseAsync(Field<ComboBox>(editor, Localized("Impact")), "Moderado");
        Assert.Equal("9,00", ScoreValue(editor));
        Assert.False(save.IsEffectivelyEnabled);

        // The last required field: the entity the risk belongs to, picked from the suggestions.
        await PickEntityAsync(editor);
        await UiHost.WaitUntilAsync(() => save.IsEffectivelyEnabled, "Save to enable once every field is valid");

        UiInput.Click(save);

        await screen.WaitForDialogsToCloseAsync("the editor to close after saving");
        server.Risks.Received(1).CreateRisk(Arg.Is<Risk>(r =>
            r.Subject == NewSubject && r.Status == "New" && r.Source == 2 && r.Category == 1 &&
            r.Owner == RiskUsers.AnalystId && r.Manager == RiskUsers.ManagerId && r.SubmittedBy == RiskUsers.AnalystId));
        server.Risks.Received(1).CreateRiskScoring(Arg.Is<RiskScoring>(s =>
            s.Id == CreatedId && s.ClassicLikelihood == 3 && s.ClassicImpact == 3 && s.CalculatedRisk == 9));
        server.Risks.Received(1).AssociateEntityToRisk(CreatedId, UnitId);

        // IX-6: the register reloads with the new risk, selects it, and offers its next stage.
        var offer = await UiMessageBoxes.WaitForAsync("the offer to plan the new risk's mitigation");
        Assert.Contains(Localized("PlanMitigationNowMSG"), UiMessageBoxes.Text(offer));
        Assert.Contains(NewSubject, screen.Subjects);
        Assert.Equal(CreatedId, screen.ViewModel.SelectedRisk?.Id);
        Assert.Equal(NewSubject, (screen.List.SelectedItem as Risk)?.Subject);

        await UiMessageBoxes.AnswerAsync(offer, "No");
        Assert.Empty(screen.Window.OwnedWindows);

        UiHost.AssertClean();
    }

    [UiFact]
    public async Task TestEditingARiskLoadsItValidatesTheSubjectAndSavesTheChange()
    {
        var server = ArrangeEditor();
        server.Risks.GetEntityIdFromRisk(Leak).Returns(UnitId);

        var screen = await OpenAsync(RiskUsers.Manager);
        await screen.SelectAsync(LeakSubject);

        // The risk's entity is part of its detail.
        Assert.Equal(UnitName, screen.DetailValue(Localized("Entity") + ":"));

        UiInput.Click(screen.Bar("BtEditRisk"));
        var editor = await screen.WaitForDialogAsync<EditRiskWindow>();
        var save = editor.Find<Button>(b => b.Name == "BtSave");
        var subject = Field<TextBox>(editor, Localized("Subject") + ": ");

        // Loaded as saved: identification, choices, the scoring's levels and the computed score.
        await UiHost.WaitUntilAsync(() => save.IsEffectivelyEnabled, "the loaded risk to pass validation");
        Assert.Contains(editor.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == Localized("Edit"));
        Assert.Equal(LeakSubject, subject.Text);
        Assert.Equal("CTRL-001", Field<TextBox>(editor, Localized("CtrlNumber") + ": ").Text);
        Assert.Equal("Auditoria interna", UiInput.TextOf(Field<ComboBox>(editor, Localized("Source") + ": ")));
        Assert.Equal("Segurança da informação", UiInput.TextOf(Field<ComboBox>(editor, Localized("Category") + ": ")));
        Assert.Equal("Gestora de Riscos", UiInput.TextOf(Field<ComboBox>(editor, Localized("Manager") + ":")));
        Assert.Equal("Possível", UiInput.TextOf(Field<ComboBox>(editor, Localized("Probability"))));
        Assert.Equal("Menor", UiInput.TextOf(Field<ComboBox>(editor, Localized("Impact"))));
        Assert.Equal("6,00", ScoreValue(editor));
        Assert.Equal(UnitName + " (" + UnitId + ")", Field<AutoCompleteBox>(editor, Localized("Entity")).Text);

        // A risk needs a subject: clearing it disables Save and says why.
        UiInput.ClearByKeyboard(subject);
        await UiHost.WaitUntilAsync(() => !save.IsEffectivelyEnabled, "Save to disable without a subject");
        Assert.Contains(Localized("RiskMustHaveASubjectMSG"), ToolTip.GetTip(save) as string);

        UiInput.Type(editor, "Vazamento de dados de clientes do CRM");
        await UiHost.WaitUntilAsync(() => save.IsEffectivelyEnabled, "Save to enable with the new subject");

        // Ctrl+S commits, like Save (DialogWindowBase).
        UiInput.Press(editor, Key.S, PhysicalKey.S, RawInputModifiers.Control);

        await screen.WaitForDialogsToCloseAsync("the editor to close after saving");
        server.Risks.Received(1).SaveRisk(Arg.Is<Risk>(r =>
            r.Id == Leak && r.Subject == "Vazamento de dados de clientes do CRM" && r.Source == 1 && r.Category == 2));
        server.Risks.Received(1).SaveRiskScoring(Arg.Is<RiskScoring>(s =>
            s.Id == Leak && s.ClassicLikelihood == 3 && s.ClassicImpact == 2 && s.CalculatedRisk == 6));
        server.Risks.DidNotReceiveWithAnyArgs().CreateRisk(default!);
        await UiHost.WaitUntilAsync(() => screen.Subjects.Contains("Vazamento de dados de clientes do CRM"),
            "the reloaded register to show the new subject");

        // An edit is not a new stage: no next-step offer.
        Assert.Empty(UiMessageBoxes.Open());

        UiHost.AssertClean();
    }

    /// <summary>A subject another risk already has is refused, with a message in the session language.</summary>
    [UiFact]
    public async Task TestASubjectAnotherRiskHasCannotBeSaved()
    {
        var server = ArrangeEditor();
        server.Risks.RiskSubjectExists(FraudSubject).Returns(true);

        var screen = await OpenAsync(RiskUsers.Manager);
        UiInput.Click(screen.Bar("BtAddRisk"));
        var editor = await screen.WaitForDialogAsync<EditRiskWindow>();
        var save = editor.Find<Button>(b => b.Name == "BtSave");

        UiInput.Click(Field<TextBox>(editor, Localized("Subject") + ": "));
        UiInput.Type(editor, FraudSubject);

        await UiHost.WaitUntilAsync(() => (ToolTip.GetTip(save) as string)?.Contains(Localized("RiskSubjectExistsMSG")) == true,
            "the duplicate-subject message");
        Assert.False(save.IsEffectivelyEnabled);

        // Esc leaves without saving anything (DialogWindowBase).
        UiInput.Press(editor, Key.Escape, PhysicalKey.Escape);
        await screen.WaitForDialogsToCloseAsync("the editor to close on Esc");
        server.Risks.DidNotReceiveWithAnyArgs().CreateRisk(default!);

        UiHost.AssertClean();
    }

    /// <summary>Cancel closes the editor and the register is not re-read.</summary>
    [UiFact]
    public async Task TestCancelLeavesTheRegisterAsItWas()
    {
        var server = ArrangeEditor();
        var screen = await OpenAsync(RiskUsers.Manager);
        await screen.SelectAsync(ErpSubject);

        UiInput.Click(screen.Bar("BtEditRisk"));
        var editor = await screen.WaitForDialogAsync<EditRiskWindow>();
        UiInput.Click(editor.Find<Button>(b => b.Name == "BtCancel"));

        await screen.WaitForDialogsToCloseAsync("the editor to close on Cancel");
        server.Risks.DidNotReceiveWithAnyArgs().SaveRisk(default!);
        await server.Risks.Received(1).GetAllRisksAsync(false);

        UiHost.AssertClean();
    }

    /// <summary>Edit with no risk selected explains itself instead of opening an empty editor.</summary>
    [UiFact]
    public async Task TestEditWithoutASelectionAsksForOne()
    {
        ArrangeEditor();
        var screen = await OpenAsync(RiskUsers.Manager);

        UiInput.Click(screen.Bar("BtEditRisk"));

        var box = await UiMessageBoxes.WaitForAsync("the \"select a risk\" message");
        Assert.Contains(Localized("SelectRiskMSG"), UiMessageBoxes.Text(box));
        Assert.Empty(screen.Window.OwnedWindows);
        await UiMessageBoxes.AnswerAsync(box, "OK");

        UiHost.AssertClean();
    }

    private static RiskServer ArrangeEditor()
    {
        var server = RiskServer.Arrange();
        var unit = new Entity
        {
            Id = UnitId, DefinitionName = "unit", DefinitionVersion = "1", Status = "active",
            EntitiesProperties = [new EntitiesProperty { Type = "name", Name = "name", Value = UnitName, OldValue = "" }]
        };
        var entities = UiServices.Current.Double<IEntitiesService>();
        entities.GetAllAsync(Arg.Any<string?>(), Arg.Any<bool>()).Returns(_ => Task.FromResult(new List<Entity> { unit }));
        entities.GetEntity(UnitId, Arg.Any<bool>()).Returns(unit);
        return server;
    }

    /// <summary>The input next to <paramref name="label"/>: the first <typeparamref name="T"/> after it in its panel.</summary>
    private static T Field<T>(Window editor, string label) where T : Control
    {
        var labelBlock = editor.Find<TextBlock>(t => t.Text == label);
        var panel = (Panel)labelBlock.Parent!;
        return panel.Children.SkipWhile(c => !ReferenceEquals(c, labelBlock)).Skip(1).OfType<T>().FirstOrDefault()
               ?? throw new InvalidOperationException($"No {typeof(T).Name} follows \"{label}\".");
    }

    /// <summary>The computed score shown under "Value".</summary>
    private static string? ScoreValue(Window editor) => Field<TextBlock>(editor, Localized("Value")).Text;

    /// <summary>Types the start of the unit's name in the entity box and clicks the suggestion.</summary>
    private static async Task PickEntityAsync(Window editor)
    {
        var box = Field<AutoCompleteBox>(editor, Localized("Entity"));
        UiInput.Click(box);
        UiInput.Type(editor, "Dir");
        await UiHost.WaitUntilAsync(() => box.IsDropDownOpen, "the entity suggestions to open");

        var popup = box.GetVisualDescendants().OfType<Popup>().First();
        var suggestion = ((Visual)popup.Child!).GetVisualDescendants().OfType<ListBoxItem>()
            .First(i => UiInput.TextOf(i).Contains(UnitName));
        UiInput.Click(suggestion);
        await UiHost.WaitUntilAsync(() => !box.IsDropDownOpen, "the suggestions to close on the choice");
    }
}
