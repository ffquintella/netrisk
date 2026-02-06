using System;
using ReactiveUI;
using Microsoft.Extensions.DependencyInjection;

namespace GUIClient.Hydrated;

public class BaseHydrated : ReactiveObject
{
    protected static T GetService<T>()
    {
        var result = Program.ServiceProvider.GetService<T>();
        if (result == null) throw new Exception("Could not find service of class: " + typeof(T).Name);
        return result;
    }
}
