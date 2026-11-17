using System;
using System.IO;
using JetBrains.Annotations;
using Tools.Security;
using Xunit;

namespace Tools.Tests.Security;

/// <summary>
/// Track 7 finding NR-2026-006: the chunked-upload endpoints passed a caller-supplied file id
/// straight to <c>Path.Combine</c>, so any authenticated user could write files outside the upload
/// directory. These tests pin the guard that replaced it.
/// </summary>
[TestSubject(typeof(SafePathTool))]
public class SafePathToolTest
{
    [Theory]
    [InlineData("2f5c1e7a-0f3f-4a4b-8b1e-7fa2c9d0e3b1")]
    [InlineData("abc")]
    [InlineData("A1_b2-c3.dat")]
    [InlineData("1.part")]
    public void AcceptsWellFormedSegments(string segment) =>
        Assert.True(SafePathTool.IsSafeSegment(segment));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("..")]
    [InlineData("../etc/passwd")]
    [InlineData("..\\windows\\system32")]
    [InlineData("a/../../b")]
    [InlineData("/etc/passwd")]
    [InlineData("C:\\Windows")]
    [InlineData(".hidden")]
    [InlineData("with space")]
    [InlineData("semi;colon")]
    [InlineData("null\0byte")]
    [InlineData("percent%2e%2e")]
    [InlineData("stream:name")]
    public void RejectsAnythingThatCouldLeaveTheDirectory(string? segment) =>
        Assert.False(SafePathTool.IsSafeSegment(segment));

    [Fact]
    public void RejectsOverlongSegments() =>
        Assert.False(SafePathTool.IsSafeSegment(new string('a', 129)));

    [Fact]
    public void CombineWithinBuildsThePathWhenTheSegmentsAreSafe()
    {
        var root = Path.Combine(Path.GetTempPath(), "netrisk-safepath-test");

        var combined = SafePathTool.CombineWithin(root, "upload-1", "3.part");

        Assert.Equal(Path.GetFullPath(Path.Combine(root, "upload-1", "3.part")), combined);
    }

    /// <summary>
    /// The regression assertion. On the pre-fix code this exact value reached
    /// <c>Directory.CreateDirectory</c> and <c>File.WriteAllBytes</c>.
    /// </summary>
    [Fact]
    public void CombineWithinRefusesToEscapeTheBase()
    {
        var root = Path.Combine(Path.GetTempPath(), "netrisk-safepath-test");

        Assert.Throws<ArgumentException>(
            () => SafePathTool.CombineWithin(root, "../../../../etc", "1.part"));
        Assert.Throws<ArgumentException>(
            () => SafePathTool.CombineWithin(root, "/etc/cron.d"));
    }

    /// <summary>
    /// A sibling directory whose name merely starts with the base name must not count as inside it —
    /// the classic off-by-one in prefix containment checks.
    /// </summary>
    [Fact]
    public void CombineWithinDoesNotTreatASiblingPrefixAsContained()
    {
        var root = Path.Combine(Path.GetTempPath(), "netrisk-api");

        // "netrisk-api-evil" starts with "netrisk-api" as a string but is not inside it.
        Assert.Throws<ArgumentException>(
            () => SafePathTool.CombineWithin(root, "..", "netrisk-api-evil"));
    }

    [Fact]
    public void CombineWithinRequiresABase()
    {
        Assert.Throws<ArgumentException>(() => SafePathTool.CombineWithin("", "x"));
        Assert.Throws<ArgumentException>(() => SafePathTool.CombineWithin("   ", "x"));
    }

    // ---- FileWithin / IsSafeFileName (Track 11, M65: the AI clients' configuration files) ------------------------

    [Theory]
    [InlineData(".claude.json")]
    [InlineData("claude_desktop_config.json")]
    [InlineData("mcp-config.json")]
    [InlineData(".claude.json.netrisk-20261117T101500123Z.bak")]
    public void FileNamesMayStartWithOneDot(string fileName) =>
        Assert.True(SafePathTool.IsSafeFileName(fileName));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("...")]
    [InlineData("..claude.json")]
    [InlineData(".a..b")]
    [InlineData("../.claude.json")]
    [InlineData("dir/.claude.json")]
    [InlineData("dir\\.claude.json")]
    [InlineData("/etc/passwd")]
    [InlineData("with space.json")]
    [InlineData("stream:name")]
    [InlineData("null\0byte")]
    public void FileNamesStillRefuseEverythingElse(string? fileName) =>
        Assert.False(SafePathTool.IsSafeFileName(fileName));

    [Fact]
    public void FileNamesAreBoundedLikeSegments() =>
        Assert.False(SafePathTool.IsSafeFileName("." + new string('a', 128)));

    [Fact]
    public void FileWithinBuildsTheDotfilePathDirectlyInsideTheBase()
    {
        var root = Path.Combine(Path.GetTempPath(), "netrisk-safepath-home");

        Assert.Equal(Path.GetFullPath(Path.Combine(root, ".claude.json")), SafePathTool.FileWithin(root, ".claude.json"));
        Assert.Equal(Path.GetFullPath(Path.Combine(root, ".claude.json")),
            SafePathTool.FileWithin(root + Path.DirectorySeparatorChar, ".claude.json"));
    }

    [Fact]
    public void FileWithinRefusesToLeaveTheBaseOrDescendIntoIt()
    {
        var root = Path.Combine(Path.GetTempPath(), "netrisk-safepath-home");

        Assert.Throws<ArgumentException>(() => SafePathTool.FileWithin(root, "../.claude.json"));
        Assert.Throws<ArgumentException>(() => SafePathTool.FileWithin(root, ".."));
        Assert.Throws<ArgumentException>(() => SafePathTool.FileWithin(root, "sub/mcp.json"));
        Assert.Throws<ArgumentException>(() => SafePathTool.FileWithin(root, "/etc/passwd"));
        Assert.Throws<ArgumentException>(() => SafePathTool.FileWithin("", ".claude.json"));
    }
}
