using GUIClient.Tools.RiskIndex;
using JetBrains.Annotations;
using Xunit;

namespace GUIClient.Tests.Tools.RiskIndex;

/// <summary>
/// T251 (S40 §10.5): an API refusal reads as <c>RiskApi_‹code›MSG</c> when the client knows the code, and as the server's own
/// sentence when it does not — never as a generic failure that hides the reason.
/// </summary>
[TestSubject(typeof(RiskApiErrorText))]
public class RiskApiErrorTextTest
{
    [Fact]
    public void TestAKnownCodeIsTranslated()
    {
        Assert.Equal("O índice está em piloto em sombra: só o comitê e os administradores o veem.",
            RiskApiErrorText.Translate(RiskApiCodes.InShadow, "The index is in its shadow pilot.", RiskIndexTestText.Portuguese,
                "fallback"));
    }

    [Fact]
    public void TestAnUnknownCodeKeepsTheServersSentence()
    {
        Assert.Equal("Something the client has never heard of.",
            RiskApiErrorText.Translate("brand_new_code", "Something the client has never heard of.", RiskIndexTestText.Portuguese,
                "fallback"));
        Assert.Equal("Server sentence.", RiskApiErrorText.Translate(null, "Server sentence.", RiskIndexTestText.Portuguese, "fallback"));
    }

    [Fact]
    public void TestNoCodeAndNoSentenceIsTheFallback()
    {
        Assert.Equal("fallback", RiskApiErrorText.Translate("brand_new_code", " ", RiskIndexTestText.Portuguese, "fallback"));
        Assert.Equal("fallback", RiskApiErrorText.Translate(null, null, RiskIndexTestText.Portuguese, "fallback"));
    }

    [Fact]
    public void TestTheResourceKeyFollowsTheConvention()
    {
        Assert.Equal("RiskApi_derived_computation_busyMSG", RiskApiErrorText.ResourceKey(RiskApiCodes.DerivedBusy));
    }
}
