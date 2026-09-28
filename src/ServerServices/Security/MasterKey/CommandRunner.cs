using System.Diagnostics;

namespace ServerServices.Security.MasterKey;

/// <summary>
/// Runs a platform key-management tool — <c>security</c> on macOS, the <c>tpm2_*</c> family on
/// Linux — and collects its output.
///
/// <para>
/// Arguments go through <see cref="ProcessStartInfo.ArgumentList"/>, never a concatenated command
/// line, so nothing here can be turned into shell injection by a path with a space in it.
/// </para>
/// </summary>
internal static class CommandRunner
{
    /// <summary>Longer than any of these tools should ever take; short enough that a wedged TPM does not hang startup.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    internal sealed record Result(bool Success, string StandardOutput, string StandardError);

    /// <summary>
    /// Runs <paramref name="fileName"/>. Never throws for a non-zero exit or a missing binary — an
    /// absent tool is the normal case on a host without a TPM, and the caller's answer to both is
    /// "fall back to the next store".
    /// </summary>
    internal static Result Run(string fileName, IReadOnlyList<string> arguments, byte[]? standardInput = null)
    {
        var info = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = standardInput is not null,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments) info.ArgumentList.Add(argument);

        try
        {
            using var process = Process.Start(info);
            if (process is null) return new Result(false, string.Empty, "the process could not be started");

            if (standardInput is not null)
            {
                process.StandardInput.BaseStream.Write(standardInput, 0, standardInput.Length);
                process.StandardInput.BaseStream.Flush();
                process.StandardInput.Close();
            }

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();

            if (!process.WaitForExit(Timeout))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
                return new Result(false, stdout, $"{fileName} did not finish within {Timeout.TotalSeconds:0} seconds");
            }

            return new Result(process.ExitCode == 0, stdout, stderr);
        }
        catch (Exception ex)
        {
            return new Result(false, string.Empty, ex.Message);
        }
    }

    /// <summary>Whether <paramref name="fileName"/> resolves to something executable on PATH.</summary>
    internal static bool Exists(string fileName)
    {
        var probe = Run(OperatingSystem.IsWindows() ? "where" : "/usr/bin/which", [fileName]);
        return probe.Success && !string.IsNullOrWhiteSpace(probe.StandardOutput);
    }
}
