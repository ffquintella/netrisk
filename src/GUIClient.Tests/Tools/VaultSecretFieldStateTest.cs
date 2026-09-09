using System.Collections.Generic;
using System.ComponentModel;
using GUIClient.Tools;
using JetBrains.Annotations;
using Model.Secrets;
using Xunit;

namespace GUIClient.Tests.Tools;

/// <summary>
/// The three-way decision behind every credential box on the integrations screen: typed in, newly
/// picked from the vault, or already bound and untouched.
///
/// Worth testing on its own because every way of getting it wrong is silent. Send a literal where a
/// reference was meant and the field stops being vault-backed with no warning. Send null where a
/// reference was meant and the operator's choice is quietly discarded. Send the reference when the
/// operator typed a replacement and the new credential never takes effect. None of those produce an
/// error in NetRisk — they produce a 401 from somebody else's API, days later.
/// </summary>
[TestSubject(typeof(VaultSecretFieldState))]
public class VaultSecretFieldStateTest
{
    private static string Reference(int connection = 3, string secret = "db-prod", string? field = "password")
        => SecretReference.Create(connection, secret, field).ToString();

    // --- what gets sent on save --------------------------------------------------------------

    [Fact]
    public void AnUntouchedFieldSendsNothing()
    {
        var state = new VaultSecretFieldState();

        // Null is what every connection endpoint reads as "leave the stored credential alone". This is
        // the behaviour that lets a form round-trip without the client ever holding a token, and the
        // vault feature must not break it.
        Assert.Null(state.ValueToSend());
    }

    [Fact]
    public void ATypedLiteralIsSent()
    {
        var state = new VaultSecretFieldState { TypedValue = "  bv-api-key  " };

        Assert.Equal("bv-api-key", state.ValueToSend());
    }

    [Fact]
    public void WhitespaceIsNotALiteral()
    {
        var state = new VaultSecretFieldState { TypedValue = "   " };

        Assert.Null(state.ValueToSend());
    }

    [Fact]
    public void APickedReferenceIsSent()
    {
        var state = new VaultSecretFieldState();

        state.Bind(Reference(), "Prod vault: db-prod / password");

        Assert.Equal(Reference(), state.ValueToSend());
    }

    [Fact]
    public void APickedReferenceWinsOverAnythingLeftInTheTextBox()
    {
        var state = new VaultSecretFieldState { TypedValue = "half-typed-key" };

        state.Bind(Reference(), null);

        // Binding clears the field's own copy, and the view-model blanks the bound text box. Without
        // both, detaching later would resend the abandoned literal.
        Assert.Equal(string.Empty, state.TypedValue);
        Assert.Equal(Reference(), state.ValueToSend());
    }

    [Fact]
    public void AnAlreadyBoundFieldThatIsNotTouchedSendsNothing()
    {
        var state = new VaultSecretFieldState();
        state.LoadFrom(Reference());

        // The server already holds this reference. Resending it would be harmless but pointless; what
        // matters is that "unchanged" is still expressible for a vault-backed field.
        Assert.Null(state.ValueToSend());
        Assert.True(state.IsVaultBacked);
    }

    [Fact]
    public void DetachingThenTypingSendsTheLiteral()
    {
        var state = new VaultSecretFieldState();
        state.LoadFrom(Reference());

        state.Detach();
        state.TypedValue = "a-literal-key";

        Assert.False(state.IsVaultBacked);
        Assert.Equal("a-literal-key", state.ValueToSend());
    }

    [Fact]
    public void DetachingWithoutTypingChangesNothingOnTheServer()
    {
        var state = new VaultSecretFieldState();
        state.LoadFrom(Reference());

        state.Detach();

        // There is no "no credential" state for a connection that needs one, so detaching is a prelude
        // to typing rather than an instruction to clear. Sending null keeps the stored reference.
        Assert.Null(state.ValueToSend());
    }

    [Fact]
    public void PickingAgainAfterDetachingRebinds()
    {
        var state = new VaultSecretFieldState();
        state.LoadFrom(Reference());
        state.Detach();

        state.Bind(Reference(secret: "tm-key", field: null), null);

        Assert.True(state.IsVaultBacked);
        Assert.False(state.Detached);
        Assert.Equal(Reference(secret: "tm-key", field: null), state.ValueToSend());
    }

    // --- what the operator sees ----------------------------------------------------------------

    [Fact]
    public void TheTextBoxIsDisabledWhileTheFieldIsVaultBacked()
    {
        var state = new VaultSecretFieldState();

        Assert.True(state.AcceptsTypedValue);

        state.LoadFrom(Reference());

        // Typing into a box whose contents will be ignored — or that silently replaces the binding — is
        // the ambiguity that gets a credential rotated at 2am.
        Assert.False(state.AcceptsTypedValue);
    }

    [Fact]
    public void ThePickerIsHiddenWhenThereIsNoVault()
    {
        var state = new VaultSecretFieldState();

        Assert.False(state.ShowPicker);
        Assert.False(state.ShowDetach);

        state.VaultAvailable = true;

        Assert.True(state.ShowPicker);

        // Nothing to detach from yet.
        Assert.False(state.ShowDetach);

        state.LoadFrom(Reference());
        Assert.True(state.ShowDetach);
    }

