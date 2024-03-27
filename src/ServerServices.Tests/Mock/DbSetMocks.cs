using System.Collections.Generic;
using System.Linq;
using System.Text;
using DAL.Context;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace ServerServices.Tests.Mock;

public static class DbSetMocks
{
    public static Mock<DbSet<T>> GetDbSetMock<T>(List<T> data) where T : class
    {
        var queryable = data.AsQueryable();
        var dbSetMock = new Mock<DbSet<T>>();
        dbSetMock.As<IQueryable<T>>().Setup(m => m.Provider).Returns(queryable.Provider);
        dbSetMock.As<IQueryable<T>>().Setup(m => m.Expression).Returns(queryable.Expression);
        dbSetMock.As<IQueryable<T>>().Setup(m => m.ElementType).Returns(queryable.ElementType);
        dbSetMock.As<IQueryable<T>>().Setup(m => m.GetEnumerator()).Returns(queryable.GetEnumerator());
        dbSetMock.Setup(m => m.Add(It.IsAny<T>())).Callback<T>(data.Add);
        //dbSetMock.Setup(m => m.Remove(It.IsAny<T>())).Callback<T>(data.Remove);
        return dbSetMock;
    }
    
    public static Mock<AuditableContext> GetAuditableContextMock<T>(List<T> data) where T : class
    {
        var dbSetMock = GetDbSetMock(data);
        var contextMock = new Mock<AuditableContext>();
        contextMock.Setup(m => m.Set<T>()).Returns(dbSetMock.Object);
        return contextMock;
    }
    
    public static Mock<AuditableContext> GetUsersDBContext()
    {
        var users = new List<User>
        {
            new()
            {
                Username = Encoding.UTF8.GetBytes("user1"),
                Value = 1
            }
        };

        return GetAuditableContextMock(users);
    }
    
}