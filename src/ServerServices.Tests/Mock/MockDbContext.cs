using System.Collections.Generic;
using System.Linq;
using DAL.Context;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace ServerServices.Tests.Mock;

public class MockDbContext
{

    public static AuditableContext Create()
    {
        var comments = new List<Comment>
        {
            new Comment { Id = 1, Type = "FixRequest", Text = "T1", FixRequestId = 1},
            new Comment { Id = 2, Type = "FixRequest", Text = "T2", FixRequestId = 2},
            new Comment { Id = 3, Type = "Vulnerability", Text = "V1", VulnerabilityId = 1},
            new Comment { Id = 4, Type = "Risk" , Text = "R1", RiskId = 1}
        };
        
       
        var context = Substitute.For<AuditableContextWrapper>();

        var dbset = MockDbSetCreator<Comment>.CreateDbSet(comments);
        
        context.Comments.Returns(dbset);
        
        return context;
    }
    
}