using System;
using Microsoft.Extensions.DependencyInjection;

namespace ClientServices;

public static class ServiceProviderAccessor
{
    public static IServiceProvider Provider { get; set; } = null!;

    public static T GetRequiredService<T>() where T : notnull => Provider.GetRequiredService<T>();

    public static object GetRequiredService(Type type) => Provider.GetRequiredService(type);
}
