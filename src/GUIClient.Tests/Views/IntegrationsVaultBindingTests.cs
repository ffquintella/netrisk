using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace GUIClient.Tests.Views;

/// <summary>
/// The vault picker buttons in <c>IntegrationsView.axaml</c>, checked against the keys the view-model
/// actually registers.
///
/// This exists because the coupling between the two files is a string. The view passes a
/// <c>CommandParameter</c>; the view-model looks it up in a dictionary and, on a miss, logs and does
/// nothing. So a typo in the XAML is a button that silently fails — no exception, no compiler error,
/// and nothing in the UI to suggest anything is wrong. Compiled bindings catch a wrong *property*
/// name; nothing catches a wrong parameter value but this.
///
/// The keys are duplicated here rather than read from the view-model, because <c>GUIClient.Tests</c>
/// deliberately does not reference <c>GUIClient</c> — that would pull Avalonia into a headless run.
/// The duplication is the point: if the two lists disagree, one of them changed without the other.
/// </summary>
public class IntegrationsVaultBindingTests
{
    /// <summary>
    /// Mirrors <c>IntegrationsViewModel.VaultFieldKeys</c>. Keep in step; that is what this file is
    /// for.
    /// </summary>
    private static readonly string[] KnownKeys =
    [
        "trendmicro-apikey",
        "scorecard-apitoken",
        "issuetracker-token",
        "issuetracker-webhooksecret",
        "idp-clientsecret",
        "channel-webhookurl",
        "channel-signingsecret"
    ];

    private static readonly XNamespace Avalonia = "https://github.com/avaloniaui";

    private static XDocument LoadView()
    {
        var path = Path.Combine(RepositoryRoot(), "src", "GUIClient", "Views", "Admin",
            "IntegrationsView.axaml");

        Assert.True(File.Exists(path), $"The integrations view was not found at {path}.");

        return XDocument.Load(path);
    }

    /// <summary>
    /// Walks up from the test binaries until the solution file appears. Fails loudly rather than
    /// skipping: a test that quietly does nothing when it cannot find its input is worse than no test.
    /// </summary>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "src", "netrisk.sln")))
            directory = directory.Parent;

        Assert.NotNull(directory);

        return directory!.FullName;
    }

    /// <summary>Every (command, parameter) pair the view wires to a vault button.</summary>
    private static List<(string Command, string? Parameter)> VaultButtons(XDocument view) =>
        view.Descendants(Avalonia + "Button")
            .Select(b => (Command: (string?)b.Attribute("Command") ?? string.Empty,
                Parameter: (string?)b.Attribute("CommandParameter")))
            .Where(b => b.Command.Contains("VaultSecretClicked", StringComparison.Ordinal))
            .ToList();

    [Fact]
    public void EveryVaultButtonPassesAKeyTheViewModelKnows()
    {
        var buttons = VaultButtons(LoadView());

        Assert.NotEmpty(buttons);

        foreach (var (command, parameter) in buttons)
        {
            Assert.False(string.IsNullOrWhiteSpace(parameter),
                $"A vault button bound to {command} passes no CommandParameter, so it can never match a field.");

            Assert.Contains(parameter, KnownKeys);
        }
    }

    [Fact]
    public void EveryCredentialFieldHasBothAPickerAndADetachButton()
    {
        var buttons = VaultButtons(LoadView());

        foreach (var key in KnownKeys)
        {
            var forKey = buttons.Where(b => b.Parameter == key).ToArray();

            Assert.True(forKey.Any(b => b.Command.Contains("BtPickVaultSecretClicked", StringComparison.Ordinal)),
                $"The '{key}' field has no picker button.");

            // Without the detach button a field bound by mistake can never be un-bound from the UI:
            // the text box is disabled while a binding is in force.
            Assert.True(forKey.Any(b => b.Command.Contains("BtDetachVaultSecretClicked", StringComparison.Ordinal)),
                $"The '{key}' field has no detach button, so a wrong binding could not be undone.");
        }
    }

    [Fact]
    public void EveryCredentialTextBoxIsDisabledWhileItsFieldIsVaultBacked()
    {
        var view = LoadView();

        var guarded = view.Descendants(Avalonia + "TextBox")
            .Select(t => (string?)t.Attribute("IsEnabled") ?? string.Empty)
            .Count(e => e.Contains(".AcceptsTypedValue", StringComparison.Ordinal));

        // One per credential field that has a picker. A box left enabled beside a bound field invites
        // typing that is then silently ignored.
        Assert.Equal(KnownKeys.Length, guarded);
    }

    [Fact]
    public void NoVaultButtonIsShownWhenTheInstallationHasNoVault()
    {
        var view = LoadView();

        var visibilities = view.Descendants(Avalonia + "Button")
            .Where(b => ((string?)b.Attribute("Command") ?? string.Empty)
                .Contains("VaultSecretClicked", StringComparison.Ordinal))
            .Select(b => (string?)b.Attribute("IsVisible") ?? string.Empty)
            .ToArray();

        // Both flags are false until the server says a vault is usable, so an installation with no
        // vault plugin sees no new controls at all.
        Assert.All(visibilities, v =>
            Assert.True(v.Contains(".ShowPicker", StringComparison.Ordinal)
                        || v.Contains(".ShowDetach", StringComparison.Ordinal),
                $"A vault button has an unexpected IsVisible binding: '{v}'."));
    }
}