    [Fact]
    public void SaysNothingWhenThereIsNothingToSay()
    {
        var state = new VaultSecretFieldState { VaultAvailable = true };

        // A caption reading "not from a vault" beside every ordinary credential field is noise.
        Assert.Equal(string.Empty, state.DisplayText);
        Assert.False(state.HasDisplayText);
        Assert.False(state.HasPendingChange);
    }

    [Fact]
    public void PrefersTheLabelTheServerOrThePickerSupplied()
    {
        var state = new VaultSecretFieldState();

        state.LoadFrom(Reference(), "Prod vault: db-prod / password");
        Assert.Equal("Prod vault: db-prod / password", state.DisplayText);

        state.Bind(Reference(secret: "tm-key", field: null), "Prod vault: tm-key");
        Assert.Equal("Prod vault: tm-key", state.DisplayText);
    }

    [Fact]
    public void FallsBackToTheReferenceWhenNobodySuppliedALabel()
    {
        var state = new VaultSecretFieldState();

        state.LoadFrom(Reference());

        // A form reloaded from a list has only the raw reference. The fallback is readable rather than
        // the base64url string.
        Assert.Equal("Vault #3: db-prod / password", state.DisplayText);
        Assert.True(state.HasDisplayText);
    }

    [Fact]
    public void DescribesAnUnreadableReferenceRatherThanShowingIt()
    {
        var state = new VaultSecretFieldState();

        state.LoadFrom("vault:v1:notanumber:zzz");

        Assert.Equal("Vault reference (unreadable)", state.DisplayText);
    }

    [Fact]
    public void MarksAPickedBindingAsNotYetSaved()
    {
        var state = new VaultSecretFieldState();

        state.Bind(Reference(), "Prod vault: db-prod / password");

        // The view adds a localized "will be saved" marker on this flag, so an operator does not close
        // the form believing the change took.
        Assert.True(state.HasPendingChange);

        state.LoadFrom(Reference(), "Prod vault: db-prod / password");
        Assert.False(state.HasPendingChange);
    }

    [Fact]
    public void StopsAdvertisingABindingOnceItIsDetached()
    {
        var state = new VaultSecretFieldState();
        state.LoadFrom(Reference(), "Prod vault: db-prod / password");

        state.Detach();

        Assert.Equal(string.Empty, state.DisplayText);
        Assert.False(state.HasDisplayText);
    }

    // --- loading -------------------------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("enc:v2:not-a-reference")]
    [InlineData("a-literal-api-key")]
    public void ALiteralStoredValueIsNotTreatedAsABinding(string? stored)
    {
        var state = new VaultSecretFieldState();

        state.LoadFrom(stored);

        Assert.False(state.IsVaultBacked);
        Assert.Null(state.StoredReference);
        Assert.True(state.AcceptsTypedValue);
    }

    [Fact]
    public void ReloadingDiscardsTheEditingSession()
    {
        var state = new VaultSecretFieldState();
        state.Bind(Reference(), "picked");
        state.TypedValue = "typed";

        state.LoadFrom(null);

        Assert.Null(state.PendingReference);
        Assert.Equal(string.Empty, state.TypedValue);
        Assert.False(state.Detached);
        Assert.Null(state.ValueToSend());
    }

    [Fact]
    public void ResetIsALoadWithNothingStored()
    {
        var state = new VaultSecretFieldState();
        state.LoadFrom(Reference());

        state.Reset();

        Assert.False(state.IsVaultBacked);
    }

    [Fact]
    public void RefusesToBindToNothing()
    {
        Assert.Throws<System.ArgumentException>(() => new VaultSecretFieldState().Bind("  ", null));
    }

    // --- change notification ---------------------------------------------------------------------

    [Fact]
    public void AnnouncesTheDerivedPropertiesTheViewBindsTo()
    {
        var state = new VaultSecretFieldState();
        var raised = new List<string?>();

        ((INotifyPropertyChanged)state).PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        state.Bind(Reference(), "Prod vault: db-prod / password");

        // The view binds straight to the state object, so a mutation that raised nothing would leave a
        // stale caption and an enabled text box beside a field that is now vault-backed.
        Assert.Contains(nameof(VaultSecretFieldState.DisplayText), raised);
        Assert.Contains(nameof(VaultSecretFieldState.IsVaultBacked), raised);
        Assert.Contains(nameof(VaultSecretFieldState.AcceptsTypedValue), raised);
        Assert.Contains(nameof(VaultSecretFieldState.ShowDetach), raised);
        Assert.Contains(nameof(VaultSecretFieldState.HasPendingChange), raised);
        Assert.Contains(nameof(VaultSecretFieldState.HasDisplayText), raised);
    }

    [Fact]
    public void AnnouncesWhenTheVaultBecomesAvailable()
    {
        var state = new VaultSecretFieldState();
        var raised = new List<string?>();

        ((INotifyPropertyChanged)state).PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        state.VaultAvailable = true;

        Assert.Contains(nameof(VaultSecretFieldState.ShowPicker), raised);

        raised.Clear();
        state.VaultAvailable = true;

        // Setting the same value again raises nothing, so the seven states a screen holds do not each
        // fire twelve notifications on every load.
        Assert.Empty(raised);
    }
}
