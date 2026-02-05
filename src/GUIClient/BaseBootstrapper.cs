using System;
using Microsoft.Extensions.DependencyInjection;

namespace GUIClient;

public class BaseBootstrapper
{
    protected static T GetService<T>()
    {
        var result = Program.ServiceProvider.GetService<T>();
        if (result == null) throw new Exception("Could not find service of class: " + typeof(T).Name);
        return result;
    }
}
