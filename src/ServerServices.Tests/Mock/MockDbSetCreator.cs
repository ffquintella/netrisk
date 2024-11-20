
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using DAL.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using NSubstitute;

namespace ServerServices.Tests.Mock;

public static class MockDbSetCreator
{
    public static DbSet<T> CreateDbSet<T>(List<T> sourceList) where T : class
    {
        
        var queryable = sourceList.AsQueryable();

        var dbSet = Substitute.For<DbSet<T>, IQueryable<T>, IAsyncEnumerable<T>>();

        //((IQueryable<T>)dbSet).Provider.Returns(queryable.Provider);
        ((IQueryable<T>)dbSet).Provider.Returns(new TestAsyncQueryProvider<T>(queryable.Provider));
        ((IQueryable<T>)dbSet).Expression.Returns(queryable.Expression);
        ((IQueryable<T>)dbSet).ElementType.Returns(queryable.ElementType);
        using var enumerator = ((IQueryable<T>)dbSet).GetEnumerator();
        using var returnThis = queryable.GetEnumerator();
        enumerator.Returns(returnThis);

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
        
        
        //((IQueryable<T>)dbSet).FirstOrDefaultAsync(Arg.Any<Expression<Func<T, bool>>>(), Arg.Any<CancellationToken>()).Returns(queryable.FirstOrDefaultAsync());
        

        
        return dbSet;
    }
}