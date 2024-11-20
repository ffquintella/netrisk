
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using DAL.Context;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.ChangeTracking.Internal;
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
        
        dbSet.Add(Arg.Any<T>()).Returns(callInfo =>
        {
            var item = callInfo.Arg<T>();
            sourceList.Add(item);

            // Create a real DbContext instance
            var options = new DbContextOptionsBuilder<NRDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            using var context = new NRDbContext(options);

            // Attach the item to the context to get a real EntityEntry
            var entityEntry = context.Entry(item);
            return entityEntry;
        });
        
        
        dbSet.When(d => d.AddAsync(Arg.Any<T>(), Arg.Any<CancellationToken>())).Do(callInfo =>
        {
            var item = callInfo.Arg<T>();
            sourceList.Add(item);
        });

        dbSet.AddAsync(Arg.Any<T>(), Arg.Any<CancellationToken>()).Returns(callInfo =>
        {
            var item = callInfo.Arg<T>();
            sourceList.Add(item);

            var options = new DbContextOptionsBuilder<NRDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            using var context = new NRDbContext(options);

            var entityEntry = context.Entry(item);
            return new ValueTask<EntityEntry<T>>(entityEntry);
        });
        
        //((IQueryable<T>)dbSet).FirstOrDefaultAsync(Arg.Any<Expression<Func<T, bool>>>(), Arg.Any<CancellationToken>()).Returns(queryable.FirstOrDefaultAsync());
        

        
        return dbSet;
    }
}