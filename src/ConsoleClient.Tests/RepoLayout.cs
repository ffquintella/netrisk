using System;
using System.IO;

namespace ConsoleClient.Tests;

/// <summary>
/// Locates the checked-in ConsoleClient sources from the test binary, so the schema-upgrade tests
/// assert against the files a release actually ships rather than a copy in the test output.
/// </summary>
public static class RepoLayout
{
    public static DirectoryInfo RepositoryRoot { get; } = FindRepositoryRoot();

    public static DirectoryInfo ConsoleClientProject { get; } =
        new(Path.Combine(RepositoryRoot.FullName, "src", "ConsoleClient"));

    public static DirectoryInfo DbDirectory { get; } =
        new(Path.Combine(ConsoleClientProject.FullName, "DB"));

    public static FileInfo ProjectFile { get; } =
        new(Path.Combine(ConsoleClientProject.FullName, "ConsoleClient.csproj"));

    private static DirectoryInfo FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "global.json")) &&
                Directory.Exists(Path.Combine(directory.FullName, "src")))
            {
                return directory;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not find the repository root walking up from {AppContext.BaseDirectory}.");
    }
}
