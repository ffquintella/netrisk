using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using JetBrains.Annotations;
using ServerServices.Secrets;
using Xunit;

namespace ServerServices.Tests.Secrets;

/// <summary>
/// S40 §11.6, §12.2 (M56, T265): the two registries of vault-capable credential columns — the one that refuses to delete a
/// vault connection in use (<c>SecretVaultService.CountReferencesAsync</c>) and the one that rewrites references
/// (<c>SecretReferenceNormalizer.NormalizeAsync</c>) — enumerate the same columns. Read from the source, since both are
/// hand-written lists and the in-memory provider cannot discover the columns from the schema.
/// </summary>
[TestSubject(typeof(SecretReferenceNormalizer))]
public class SecretColumnRegistriesAgreeTest
{
    private static string Source(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "ServerServices", "Secrets")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir.FullName, "ServerServices", "Secrets", relative));
    }

    private static string[] Columns(string source, string pattern) =>
        Regex.Matches(source, pattern, RegexOptions.Singleline)
            .Select(m => $"{m.Groups[1].Value}.{m.Groups[2].Value}")
            .Distinct().Order(StringComparer.Ordinal).ToArray();

    [Fact]
    public void TestBothRegistriesEnumerateTheSameColumns()
    {
        var counted = Columns(Source("SecretVaultService.cs"),
            @"count\s*\+=\s*await\s+db\.(\w+)\s*\.CountAsync\(\s*\w+\s*=>\s*\w+\.(\w+)");
        var walked = Columns(Source("SecretReferenceNormalizer.cs"),
            @"Walk(?:Json)?Async\(\s*db\.(\w+)\s*,\s*\w+\s*=>\s*\w+\.(\w+)");

        Assert.Contains("TenableConnections.EncryptedAccessKey", counted);
        Assert.Contains("TenableConnections.EncryptedSecretKey", counted);
        Assert.Equal(counted, walked);
    }
}
