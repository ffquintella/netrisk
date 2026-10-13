using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace GUIClient.Tests.Views;

/// <summary>
/// Every <c>Str*</c> caption a view binds is public on the view model the view declares as its <c>x:DataType</c>.
///
/// <c>UsersViewModel.StrCleanAll</c> was declared <c>private</c>. The build accepted the binding, but nothing could
/// read the property at run time, so the "Clear all" button under the user permission list rendered as a bare icon
/// with no caption. Neither <c>LintUi</c> (it checks that copy is bound, not that the binding resolves) nor the
/// localization tests (the key existed) could see it.
///
/// Source is scanned as text because this project deliberately does not reference <c>GUIClient</c> (no Avalonia in a
/// headless run). A view model's class body runs from its <c>class</c> line to the next <c>class</c> line of the file,
/// which is enough for the one-class-per-region layout of <c>ViewModels/</c>.
/// </summary>
public class ViewStringBindingVisibilityTest
{
    private static readonly Regex DataType = new(@"x:DataType\s*=\s*""\w+:(?<type>\w+)""", RegexOptions.Compiled);

    private static readonly Regex StringBinding = new(
        @"\{\s*(?:Compiled)?Binding\s+(?:Path\s*=\s*)?(?<name>Str\w+)", RegexOptions.Compiled);

    private static readonly Regex ClassDeclaration = new(
        @"^\s*(?:public\s+|internal\s+)?(?:sealed\s+|abstract\s+|partial\s+|static\s+)*class\s+(?<name>\w+)",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex NonPublicString = new(
        @"^\s*(?:private|protected|internal)(?:\s+(?:protected|internal))?\s+(?:new\s+)?(?:static\s+)?string\??\s+(?<name>Str\w+)\s*(?:\{|=>)",
        RegexOptions.Compiled | RegexOptions.Multiline);

    [Fact]
    public void EveryBoundCaptionIsPublicOnTheViewsViewModel()
    {
        var classes = ViewModelClasses();

        var offenders = new List<string>();
        foreach (var view in ViewFiles())
        {
            var text = File.ReadAllText(view);
            var dataType = DataType.Match(text);
            if (!dataType.Success || !classes.TryGetValue(dataType.Groups["type"].Value, out var body)) continue;

            var hidden = NonPublicString.Matches(body).Select(m => m.Groups["name"].Value).ToHashSet(StringComparer.Ordinal);
            offenders.AddRange(StringBinding.Matches(text)
                .Select(m => m.Groups["name"].Value)
                .Where(hidden.Contains)
                .Distinct()
                .Select(name => $"  {Path.GetFileName(view)} binds {dataType.Groups["type"].Value}.{name}, which is not public"));
        }

        Assert.True(offenders.Count == 0,
            "A binding cannot read a non-public property, so the caption renders empty:" + Environment.NewLine +
            string.Join(Environment.NewLine, offenders.Order()));
    }

    [Fact]
    public void TheScanFindsTheUsersViewAndItsViewModel()
    {
        var classes = ViewModelClasses();
        var users = File.ReadAllText(ViewFiles().Single(f => Path.GetFileName(f) == "UsersView.axaml"));

        Assert.Equal("UsersViewModel", DataType.Match(users).Groups["type"].Value);
        Assert.Contains("UsersViewModel", classes.Keys);
        Assert.Contains("StrCleanAll", StringBinding.Matches(users).Select(m => m.Groups["name"].Value));
    }

    private static Dictionary<string, string> ViewModelClasses()
    {
        var classes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(GuiClientSourceRoot(), "ViewModels"), "*.cs",
                     SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            var declarations = ClassDeclaration.Matches(text);
            for (var i = 0; i < declarations.Count; i++)
            {
                var end = i + 1 < declarations.Count ? declarations[i + 1].Index : text.Length;
                var name = declarations[i].Groups["name"].Value;
                classes[name] = classes.TryGetValue(name, out var partial)
                    ? partial + text[declarations[i].Index..end]
                    : text[declarations[i].Index..end];
            }
        }
        return classes;
    }

    private static IEnumerable<string> ViewFiles() =>
        Directory.EnumerateFiles(Path.Combine(GuiClientSourceRoot(), "Views"), "*.axaml", SearchOption.AllDirectories);

    private static string GuiClientSourceRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var project = Path.Combine(directory.FullName, "GUIClient", "GUIClient.csproj");
            if (File.Exists(project)) return Path.GetDirectoryName(project)!;
        }
        throw new InvalidOperationException($"Could not find GUIClient/GUIClient.csproj walking up from {AppContext.BaseDirectory}.");
    }
}
