using ClientServices.Interfaces;
using DAL.Entities;
using Model.Exceptions;
using RestSharp;

namespace ClientServices.Services;

public class VulnerabilitiesRestService: RestServiceBase, IVulnerabilitiesService
{
    public VulnerabilitiesRestService(IRestService restService) : base(restService)
    {
    }

    public List<Vulnerability> GetAll()
    {
        var client = RestService.GetClient();
        
        var request = new RestRequest($"/Vulnerabilities");
        try
        {
            var response = client.Get<List<Vulnerability>>(request);

            if (response == null)
            {
                Logger.Error("Error listing vulnerabilities");
                throw new InvalidHttpRequestException("Error listing vulnerabilities", "/Vulnerabilities", "GET");
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error listing vulnerabilities message:{Message}", ex.Message);
            throw new RestComunicationException("Error listing vulnerabilities", ex);
        }
    }

    public Vulnerability GetOne(int id)
    {
        var client = RestService.GetClient();
        
        var request = new RestRequest($"/Vulnerabilities/{id}");

        request.AddParameter("includeDetails", "true");
        
        try
        {
            var response = client.Get<Vulnerability>(request);

            if (response == null)
            {
                Logger.Error("Error getting vulnerability");
                throw new InvalidHttpRequestException("Error getting vulnerability", $"/Vulnerabilities/{id}", "GET");
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error getting vulnerability message:{Message}", ex.Message);
            throw new RestComunicationException("Error getting vulnerability", ex);
        } 
    }

    public List<RiskScoring> GetRisksScores(int vulnerabilityId)
    {
        var client = RestService.GetClient();
        
        var request = new RestRequest($"/Vulnerabilities/{vulnerabilityId}/RisksScores");

        
        try
        {
            var response = client.Get<List<RiskScoring>>(request);

            if (response == null)
            {
                Logger.Error("Error getting vulnerability risks scores");
                throw new InvalidHttpRequestException("Error getting vulnerability risks scores", $"/Vulnerabilities/{vulnerabilityId}", "GET");
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error getting vulnerability  risks scores message:{Message}", ex.Message);
            throw new RestComunicationException("Error getting vulnerability  risks scores", ex);
        } 
    }
}