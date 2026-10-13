using System;
using GUIClient.Tools;
using JetBrains.Annotations;
using Material.Icons;
using Model;
using Xunit;

namespace GUIClient.Tests.Tools;

/// <summary>
/// T325 — <c>IntStatusToMaterialIconkindConverter</c>, whose table and target-type rule are
/// <see cref="StatusIconMap"/>, answered only a target assignable to <see cref="MaterialIconKind"/>. The property it
/// feeds, <c>MaterialIcon.Kind</c>, is a <c>MaterialIconKind?</c> in Material.Icons.Avalonia 3.x, so every call fell
/// through to an error and no status icon was ever drawn — on the host header, the host's findings grid, the findings
/// grid and the risk screen. (That the property really is nullable is what the GUIClient.UiTests smoke tests of the
/// hosts and findings views observe; this project has no Avalonia to ask.)
/// </summary>
[TestSubject(typeof(StatusIconMap))]
public class StatusIconMapTest
{
    /// <summary>The defect: the target type <c>MaterialIcon.Kind</c> has.</summary>
    [Fact]
    public void TestANullableIconTargetIsAnswered()
    {
        Assert.True(StatusIconMap.TryResolve((short)IntStatus.Active, typeof(MaterialIconKind?), out var kind));
        Assert.Equal(MaterialIconKind.CheckCircleOutline, kind);
    }

    [Fact]
    public void TestANonNullableIconTargetIsStillAnswered()
    {
        Assert.True(StatusIconMap.TryResolve((ushort)IntStatus.New, typeof(MaterialIconKind), out var kind));
        Assert.Equal(MaterialIconKind.NewReleases, kind);
    }

    /// <summary>Host.Status is a short, Vulnerability.Status a ushort; an int is accepted too.</summary>
    [Theory]
    [InlineData((short)13)]
    [InlineData((ushort)13)]
    [InlineData(13)]
    public void TestEveryStatusNumberTypeIsRead(object status)
    {
        Assert.True(StatusIconMap.TryResolve(status, typeof(MaterialIconKind?), out var kind));
        Assert.Equal(StatusIconMap.For(13), kind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData((short)-1)]
    [InlineData(-1)]
    [InlineData("New")]
    public void TestSomethingThatIsNotAStatusNumberIsRefused(object? status)
    {
        Assert.False(StatusIconMap.TryResolve(status, typeof(MaterialIconKind?), out _));
    }

    [Theory]
    [InlineData(typeof(string))]
    [InlineData(typeof(int?))]
    public void TestATargetThatCannotHoldAnIconIsRefused(Type targetType)
    {
        Assert.False(StatusIconMap.TryResolve((short)IntStatus.Active, targetType, out _));
    }

    /// <summary>Every status the model defines draws a real icon, and an unknown number the placeholder.</summary>
    [Fact]
    public void TestEveryStatusMapsToADefinedIcon()
    {
        foreach (var status in Enum.GetValues<IntStatus>())
            Assert.True(Enum.IsDefined(StatusIconMap.For((ushort)status)), $"{status} has no defined icon");

        Assert.Equal(MaterialIconKind.FlaskEmpty, StatusIconMap.For(ushort.MaxValue));
    }
}
