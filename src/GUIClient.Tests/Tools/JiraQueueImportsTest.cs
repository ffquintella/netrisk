using GUIClient.Tools;
using JetBrains.Annotations;
using Xunit;

namespace GUIClient.Tests.Tools;

/// <summary>The queue selection the Service Management Save sends.</summary>
[TestSubject(typeof(JiraQueueImports))]
public class JiraQueueImportsTest
{
    [Fact]
    public void AnUnloadedGridSendsNullSoTheStoredSelectionIsKept()
    {
        // Regression: the empty initial grid was projected to an empty list, which deleted every import.
        Assert.Null(JiraQueueImports.ForSave(queuesLoaded: false, serviceDeskId: 3, []));
    }

    [Fact]
    public void ALoadedGridSendsOnlyTheTickedQueues()
    {
        var sent = JiraQueueImports.ForSave(true, 3,
        [
            new QueueRow(10, "Open", Import: true, MaxRequests: 50),
            new QueueRow(11, "Escalated", Import: false, MaxRequests: 500)
        ]);

        var queue = Assert.Single(sent!);
        Assert.Equal(10, queue.QueueId);
        Assert.Equal("Open", queue.QueueName);
        Assert.Equal(3, queue.ServiceDeskId);
        Assert.Equal(50, queue.MaxRequests);
        Assert.True(queue.Enabled);
    }

    [Fact]
    public void ALoadedGridWithNothingTickedSendsAnEmptyListToClearTheSelection()
    {
        var sent = JiraQueueImports.ForSave(true, 3, [new QueueRow(10, "Open", false, 500)]);

        Assert.NotNull(sent);
        Assert.Empty(sent);
    }
}
