using ClientServices.Interfaces;
using DAL.Entities;
using Model.Exceptions;
using Model.DTO;
using Newtonsoft.Json;
using RestSharp;

namespace ClientServices.Services;

public class EmailsRestService(IRestService restService) : RestServiceBase(restService), IEmailsService
{
    public async Task SendVulnerabilityFixRequestMailAsync(FixRequestDto fixRequestDto, bool sendToGroup = false)
    {
        using var client = RestService.GetReliableClient();
        
        var request = new RestRequest($"/Email/Vulnerability/FixRequest");
        request.AddJsonBody(fixRequestDto);
        request.AddQueryParameter("sendToGroup", sendToGroup.ToString());
        
        try
        {
            var response = await client.PostAsync<string>(request);            
            
            if (response == null )
            {
                Logger.Error("Error sending vulnerability fix request mail");
                throw new InvalidHttpRequestException("Error sending vulnerability fix request mail", $"/Email/Vulnerability/FixRequest", "POST");
            }

        }
        catch (Exception ex)
        {
            Logger.Error("Error sending vulnerability fix request mail message:{Message}", ex.Message);
            throw new RestComunicationException("Error sending vulnerability fix request mail", ex);
        }
    }

    public async Task SendVulnerabilityUpdateMailAsync(int fixRequestId, string comment)
    {
        using var client = RestService.GetReliableClient();
        
        var request = new RestRequest($"/Email/Vulnerability/Update/{fixRequestId}");
        //request.AddJsonBody(comment);
        request.AddParameter("application/json", JsonConvert.SerializeObject(comment), ParameterType.RequestBody);

        try
        {
            var response = await client.PostAsync<string>(request);            
            
            if (response == null )
            {
                Logger.Error("Error sending vulnerability update mail");
                throw new InvalidHttpRequestException("Error sending vulnerability update mail", $"/Email/Vulnerability/FixRequest", "POST");
            }

        }
        catch (Exception ex)
        {
            Logger.Error("Error sending vulnerability update mail message:{Message}", ex.Message);
            throw new RestComunicationException("Error sending vulnerability update mail", ex);
        }
    }
}