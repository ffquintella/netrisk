using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Xml.Linq;
using GUIClient.Tools.Track9;
using JetBrains.Annotations;
using Model.Authentication;
using Model.DecisionCycle;
using Model.Exceptions;
using Xunit;

namespace GUIClient.Tests.Track9;

/// <summary>
/// Stage 9.9 (S50 R10): the user and role editors explain a refused save instead of showing "Error saving users", and
/// do not send a save that would put the caller's own account in the third line.
/// </summary>
[TestSubject(typeof(Track9UserAdministrationRefusal))]
public class Track9UserAdministrationRefusalTest
{
    private static AuthenticatedUserInfo Admin(int id, params string[] permissions) => new()
    {
        UserId = id, UserAccount = "admin", UserRole = "Administrator", IsAdmin = true,
        UserPermissions = permissions.ToList()
    };

    /// <summary>
    /// Regression (nr-gui20261013.log): an administrator holding the marker saved a user; PUT /Users/{id} answered 403
    /// and the screen said only "Error saving users". The refusal is now explained as the third line's.
    /// </summary>
    [Fact]
    public void TestAForbiddenSaveByTheThirdLineIsExplainedAsTheThirdLines()
    {
        var caller = Admin(1, "riskmanagement", ThirdLineAssurance.PermissionKey);
        var refusal = new InvalidHttpRequestException("Error saving user", "/Users/1", "PUT")
            { StatusCode = HttpStatusCode.Forbidden };

        Assert.True(Track9UserAdministrationRefusal.CallerIsReadOnly(caller));
        Assert.Equal(Track9UserAdministrationRefusal.ThirdLineReadOnlyKey,
            Track9UserAdministrationRefusal.MessageKey(refusal, caller));
    }

    [Fact]
    public void TestTheThirdLinesForbiddenSaveIsRecognisedWhereverRestSharpPutsTheStatus()
    {
        var caller = Admin(1, ThirdLineAssurance.PermissionKey);
        var wrapped = new RestComunicationException("Error saving users",
            new HttpRequestException("Request failed with status code Forbidden", null, HttpStatusCode.Forbidden));

        Assert.Equal(Track9UserAdministrationRefusal.ThirdLineReadOnlyKey,
            Track9UserAdministrationRefusal.MessageKey(wrapped, caller));
    }

    [Fact]
    public void TestAForbiddenSaveByAnyoneElseIsAPlainRefusal()
    {
        var refusal = new InvalidHttpRequestException("Error saving user", "/Users/2", "PUT")
            { StatusCode = HttpStatusCode.Forbidden };

        Assert.False(Track9UserAdministrationRefusal.CallerIsReadOnly(Admin(1, "riskmanagement")));
        Assert.Equal(Track9UserAdministrationRefusal.ForbiddenKey,
            Track9UserAdministrationRefusal.MessageKey(refusal, Admin(1, "riskmanagement")));
        Assert.Equal(Track9UserAdministrationRefusal.ForbiddenKey,
            Track9UserAdministrationRefusal.MessageKey(refusal, null));
    }

    [Fact]
    public void TestTheSelfAppointmentRuleIsExplained()
    {
        var refusal = new RuleBrokenException("not your own account", ThirdLineAssurance.SelfAppointmentRule);

        Assert.Equal(Track9UserAdministrationRefusal.SelfAppointmentKey,
            Track9UserAdministrationRefusal.MessageKey(refusal, Admin(1)));
    }

    [Fact]
    public void TestOtherFailuresAreLeftToTheGenericMessage()
    {
        Assert.Null(Track9UserAdministrationRefusal.MessageKey(
            new RestComunicationException("Error saving users", new HttpRequestException("boom")), Admin(1)));
        Assert.Null(Track9UserAdministrationRefusal.MessageKey(
            new InvalidHttpRequestException("Error saving user", "/Users/2", "PUT") { StatusCode = HttpStatusCode.NotFound },
            Admin(1)));
        Assert.Null(Track9UserAdministrationRefusal.MessageKey(
            new RuleBrokenException("another rule", ThirdLineAssurance.CannotApproveRule), Admin(1)));
        Assert.Null(Track9UserAdministrationRefusal.MessageKey(null, Admin(1)));
    }

    [Fact]
    public void TestTickingTheMarkerOnTheCallersOwnAccountIsCaughtBeforeAnythingIsSent()
    {
        var caller = Admin(1, "riskmanagement");

        Assert.True(Track9UserAdministrationRefusal.AppointsSelf(1, caller, grantsMarker: true));
        Assert.False(Track9UserAdministrationRefusal.AppointsSelf(1, caller, grantsMarker: false));
        Assert.False(Track9UserAdministrationRefusal.AppointsSelf(2, caller, grantsMarker: true));
        Assert.False(Track9UserAdministrationRefusal.AppointsSelf(1, null, grantsMarker: true));
    }

    [Fact]
    public void TestEveryMessageKeyExistsInEveryResource()
    {
        string[] keys =
        [
            Track9UserAdministrationRefusal.ThirdLineReadOnlyKey,
            Track9UserAdministrationRefusal.SelfAppointmentKey,
            Track9UserAdministrationRefusal.ForbiddenKey
        ];

        foreach (var resource in new[] { "Localization.resx", "Localization.en-US.resx", "Localization.pt-BR.resx" })
        {
            var present = XDocument.Load(Path.Combine(GuiClientRoot(), "Resources", resource)).Descendants("data")
                .Where(element => !string.IsNullOrWhiteSpace((string?)element.Element("value")))
                .Select(element => (string?)element.Attribute("name"))
                .ToHashSet(StringComparer.Ordinal);
            var missing = keys.Where(key => !present.Contains(key)).ToList();
            Assert.True(missing.Count == 0, $"{resource} is missing {string.Join(", ", missing)}");
        }
    }

    private static string GuiClientRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "GUIClient");
            if (File.Exists(Path.Combine(path, "GUIClient.csproj"))) return path;
        }
        throw new DirectoryNotFoundException("GUIClient project was not found.");
    }
}
