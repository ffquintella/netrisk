using System;
using API.Tests.DI;

namespace API.Tests.APITests;

public class BaseControllerTest
{
    protected readonly IServiceProvider _serviceProvider = ServiceRegistration.GetServiceProvider();
}