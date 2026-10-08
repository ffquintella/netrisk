using JetBrains.Annotations;
using Tools.DataCatalogue;
using Xunit;

namespace Tools.Tests.DataCatalogue;

/// <summary>
/// Stage 9.11 (S52 D13, §8 G1–G2) — the catalogue describes kinds of data; a text carrying an actual e-mail address or a
/// formatted CPF was pasted from a record and is refused. Narrow on purpose (S52 R1): the kinds themselves pass.
/// </summary>
[TestSubject(typeof(PersonalValueGuard))]
public class PersonalValueGuardTest
{
    /// <summary>G1 — an e-mail address or a formatted CPF anywhere in the text.</summary>
    [Theory]
    [InlineData("Students such as maria.souza@fgv.br")]
    [InlineData("contact: joao+lgpd@exemplo.com.br")]
    [InlineData("CPF 123.456.789-09 of the applicant")]
    [InlineData("123.456.789-09")]
    public void TestG1_APersonalValueIsCaught(string text) => Assert.True(PersonalValueGuard.LooksLikePersonalValue(text));

    /// <summary>G2 — the kinds, ordinary text, numbers that are not a formatted CPF and nothing at all pass.</summary>
    [Theory]
    [InlineData("Name, CPF, institutional e-mail, grades")]
    [InlineData("Undergraduate students and their guardians")]
    [InlineData("Contract 031/2026, clause 12.4")]
    [InlineData("Law 13.709/2018 art. 46")]
    [InlineData("12345678909")]
    [InlineData("1123.456.789-091")]
    [InlineData("e-mail @ domain")]
    [InlineData("")]
    [InlineData(null)]
    public void TestG2_KindsPass(string? text) => Assert.False(PersonalValueGuard.LooksLikePersonalValue(text));
}
