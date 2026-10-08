using System;
using DAL.Entities;
using GUIClient.Tools.Track9;
using Xunit;
namespace GUIClient.Tests.Track9;
public class Track9TaskEvidenceTest
{
    [Fact]
    public void UpdatingEvidencePreservesTaskIdentityOwnerDueDateAndStatus()
    {
        var task = new MitigationTask { Id = 7, MitigationId = 12, Title = "Restore", OwnerId = 4,
            DueDate = new DateTime(2026, 12, 1), Description = "Recover service" };
        var request = Track9TaskEvidence.Update(task, "Restore within RTO", "Test run 42");
        Assert.Equal(7, request.Id);
        Assert.Equal(12, request.MitigationId);
        Assert.Equal("Restore", request.Title);
        Assert.Equal(4, request.OwnerId);
        Assert.Equal(task.DueDate, request.DueDate);
        Assert.Equal(task.Status, request.Status);
        Assert.Equal("Restore within RTO", request.AcceptanceCriterion);
        Assert.Equal("Test run 42", request.CompletionEvidence);
    }
}
