using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Contracts.Secrets;
using DAL.Entities;
using JetBrains.Annotations;
using Model.Secrets;
using NSubstitute;
using Serilog;
using ServerServices.Interfaces;
using ServerServices.Services;
using ServerServices.Secrets;
using ServerServices.Tests.Mock;
using ServerServices.Tests.ServiceTests;
using Xunit;

namespace ServerServices.Tests.Secrets;

/// <summary>
/// Rewriting stored references into the form their plugin now produces.
///
/// The property that matters most is the one about who knows what. The references this walks carry
/// a plugin's own grammar inside a secret id, and the host must not learn it — so every candidate
/// goes through the plugin and nothing here inspects an id. The second is that it is a plan until
/// an operator says otherwise: a rewrite a plugin got wrong repoints a credential field at a
/// different secret, and discovering that after the write is discovering it too late.
/// </summary>
[TestSubject(typeof(SecretReferenceNormalizer))]
public class SecretReferenceNormalizerTest : InMemoryServiceTestBase
{
    private static readonly ILogger Log = new LoggerConfiguration().CreateLogger();

    private readonly FakeSecretVaultPlugin _plugin = new();
    private readonly IPluginsService _plugins = Substitute.For<IPluginsService>();
    private readonly ISecretReferenceNormalizer _normalizer;

    public SecretReferenceNormalizerTest()
    {
        _plugins.GetPluginByNameAsync<INetriskSecretVaultPlugin>(Arg.Any<string>()).Returns(_plugin);

        // The plugin's private reading of the form it used to write, and the only thing that knows
        // what "?env=" means.
        _plugin.Normalizer = reference =>
        {
            var marker = reference.SecretId.IndexOf("?env=", StringComparison.Ordinal);

            return marker < 0
                ? reference
                : new VaultSecretReference
                {
                    SecretId = reference.SecretId[..marker],
                    Field = reference.Field,
                    Options = new Dictionary<string, string>
                    {
                        ["environment"] = reference.SecretId[(marker + 5)..]
                    }
                };
        };

        _normalizer = new SecretReferenceNormalizer(Log, GetService<IDalService>(), _plugins);
    }

    private int ArrangeConnection()
    {
        using var context = GetService<IDalService>().GetContext();

        var connection = new SecretVaultConnection
        {
            Name = "Prod vault",
            PluginName = _plugin.PluginName,
            BaseUrl = "https://vault.example.com",
            EncryptedApiKey = "x",
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };

        context.SecretVaultConnections.Add(connection);
        context.SaveChanges();

        return connection.Id;
    }

    private void ArrangeTrendMicro(string storedValue)
    {
        using var context = GetService<IDalService>().GetContext();

        context.TrendMicroConnections.Add(new TrendMicroConnection
        {
            Name = "Vision One",
            Region = "us-east-1",
            BaseUrl = "https://api.xdr.trendmicro.com",
            EncryptedApiKey = storedValue
        });

        context.SaveChanges();
    }

    private string StoredApiKey()
    {
        using var context = GetService<IDalService>().GetContext();
        return context.TrendMicroConnections.Single().EncryptedApiKey!;
    }

    [Fact]
    public async Task ReportsARewriteWithoutWritingIt()
    {
        var connectionId = ArrangeConnection();
        var stored = SecretReference.Create(connectionId, "secret/prod/db?env=hml", "password").ToString();
        ArrangeTrendMicro(stored);

        var rewrite = Assert.Single(await _normalizer.NormalizeAsync(apply: false));

        Assert.Contains("encrypted_api_key", rewrite.Location);
        Assert.Equal(stored, rewrite.Before);

        Assert.True(SecretReference.TryParse(rewrite.After, out var after));
        Assert.Equal("secret/prod/db", after.SecretId);
        Assert.Equal("hml", after.Options["environment"]);

        Assert.Equal(stored, StoredApiKey());
    }

    [Fact]
    public async Task WritesTheRewriteWhenAsked()
    {
        var connectionId = ArrangeConnection();
        ArrangeTrendMicro(
            SecretReference.Create(connectionId, "secret/prod/db?env=hml", "password").ToString());

        await _normalizer.NormalizeAsync(apply: true);

        Assert.True(SecretReference.TryParse(StoredApiKey(), out var after));
        Assert.Equal("secret/prod/db", after.SecretId);
        Assert.Equal("password", after.Field);
        Assert.Equal("hml", after.Options["environment"]);
    }

    [Fact]
    public async Task LeavesAReferenceThePluginReadsTheSameWayAlone()
    {
        var connectionId = ArrangeConnection();
        ArrangeTrendMicro(SecretReference.Create(connectionId, "secret/prod/db", "password").ToString());

        Assert.Empty(await _normalizer.NormalizeAsync(apply: true));
    }

    [Fact]
    public async Task IgnoresALiteralCredential()
    {
        ArrangeConnection();
        ArrangeTrendMicro("an-actual-api-key");

        Assert.Empty(await _normalizer.NormalizeAsync(apply: true));
        Assert.Equal("an-actual-api-key", StoredApiKey());
    }

    /// <summary>
    /// A reference that already carries declared values came from a declared control, so there is
    /// no older form in it and the plugin is not invited to rewrite what an operator chose.
    /// </summary>
    [Fact]
    public async Task DoesNotAskAboutAReferenceThatAlreadyCarriesValues()
    {
        var connectionId = ArrangeConnection();
        _plugin.Normalizer = _ => throw new InvalidOperationException("must not be asked");

        ArrangeTrendMicro(SecretReference.Create(connectionId, "secret/prod/db", "password",
            new Dictionary<string, string> { ["environment"] = "hml" }).ToString());

        Assert.Empty(await _normalizer.NormalizeAsync(apply: true));
    }

    /// <summary>
    /// A plugin that is not installed cannot say how its references should read, and guessing is
    /// the one thing this must never do.
    /// </summary>
    [Fact]
    public async Task LeavesReferencesAloneWhenThePluginIsAbsent()
    {
        var connectionId = ArrangeConnection();
        _plugins.GetPluginByNameAsync<INetriskSecretVaultPlugin>(Arg.Any<string>())
            .Returns((INetriskSecretVaultPlugin?)null);

        ArrangeTrendMicro(
            SecretReference.Create(connectionId, "secret/prod/db?env=hml", "password").ToString());

        Assert.Empty(await _normalizer.NormalizeAsync(apply: true));
    }

    /// <summary>
    /// A channel keeps its secrets inside a JSON document rather than in a column, so the rewrite
    /// there is a replacement of one exact reference by another — and it must not disturb the rest
    /// of the document.
    /// </summary>
    [Fact]
    public async Task RewritesAReferenceEmbeddedInAChannelConfiguration()
    {
        var connectionId = ArrangeConnection();
        var stored = SecretReference.Create(connectionId, "secret/hooks/slack?env=prd").ToString();

        await using (var context = GetService<IDalService>().GetContext())
        {
            context.NotificationChannels.Add(new NotificationChannel
            {
                Name = "Slack",
                Kind = DAL.Enums.NotificationChannelKind.Slack,
                ConfigurationJson = $$"""{"webhookUrl":"{{stored}}","channel":"#alerts"}""",
                Enabled = true
            });

            await context.SaveChangesAsync();
        }

        var rewrite = Assert.Single(await _normalizer.NormalizeAsync(apply: true));

        await using var read = GetService<IDalService>().GetContext();
        var json = read.NotificationChannels.Single().ConfigurationJson;

        Assert.Contains(rewrite.After, json);
        Assert.DoesNotContain(stored, json);
        Assert.Contains("\"channel\":\"#alerts\"", json);
    }
}
