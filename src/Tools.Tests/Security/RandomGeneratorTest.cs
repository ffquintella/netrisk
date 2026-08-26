using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using JetBrains.Annotations;
using Tools;
using Xunit;

namespace Tools.Tests.Security;

/// <summary>
/// Track 7 finding NR-2026-002: <see cref="RandomGenerator"/> produced the JWT signing key,
/// password-reset link keys, file access keys and generated passwords from one shared
/// <see cref="Random"/>. Because several of those values are handed to the requester by design, an
/// attacker could recover the generator state from tokens they were legitimately given and predict
/// everyone else's.
/// </summary>
[TestSubject(typeof(RandomGenerator))]
public class RandomGeneratorTest
{
    /// <summary>
    /// The regression assertion. A non-cryptographic generator has to keep state between calls, so
    /// it shows up as a field; the CSPRNG version is stateless. This fails on the pre-fix code,
    /// which held a <c>private static readonly Random _random</c>.
    /// </summary>
    [Fact]
    public void RandomGeneratorHoldsNoPseudoRandomState()
    {
        var fields = typeof(RandomGenerator)
            .GetFields(BindingFlags.Static | BindingFlags.Instance
                                           | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.DoesNotContain(fields, f => typeof(Random).IsAssignableFrom(f.FieldType));
    }

    [Fact]
    public void RandomStringHasTheRequestedLengthAndAlphabet()
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnoprstuvxijtuwyz0123456789";

        var value = RandomGenerator.RandomString(40);

        Assert.Equal(40, value.Length);
        Assert.All(value, c => Assert.Contains(c, alphabet));
    }

    [Fact]
    public void RandomStringOfZeroLengthIsEmptyAndNegativeIsRejected()
    {
        Assert.Equal("", RandomGenerator.RandomString(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => RandomGenerator.RandomString(-1));
    }

    /// <summary>
    /// Two independently generated 40-character keys colliding would mean roughly no entropy at
    /// all; 2000 of them colliding once is a stronger statement than a single-pair check.
    /// </summary>
    [Fact]
    public void RandomStringsDoNotRepeat()
    {
        var seen = new HashSet<string>();

        for (var i = 0; i < 2000; i++)
            Assert.True(seen.Add(RandomGenerator.RandomString(40)), "a generated key repeated");
    }

    /// <summary>
    /// A crude uniformity check: over 65 000 draws every character of the 65-symbol alphabet should
    /// appear. A generator stuck on a subrange (the classic symptom of a seeded, reused
    /// pseudo-random source) fails this.
    /// </summary>
    [Fact]
    public void EveryAlphabetCharacterIsReachable()
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnoprstuvxijtuwyz0123456789";
        var distinct = RandomGenerator.RandomString(65_000).Distinct().ToHashSet();

        Assert.All(alphabet.Distinct(), c => Assert.Contains(c, distinct));
    }

    [Fact]
    public void RandomTokenIsUrlSafeAndUnpadded()
    {
        var token = RandomGenerator.RandomToken(32);

        Assert.DoesNotContain('+', token);
        Assert.DoesNotContain('/', token);
        Assert.DoesNotContain('=', token);
        // 32 bytes → 43 base64 characters once the single '=' of padding is trimmed.
        Assert.Equal(43, token.Length);
    }

    [Fact]
    public void RandomTokenRejectsNonPositiveSizes()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RandomGenerator.RandomToken(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => RandomGenerator.RandomToken(-8));
    }
}
