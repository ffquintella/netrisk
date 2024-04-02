using System.Net;
using ClientServices.Interfaces;
using DAL.Entities;
using Model.Exceptions;
using RestSharp;

namespace ClientServices.Services;

public class MessagesRestService: RestServiceBase, IMessagesService
{
    public MessagesRestService(IRestService restService) : base(restService)
    {
    }

    public async Task<int> GetCountAsync()
    {
        var client = RestService.GetClient();
        
        var request = new RestRequest($"/Messages/count");
        try
        {
            var response = await client.GetAsync<int>(request);
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error getting messages count message:{Message}", ex.Message);
            throw new RestComunicationException("Error getting messages count", ex);
        }
    }

    public async Task<bool> HasUnreadMessages()
    {
        var client = RestService.GetClient();
        
        var request = new RestRequest($"/Messages/has_unread");
        try
        {
            var response = await client.GetAsync<bool>(request);
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error checking if user has unread messages message:{Message}", ex.Message);
            throw new RestComunicationException("Error checking if user has unread messages", ex);
        }
    }

    public async Task<List<Message>> GetMessagesAsync()
    {
        var client = RestService.GetClient();
        
        var request = new RestRequest($"/Messages");
        try
        {
            var response = await client.GetAsync<List<Message>>(request);
            
            if(response == null)
                throw new RestComunicationException("Error getting messages");
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error getting messages message:{Message}", ex.Message);
            throw new RestComunicationException("Error getting messages", ex);
        }
    }
}