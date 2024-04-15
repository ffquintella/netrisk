using System;
using ServerServices.Tests.DI;

namespace ServerServices.Tests.ServiceTests;

public class BaseServiceTest
{
    protected readonly IServiceProvider _serviceProvider = ServiceRegistration.GetServiceProvider();
}