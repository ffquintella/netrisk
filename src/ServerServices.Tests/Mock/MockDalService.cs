using System.Collections.Generic;
using System.Linq;
using DAL.Context;
using DAL.Entities;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using ServerServices.Services;

namespace ServerServices.Tests.Mock;

public class MockDalService
{
    public static IDalService Create()
    {
        var context = MockDbContext.Create();
        
        //using var context = InMemoryDbContext.Create();
        //MockDBContextPopulate.Populate(context);
        
        var dalService = Substitute.For<IDalService>();
        dalService.GetContext().Returns(context);
        return dalService;
    }
    
    /*public static AuditableContext CreateContext()
    {
        
        return MockDbContext.Create();
        //return InMemoryDbContext.Create();
    }*/
}