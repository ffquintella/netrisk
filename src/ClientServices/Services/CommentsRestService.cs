using ClientServices.Interfaces;
using DAL.Entities;
using Model.Exceptions;
using RestSharp;

namespace ClientServices.Services;

public class CommentsRestService: RestServiceBase, ICommentsService
{
    public CommentsRestService(IRestService restService) : base(restService)
    {
    }
    
    
    public async Task<List<Comment>> GetAllUserCommentsAsync()
    {
        
        using var client = RestService.GetClient();
        
        var request = new RestRequest($"/Comments");
        try
        {
            var response = await client.GetAsync<List<Comment>>(request);

            if (response == null)
            {
                Logger.Error("Error listing user comments");
                throw new InvalidHttpRequestException("Error listing user comments", "/Comments", "GET");
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error listing user comments message:{Message}", ex.Message);
            throw new RestComunicationException("Error listing user comments", ex);
        }
    }

    public async Task<List<Comment>> GetFixRequestCommentsAsync(int requestId)
    {
        using var client = RestService.GetReliableClient();
        
        var request = new RestRequest($"/Comments/fixrequest/{requestId}");
        try
        {
            var response = await client.GetAsync<List<Comment>>(request);

            if (response == null)
            {
                Logger.Error("Error listing fixrequest comments");
                throw new InvalidHttpRequestException("Error listing fixrequest comments", $"/Comments/fixrequest/{requestId}", "GET");
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error listing fixrequest comments message:{Message}", ex.Message);
            throw new RestComunicationException("Error listing fixrequest comments", ex);
        }
    }

    public async Task<Comment> CreateCommentAsync(Comment comment)
    {
        using var client = RestService.GetReliableClient();
        
        var request = new RestRequest($"/Comments");
        
        request.AddJsonBody(comment);
        
        try
        {
            var response = await client.PostAsync<Comment>(request);

            if (response == null)
            {
                Logger.Error("Error creating comment ");
                throw new InvalidHttpRequestException("Error  creating comment", $"/Comments", "POST");
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error  creating comment message:{Message}", ex.Message);
            throw new RestComunicationException("Error creating comment", ex);
        }
    }
}