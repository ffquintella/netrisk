using System.Net;
using ClientServices.Interfaces;
using DAL.Entities;
using Model.Exceptions;
using RestSharp;

namespace ClientServices.Services;

public class MgmtReviewsService: ServiceBase, IMgmtReviewsService
{
    public MgmtReviewsService(IRestService restService) : base(restService)
    {
    }

    public List<Review> GetReviewTypes()
    {
        var client = _restService.GetClient();
        
        var request = new RestRequest($"/MgmtReviews/Types");
        
        
        try
        {
            var response = client.Get<List<Review>>(request);

            if (response == null)
            {
                _logger.Error("Error getting review types");
                throw new RestComunicationException($"Error getting review types" );
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            _logger.Error("Error getting review types: {Message}", ex.Message);
            throw new RestComunicationException("Error getting review types", ex);
        }

    }
}