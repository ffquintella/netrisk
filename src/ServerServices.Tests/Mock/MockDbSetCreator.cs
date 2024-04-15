using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DAL.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using NSubstitute;

namespace ServerServices.Tests.Mock;

public static class MockDbSetCreator<T> where T : class
{
    public static DbSet<T> CreateDbSet<T>(List<T> sourceList) where T : class
    {

        
        var queryable = sourceList.AsQueryable();
        

        var dbSet = Substitute.For<DbSet<T>, IQueryable<T>, IAsyncEnumerable<T>>();

        //((IQueryable<T>)dbSet).Provider.Returns(queryable.Provider);
        ((IQueryable<T>)dbSet).Provider.Returns(new TestAsyncQueryProvider<T>(queryable.Provider));
        ((IQueryable<T>)dbSet).Expression.Returns(queryable.Expression);
        ((IQueryable<T>)dbSet).ElementType.Returns(queryable.ElementType);
        ((IQueryable<T>)dbSet).GetEnumerator().Returns(queryable.GetEnumerator());

        ((IAsyncEnumerable<T>)dbSet).GetAsyncEnumerator(Arg.Any<CancellationToken>())
            .Returns(new AsyncEnumerator<T>(sourceList.GetEnumerator()));
        
        
        // Mock the Add operation
        //dbSet.Add(Arg.Do<T>(item => sourceList.Add(item)));
        
        // Mock the Add operation
        dbSet.When(d => d.Add(Arg.Any<T>())).Do(callInfo =>
        {
            var item = callInfo.Arg<T>();
            sourceList.Add(item);
        });
        

        
        return dbSet;
    }
}