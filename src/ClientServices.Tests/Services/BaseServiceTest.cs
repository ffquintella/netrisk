using System;
using ClientServices.Tests.DI;

namespace ClientServices.Tests.Services;

public class BaseServiceTest
{
    protected readonly IServiceProvider _serviceProvider = ServiceRegistration.GetServiceProvider();
}