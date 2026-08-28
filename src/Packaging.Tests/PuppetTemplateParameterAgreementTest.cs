using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Packaging.Tests;

/// <summary>
/// Every <c>epp()</c> call in the Puppet module agrees with the parameter block of the template it
/// renders.
///
/// An EPP template that declares its parameters rejects any argument it does not declare, and the
/// failure is a catalog compilation error at apply time — which, inside a container whose entrypoint
/// runs <c>puppet apply</c> under <c>set -e</c>, is a container that exits before the application
/// starts. That is how 2.17.0 shipped: NR-2026-025 removed <c>$db_port</c> from the console and
/// website <c>appsettings.json</c> templates but left <c>'db_port' => Integer($dbport)</c> in
/// <c>console.pp</c> and <c>website.pp</c>, so both hosts restart-looped on
///
///   <c>Evaluation Error: … lambda: has no parameter named 'db_port'</c>
///
/// Puppet is not available here, so this test does the one check that catches the whole class:
/// argument keys and declared parameters have to be the same set. None of these templates gives a
/// parameter a default, so a missing argument is equally fatal and is checked in both directions.
/// </summary>
public class PuppetTemplateParameterAgreementTest
{
    private static string PuppetRoot =>
        Path.Combine(RepositoryPaths.RepositoryRoot, "build", "puppet", "modules", "netrisk");

    /// <summary>Matches the start of an <c>epp('netrisk/…', {</c> call and captures the template.</summary>
    private static readonly Regex EppCall =
        new(@"epp\(\s*'netrisk/(?<template>[^']+)'\s*,\s*\{", RegexOptions.Compiled);

    [Fact]
    public void TestEveryEppCallMatchesItsTemplateParameters()
    {
        var problems = new List<string>();
        var calls = 0;

        foreach (var manifest in Directory
                     .EnumerateFiles(Path.Combine(PuppetRoot, "manifests"), "*.pp")
                     .OrderBy(f => f, StringComparer.Ordinal))
        {
            var content = File.ReadAllText(manifest);
            var name = Path.GetFileName(manifest);

            foreach (var call in EppCall.Matches(content).Cast<Match>())
            {
                calls++;

                var template = call.Groups["template"].Value;
                var templatePath = Path.Combine(
                    PuppetRoot, "templates", template.Replace('/', Path.DirectorySeparatorChar));

                if (!File.Exists(templatePath))
                {
                    problems.Add($"{name} renders 'netrisk/{template}', which does not exist");
                    continue;
                }

                var passed = ArgumentKeys(content, call.Index + call.Length - 1);
                var declared = DeclaredParameters(templatePath);

                foreach (var key in passed.Except(declared).OrderBy(k => k, StringComparer.Ordinal))
                    problems.Add($"{name} passes '{key}' to {template}, which does not declare ${key}");

                foreach (var key in declared.Except(passed).OrderBy(k => k, StringComparer.Ordinal))
                    problems.Add($"{template} declares ${key}, which {name} does not pass");
            }
        }

        // A parser that silently matches nothing would make this test pass forever.
        Assert.True(calls >= 9, $"only found {calls} epp() calls in the Puppet module");
        Assert.Empty(problems);
    }

    /// <summary>
    /// The <c>'key' =&gt;</c> keys of the hash literal that starts at <paramref name="openBrace"/>,
    /// ignoring anything nested inside a deeper brace.
    /// </summary>
    private static HashSet<string> ArgumentKeys(string content, int openBrace)
    {
        Assert.Equal('{', content[openBrace]);

        var depth = 0;
        var end = -1;

        for (var i = openBrace; i < content.Length; i++)
        {
            if (content[i] == '{') depth++;
            else if (content[i] == '}' && --depth == 0)
            {
                end = i;
                break;
            }
        }

        Assert.True(end > openBrace, "an epp() hash literal is not closed");

        var keys = new HashSet<string>(StringComparer.Ordinal);
        depth = 0;

        foreach (var match in Regex.Matches(content[openBrace..end], @"[{}]|'(?<key>[^']*)'\s*=>").Cast<Match>())
        {
            if (match.Value == "{") depth++;
            else if (match.Value == "}") depth--;
            else if (depth == 1) keys.Add(match.Groups["key"].Value);
        }

        return keys;
    }

    /// <summary>The <c>$name</c>s of a template's leading <c>&lt;%- | … | -%&gt;</c> parameter block.</summary>
    private static HashSet<string> DeclaredParameters(string templatePath)
    {
        var template = File.ReadAllText(templatePath);

        var end = template.IndexOf("-%>", StringComparison.Ordinal);
        Assert.True(end > 0, $"{Path.GetFileName(templatePath)} has no parameter block");

        var header = template[..end];
        Assert.Contains("<%-", header);
        Assert.Contains("|", header);

        return Regex.Matches(header, @"\$(?<name>\w+)")
            .Cast<Match>()
            .Select(m => m.Groups["name"].Value)
            .ToHashSet(StringComparer.Ordinal);
    }
}
