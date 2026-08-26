using System;
using System.Linq;
using System.Threading.Tasks;
using DAL.Entities;
using JetBrains.Annotations;
using Model.Exceptions;
using NSubstitute;
using ServerServices.Interfaces;
using ServerServices.Services;
using ServerServices.Tests.ServiceTests;
using Tools.Security;
using Xunit;

namespace ServerServices.Tests.Track8;

/// <summary>
/// Column-level protection of the biometric template and the signature seed
/// (security finding NR-2026-032).
///
/// The reason this finding was worth closing is not exploitability — reading the column already
/// requires database access, which is the whole dataset. It is irrevocability: a leaked password is
/// rotated in a minute and a leaked face is not rotated at all, which is what ASVS 6.1.2 is about.
///
/// The tests that matter are the two halves of the in-place upgrade. A new write must be protected,
/// and an existing plaintext row written before the change must keep working — refusing it would lock
/// every current enrolment out of the product until they re-enrolled, which is a worse outcome than
/// the exposure being closed.
/// </summary>
[TestSubject(typeof(FaceIDService))]
public class FaceIdTemplateProtectionInMemoryTest : InMemoryServiceTestBase
{
    private readonly ISecretProtector _protector;

    public FaceIdTemplateProtectionInMemoryTest()
    {
        _protector = GetService<ISecretProtector>();
    }

    /// <summary>
    /// A service whose plugin gate is open. The FaceID plugin itself is never loaded here — nothing
    /// in these tests extracts a face — but the enrolment and transaction paths refuse to run at all
    /// unless the plugin reports as present and enabled.
    /// </summary>
    private FaceIDService NewService()
    {
        var plugins = Substitute.For<IPluginsService>();
        plugins.PluginExistsAsync("FaceIdPlugin").Returns(Task.FromResult(true));
        plugins.PluginIsEnabledAsync("FaceIdPlugin").Returns(Task.FromResult(true));

        return new FaceIDService(
            GetService<Serilog.ILogger>(),
            GetService<IDalService>(),
            plugins,
            GetService<IUsersService>(),
            new EnvironmentService(),
            _protector);
    }

    private void SeedUser(int id)
    {
        SeedUnscoped(ctx => ctx.Users.Add(new User
        {
            Value = id, Name = "Face User", Login = $"face{id}", Email = $"face{id}@example.test",
            Enabled = true, Lockout = 0, Type = "local", Salt = "s", Password = new byte[60],
            Admin = false, RoleId = 1
        }));
    }

    private FaceIDUser ReadRow(int userId)
    {
        using var ctx = OpenContext();
        return ctx.FaceIDUsers.First(f => f.UserId == userId);
    }

    /// <summary>
    /// Enrolling a user generates a signature seed. It must not reach the column in the clear.
    /// </summary>
    [Fact]
    public async Task TestTheSignatureSeedIsProtectedOnWrite()
    {
        SeedUser(701);

        await NewService().SetUserEnabledStatusAsync(701, enabled: true, loggedUserId: 701);

        var stored = ReadRow(701).SignatureSeed;

        Assert.True(_protector.LooksProtected(stored),
            "the seed reached the database without protection");

        // And it is genuinely the seed underneath, not a hash or a discarded value.
        var revealed = _protector.Unprotect(stored);
        Assert.False(string.IsNullOrWhiteSpace(revealed));
        Assert.NotEqual(stored, revealed);
    }

    /// <summary>
    /// The seed is base64 of random bytes, so a protected value must not accidentally be readable as
    /// the plaintext it wraps — this catches a protector that silently passes the value through.
    /// </summary>
    [Fact]
    public async Task TestTheProtectedSeedIsNotItsOwnPlaintext()
    {
        SeedUser(702);

        await NewService().SetUserEnabledStatusAsync(702, enabled: true, loggedUserId: 702);

        var stored = ReadRow(702).SignatureSeed;

        Assert.NotEqual(_protector.Unprotect(stored), stored);
        Assert.False(_protector.LooksProtected(_protector.Unprotect(stored)!));
    }

