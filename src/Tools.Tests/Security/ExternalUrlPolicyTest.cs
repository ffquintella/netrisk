using JetBrains.Annotations;
using Tools.Security;
using Xunit;

namespace Tools.Tests.Security;

/// <summary>
/// Track 7 finding NR-2026-023 — the desktop client handed scan-report URLs to a shell-executing
/// <c>Process.Start</c>.
///
/// The macOS branch was <c>Process.Start("open", "-u " + url)</c>: the second parameter is one string
/// the operating system re-splits, so a URL containing a space smuggled further arguments to
/// <c>open</c>, <c>-a</c> among them. The Windows branch set an arbitrary <c>FileName</c> with
/// <c>UseShellExecute = true</c>, which launches a local path or an executable just as readily as it
/// opens a link.
/// </summary>
[TestSubject(typeof(ExternalUrlPolicy))]
public class ExternalUrlPolicyTest
{
    [Theory]
    [InlineData("https://nvd.nist.gov/vuln/detail/CVE-2026-0001")]
    [InlineData("http://example.com/advisory?id=1&x=2#frag")]
    [InlineData("https://example.com:8443/path")]
    public void AnOrdinaryWebLinkIsOpenable(string url)
    {
        Assert.True(ExternalUrlPolicy.TryParseOpenable(url, out var parsed));
        Assert.NotNull(parsed);
    }

    /// <summary>The regression assertion for the macOS argument-smuggling case.</summary>
    [Theory]
    [InlineData("https://example.com -a Calculator")]
    [InlineData("https://example.com\t-a /Applications/Terminal.app")]
    [InlineData("https://example.com\n-a Calculator")]
    [InlineData("https://example.com\r\n-a Calculator")]
    public void AUrlCarryingWhitespaceIsRefused(string url) =>
        Assert.False(ExternalUrlPolicy.IsOpenable(url));

    /// <summary>The regression assertion for the Windows shell-execute case.</summary>
    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("file://C:/Windows/System32/cmd.exe")]
    [InlineData("smb://attacker.example.com/share")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html;base64,PHNjcmlwdD4=")]
    [InlineData("ms-msdt:/id")]
    [InlineData("vbscript:msgbox")]
    public void ANonWebSchemeIsRefused(string url) =>
        Assert.False(ExternalUrlPolicy.IsOpenable(url));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("cmd.exe")]
    [InlineData("C:\\Windows\\System32\\calc.exe")]
    [InlineData("/Applications/Calculator.app")]
    [InlineData("\\\\attacker.example.com\\share\\payload.exe")]
    [InlineData("example.com/no-scheme")]
    [InlineData("//example.com/protocol-relative")]
    public void AnythingThatIsNotAnAbsoluteWebUrlIsRefused(string? url) =>
        Assert.False(ExternalUrlPolicy.IsOpenable(url));

    [Fact]
    public void AnAbsurdlyLongUrlIsRefused() =>
        Assert.False(ExternalUrlPolicy.IsOpenable("https://example.com/" + new string('a', 5000)));

    /// <summary>
    /// A control character is invisible in the UI that shows the link and is exactly what a
    /// command-line splitter reacts to.
    /// </summary>
    [Fact]
    public void AControlCharacterIsRefused() =>
        Assert.False(ExternalUrlPolicy.IsOpenable("https://example.com/\u0000payload"));

    /// <summary>
    /// Callers use the parsed <c>AbsoluteUri</c> rather than the original string, so the value handed
    /// to the launcher is one the framework produced.
    /// </summary>
    [Fact]
    public void TheParsedFormIsReturnedForTheCallerToUse()
    {
        Assert.True(ExternalUrlPolicy.TryParseOpenable("HTTPS://Example.COM/Path", out var parsed));
        Assert.Equal("https://example.com/Path", parsed!.AbsoluteUri);
    }

    [Fact]
    public void TheParsedOutputIsNullWhenRefused()
    {
        Assert.False(ExternalUrlPolicy.TryParseOpenable("file:///etc/passwd", out var parsed));
        Assert.Null(parsed);
    }
}
