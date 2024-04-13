using System;
using NSubstitute;

namespace ServerServices.Tests.Mock;

public class SubstituteForWithConstructor
{
    public static T Create<T>(params object[] constructorArgs) where T : class
    {
        // Create an instance of the class using the constructor arguments
        var instance = Activator.CreateInstance(typeof(T), constructorArgs);

        // Create a substitute for the class
        var substitute = Substitute.For(new[] { instance.GetType() }, new object[0]);

        // Copy the state from the instance to the substitute
        foreach (var property in instance.GetType().GetProperties())
        {
            if (property.CanRead && property.CanWrite)
            {
                property.SetValue(substitute, property.GetValue(instance));
            }
        }

        return (T)substitute;
    }  
}