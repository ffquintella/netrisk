using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using JetBrains.Annotations;
using ServerServices.Plugins;
using Xunit;

namespace ServerServices.Tests.Plugins;

/// <summary>
/// Validation and extraction of an uploaded plugin package (administration → plugins → upload).
///
/// The weight here is on rejection, for the same reason the signature verifier's tests are: the
/// endpoint hands an archive from a browser to code that writes files on the server and then loads
/// a DLL out of them. Two of these cases are the ones that actually matter — an entry whose name
/// walks out of the destination (zip slip), and an archive whose declared expansion would fill the
/// disk — and both are decided from the table of contents, before anything is written.
///
/// The rest are usability rejections that exist because their alternative is a *silent* failure: a
/// package whose assembly sits one folder too deep, or which ships Contracts.dll, would install
/// without complaint and then simply never appear in the plugin list.
/// </summary>
[TestSubject(typeof(PluginPackageInstaller))]
public class PluginPackageInstallerTest : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(),
        "nr-plugin-pkg-" + Guid.NewGuid().ToString("N"));

    public PluginPackageInstallerTest() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }
        catch (IOException)
        {
            // A leaked temp directory is not a test failure.
        }

        GC.SuppressFinalize(this);
    }

    private static List<PluginPackageEntry> Entries(params (string Name, long Length)[] entries) =>
        entries.Select(e => new PluginPackageEntry(e.Name, e.Length)).ToList();

    #region NAME DERIVATION

    [Theory]
    [InlineData("MyVault.Plugin.zip", "MyVault.Plugin")]
    [InlineData("MyVault.Plugin.ZIP", "MyVault.Plugin")]
    [InlineData("my-vault_plugin", "my-vault_plugin")]
    [InlineData("/home/op/downloads/MyVault.Plugin.zip", "MyVault.Plugin")]
    [InlineData("C:\\Users\\op\\MyVault.Plugin.zip", "MyVault.Plugin")]
    public void TestDerivePackageNameAcceptsUsableNames(string fileName, string expected)
    {
        Assert.Equal(expected, PluginPackageInstaller.DerivePackageName(fileName));
    }

    [Fact]
    public void TestDerivePackageNameSanitizesRatherThanRejects()
    {
        // A browser download that collided produces "MyVault.Plugin (1).zip", which is an ordinary
        // thing for an operator to upload and not worth refusing over.
        // The trailing separator left by ")" is trimmed rather than kept as a dangling underscore.
        Assert.Equal("MyVault.Plugin__1", PluginPackageInstaller.DerivePackageName("MyVault.Plugin (1).zip"));
    }

    [Fact]
    public void TestDerivePackageNameNeverEscapesTheDirectory()
    {
        // The name is the one path segment the caller controls. A path is reduced to its leaf, so
        // the directory separators and the traversal in it cannot survive into the target path.
        Assert.Equal("evil", PluginPackageInstaller.DerivePackageName("../../etc/cron.d/evil.zip"));
        Assert.Null(PluginPackageInstaller.DerivePackageName("../../"));
        Assert.Null(PluginPackageInstaller.DerivePackageName(".."));
        Assert.Null(PluginPackageInstaller.DerivePackageName(""));
        Assert.Null(PluginPackageInstaller.DerivePackageName("   "));
        Assert.Null(PluginPackageInstaller.DerivePackageName(null));
    }

    #endregion

    #region INSTALL IDENTITY

    /// <summary>
    /// The directory a package installs into is named after its plugin assembly, not after the
    /// uploaded file.
    ///
    /// This is the reported defect: releases are named for their version, so
    /// BastionVaultPlugin-1.2.0.zip and BastionVaultPlugin-1.2.1.zip installed into two directories,
    /// the loader globbed both, and the same plugin appeared twice in administration with two
    /// independent enabled switches. Deriving the directory from the assembly makes the second
    /// upload land on the first.
    /// </summary>
    [Fact]
    public void TestInstallDirectoryIsNamedAfterThePluginAssembly()
    {
        var v120 = PluginPackageInstaller.Validate(Entries(
            ("BastionVaultPlugin-1.2.0/BastionVaultPlugin.dll", 2048),
            ("BastionVaultPlugin-1.2.0/BastionVaultPlugin.deps.json", 256)));

        var v121 = PluginPackageInstaller.Validate(Entries(
            ("BastionVaultPlugin.dll", 2048),
            ("BastionVaultPlugin.deps.json", 256)));

        Assert.Equal("BastionVaultPlugin", PluginPackageInstaller.DeriveInstallDirectoryName(v120));
        Assert.Equal("BastionVaultPlugin", PluginPackageInstaller.DeriveInstallDirectoryName(v121));
    }

    /// <summary>
    /// Two plugin assemblies in one package give no single identity to install under, so the caller
    /// is told to fall back to the file name rather than being handed one of them arbitrarily.
    /// </summary>
    [Fact]
    public void TestInstallDirectoryIsUndecidedWhenAPackageCarriesTwoPluginAssemblies()
    {
        var validation = PluginPackageInstaller.Validate(Entries(
            ("OnePlugin.dll", 1024),
            ("AnotherPlugin.dll", 1024)));

        Assert.True(validation.IsValid);
        Assert.Null(PluginPackageInstaller.DeriveInstallDirectoryName(validation));
    }

    [Fact]
    public void TestInstallDirectoryIsUndecidedForAnInvalidPackage()
    {
        Assert.Null(PluginPackageInstaller.DeriveInstallDirectoryName(
            PluginPackageValidation.Invalid("nope")));
    }

    [Fact]
    public void TestPluginAssemblyNamesReadsOnlyTheTopLevel()
    {
        var names = PluginPackageInstaller.PluginAssemblyNames(
        [
            "BastionVaultPlugin.dll",
            "BastionVaultPlugin.deps.json",
            "runtimes/win-x64/native/SomethingPlugin.dll"
        ]);

        Assert.Equal(["BastionVaultPlugin.dll"], names);
    }

    /// <summary>
    /// Every earlier directory holding the same assembly is superseded, not just one of them: an
    /// installation that accumulated 1.2.0 and 1.2.1 has to come out clean on the next upload.
    /// </summary>
    [Fact]
    public void TestFindSupersededDirectoriesReturnsEveryEarlierInstallation()
    {
        var superseded = PluginPackageInstaller.FindSupersededDirectories(
            "BastionVaultPlugin",
            ["BastionVaultPlugin.dll"],
            [
                ("BastionVaultPlugin", ["BastionVaultPlugin.dll"]),
                ("BastionVaultPlugin-1.2.0", ["BastionVaultPlugin.dll"]),
                ("BastionVaultPlugin-1.2.1", ["BastionVaultPlugin.dll"]),
                ("FaceIdPlugin", ["FaceIdPlugin.dll"])
            ]);

        Assert.Equal(2, superseded.Count);
        Assert.Contains("BastionVaultPlugin-1.2.0", superseded);
        Assert.Contains("BastionVaultPlugin-1.2.1", superseded);
    }

    /// <summary>
    /// The target directory is never reported: it is being replaced in place, and deleting it as a
    /// superseded copy would remove what was just installed.
    /// </summary>
    [Fact]
    public void TestFindSupersededDirectoriesNeverIncludesTheTarget()
    {
        var superseded = PluginPackageInstaller.FindSupersededDirectories(
            "BastionVaultPlugin",
            ["BastionVaultPlugin.dll"],
            [("bastionvaultplugin", ["BastionVaultPlugin.dll"])]);

        Assert.Empty(superseded);
    }

    [Fact]
    public void TestFindSupersededDirectoriesLeavesOtherPluginsAlone()
    {
        var superseded = PluginPackageInstaller.FindSupersededDirectories(
            "BastionVaultPlugin",
            ["BastionVaultPlugin.dll"],
            [("FaceIdPlugin", ["FaceIdPlugin.dll"]), ("Empty", [])]);

        Assert.Empty(superseded);
    }

    [Fact]
    public void TestFindSupersededDirectoriesFindsNothingWithoutAnAssemblyToMatch()
    {
        var superseded = PluginPackageInstaller.FindSupersededDirectories(
            "Whatever", [], [("BastionVaultPlugin", ["BastionVaultPlugin.dll"])]);

        Assert.Empty(superseded);
    }

    #endregion

    #region VALIDATION

    [Fact]
    public void TestValidateAcceptsFlatPackage()
    {
        var result = PluginPackageInstaller.Validate(Entries(
            ("MyVault.Plugin.dll", 4096),
            ("MyVault.Plugin.deps.json", 512),
            ("runtimes/linux-x64/native/libfoo.so", 2048)));

        Assert.True(result.IsValid, result.Error);
        Assert.Null(result.RootFolder);
        Assert.Contains("MyVault.Plugin.dll", result.Files);
        Assert.Contains("runtimes/linux-x64/native/libfoo.so", result.Files);
    }

    [Fact]
    public void TestValidateStripsSingleRootFolder()
    {
        // "Compress this folder" and "compress these files" must put the assembly in the same place,
        // because the loader only globs the top level of a plugin directory.
        var result = PluginPackageInstaller.Validate(Entries(
            ("MyVault/", 0),
            ("MyVault/MyVault.Plugin.dll", 4096),
            ("MyVault/MyVault.Plugin.deps.json", 512)));

        Assert.True(result.IsValid, result.Error);
        Assert.Equal("MyVault", result.RootFolder);
        Assert.Equal(new[] { "MyVault.Plugin.dll", "MyVault.Plugin.deps.json" }, result.Files);
    }

    [Fact]
    public void TestValidateKeepsSeveralTopLevelFoldersIntact()
    {
        var result = PluginPackageInstaller.Validate(Entries(
            ("MyVault.Plugin.dll", 4096),
            ("lib/helper.dll", 512),
            ("res/strings.json", 32)));

        Assert.True(result.IsValid, result.Error);
        Assert.Null(result.RootFolder);
    }

    [Fact]
    public void TestValidateRejectsZipSlip()
    {
        var result = PluginPackageInstaller.Validate(Entries(
            ("MyVault.Plugin.dll", 4096),
            ("../../../etc/cron.d/evil", 128)));

        Assert.False(result.IsValid);
        Assert.Contains("will not write", result.Error);
    }

    [Fact]
    public void TestValidateRejectsAbsolutePaths()
    {
        Assert.False(PluginPackageInstaller.Validate(Entries(
            ("MyVault.Plugin.dll", 4096), ("/etc/passwd", 128))).IsValid);

        Assert.False(PluginPackageInstaller.Validate(Entries(
            ("MyVault.Plugin.dll", 4096), ("C:\\Windows\\System32\\evil.dll", 128))).IsValid);
    }

    [Fact]
    public void TestValidateRejectsZipBomb()
    {
        var result = PluginPackageInstaller.Validate(Entries(
            ("MyVault.Plugin.dll", 4096),
            ("payload.bin", PluginPackageInstaller.MaxUncompressedBytes)));

        Assert.False(result.IsValid);
        Assert.Contains("expands to more than", result.Error);
    }

    [Fact]
    public void TestValidateRejectsTooManyEntries()
    {
        var entries = Enumerable.Range(0, PluginPackageInstaller.MaxEntries + 1)
            .Select(i => new PluginPackageEntry($"file{i}.txt", 1))
            .ToList();

        var result = PluginPackageInstaller.Validate(entries);

        Assert.False(result.IsValid);
        Assert.Contains("more than", result.Error);
    }

    [Fact]
    public void TestValidateRejectsPackageWithoutPluginAssembly()
    {
        var result = PluginPackageInstaller.Validate(Entries(
            ("readme.txt", 12),
            ("MyVault.dll", 4096)));

        Assert.False(result.IsValid);
        Assert.Contains("Plugin.dll", result.Error);
    }

    [Fact]
    public void TestValidateRejectsPluginAssemblyBuriedTooDeep()
    {
        // Installs cleanly and never loads: GetPluginsDlls globs Plugins/<dir>/*Plugin.dll only.
        var result = PluginPackageInstaller.Validate(Entries(
            ("MyVault/bin/MyVault.Plugin.dll", 4096),
            ("MyVault/readme.txt", 12)));

        Assert.False(result.IsValid);
        Assert.Contains("top level", result.Error);
    }

    [Fact]
    public void TestValidateRejectsShippedContractsAssembly()
    {
        var result = PluginPackageInstaller.Validate(Entries(
            ("MyVault.Plugin.dll", 4096),
            ("Contracts.dll", 8192)));

        Assert.False(result.IsValid);
        Assert.Contains("Contracts.dll", result.Error);
    }

    [Fact]
    public void TestValidateRejectsEmptyPackage()
    {
        Assert.False(PluginPackageInstaller.Validate(Entries()).IsValid);
        Assert.False(PluginPackageInstaller.Validate(Entries(("MyVault/", 0))).IsValid);
    }

    #endregion

    #region EXTRACTION

    private string BuildZip(string name, params (string Entry, string Content)[] files)
    {
        var path = Path.Combine(_dir, name);

        using var stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

        foreach (var (entry, content) in files)
        {
            var zipEntry = archive.CreateEntry(entry);
            using var writer = new StreamWriter(zipEntry.Open(), Encoding.UTF8);
            writer.Write(content);
        }

        return path;
    }

    [Fact]
    public void TestExtractWritesFilesAndStripsRootFolder()
    {
        var zipPath = BuildZip("pkg.zip",
            ("MyVault/MyVault.Plugin.dll", "assembly"),
            ("MyVault/config/settings.json", "{}"));

        using var archive = ZipFile.OpenRead(zipPath);
        var validation = PluginPackageInstaller.Validate(PluginPackageInstaller.Describe(archive));

        Assert.True(validation.IsValid, validation.Error);

        var target = Path.Combine(_dir, "out");
        var written = PluginPackageInstaller.Extract(archive, validation, target);

        Assert.Equal(2, written.Count);
        Assert.True(File.Exists(Path.Combine(target, "MyVault.Plugin.dll")));
        Assert.True(File.Exists(Path.Combine(target, "config", "settings.json")));
        Assert.Equal("assembly", File.ReadAllText(Path.Combine(target, "MyVault.Plugin.dll")));
    }

    [Fact]
    public void TestExtractOverwritesAnExistingFile()
    {
        var zipPath = BuildZip("pkg.zip", ("MyVault.Plugin.dll", "new"));

        var target = Path.Combine(_dir, "out");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "MyVault.Plugin.dll"), "old");

        using var archive = ZipFile.OpenRead(zipPath);
        var validation = PluginPackageInstaller.Validate(PluginPackageInstaller.Describe(archive));

        PluginPackageInstaller.Extract(archive, validation, target);

        Assert.Equal("new", File.ReadAllText(Path.Combine(target, "MyVault.Plugin.dll")));
    }

    [Fact]
    public void TestExtractRefusesAnUnvalidatedPackage()
    {
        var zipPath = BuildZip("pkg.zip", ("MyVault.Plugin.dll", "assembly"));

        using var archive = ZipFile.OpenRead(zipPath);

        Assert.Throws<InvalidOperationException>(() =>
            PluginPackageInstaller.Extract(archive, PluginPackageValidation.Invalid("nope"),
                Path.Combine(_dir, "out")));
    }

    [Fact]
    public void TestExtractDoesNotWriteOutsideTheTargetDirectory()
    {
        // Belt-and-braces on the write path itself: even handed a validation that claims a
        // traversing entry is fine, Extract must not put a file outside the target.
        var zipPath = BuildZip("pkg.zip", ("../escaped.txt", "pwned"));

        using var archive = ZipFile.OpenRead(zipPath);
        var forged = new PluginPackageValidation(true, null, null, ["../escaped.txt"]);

        var target = Path.Combine(_dir, "out");

        Assert.Throws<ArgumentException>(() =>
            PluginPackageInstaller.Extract(archive, forged, target));

        Assert.False(File.Exists(Path.Combine(_dir, "escaped.txt")));
    }

    #endregion
}
