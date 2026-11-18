using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClientServices.Interfaces;
using DAL.Entities;
using Model.DTO;
using Model.Exceptions;
using Model.Governance;
using RestSharp;

namespace ClientServices.Services;

public class MgmtReviewsRestService: RestServiceBase, IMgmtReviewsService
{
    public MgmtReviewsRestService(IRestService restService) : base(restService)
    {
    }

    public List<Review> GetReviewTypes()
    {
        var client = RestService.GetClient();
        
        var request = new RestRequest($"/MgmtReviews/Types");
        
        
        try
        {
            var response = client.Get<List<Review>>(request);

            if (response == null)
            {
                Logger.Error("Error getting review types");
                throw new RestComunicationException($"Error getting review types" );
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error getting review types: {Message}", ex.Message);
            throw new RestComunicationException("Error getting review types", ex);
        }

    }

    public List<NextStep> GetNextSteps()
    {
        var client = RestService.GetClient();
        var request = new RestRequest($"/MgmtReviews/NextSteps");
        
        try
        {
            var response = client.Get<List<NextStep>>(request);

            if (response == null)
            {
                Logger.Error("Error getting review next steps");
                throw new RestComunicationException($"Error getting review next steps" );
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error getting review next steps: {Message}", ex.Message);
            throw new RestComunicationException("Error getting review next steps", ex);
        }
    }

    /// <summary>
    /// Records a review in the caller's name (<c>POST /MgmtReviews</c>, which dates it and applies segregation of duties
    /// on the server). A refusal that names a rule — the caller submitted, owns or manages the risk — arrives as a
    /// <see cref="RuleBrokenException"/> carrying it, so the dialog can say why (T278); any other failure stays a
    /// <see cref="RestComunicationException"/>.
    /// </summary>
    public MgmtReview Create(MgmtReviewDto mgmtReviewDto)
    {
        using var client = MutatingClient(reportErrorResponses: true);
        var request = new RestRequest(CreateRoute, Method.Post);

        request.AddJsonBody(mgmtReviewDto);

        try
        {
            var response = client.Execute(request);

            if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
                ThrowIfRefused(response, CreateRoute, "POST", "Error creating review");

            if (response.StatusCode != HttpStatusCode.Created)
            {
                Logger.Error("Error creating review: {Status}", response.StatusCode);
                throw new RestComunicationException("Error creating review",
                    response.ErrorException ?? new HttpRequestException($"Status {(int)response.StatusCode}"));
            }

            var review = JsonSerializer.Deserialize<MgmtReview>(response.Content!, JsonOptions);

            if (review == null)
                throw new InvalidHttpRequestException("Error deserializing review", CreateRoute, "POST");

            return review;
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error creating review: {Message}", ex.Message);
            throw new RestComunicationException("Error creating review", ex);
        }
    }

    public async Task<List<OverdueReview>> GetOverdueReviewsAsync()
    {
        using var client = RestService.GetReliableClient();
        var request = new RestRequest(OverdueRoute);

        try
        {
            var response = await client.GetAsync(request);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                Logger.Error("Error listing overdue reviews: {Status}", response.StatusCode);
                throw new InvalidHttpRequestException("Error listing overdue reviews", OverdueRoute, "GET");
            }

            return JsonSerializer.Deserialize<List<OverdueReview>>(response.Content!, JsonOptions) ?? [];
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error listing overdue reviews: {Message}", ex.Message);
            throw new RestComunicationException("Error listing overdue reviews", ex);
        }
    }

    private const string CreateRoute = "/MgmtReviews";
    private const string OverdueRoute = "/MgmtReviews/Overdue";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: true) }
    };
}
