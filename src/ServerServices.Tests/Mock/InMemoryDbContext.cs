using System;
using DAL.Context;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;

namespace ServerServices.Tests.Mock;

public class InMemoryDbContext
{
    
    [CanBeNull] private static string _guid;
    public static AuditableContext Create()
    {
        bool firstRun = false;
        if (_guid == null)
        {
            _guid = Guid.NewGuid().ToString();
            firstRun = true;
        }
       
        var options = new DbContextOptionsBuilder<NRDbContext>()
                .UseInMemoryDatabase(_guid)
                .Options;
    
        var context = new AuditableContext(options);

        context.Database.EnsureCreated();
        
        return context;

    }
    
}