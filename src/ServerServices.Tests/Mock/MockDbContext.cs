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
       
        var context = Substitute.For<AuditableContextWrapper>();

        var comments = GetCommentsDbSet();
        context.Comments.Returns(comments);
        var clients = GetClientRegistrationsDbSet();
        context.ClientRegistrations.Returns(clients);
        
        return context;
    }

    private static DbSet<Comment> GetCommentsDbSet()
    {
        var comments = new List<Comment>
        {
            new Comment { Id = 1, Type = "FixRequest", Text = "T1", FixRequestId = 1},
            new Comment { Id = 2, Type = "FixRequest", Text = "T2", FixRequestId = 2},
            new Comment { Id = 3, Type = "Vulnerability", Text = "V1", VulnerabilityId = 1},
            new Comment { Id = 4, Type = "Risk" , Text = "R1", RiskId = 1}
        };

        var dbset = MockDbSetCreator<Comment>.CreateDbSet(comments);
        return dbset;
    }

    private static DbSet<ClientRegistration> GetClientRegistrationsDbSet()
    {
        var registrations = new List<ClientRegistration>
        {
            new ClientRegistration { Id = 1, Name = "N1", Status = "pending", ExternalId = "id1"},
            new ClientRegistration { Id = 2, Name = "N2", Status = "approved", ExternalId = "id2"},
            new ClientRegistration { Id = 3, Name = "N3", Status = "pending", ExternalId = "id3"},
            new ClientRegistration { Id = 4, Name = "N4", Status = "pending", ExternalId = "id4"}
        };
        var dbset = MockDbSetCreator<ClientRegistration>.CreateDbSet(registrations);
        return dbset;
    }
    
}