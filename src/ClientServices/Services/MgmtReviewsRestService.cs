﻿using System.Net;
using System.Text.Json;
using ClientServices.Interfaces;
using DAL.Entities;
using Model.DTO;
using Model.Exceptions;
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

    public MgmtReview Create(MgmtReviewDto mgmtReviewDto)
    {
        var client = RestService.GetClient();
        var request = new RestRequest($"/MgmtReviews");

        request.AddJsonBody(mgmtReviewDto);
        
        try
        {
            var response = client.Post(request);

            if (response.StatusCode != HttpStatusCode.Created)
            {
                Logger.Error("Error creating review");
                throw new RestComunicationException($"Error creating review" );
            }
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            var review = JsonSerializer.Deserialize<MgmtReview>(response.Content!, options);

            if (review == null) throw new Exception("Error deserializing review");
            
            return review;
            
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error creating review: {Message}", ex.Message);
            throw new RestComunicationException("Error creating review", ex);
        }
    }
    
}