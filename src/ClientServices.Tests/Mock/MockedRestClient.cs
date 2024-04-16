using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RestSharp;
using RestSharp.Serializers;

namespace ClientServices.Tests.Mock;

public class MockedRestClient: RestClient, IRestClient
{
    public Dictionary<string, object?> Responses { get; set; } = new Dictionary<string, object?>();
    
    public void Dispose()
    {
        Responses.Clear();
    }

    public new Task<RestResponse> ExecuteAsync(RestRequest request, CancellationToken cancellationToken = new CancellationToken())
    {
        if(request == null) throw new System.ArgumentNullException(nameof(request));
        
        if(Responses.ContainsKey(request.Resource))
        {
            var response = Responses[request.Resource];
            var value = Task.FromResult(new RestResponse
            {
                StatusCode = System.Net.HttpStatusCode.OK,
                ResponseStatus = ResponseStatus.Completed,
                Content = System.Text.Json.JsonSerializer.Serialize(response),
                ContentType = "application/json",
                ContentLength = System.Text.Json.JsonSerializer.Serialize(response).Length
            });
            return value;
        }
        

        
        return Task.FromResult(new RestResponse { StatusCode = System.Net.HttpStatusCode.NotImplemented, ResponseStatus = ResponseStatus.Completed });
        
    }
    
    public new Task<DATA?> GetAsync<DATA>(RestRequest request, CancellationToken ct = new CancellationToken()) where DATA: class
    {
        if(request == null) throw new System.ArgumentNullException(nameof(request));
        
        if(Responses.ContainsKey(request.Resource))
        {
            var response = Responses[request.Resource];
            return Task.FromResult(response as DATA);
        }
        
        return Task.FromResult(null as DATA);
    }
    
    public Task<RestResponse<T>> ExecuteAsync<T>(RestRequest request, CancellationToken cancellationToken = new CancellationToken()) where T:  class
    {
        if(request == null) throw new System.ArgumentNullException(nameof(request));
        
        if(Responses.ContainsKey(request.Resource))
        {
            var response = Responses[request.Resource];
            return Task.FromResult(new RestResponse<T> (request) { StatusCode = System.Net.HttpStatusCode.OK, ResponseStatus = ResponseStatus.Completed, Data = response as T});
        }
        
        return Task.FromResult(new RestResponse<T> (request) { StatusCode = System.Net.HttpStatusCode.NotImplemented, ResponseStatus = ResponseStatus.Completed });
    }
    
    public static Task<RestResponse<T>> ExecuteGetAsync<T>(
        RestRequest       request,
        CancellationToken cancellationToken = default
    ) where T : class
    {
        throw new System.NotImplementedException();
        //return client.ExecuteAsync<T>(request, cancellationToken);
    }


    public Task<Stream> DownloadStreamAsync(RestRequest request, CancellationToken cancellationToken = new CancellationToken())
    {
        throw new System.NotImplementedException();
    }

    public ReadOnlyRestClientOptions Options { get; }
    public RestSerializers Serializers { get; }
    public DefaultParameters DefaultParameters { get; }
}