using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Model.Exceptions;
using Model.Secrets;
using NSubstitute;
using ServerServices.Interfaces;

namespace API.Tests.Mock;

/// <summary>
/// A deterministic <see cref="ISecretVaultService"/> for the controller tests.
///
/// It throws the domain exceptions the controller maps onto status codes, because that mapping is
/// most of what a controller test is for: a <see cref="DataNotFoundException"/> has to be a 404 and
/// an <see cref="InvalidParameterException"/> a 400 on this controller exactly as on the other nine,
/// and the shared <c>IntegrationsControllerBase</c> is what guarantees it.
///
/// Note that it has no way to produce a secret <em>value</em>, mirroring the real service's API: the
/// controller has no endpoint that returns one, and a mock that could would make it possible to write
/// a test asserting behaviour the product does not have.
/// </summary>
public static class MockedSecretVaultService
{
    public const int KnownConnectionId = 1;

    public const string KnownPluginName = "BastionVaultPlugin";

    public static ISecretVaultService Create()
    {
        var service = Substitute.For<ISecretVaultService>();

        service.IsAvailableAsync().Returns(Task.FromResult(true));

        service.GetAvailablePluginsAsync().Returns(Task.FromResult(new List<SecretVaultPluginInfo>
        {
            new()
            {
                PluginName = KnownPluginName, VaultKind = "bastionvault",
                Description = "BastionVault", Version = "1.0.0", RequiresMachineId = false
            }
        }));

        service.GetConnectionsAsync(Arg.Any<bool>())
            .Returns(Task.FromResult(new List<SecretVaultConnectionView> { Connection(KnownConnectionId) }));

        service.GetConnectionAsync(Arg.Any<int>()).Returns(call =>
        {
            var id = call.ArgAt<int>(0);
            if (id != KnownConnectionId) throw NotFound(id);

            return Task.FromResult(Connection(id));
        });

        service.CreateConnectionAsync(Arg.Any<SecretVaultConnectionInput>(), Arg.Any<string?>(),
            Arg.Any<int?>()).Returns(call =>
        {
            var input = call.ArgAt<SecretVaultConnectionInput>(0);
            var apiKey = call.ArgAt<string?>(1);

            if (string.IsNullOrWhiteSpace(input.Name))
                throw new InvalidParameterException(nameof(input.Name), "A vault connection name is required.");

            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidParameterException("apiKey", "A vault API key is required.");

            var created = Connection(42);
            created.Name = input.Name;
            return Task.FromResult(created);
        });

        service.UpdateConnectionAsync(Arg.Any<SecretVaultConnectionInput>(), Arg.Any<string?>())
            .Returns(call =>
            {
                var input = call.ArgAt<SecretVaultConnectionInput>(0);
                if (input.Id != KnownConnectionId) throw NotFound(input.Id);

                var updated = Connection(input.Id);
                updated.Name = input.Name;
                return Task.FromResult(updated);
            });

        service.DeleteConnectionAsync(Arg.Any<int>()).Returns(call =>
        {
            var id = call.ArgAt<int>(0);
            if (id != KnownConnectionId) throw NotFound(id);

            return Task.CompletedTask;
        });

        service.TestConnectionAsync(Arg.Any<int>()).Returns(call =>
        {
            var id = call.ArgAt<int>(0);
            if (id != KnownConnectionId) throw NotFound(id);

            return Task.FromResult(new SecretVaultTestResultView
            {
                Success = true, Message = "Connected.", VisibleSecretCount = 2
            });
        });

        service.ListSecretsAsync(Arg.Any<int>()).Returns(call =>
        {
            var id = call.ArgAt<int>(0);
            if (id != KnownConnectionId) throw NotFound(id);

            return Task.FromResult(new List<VaultSecretSummary>
            {
                new()
                {
                    Id = "db-prod", Name = "Production database", Path = "infra",
                    Fields = ["username", "password"]
                },
                new() { Id = "tm-key", Name = "Vision One key" }
            });
        });

        service.DescribeAsync(Arg.Any<string?>()).Returns(call =>
        {
            var value = call.ArgAt<string?>(0);

            if (!SecretReference.TryParse(value, out var reference))
                return Task.FromResult(new SecretReferenceView { IsVaultReference = false });

            return Task.FromResult(new SecretReferenceView
            {
                IsVaultReference = true,
                Resolvable = true,
                ConnectionId = reference.ConnectionId,
                ConnectionName = "Prod vault",
                SecretId = reference.SecretId,
                Field = reference.Field,
                DisplayName = "Prod vault: " + reference.DisplayKey
            });
        });

        service.CountReferencesAsync(Arg.Any<int>()).Returns(Task.FromResult(3));

        service.ResolveAsync(Arg.Any<SecretReference>(), Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new InvalidOperationException(
                "No controller may resolve a secret value; there is no endpoint that returns one."));

        return service;
    }

    private static DataNotFoundException NotFound(int id) =>
        new("SecretVaultConnection", id.ToString(),
            new Exception($"Vault connection {id} was not found."));

    private static SecretVaultConnectionView Connection(int id) => new()
    {
        Id = id,
        Name = "Prod vault",
        PluginName = KnownPluginName,
        VaultKind = "bastionvault",
        BaseUrl = "https://vault.example.com",
        HasApiKey = true,
        MachineId = "machine-42",
        AppId = "netrisk-prod",
        IgnoreSslErrors = true,
        Enabled = true,
        CacheTtlMinutes = SecretVaultDefaults.CacheTtlMinutes,
        PluginAvailable = true
    };
}
