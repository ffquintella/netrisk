﻿using ClientServices.Interfaces;
using DAL.Entities;
using Model.Exceptions;
using RestSharp;

namespace ClientServices.Services;

public class TechnologiesRestService: RestServiceBase, ITechnologiesService
{
    public TechnologiesRestService(IRestService restService) : base(restService)
    {
    }
    
    
    public List<Technology> GetAll()
    {
        var client = RestService.GetClient();
        
        var request = new RestRequest($"/Technologies");
        try
        {
            var response = client.Get<List<Technology>>(request);

            if (response == null)
            {
                Logger.Error("Error listing Technologies");
                throw new InvalidHttpRequestException("Error listing Technologies", "/Technology", "GET");
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error listing Technologies message:{Message}", ex.Message);
            throw new RestComunicationException("Error listing Technologies", ex);
        }
    }
}