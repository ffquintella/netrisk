using System;
using System.Linq;
using GUIClient.Tools.Hosts;
using JetBrains.Annotations;
using Material.Icons;
using Xunit;

namespace GUIClient.Tests.Tools.Hosts;

/// <summary>The service chip icons, row by row from the S38 §3.4 table.</summary>
[TestSubject(typeof(ServiceIconMap))]
public class ServiceIconMapTest
{
    [Theory]
    [InlineData("ssh", MaterialIconKind.Console)]
    [InlineData("telnet", MaterialIconKind.Console)]
    [InlineData("shell", MaterialIconKind.Console)]
    [InlineData("http", MaterialIconKind.Web)]
    [InlineData("https", MaterialIconKind.Web)]
    [InlineData("www", MaterialIconKind.Web)]
    [InlineData("http-alt", MaterialIconKind.Web)]
    [InlineData("smtp", MaterialIconKind.Email)]
    [InlineData("imap", MaterialIconKind.Email)]
    [InlineData("pop3", MaterialIconKind.Email)]
    [InlineData("dns", MaterialIconKind.Dns)]
    [InlineData("domain", MaterialIconKind.Dns)]
    [InlineData("mysql", MaterialIconKind.Database)]
    [InlineData("mariadb", MaterialIconKind.Database)]
    [InlineData("mssql", MaterialIconKind.Database)]
    [InlineData("postgres", MaterialIconKind.Database)]
    [InlineData("mongodb", MaterialIconKind.Database)]
    [InlineData("oracle", MaterialIconKind.Database)]
    [InlineData("redis", MaterialIconKind.Database)]
    [InlineData("rdp", MaterialIconKind.RemoteDesktop)]
    [InlineData("vnc", MaterialIconKind.RemoteDesktop)]
    [InlineData("ms-wbt-server", MaterialIconKind.RemoteDesktop)]
    [InlineData("ldap", MaterialIconKind.AccountKey)]
    [InlineData("ldaps", MaterialIconKind.AccountKey)]
    [InlineData("kerberos", MaterialIconKind.AccountKey)]
    [InlineData("smb", MaterialIconKind.FolderNetwork)]
    [InlineData("netbios", MaterialIconKind.FolderNetwork)]
    [InlineData("microsoft-ds", MaterialIconKind.FolderNetwork)]
    [InlineData("snmp", MaterialIconKind.Lan)]
    [InlineData("ntp", MaterialIconKind.Lan)]
    public void AWellKnownNameMapsToItsIconWhateverThePort(string name, MaterialIconKind expected)
    {
        Assert.Equal(expected, ServiceIconMap.KindFor(name, null));
        Assert.Equal(expected, ServiceIconMap.KindFor(name, 0));
        Assert.Equal(expected, ServiceIconMap.KindFor(name, 65000));
    }

    [Theory]
    [InlineData(22, MaterialIconKind.Console)]
    [InlineData(23, MaterialIconKind.Console)]
    [InlineData(514, MaterialIconKind.Console)]
    [InlineData(80, MaterialIconKind.Web)]
    [InlineData(443, MaterialIconKind.Web)]
    [InlineData(8080, MaterialIconKind.Web)]
    [InlineData(8443, MaterialIconKind.Web)]
    [InlineData(25, MaterialIconKind.Email)]
    [InlineData(143, MaterialIconKind.Email)]
    [InlineData(110, MaterialIconKind.Email)]
    [InlineData(465, MaterialIconKind.Email)]
    [InlineData(587, MaterialIconKind.Email)]
    [InlineData(993, MaterialIconKind.Email)]
    [InlineData(995, MaterialIconKind.Email)]
    [InlineData(53, MaterialIconKind.Dns)]
    [InlineData(3306, MaterialIconKind.Database)]
    [InlineData(1433, MaterialIconKind.Database)]
    [InlineData(5432, MaterialIconKind.Database)]
    [InlineData(27017, MaterialIconKind.Database)]
    [InlineData(1521, MaterialIconKind.Database)]
    [InlineData(6379, MaterialIconKind.Database)]
    [InlineData(3389, MaterialIconKind.RemoteDesktop)]
    [InlineData(5900, MaterialIconKind.RemoteDesktop)]
    [InlineData(389, MaterialIconKind.AccountKey)]
    [InlineData(636, MaterialIconKind.AccountKey)]
    [InlineData(88, MaterialIconKind.AccountKey)]
    [InlineData(139, MaterialIconKind.FolderNetwork)]
    [InlineData(445, MaterialIconKind.FolderNetwork)]
    [InlineData(161, MaterialIconKind.Lan)]
    [InlineData(123, MaterialIconKind.Lan)]
    public void AnUnknownOrGenericNameFallsBackToThePort(int port, MaterialIconKind expected)
    {
        Assert.Equal(expected, ServiceIconMap.KindFor("general", port));
        Assert.Equal(expected, ServiceIconMap.KindFor("something-new", port));
        Assert.Equal(expected, ServiceIconMap.KindFor(null, port));
    }

    [Fact]
    public void TheNameWinsOverAConflictingPort()
    {
        // A scanner that says http on the SSH port knows more than the port number does.
        Assert.Equal(MaterialIconKind.Web, ServiceIconMap.KindFor("http", 22));
    }

    [Theory]
    [InlineData("ssl/http", MaterialIconKind.Web)]
    [InlineData("tls/imap", MaterialIconKind.Email)]
    [InlineData("SSH", MaterialIconKind.Console)]
    [InlineData("  MySQL ", MaterialIconKind.Database)]
    [InlineData("http?", MaterialIconKind.Web)]
    public void NamesAreMatchedCaseInsensitivelyAndWithoutTheirTlsWrapper(string name, MaterialIconKind expected)
    {
        Assert.Equal(expected, ServiceIconMap.KindFor(name, null));
    }

    [Theory]
    [InlineData("general", 0)]
    [InlineData("general", null)]
    [InlineData(null, null)]
    [InlineData("", 0)]
    [InlineData("custom-agent", 9999)]
    public void AnythingElseIsTheGenericNetworkIcon(string? name, int? port)
    {
        Assert.Equal(MaterialIconKind.Lan, ServiceIconMap.KindFor(name, port));
        Assert.Equal(ServiceIconMap.Fallback, ServiceIconMap.KindFor(name, port));
    }

    [Fact]
    public void EveryKindTheMapReturnsIsARealIcon()
    {
        Assert.All(ServiceIconMap.AllKinds().Distinct(), kind => Assert.True(Enum.IsDefined(kind)));
    }
}
