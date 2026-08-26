using System;
using System.IO;
using System.Linq;

namespace Packaging.Tests;

/// <summary>
/// Locates the installer templates in the working tree. The tests render the real files, not
/// copies, so a template edit that breaks a manifest is caught by `dotnet test`.
/// </summary>
internal static class RepositoryPaths
{
    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    public static string Installers => Path.Combine(RepositoryRoot, "build", "installers");

    /// <summary>Reads a file under <c>build/installers</c>.</summary>
    public static string Read(params string[] relativeToInstallers)
    {
        var path = Path.Combine(new[] { Installers }.Concat(relativeToInstallers).ToArray());

        if (!File.Exists(path))
            throw new FileNotFoundException($"Installer template '{path}' is missing.", path);

        return File.ReadAllText(path);
    }

    public static bool Exists(params string[] relativeToInstallers) =>
        File.Exists(Path.Combine(new[] { Installers }.Concat(relativeToInstallers).ToArray()));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "build", "installers")) &&
                File.Exists(Path.Combine(directory.FullName, "CLAUDE.md")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not find the repository root above '{AppContext.BaseDirectory}'.");
    }
}
