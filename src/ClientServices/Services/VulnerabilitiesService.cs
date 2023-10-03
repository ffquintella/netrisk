﻿using ClientServices.Interfaces;
using DAL.Entities;
using Model.Exceptions;
using RestSharp;

namespace ClientServices.Services;

public class VulnerabilitiesService: ServiceBase, IVulnerabilitiesService
{
    public VulnerabilitiesService(IRestService restService) : base(restService)
    {
    }

    public List<Vulnerability> GetAll()
    {
        var client = _restService.GetClient();
        
        var request = new RestRequest($"/Vulnerabilities");
        try
        {
            var response = client.Get<List<Vulnerability>>(request);

            if (response == null)
            {
                _logger.Error("Error listing vulnerabilities");
                throw new InvalidHttpRequestException("Error listing vulnerabilities", "/Vulnerabilities", "GET");
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            _logger.Error("Error listing vulnerabilities message:{Message}", ex.Message);
            throw new RestComunicationException("Error listing vulnerabilities", ex);
        }
    }
}