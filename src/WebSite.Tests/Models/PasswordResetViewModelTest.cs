using System.ComponentModel.DataAnnotations;
using JetBrains.Annotations;
using WebSite.Models;
using Xunit;

namespace WebSite.Tests.Models;

[TestSubject(typeof(PasswordResetViewModel))]
public class PasswordResetViewModelTest
{
    private static List<ValidationResult> Validate(PasswordResetViewModel model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results,
            validateAllProperties: true);
        return results;
    }

    [Fact]
    public void TestValidModelProducesNoValidationErrors()
    {
        var model = new PasswordResetViewModel
        {
            Key = "abc",
            Username = "user",
            NewPassword = "S3cret!",
            ConfirmPassword = "S3cret!"
        };

        Assert.Empty(Validate(model));
    }

    [Fact]
    public void TestEmptyPasswordsFailRequired()
    {
        var model = new PasswordResetViewModel();

        var results = Validate(model);

        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(model.NewPassword)));
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(model.ConfirmPassword)));
    }

    [Fact]
    public void TestMissingNewPasswordFailsRequired()
    {
        var model = new PasswordResetViewModel { NewPassword = "", ConfirmPassword = "S3cret!" };

        var results = Validate(model);

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(model.NewPassword)));
    }

    [Fact]
    public void TestMismatchedConfirmationProducesCompareError()
    {
        var model = new PasswordResetViewModel
        {
            NewPassword = "S3cret!",
            ConfirmPassword = "different"
        };

        var results = Validate(model);

        var error = Assert.Single(results);
        Assert.Contains(nameof(model.ConfirmPassword), error.MemberNames);
        Assert.Equal("The password and confirmation password do not match.", error.ErrorMessage);
    }

    [Fact]
    public void TestKeyAndUsernameDefaultToEmptyAndAreNotRequired()
    {
        var model = new PasswordResetViewModel
        {
            NewPassword = "S3cret!",
            ConfirmPassword = "S3cret!"
        };

        Assert.Equal("", model.Key);
        Assert.Equal("", model.Username);
        Assert.Empty(Validate(model));
    }
}
