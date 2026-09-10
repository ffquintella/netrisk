using System.IO.Compression;
using Tools.Security;

namespace ServerServices.Plugins;

/// <summary>One file inside a plugin package, as the validator sees it.</summary>
/// <param name="FullName">The entry name exactly as the archive records it.</param>
/// <param name="Length">Uncompressed size in bytes.</param>
public readonly record struct PluginPackageEntry(string FullName, long Length);

/// <summary>
/// What the validator concluded about a package.
/// </summary>
/// <param name="IsValid">Whether the package may be extracted.</param>
/// <param name="Error">Why not, when it may not. Written for an operator, not a developer.</param>
/// <param name="RootFolder">
/// The single top-level folder every entry sits under, when there is one. Archives produced by
/// "compress this folder" have one and archives produced by "compress these files" do not, and the
/// difference must not change where the assembly lands.
/// </param>
/// <param name="Files">The file entries with <paramref name="RootFolder"/> stripped off.</param>
public sealed record PluginPackageValidation(
    bool IsValid,
    string? Error,
    string? RootFolder,
    IReadOnlyList<string> Files)
{
    public static PluginPackageValidation Invalid(string error) =>
        new(false, error, null, []);
}

/// <summary>
/// Validates and unpacks an uploaded plugin package into the host's <c>Plugins</c> directory
/// (administration → plugins → upload).
///
/// <para><b>Why the validation is a separate, pure step.</b> Everything that can make an upload
/// dangerous is decidable from the archive's table of contents — an entry that escapes the
/// destination (zip slip), an archive that expands to more than the disk holds (zip bomb), an
/// archive with no plugin assembly in it at all. Deciding it before a single byte is written means
/// the rejection path never has to clean up after itself, and it means the rules are testable
/// without a filesystem.</para>
///
/// <para><b>What this deliberately does not do.</b> It does not make an uploaded plugin safe to
/// run. A loaded plugin executes with the API's full authority (see
/// <see cref="ServerServices.Security.PluginSignatureVerifier"/> and finding NR-2026-027), so
/// uploading one is exactly as privileged as dropping a DLL on the server by hand — which is why
/// the endpoint is administrator-only and why the signature policy still applies at load time.
/// Upload is a convenience over scp, not a new trust boundary.</para>
/// </summary>
public static class PluginPackageInstaller
{
    /// <summary>Largest package accepted, compressed. A plugin with its dependencies is a few MB.</summary>
    public const long MaxPackageBytes = 100L * 1024 * 1024;

    /// <summary>Largest total expansion accepted. Bounds a zip bomb before anything is written.</summary>
    public const long MaxUncompressedBytes = 400L * 1024 * 1024;

    /// <summary>Most entries accepted. Bounds an archive of a million empty files.</summary>
    public const int MaxEntries = 5000;

    /// <summary>The suffix the loader discovers by — see <c>PluginsService.GetPluginsDlls</c>.</summary>
    public const string PluginAssemblySuffix = "Plugin.dll";

    /// <summary>
    /// The assembly a package must never carry. Shipping it gives the plugin a second copy of the
    /// shared interfaces, so <c>IsAssignableFrom</c> fails and the host ignores the plugin with no
    /// error anywhere. Refusing the package converts that silent no-op into a message.
    /// </summary>
    public const string ForbiddenAssembly = "Contracts.dll";

    /// <summary>
    /// Turns an uploaded file name into the directory name the package installs under.
    /// Returns null when nothing usable survives sanitisation.
    /// </summary>
    public static string? DerivePackageName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;

        // Only the leaf matters: a browser or client may send a full path as the file name.
        var leaf = fileName.Replace('\\', '/').Split('/').LastOrDefault();
        if (string.IsNullOrWhiteSpace(leaf)) return null;

        if (leaf.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            leaf = leaf[..^4];

        var sanitized = new string(leaf
            .Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_')
            .ToArray())
            .Trim('.', '_');

        if (sanitized.Length > 128) sanitized = sanitized[..128];

        return SafePathTool.IsSafeSegment(sanitized) ? sanitized : null;
    }

