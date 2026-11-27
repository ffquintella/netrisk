using System.Threading.Tasks;
using ClientServices.Interfaces;

namespace GUIClient.Tools;

/// <summary>
/// What the Users screen knows about a user's FaceID state. Both flags are false when the FaceID plugin is not active.
/// </summary>
public readonly record struct FaceIdEnrollment(bool Enabled, bool FaceSet);

/// <summary>
/// Loads the selected user's FaceID state for the Users screen, and only when the FaceID plugin is active. With the
/// plugin off there is nothing to read, and the face-set endpoint refuses a caller that is neither the user nor an
/// administrator with a 401, which the screen logged as an error on every selection. Pure: no Avalonia.
/// </summary>
public static class FaceIdEnrollmentLoader
{
    public static async Task<FaceIdEnrollment> LoadAsync(IFaceIDService faceIdService, bool pluginActive, int userId)
    {
        if (!pluginActive)
            return new FaceIdEnrollment(false, false);

        var enabled = await faceIdService.IsUserEnabledAsync(userId);
        var faceSet = await faceIdService.UserHasFaceSetAsync(userId);
        return new FaceIdEnrollment(enabled, faceSet);
    }

    /// <summary>
    /// Writes the user's FaceID flag on save, under the same signal as <see cref="LoadAsync"/>: with the plugin off the
    /// flag was never read, so there is nothing to write back, and the request was a round trip on every save.
    /// </summary>
    public static Task SaveEnabledAsync(IFaceIDService faceIdService, bool pluginActive, int userId, bool enabled) =>
        pluginActive ? faceIdService.SetUserEnabledStatusAsync(userId, enabled) : Task.CompletedTask;
}
