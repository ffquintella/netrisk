using System.Collections.Generic;
using DAL.Context;
using DAL.Entities;

namespace ServerServices.Tests.Mock;

public class MockDbContext
{

    public static AuditableContextWrapper Create()
    {
        var mockContext = new Moq.Mock<AuditableContextWrapper>();
        
        var comments = new List<Comment>
        {
            new Comment { Id = 1, Type = "FixRequest", Text = "T1", FixRequestId = 1},
            new Comment { Id = 2, Type = "FixRequest", Text = "T2", FixRequestId = 2},
            new Comment { Id = 3, Type = "Vulnerability", Text = "V1", VulnerabilityId = 1},
            new Comment { Id = 4, Type = "Risk" , Text = "R1", RiskId = 1}
        };
        
        var commentsDbSet = DbSetMocks.GetDbSetMock(comments);

        mockContext.Setup(m => m.Comments).Returns(commentsDbSet.Object);

        // Setup other DbSets and DbContext methods as needed

        return mockContext.Object;
    }
    
}