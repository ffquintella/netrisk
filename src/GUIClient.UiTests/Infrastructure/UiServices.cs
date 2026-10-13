using System;
using System.Collections.Concurrent;
using System.Reflection;
using ClientServices;
using ClientServices.Interfaces;
using ClientServices.Services;
using GUIClient.Navigation;
using GUIClient.Notifications;
using GUIClient.Tools;
using GUIClient.ViewModels.Dialogs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Model.Configuration;
using NSubstitute;

namespace GUIClient.UiTests.Infrastructure;

/// <summary>
/// The service container a UI test runs against (S54 §4, "Serviços").
///
/// The client's view-models resolve their dependencies through a static service locator
/// (<c>Program.ServiceProvider</c>, about 140 call sites), so a view cannot be built until that
/// locator answers. This class answers it without any server, database or network:
/// <list type="bullet">
///   <item>the handful of services that are pure client-side logic are the real ones — localization
///   (so every <c>Str*</c> binding resolves real pt-BR text), the toast channel, the navigation
///   and dialog routers (pointed at the windows the test supplies) and the shared constant cache;</item>
///   <item>every other <em>interface</em> a view-model asks for — each <c>ClientServices</c> REST
///   client, authentication, configuration, dialogs — is an NSubstitute double, created on first
///   request and kept for the rest of the test, so a test configures exactly the calls it cares about
///   through <see cref="Double{T}"/>. Everything else answers like a server with no data yet
///   (<see cref="EmptyServerDefaults"/>): empty lists, zero, false, null objects.</item>
/// </list>
/// A fresh instance is installed for every test (the Application is rebuilt per test, S54 §4), so no
/// double leaks between tests. A concrete type nobody registered still throws, exactly as it would in
/// the client — the harness does not hide a missing registration.
/// </summary>
public sealed class UiServices : IServiceProvider
{
    private readonly ConcurrentDictionary<Type, object> _doubles = new();
    private readonly IServiceProvider _real;

    private UiServices()
    {
        var services = new ServiceCollection();

        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton<ILocalizationService>(sp => new LocalizationService(
            sp.GetRequiredService<ILoggerFactory>(), typeof(GUIClient.App).Assembly));

        // The client binds its options from configuration; an empty one is what a fresh install has.
        services.AddSingleton(new ServerConfiguration());

        services.AddSingleton<NotificationService>();
        services.AddSingleton<INotificationService>(sp => sp.GetRequiredService<NotificationService>());

        // Navigation and dialogs are the client's own routers, pointed at whichever windows the test
        // hands the IMainWindowProvider double — so a dialog opens through the same naming convention,
        // view-model resolution and Activate call as in the app.
        services.AddSingleton<INavigationService>(_ =>
            new NavigationService(Double<IMainWindowProvider>()));
        services.AddSingleton<IDialogService>(_ => new DialogService(Double<IMainWindowProvider>()));
        RegisterDialogViewModels(services);

        services.AddSingleton(_ => new ConstantManager());
        services.AddSingleton(sp => new PluginManager(
            sp.GetRequiredService<ILoggerFactory>(),
            Double<IPluginsService>(),
            Double<IAuthenticationService>(),
            Double<IFaceIDService>(),
            Double<IMemoryCacheService>()));

        _real = services.BuildServiceProvider();
    }

    /// <summary>
    /// What <c>GeneralServicesBootstrapper.RegisterDialogViewModels</c> does for the client (a private
    /// method there): every concrete dialog view-model as a transient, because
    /// <see cref="DialogService"/> resolves them from the container by type.
    /// </summary>
    private static void RegisterDialogViewModels(IServiceCollection services)
    {
        foreach (var type in typeof(GUIClient.App).Assembly.GetTypes())
        {
            if (type.IsAbstract || type.IsGenericTypeDefinition || !IsDialogViewModel(type)) continue;
            services.AddTransient(type);
        }
    }

    private static bool IsDialogViewModel(Type type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(DialogViewModelBase<>))
                return true;
        }

        return false;
    }

    /// <summary>The container of the test that is running now.</summary>
    public static UiServices Current { get; private set; } = null!;

    /// <summary>
    /// Builds a fresh container and points the client's service locators at it. Called by
    /// <see cref="TestAppBuilder"/> before each test's Application is created, because
    /// <c>App.Initialize</c> already reads the localizer.
    /// </summary>
    public static UiServices Install()
    {
        var services = new UiServices();
        Current = services;

        ProgramServiceProviderSetter.Value(services);
        ServiceProviderAccessor.Provider = services;

        return services;
    }

    /// <summary>
    /// The double for <typeparamref name="T"/>, created on first use. Configure it before the view
    /// that consumes it is built: view-models read their services in their constructors.
    /// </summary>
    public T Double<T>() where T : class => (T)GetService(typeof(T))!;

    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(IServiceProvider)) return this;

        var real = _real.GetService(serviceType);
        if (real != null) return real;

        if (!serviceType.IsInterface) return null;

        return _doubles.GetOrAdd(serviceType, type =>
        {
            var substitute = Substitute.For([type], []);
            EmptyServerDefaults.Apply(substitute, type);
            return substitute;
        });
    }

    /// <summary>
    /// <c>GUIClient.Program.ServiceProvider</c> has a private setter on an internal class, and the
    /// client's production code is not changed to suit the tests (S54 header). Reflection reaches it;
    /// the lookup is resolved once and fails loudly, naming the member, if the client ever renames it.
    /// </summary>
    private static readonly Lazy<Action<IServiceProvider>> ProgramServiceProviderSetter = new(() =>
    {
        const string typeName = "GUIClient.Program";
        const string propertyName = "ServiceProvider";

        var program = typeof(GUIClient.App).Assembly.GetType(typeName)
                      ?? throw new InvalidOperationException(
                          $"{typeName} not found: the UI test harness needs the client's service locator.");

        var property = program.GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                       ?? throw new InvalidOperationException($"{typeName}.{propertyName} not found.");

        var setter = property.GetSetMethod(nonPublic: true)
                     ?? throw new InvalidOperationException($"{typeName}.{propertyName} has no setter.");

        return provider => setter.Invoke(null, [provider]);
    });
}
