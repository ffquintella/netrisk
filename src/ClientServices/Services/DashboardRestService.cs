using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using Model.Dashboard;
using Model.Exceptions;
using RestSharp;

namespace ClientServices.Services;

/// <summary>
/// REST client for the cross-entity Master Dashboard (Track 2 milestone 2.3.3).
/// </summary>
public class DashboardRestService(IRestService restService, IAuthenticationService authenticationService)
    : RestServiceBase(restService), IDashboardService
{
    public async Task<MasterDashboard> GetMasterDashboardAsync(bool refresh = false)
    {
        using var client = RestService.GetReliableClient();

        var request = new RestRequest("/Dashboard/Master");
        if (refresh) request.AddParameter("refresh", true);

        try
        {
            var response = await client.GetAsync<MasterDashboard>(request);

            if (response == null)
            {
                Logger.Error("Error getting the master dashboard");
                throw new InvalidHttpRequestException("Error getting the master dashboard", "/Dashboard/Master", "GET");
            }

            return response;
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                authenticationService.DiscardAuthenticationToken();
            }

            // The endpoint is admin-only, so a 403 here is an ordinary outcome for a
            // non-admin rather than a fault: let the caller distinguish it from a transport error.
            if (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                Logger.Warning("User is not authorized to view the master dashboard");
                throw new InvalidHttpRequestException("Not authorized to view the master dashboard",
                    "/Dashboard/Master", "GET");
            }

            Logger.Error("Error getting the master dashboard message:{Message}", ex.Message);
            throw new RestComunicationException("Error getting the master dashboard", ex);
        }
    }
}