    /// <summary>
    /// Decides whether the archive described by <paramref name="entries"/> may be extracted.
    /// Never touches the filesystem.
    /// </summary>
    public static PluginPackageValidation Validate(IReadOnlyList<PluginPackageEntry> entries)
    {
        if (entries.Count == 0)
            return PluginPackageValidation.Invalid("The package is empty.");

        if (entries.Count > MaxEntries)
            return PluginPackageValidation.Invalid(
                $"The package has {entries.Count} entries, more than the {MaxEntries} allowed.");

        long total = 0;
        var files = new List<string>();

        foreach (var entry in entries)
        {
            var name = entry.FullName.Replace('\\', '/');

            // A trailing slash is the archive's way of recording a directory. Directories are
            // created implicitly on extraction, so they carry no information here.
            if (name.EndsWith('/')) continue;

            if (string.IsNullOrWhiteSpace(name))
                return PluginPackageValidation.Invalid("The package contains an entry with no name.");

            if (name.StartsWith('/') || (name.Length > 1 && name[1] == ':'))
                return PluginPackageValidation.Invalid(
                    $"The package contains an absolute path ('{entry.FullName}').");

            var segments = name.Split('/', StringSplitOptions.RemoveEmptyEntries);

            foreach (var segment in segments)
                if (!SafePathTool.IsSafeSegment(segment))
                    return PluginPackageValidation.Invalid(
                        $"The package contains an entry this server will not write ('{entry.FullName}'). " +
                        "Entry names may use letters, digits, dashes, underscores and dots only.");

            if (string.Equals(segments[^1], ForbiddenAssembly, StringComparison.OrdinalIgnoreCase))
                return PluginPackageValidation.Invalid(
                    $"The package ships {ForbiddenAssembly}. A plugin must reference it without copying " +
                    "it, or the host will load a second copy of the shared interfaces and ignore the " +
                    "plugin silently. Rebuild with Private=\"false\" and ExcludeAssets=\"runtime\".");

            if (entry.Length < 0)
                return PluginPackageValidation.Invalid(
                    $"The package declares a negative size for '{entry.FullName}'.");

            total += entry.Length;

            if (total > MaxUncompressedBytes)
                return PluginPackageValidation.Invalid(
                    $"The package expands to more than {MaxUncompressedBytes / (1024 * 1024)} MB.");

            files.Add(string.Join('/', segments));
        }

        if (files.Count == 0)
            return PluginPackageValidation.Invalid("The package contains no files.");

        var root = CommonRootFolder(files);

        var stripped = root is null
            ? files
            : files.Select(f => f[(root.Length + 1)..]).ToList();

        // The loader only globs the top level of each plugin directory, so an assembly buried one
        // folder deeper would install without error and never load.
        var hasPluginAssembly = stripped.Any(f =>
            !f.Contains('/') && f.EndsWith(PluginAssemblySuffix, StringComparison.OrdinalIgnoreCase));

        if (!hasPluginAssembly)
            return PluginPackageValidation.Invalid(
                $"The package has no *{PluginAssemblySuffix} at its top level. NetRisk discovers a " +
                $"plugin by that file-name suffix.");

        return new PluginPackageValidation(true, null, root, stripped);
    }

    /// <summary>
    /// The single folder every file sits under, or null when the files are at the archive root or
    /// spread across several folders.
    /// </summary>
    private static string? CommonRootFolder(IReadOnlyList<string> files)
    {
        string? candidate = null;

        foreach (var file in files)
        {
            var slash = file.IndexOf('/');
            if (slash <= 0) return null;

            var head = file[..slash];

            if (candidate is null) candidate = head;
            else if (!string.Equals(candidate, head, StringComparison.Ordinal)) return null;
        }

        return candidate;
    }

    /// <summary>
    /// Reads <paramref name="archive"/>'s table of contents in the shape <see cref="Validate"/> wants.
    /// </summary>
    public static List<PluginPackageEntry> Describe(ZipArchive archive) =>
        archive.Entries.Select(e => new PluginPackageEntry(e.FullName, e.Length)).ToList();

    /// <summary>
    /// Extracts a package that <see cref="Validate"/> has already accepted into
    /// <paramref name="targetDirectory"/>, and returns the relative paths written.
    /// </summary>
    /// <remarks>
    /// Each destination goes through <see cref="SafePathTool.CombineWithin"/> a second time even
    /// though the validator already checked the names. That is deliberate: this is the last line
    /// before a write, and it is the only check that also catches a symlink planted inside the
    /// target directory.
    /// </remarks>
    public static List<string> Extract(ZipArchive archive, PluginPackageValidation validation,
        string targetDirectory)
    {
        if (!validation.IsValid)
            throw new InvalidOperationException("Refusing to extract a package that failed validation.");

        Directory.CreateDirectory(targetDirectory);

        var written = new List<string>();
        var prefix = validation.RootFolder is null ? string.Empty : validation.RootFolder + "/";

        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            if (name.EndsWith('/')) continue;

            var segments = name.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var relative = string.Join('/', segments);

            if (prefix.Length > 0)
            {
                if (!relative.StartsWith(prefix, StringComparison.Ordinal)) continue;
                relative = relative[prefix.Length..];
            }

            var destination = SafePathTool.CombineWithin(targetDirectory, relative.Split('/'));

            var parent = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

            entry.ExtractToFile(destination, overwrite: true);
            written.Add(relative);
        }

        return written;
    }
}
