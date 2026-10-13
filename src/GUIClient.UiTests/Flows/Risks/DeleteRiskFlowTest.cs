using System.Linq;
using System.Threading.Tasks;
using DAL.Entities;
using GUIClient.UiTests.Infrastructure;
using NSubstitute;
using Xunit;
using static GUIClient.UiTests.Flows.Risks.RiskScreen;
using static GUIClient.UiTests.Flows.Risks.RiskServer;

namespace GUIClient.UiTests.Flows.Risks;

/// <summary>
/// Delete on the risk screen goes through the single confirmation pattern (IX-4, <c>ConfirmationDialog</c>): Yes/No,
/// the risk's subject in the question and the consequence spelled out.
/// </summary>
public class DeleteRiskFlowTest
{
    [UiFact]
    public async Task TestDeleteAsksNamingTheRiskAndOnlyYesDeletesIt()
    {
        var server = Arrange();
        server.Risks.When(r => r.DeleteRisk(Arg.Any<Risk>()))
            .Do(call => server.Rows.RemoveAll(r => r.Id == call.Arg<Risk>().Id));

        var screen = await OpenAsync(RiskUsers.Manager);
        await screen.SelectAsync(LeakSubject);

        UiInput.Click(screen.Bar("BtDeleteRisk"));
        var question = await UiMessageBoxes.WaitForAsync("the delete confirmation");
        var text = UiMessageBoxes.Text(question);
        Assert.Contains(string.Format(Localized("ConfirmDeleteMSG"), LeakSubject), text);
        Assert.Contains(Localized("RiskDeleteConfirmationMSG"), text);

        await UiMessageBoxes.AnswerAsync(question, "No");
        server.Risks.DidNotReceiveWithAnyArgs().DeleteRisk(default!);
        server.Risks.DidNotReceiveWithAnyArgs().DeleteRiskScoring(default);
        Assert.Contains(LeakSubject, screen.Subjects);

        UiInput.Click(screen.Bar("BtDeleteRisk"));
        await UiMessageBoxes.AnswerAsync(await UiMessageBoxes.WaitForAsync("the delete confirmation, again"), "Yes");

        server.Risks.Received(1).DeleteRiskScoring(Leak);
        server.Risks.Received(1).DeleteRisk(Arg.Is<Risk>(r => r.Id == Leak));
        await UiHost.WaitUntilAsync(() => !screen.Subjects.Contains(LeakSubject),
            "the deleted risk to leave the re-read register");
        Assert.Equal([ErpSubject, FraudSubject], screen.Subjects);

        UiHost.AssertClean();
    }

    [UiFact]
    public async Task TestDeleteWithoutASelectionAsksForOne()
    {
        var server = Arrange();
        var screen = await OpenAsync(RiskUsers.Manager);

        UiInput.Click(screen.Bar("BtDeleteRisk"));

        var box = await UiMessageBoxes.WaitForAsync("the \"select a risk\" message");
        Assert.Contains(Localized("SelectRiskDeleteMSG"), UiMessageBoxes.Text(box));
        await UiMessageBoxes.AnswerAsync(box, "OK");
        server.Risks.DidNotReceiveWithAnyArgs().DeleteRisk(default!);

        UiHost.AssertClean();
    }
}
