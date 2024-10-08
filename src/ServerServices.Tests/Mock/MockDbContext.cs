﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DAL.Context;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Model;
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
        
        var hosts = GetHostsDbSet();
        context.Hosts.Returns(hosts);
        
        var fixRequests = GetFixRequestsDbSet();
        context.FixRequests.Returns(fixRequests);
        
        return context;
    }

    private static DbSet<Comment> GetCommentsDbSet()
    {
        var comments = new List<Comment>
        {
            new Comment { Id = 1, Type = "FixRequest", Text = "T1", FixRequestId = 1, UserId = 1},
            new Comment { Id = 2, Type = "FixRequest", Text = "T2", FixRequestId = 2, UserId = 1},
            new Comment { Id = 3, Type = "Vulnerability", Text = "V1", VulnerabilityId = 1},
            new Comment { Id = 4, Type = "Risk" , Text = "R1", RiskId = 1}
        };

        var dbset = MockDbSetCreator.CreateDbSet(comments);
        return dbset;
    }
    
    private static DbSet<FixRequest> GetFixRequestsDbSet()
    {
        var fixRequests = new List<FixRequest>
        {
            new FixRequest() { Id = 1, Comments = new List<Comment>(), CreationDate = DateTime.Now, FixTeamId = 1, Identifier = "id1", IsTeamFix = true, LastInteraction = DateTime.Now, RequestingUserId = 1, VulnerabilityId = 1},
            new FixRequest() { Id = 2, Comments = new List<Comment>(), CreationDate = DateTime.Now, FixTeamId = 1, Identifier = "id2", IsTeamFix = true, LastInteraction = DateTime.Now, RequestingUserId = 1, VulnerabilityId = 1},
        };

        var dbset = MockDbSetCreator.CreateDbSet(fixRequests);
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
        var dbset = MockDbSetCreator.CreateDbSet(registrations);
        return dbset;
    }
    
    private static DbSet<Host> GetHostsDbSet()
    {
        var hosts = new List<Host>
        {
            new Host { Id = 1, Ip = "127.0.0.1", HostName = "H1", Status = (int)IntStatus.Active, 
                Os = "windows", Fqdn = "h1.dm.com", RegistrationDate = new DateTime(2020,1,1)},
            new Host { Id = 2, Ip = "127.0.0.2", HostName = "H2", Status = (int)IntStatus.Active, 
                Os = "windows", Fqdn = "h1.dm.com", RegistrationDate = new DateTime(2020,1,1)},
            new Host { Id = 3, Ip = "127.0.0.3", HostName = "H3", Status = (int)IntStatus.Active, 
                Os = "windows", Fqdn = "h1.dm.com", RegistrationDate = new DateTime(2020,1,1)},
            new Host { Id = 4, Ip = "127.0.0.4", HostName = "H4", Status = (int)IntStatus.Active, 
                Os = "linux", Fqdn = "h1.dm.com", RegistrationDate = new DateTime(2021,1,1)},
            new Host { Id = 5, Ip = "127.0.0.5", HostName = "H5", Status = (int)IntStatus.Active, 
                Os = "windows", Fqdn = "h1.dm.com", RegistrationDate = new DateTime(2021,1,1)},
            new Host { Id = 6, Ip = "127.0.0.6", HostName = "H6", Status = (int)IntStatus.Active, 
                Os = "windows", Fqdn = "h1.dm.com", RegistrationDate = new DateTime(2022,1,1)},
            new Host { Id = 7, Ip = "127.0.0.7", HostName = "H7", Status = (int)IntStatus.Active, 
                Os = "windows", Fqdn = "h1.dm.com", RegistrationDate = new DateTime(2022,1,1)},
            new Host { Id = 8, Ip = "127.0.0.8", HostName = "H8", Status = (int)IntStatus.Active, 
                Os = "linux", Fqdn = "h1.dm.com", RegistrationDate = new DateTime(2023,1,1)},
            new Host { Id = 9, Ip = "127.0.0.9", HostName = "H9", Status = (int)IntStatus.Active, 
                Os = "windows", Fqdn = "h1.dm.com", RegistrationDate = new DateTime(2023,1,1)},
            new Host { Id = 10, Ip = "127.0.0.10", HostName = "H10", Status = (int)IntStatus.Active, 
                Os = "windows", Fqdn = "h1.dm.com", RegistrationDate = new DateTime(2023,1,1)},
            new Host { Id = 11, Ip = "127.0.0.11", HostName = "H11", Status = (int)IntStatus.Active, 
                Os = "linux", Fqdn = "h1.dm.com", RegistrationDate = new DateTime(2023,1,1)},
            new Host { Id = 12, Ip = "127.0.0.12", HostName = "H12", Status = (int)IntStatus.Active, 
                Os = "windows", Fqdn = "h1.dm.com", RegistrationDate = new DateTime(2024,1,1)},
            new Host { Id = 13, Ip = "127.0.0.13", HostName = "H13", Status = (int)IntStatus.Active, 
                Os = "windows", Fqdn = "h1.dm.com", RegistrationDate = new DateTime(2024,1,1)},
            new Host { Id = 14, Ip = "127.0.0.14", HostName = "H14", Status = (int)IntStatus.Active, 
                Os = "windows", Fqdn = "h1.dm.com", RegistrationDate = new DateTime(2024,1,1)},
            new Host { Id = 15, Ip = "127.0.0.15", HostName = "H15", Status = (int)IntStatus.Active, 
                Os = "linux", Fqdn = "h1.dm.com", RegistrationDate = new DateTime(2024,1,1)},
        };
        var dbset = MockDbSetCreator.CreateDbSet(hosts);
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
        var dbset = MockDbSetCreator.CreateDbSet(users);
        return dbset;
    }
    
}