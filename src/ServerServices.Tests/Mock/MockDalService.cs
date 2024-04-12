using System.Collections.Generic;
using System.Linq;
using DAL.Context;
using DAL.Entities;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ServerServices.Services;

namespace ServerServices.Tests.Mock;

public class MockDalService
{
    public static IDalService Create()
    {
        var mockDalService = new Moq.Mock<IDalService>();
        
        var mockContext = new Moq.Mock<AuditableContextWrapper>();
        
        var comments = new List<Comment>
        {
            new Comment { Id = 1, Type = "FixRequest", Text = "T1", FixRequestId = 1},
            new Comment { Id = 2, Type = "FixRequest", Text = "T2", FixRequestId = 2},
            new Comment { Id = 3, Type = "Vulnerability", Text = "V1", VulnerabilityId = 1},
            new Comment { Id = 4, Type = "Risk" , Text = "R1", RiskId = 1}
        };
        
        // Create a Queryable version of your list
        var queryable = comments.AsQueryable();

        // Create a Mock<DbSet<Comment>>
        var dbSetMock = new Moq.Mock<DbSet<Comment>>();

        // Setup the IQueryable methods
        dbSetMock.As<IQueryable<Comment>>().Setup(m => m.Provider).Returns(queryable.Provider);
        dbSetMock.As<IQueryable<Comment>>().Setup(m => m.Expression).Returns(queryable.Expression);
        dbSetMock.As<IQueryable<Comment>>().Setup(m => m.ElementType).Returns(queryable.ElementType);
        dbSetMock.As<IQueryable<Comment>>().Setup(m => m.GetEnumerator()).Returns(queryable.GetEnumerator());

        // Assign the Mock<DbSet<Comment>> to the Comments property
        
        mockContext.Setup(m => m.Comments).Returns(dbSetMock.Object);
  

        mockDalService.Setup(m => m.GetContext(false)).Returns(mockContext.Object);
        mockDalService.Setup(m => m.GetContext(true)).Returns(mockContext.Object);
        

        return mockDalService.Object;
    }
}