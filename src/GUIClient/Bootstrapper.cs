﻿using Splat;

namespace GUIClient;

public class Bootstrapper
{
    public static void Register(IMutableDependencyResolver services, IReadonlyDependencyResolver resolver, string environment)
    {
        EnvironmentServicesBootstrapper.RegisterEnvironmentServices(services, resolver, environment);
        ConfigurationBootstrapper.RegisterConfiguration(services, resolver);
        LoggingBootstrapper.RegisterLogging(services, resolver);
        MapperBootstrapper.RegisterServices(services, resolver);
        GeneralServicesBootstrapper.RegisterServices(services, resolver);
        
        GeneralServicesBootstrapper.Initialize();

    }
}