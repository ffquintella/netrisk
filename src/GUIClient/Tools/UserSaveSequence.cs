using System.Collections.Generic;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using DAL.Entities;
using Model.DTO;

namespace GUIClient.Tools;

/// <summary>
/// The requests a save on the Users screen makes, in order. An existing user and their permissions go in one request and
/// one server transaction; a new user is created first, because its permissions need the id the server assigns. The
/// FaceID flag is written only while the plugin is active (<see cref="FaceIdEnrollmentLoader"/>). Pure: no Avalonia.
/// </summary>
public static class UserSaveSequence
{
    /// <returns>The user as saved: for a new one, the server's copy with its id.</returns>
    public static async Task<UserDto> RunAsync(IUsersService users, IFaceIDService faceId, bool faceIdPluginActive,
        UserDto user, List<Permission?> permissions, bool faceIdEnabled)
    {
        if (user.Id == 0)
        {
            var created = users.CreateUser(user);
            await FaceIdEnrollmentLoader.SaveEnabledAsync(faceId, faceIdPluginActive, created.Id, faceIdEnabled);
            users.SaveUserPermissions(created.Id, permissions);
            return created;
        }

        await users.SaveUserWithPermissionsAsync(user, permissions);
        await FaceIdEnrollmentLoader.SaveEnabledAsync(faceId, faceIdPluginActive, user.Id, faceIdEnabled);
        return user;
    }
}
