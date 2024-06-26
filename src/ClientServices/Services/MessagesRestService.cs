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

    public async Task<int> GetCountAsync(List<int?>? chats = null)
    {
        var client = RestService.GetClient();
        
        var request = new RestRequest($"/Messages/count");
        
        if (chats != null)
        {
            foreach (var chat in chats)
            {
                request.AddQueryParameter("chats", chat.ToString());
            }
        }
        
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

    public async Task<bool> HasUnreadMessages(List<int?>? chats = null)
    {
        var client = RestService.GetClient();
        var request = new RestRequest($"/Messages/has_unread");
        
        if (chats != null)
        {
            foreach (var chat in chats)
            {
                request.AddQueryParameter("chats", chat.ToString());
            }
        }
        
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

    public async Task<List<Message>> GetMessagesAsync(List<int?>? chats = null)
    {
        var client = RestService.GetClient();
        var request = new RestRequest($"/Messages");

        if (chats != null)
        {
            foreach (var chat in chats)
            {
                request.AddQueryParameter("chats", chat.ToString());
            }
        }
        
        try
        {
            var response = await client.GetAsync<List<Message>>(request);
            
            if(response == null)
                throw new RestComunicationException("Error getting messages");
            
            return response.OrderBy(r => r.Status).ToList();
            
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error getting messages message:{Message}", ex.Message);
            throw new RestComunicationException("Error getting messages", ex);
        }
    }

    public async Task ReadMessageAsync(int id)
    {
        var client = RestService.GetClient();
        var request = new RestRequest($"/Messages/{id}");
        request.AddQueryParameter("operation", "read");
        
        try
        {
            await client.PatchAsync<string>(request);
            
            return;
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error reading message message:{Message}", ex.Message);
            throw new RestComunicationException("Error reading message ", ex);
        }
    }

    public async Task DeleteMessageAsync(int id)
    {
        var client = RestService.GetClient();
        var request = new RestRequest($"/Messages/{id}");
        
        try
        {
            await client.DeleteAsync(request);
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error reading message message:{Message}", ex.Message);
            throw new RestComunicationException("Error reading message ", ex);
            
        }
    }
}