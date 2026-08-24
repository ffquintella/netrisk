using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using DAL.Entities;
using Model.Exceptions;
using Model.Rest;
using RestSharp;

namespace ClientServices.Services;

/// <summary>REST client for per-entity role assignments (Track 2 milestone 2.3.2).</summary>
public class UserAccessRestService(IRestService restService)
    : RestServiceBase(restService), IUserAccessService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<List<UserEntityRole>> GetUserEntityRolesAsync(int userId)
    {
        using var client = RestService.GetReliableClient();
        var request = new RestRequest($"/UserAccess/users/{userId}/entity-roles");

        try
        {
            var response = await client.GetAsync<List<UserEntityRole>>(request);

            if (response == null)
            {
                Logger.Error("Error listing entity roles of user {UserId}", userId);
                throw new InvalidHttpRequestException("Error listing entity roles",
                    $"/UserAccess/users/{userId}/entity-roles", "GET");
            }

            return response;
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error listing entity roles message:{Message}", ex.Message);
            throw new RestComunicationException("Error listing entity roles", ex);
        }
    }

    public async Task<UserEntityRole> AssignEntityRoleAsync(int userId, int entityId, int roleId)
    {
        using var client = RestService.GetReliableClient();
        var request = new RestRequest($"/UserAccess/users/{userId}/entity-roles");
        request.AddJsonBody(new { EntityId = entityId, RoleId = roleId });

        try
        {
            var response = await client.PostAsync(request);

            if (response.StatusCode != HttpStatusCode.Created || response.Content == null)
            {
                Logger.Error("Error assigning entity role to user {UserId}", userId);
                var opResult = JsonSerializer.Deserialize<OperationError>(response.Content ?? "{}");
                throw new ErrorSavingException("Error assigning entity role", opResult!);
            }

            return JsonSerializer.Deserialize<UserEntityRole>(response.Content, JsonOptions)!;
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error assigning entity role message:{Message}", ex.Message);
            throw new RestComunicationException("Error assigning entity role", ex);
        }
    }

    public async Task RevokeEntityRoleAsync(int assignmentId)
    {
        using var client = RestService.GetReliableClient();
        var request = new RestRequest($"/UserAccess/user-entity-roles/{assignmentId}");

        try
        {
            var response = await client.DeleteAsync(request);

            if (response.StatusCode != HttpStatusCode.NoContent && response.StatusCode != HttpStatusCode.OK)
            {
                Logger.Error("Error revoking entity role assignment {AssignmentId}", assignmentId);
                throw new InvalidHttpRequestException("Error revoking entity role",
                    $"/UserAccess/user-entity-roles/{assignmentId}", "DELETE");
            }
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error revoking entity role message:{Message}", ex.Message);
            throw new RestComunicationException("Error revoking entity role", ex);
        }
    }
}
