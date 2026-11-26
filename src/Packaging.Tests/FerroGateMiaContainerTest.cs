using System;
using System.Diagnostics;
using System.IO;
using Xunit;

namespace Packaging.Tests;

/// <summary>
/// The API and BackgroundJobs containers carry the FerroGate MIA client so NetRisk can consume data
/// FerroGate protects. Two things must hold and neither is visible from a Mac build host: the images
/// install the agent and expose its configuration, and <c>mia-container.sh</c> decides correctly
/// when to start it and never overwrites a configuration the operator mounted.
/// </summary>
public class FerroGateMiaContainerTest
{
    private static string Docker(params string[] parts) =>
        Path.Combine([RepositoryPaths.RepositoryRoot, "build", "Docker", .. parts]);

    public static TheoryData<string, string> Images() => new()
    {
        { "Dockerfile-API", "entrypoint-api.sh" },
        { "Dockerfile-BackgroundJobs", "entrypoint-backgroundjobs.sh" },
    };

    [Theory]
    [MemberData(nameof(Images))]
    public void TestImageInstallsTheAgentAndExposesItsConfiguration(string dockerfile, string entrypoint)
    {
        var image = File.ReadAllText(Docker(dockerfile));

        Assert.Contains("ARG MIA_VERSION=", image);
        Assert.Contains("ARG MIA_RPM_URL=", image);
        Assert.Contains("ARG MIA_RPM_SHA256=", image);
        Assert.Contains("dnf install -y /tmp/ferrogate-mia.rpm", image);
        Assert.Contains("rpm -q ferrogate-mia", image);
        Assert.Contains("VOLUME /etc/ferrogate", image);
        Assert.Contains("VOLUME /var/lib/ferrogate", image);
        Assert.Contains("COPY mia-container.sh /opt/scripts/mia-container.sh", image);

        var script = File.ReadAllText(Docker(entrypoint));
        Assert.Contains(". /opt/scripts/mia-container.sh", script);
        Assert.Contains("start_mia", script);
    }

    [Fact]
    public void TestTheBuildStagesTheMiaScriptForBothImages()
    {
        // Build.cs copies each entrypoint into the Docker context; the script must ride along or
        // `COPY mia-container.sh` fails the image build.
        var build = File.ReadAllText(Path.Combine(RepositoryPaths.RepositoryRoot, "build", "Build.cs"));
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(
            build, "Copy\\(BuildWorkDirectory / \"mia-container\\.sh\"").Count);
    }

    [Theory]
    [InlineData("", "auto", false)]
    [InlineData("https://cmis.example.com:8443", "auto", true)]
    [InlineData("https://cmis.example.com:8443", "0", false)]
    [InlineData("", "1", true)]
    public void TestAgentStartsOnlyWhenAnEndpointIsConfiguredOrForced(string endpoint, string enable, bool expected)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "the entrypoints are bash scripts");

        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var result = RunBash(dir, endpoint, enable, "mia_enabled && echo yes || echo no");
            Assert.Equal(expected ? "yes" : "no", result);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void TestAnEndpointInTheMountedFileEnablesTheAgent()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "the entrypoints are bash scripts");

        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "mia.toml"), "[cmis]\nendpoint = \"https://cmis:8443\"\n");
            Assert.Equal("yes", RunBash(dir, "", "auto", "mia_enabled && echo yes || echo no"));

            // A commented-out template line (the packaged default) must not count.
            File.WriteAllText(Path.Combine(dir, "mia.toml"), "[cmis]\n#endpoint = \"https://cmis:8443\"\n");
            Assert.Equal("no", RunBash(dir, "", "auto", "mia_enabled && echo yes || echo no"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void TestSeedingFillsMissingFilesAndKeepsAMountedOne()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "the entrypoints are bash scripts");

        var dir = Directory.CreateTempSubdirectory().FullName;
        var defaults = Directory.CreateTempSubdirectory().FullName;
        try
        {
            File.WriteAllText(Path.Combine(defaults, "mia.toml"), "default");
            File.WriteAllText(Path.Combine(defaults, "mia.env"), "default-env");
            File.WriteAllText(Path.Combine(dir, "mia.toml"), "mounted");

            RunBash(dir, "", "auto", "mia_seed_config", defaults);

            Assert.Equal("mounted", File.ReadAllText(Path.Combine(dir, "mia.toml")));
            Assert.Equal("default-env", File.ReadAllText(Path.Combine(dir, "mia.env")));
        }
        finally { Directory.Delete(dir, true); Directory.Delete(defaults, true); }
    }

    [Fact]
    public void TestRequiredAgentAbortsWhenTheBinaryIsMissing()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "the entrypoints are bash scripts");

        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var output = RunBash(dir, "https://cmis:8443", "auto",
                "PATH=/nonexistent; MIA_REQUIRED=1 start_mia; echo survived");
            Assert.DoesNotContain("survived", output);
        }
        finally { Directory.Delete(dir, true); }
    }

    private static string RunBash(string configDir, string endpoint, string enable, string body, string? defaults = null)
    {
        var script = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".sh");
        File.WriteAllText(script, string.Join("\n",
            $". '{Docker("mia-container.sh")}'",
            body) + "\n");

        try
        {
            var start = new ProcessStartInfo("/bin/bash")
            {
                ArgumentList = { script },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            start.Environment["MIA_CONFIG_DIR"] = configDir;
            start.Environment["MIA_DEFAULTS_DIR"] = defaults ?? Path.Combine(configDir, "no-defaults");
            start.Environment["MIA_ENABLE"] = enable;
            start.Environment["FERROGATE_CMIS_ENDPOINT"] = endpoint;
            start.Environment.Remove("FERROGATE_CMIS_SRV");
            start.Environment.Remove("FERROGATE_CONFIG");

            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit();
            return stdout.Trim();
        }
        finally { File.Delete(script); }
    }
}
