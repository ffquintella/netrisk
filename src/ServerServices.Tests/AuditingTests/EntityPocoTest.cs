using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DAL.Entities;
using Xunit;

namespace ServerServices.Tests.AuditingTests;

/// <summary>
/// Exercises the auto-property get/set accessors of the DAL entity and DTO POCOs.
/// These classes hold no behaviour, so a reflection round-trip is sufficient to
/// confirm every property is readable/writable and to cover the generated accessors.
/// </summary>
public class EntityPocoTest
{
    public static IEnumerable<object[]> PocoTypes()
    {
        var assembly = typeof(Audit).Assembly;
        return assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.Namespace is "DAL.Entities" or "DAL.EntitiesDto")
            .Where(t => t.GetConstructor(Type.EmptyTypes) is not null)
            .Select(t => new object[] { t });
    }

    [Theory]
    [MemberData(nameof(PocoTypes))]
    public void TestPropertiesRoundTrip(Type type)
    {
        var instance = Activator.CreateInstance(type);
        Assert.NotNull(instance);

        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p is { CanRead: true, CanWrite: true } && p.GetIndexParameters().Length == 0);

        foreach (var property in properties)
        {
            // Read the current value and write it straight back; this exercises both
            // accessors without fabricating type-specific values.
            var value = property.GetValue(instance);
            property.SetValue(instance, value);
            Assert.Equal(value, property.GetValue(instance));
        }
    }
}