    /// <summary>
    /// The round trip: a seed written protected is read back and used to build a biometric anchor. If
    /// the read path did not unprotect, the base64 decode would throw and enrolled users could not
    /// start a transaction.
    /// </summary>
    [Fact]
    public async Task TestATransactionStartsAgainstAProtectedSeed()
    {
        SeedUser(703);

        var service = NewService();

        await service.SetUserEnabledStatusAsync(703, enabled: true, loggedUserId: 703);

        // IsUserEnabledAsync gates on a non-empty template, so enrolment is simulated directly rather
        // than by running the plugin's face extraction.
        SeedUnscoped(ctx =>
        {
            var row = ctx.FaceIDUsers.First(f => f.UserId == 703);
            row.FaceIdentification = _protector.Protect(Convert.ToBase64String(new byte[512]))!;
        });

        var transaction = await service.StartTransactionAsync(703);

        Assert.NotEqual(Guid.Empty, transaction.TransactionId);
    }

    /// <summary>
    /// The in-place upgrade. A row written before this change holds plaintext, and it has to keep
    /// working — <c>LooksProtected</c> is what distinguishes the two, and this is the test that would
    /// fail if the read path assumed everything is protected.
    /// </summary>
    [Fact]
    public async Task TestALegacyPlaintextSeedStillWorks()
    {
        SeedUser(704);

        var plaintextSeed = Convert.ToBase64String(new byte[32]);

        SeedUnscoped(ctx => ctx.FaceIDUsers.Add(new FaceIDUser
        {
            UserId = 704, IsEnabled = true,
            SignatureSeed = plaintextSeed,
            FaceIdentification = Convert.ToBase64String(new byte[512]),
            LastUpdate = DateTime.UtcNow, LastUpdateUserId = 704
        }));

        Assert.False(_protector.LooksProtected(ReadRow(704).SignatureSeed));

        var transaction = await NewService().StartTransactionAsync(704);

        Assert.NotEqual(Guid.Empty, transaction.TransactionId);
    }

    /// <summary>
    /// Toggling an already-enrolled user off and on must not regenerate their seed — that would
    /// invalidate every anchor already issued against it. The write path only creates a seed when the
    /// row is new.
    /// </summary>
    [Fact]
    public async Task TestTogglingAnExistingUserDoesNotRegenerateTheSeed()
    {
        SeedUser(705);

        var service = NewService();

        await service.SetUserEnabledStatusAsync(705, enabled: true, loggedUserId: 705);
        var first = ReadRow(705).SignatureSeed;

        await service.SetUserEnabledStatusAsync(705, enabled: false, loggedUserId: 705);
        await service.SetUserEnabledStatusAsync(705, enabled: true, loggedUserId: 705);

        Assert.Equal(first, ReadRow(705).SignatureSeed);
    }

    /// <summary>
    /// A user with no FaceID row cannot start a transaction. Asserted because the protection change
    /// touched this path and a null seed reaching <c>Convert.FromBase64String</c> would be a crash
    /// rather than a refusal.
    /// </summary>
    [Fact]
    public async Task TestAUserWithNoEnrolmentIsRefusedRatherThanCrashing()
    {
        SeedUser(706);

        await Assert.ThrowsAsync<UserNotFoundException>(
            () => NewService().StartTransactionAsync(706));
    }

    /// <summary>
    /// With the plugin disabled the write path is a no-op, so nothing — protected or not — is
    /// written. Confirms the gate still holds after the change.
    /// </summary>
    [Fact]
    public async Task TestNothingIsWrittenWhenThePluginIsDisabled()
    {
        SeedUser(707);

        var plugins = Substitute.For<IPluginsService>();
        plugins.PluginExistsAsync("FaceIdPlugin").Returns(Task.FromResult(false));

        var service = new FaceIDService(
            GetService<Serilog.ILogger>(), GetService<IDalService>(), plugins,
            GetService<IUsersService>(), new EnvironmentService(), _protector);

        await service.SetUserEnabledStatusAsync(707, enabled: true, loggedUserId: 707);

        using var ctx = OpenContext();
        Assert.Empty(ctx.FaceIDUsers.Where(f => f.UserId == 707));
    }
}
