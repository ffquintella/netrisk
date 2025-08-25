
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

        
        ((IQueryable<T>)dbSet).Provider.Returns(new TestAsyncQueryProvider<T>(queryable.Provider));
        ((IQueryable<T>)dbSet).Expression.Returns(queryable.Expression);
        ((IQueryable<T>)dbSet).ElementType.Returns(queryable.ElementType);
        ((IQueryable<T>)dbSet).GetEnumerator().Returns(queryable.GetEnumerator());
        /*using var enumerator = ((IQueryable<T>)dbSet).GetEnumerator();
        using var returnThis = queryable.GetEnumerator();
        enumerator.Returns(returnThis);*/

        ((IAsyncEnumerable<T>)dbSet).GetAsyncEnumerator(Arg.Any<CancellationToken>())
            .Returns(new AsyncEnumerator<T>(sourceList.GetEnumerator()));

        
        // Mock the Add operation (handled in Returns below)
        dbSet.When(d => d.Add(Arg.Any<T>())).Do(_ => { });
        
        dbSet.Add(Arg.Any<T>()).Returns(callInfo =>
        {
            var item = callInfo.Arg<T>();
            // Simulate identity key assignment if property "Id" exists and is default
            // Only simulate identity for specific types that rely on IsKeySet checks
            if (typeof(T).Name == nameof(DAL.Entities.Assessment))
            {
                var idProp = typeof(T).GetProperty("Id");
                if (idProp != null && idProp.PropertyType == typeof(int))
                {
                    var current = (int)(idProp.GetValue(item) ?? 0);
                    if (current == 0)
                    {
                        var next = 1;
                        if (sourceList.Count > 0)
                        {
                            var max = sourceList
                                .Select(x => (int)(idProp.GetValue(x) ?? 0))
                                .DefaultIfEmpty(0)
                                .Max();
                            next = max + 1;
                        }
                        idProp.SetValue(item, next);
                    }
                }
            }
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
        
        // Mock the AddAsync (handled in Returns below)
        dbSet.When(d => d.AddAsync(Arg.Any<T>(), Arg.Any<CancellationToken>())).Do(_ => { });

        dbSet.AddAsync(Arg.Any<T>(), Arg.Any<CancellationToken>()).Returns(callInfo =>
        {
            var item = callInfo.Arg<T>();
            if (typeof(T).Name == nameof(DAL.Entities.Assessment))
            {
                var idProp = typeof(T).GetProperty("Id");
                if (idProp != null && idProp.PropertyType == typeof(int))
                {
                    var current = (int)(idProp.GetValue(item) ?? 0);
                    if (current == 0)
                    {
                        var next = 1;
                        if (sourceList.Count > 0)
                        {
                            var max = sourceList
                                .Select(x => (int)(idProp.GetValue(x) ?? 0))
                                .DefaultIfEmpty(0)
                                .Max();
                            next = max + 1;
                        }
                        idProp.SetValue(item, next);
                    }
                }
            }
            sourceList.Add(item);

            var options = new DbContextOptionsBuilder<NRDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            using var context = new NRDbContext(options);

            var entityEntry = context.Entry(item);
            return new ValueTask<EntityEntry<T>>(entityEntry);
        });

        // Mock Find (by primary key "Id")
        dbSet.Find(Arg.Any<object[]>()).Returns(callInfo =>
        {
            var keys = callInfo.Arg<object[]>();
            if (keys is { Length: > 0 } && keys[0] is not null)
            {
                var idProp = typeof(T).GetProperty("Id") ?? typeof(T).GetProperty("Value");
                if (idProp != null) return sourceList.FirstOrDefault(x => Equals(idProp.GetValue(x), keys[0]));
            }
            return null;
        });

        // Mock Update: replace existing by Id or add if missing
        dbSet.Update(Arg.Any<T>()).Returns(callInfo =>
        {
            var item = callInfo.Arg<T>();
            var idProp = typeof(T).GetProperty("Id");
            if (idProp != null)
            {
                var idVal = idProp.GetValue(item);
                var idx = sourceList.FindIndex(x => Equals(idProp.GetValue(x), idVal));
                if (idx >= 0) sourceList[idx] = item; else sourceList.Add(item);
            }

            var options = new DbContextOptionsBuilder<NRDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            using var context = new NRDbContext(options);
            var entityEntry = context.Entry(item);
            return entityEntry;
        });
        
        // Mock the Remove operation
        dbSet.When(d => d.Remove(Arg.Any<T>())).Do(callInfo =>
        {
            var item = callInfo.Arg<T>();
            sourceList.Remove(item);
        });

        dbSet.Remove(Arg.Any<T>()).Returns(callInfo =>
        {
            var item = callInfo.Arg<T>();
            sourceList.Remove(item);

            var options = new DbContextOptionsBuilder<NRDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            using var context = new NRDbContext(options);

            var entityEntry = context.Entry(item);
            return entityEntry;
        });
        

  
        return dbSet;
    }
}
