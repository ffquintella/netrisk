using System.Collections.Generic;
using System.Linq;
using System.Text;
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
        
        var users = GetUsersDbSet();
        context.Users.Returns(users);
        
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
    
    private static DbSet<User> GetUsersDbSet()
    {
        var users = new List<User>
        {
            new User { Value = 1, 
                Username = "u1"u8.ToArray(), Password = "pass1"u8.ToArray(), 
                Name = "u1", Admin = true, Enabled = true, Lockout = 0
            },
            new User { Value = 2, 
                Username = "u2"u8.ToArray(), Password = "pass1"u8.ToArray(), 
                Name = "u2", Admin = true, Enabled = false, Lockout = 0
            },
            new User { Value = 3, 
                Username = "user3"u8.ToArray(), Password = "pass1"u8.ToArray(), 
                Name = "u3", Admin = false, Enabled = true, Lockout = 1
            }

        };
        var dbset = MockDbSetCreator<User>.CreateDbSet(users);
        return dbset;
    }
    
}