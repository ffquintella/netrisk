using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using ClientServices.Interfaces;
using Model.DTO.Statistics;
using Model.Exceptions;
using Model.Statistics;
using RestSharp;

namespace ClientServices.Services;

public class StatisticsRestService: RestServiceBase, IStatisticsService
{

    private IAuthenticationService _authenticationService;

    public StatisticsRestService(IRestService restService, IAuthenticationService authenticationService): base(restService)
    {
        _authenticationService = authenticationService;
    }
    
    public List<RisksOnDay> GetRisksOverTime()
    {
        var client = RestService.GetClient();
        
        var request = new RestRequest("/Statistics/RisksOverTime");

        request.AddParameter("daysSpan", 90);
        
        try
        {
            var response = client.Get<List<RisksOnDay>>(request);

            if (response == null)
            {
                Logger.Error("Error getting risks over time");
                response = new List<RisksOnDay>();
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                _authenticationService.DiscardAuthenticationToken();
            }
            Logger.Error("Error getting risks over time message:{ExMessage}", ex.Message);
            throw new RestComunicationException("Error getting risks over time", ex);
        }
        
    }

    public List<ValueName> GetVulnerabilitiesDistribution()
    {
        var client = RestService.GetClient();
        
        var request = new RestRequest("/Statistics/Vulnerabilities/Distribution");

        try
        {
            var response = client.Get<List<ValueName>>(request);

            if (response == null)
            {
                Logger.Error("Error getting vulnerabilities distribution");
                response = new List<ValueName>();
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                _authenticationService.DiscardAuthenticationToken();
            }
            Logger.Error("Error getting vulnerabilities distribution message:{ExMessage}", ex.Message);
            throw new RestComunicationException("Error getting vulnerabilities distribution", ex);
        }
    }

    public List<ValueName> GetVulnerabilityImportSources()
    {
        var client = RestService.GetClient();
        
        var request = new RestRequest("/Statistics/Vulnerabilities/Sources");

        try
        {
            var response = client.Get<List<ValueName>>(request);

            if (response == null)
            {
                Logger.Error("Error getting vulnerabilities import sources");
                response = new List<ValueName>();
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                _authenticationService.DiscardAuthenticationToken();
            }
            Logger.Error("Error getting vulnerabilities import sources message:{ExMessage}", ex.Message);
            throw new RestComunicationException("Error getting vulnerabilities import sources", ex);
        }
    }

    public float GetVulnerabilitiesVerifiedPercentage()
    {
        var client = RestService.GetClient();
        
        var request = new RestRequest("/Statistics/Vulnerabilities/VerifiedPercentage");

        try
        {
            var response = client.Get<float>(request);
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                _authenticationService.DiscardAuthenticationToken();
            }
            Logger.Error("Error getting vulnerabilities verified percentage message:{ExMessage}", ex.Message);
            throw new RestComunicationException("Error getting vulnerabilities verified percentage", ex);
        }
    }

    public VulnerabilityNumbers GetVulnerabilityNumbers()
    {
        var client = RestService.GetClient();
        
        var request = new RestRequest("/Statistics/Vulnerabilities/Numbers");

        try
        {
            var response = client.Get<VulnerabilityNumbers>(request);
            
            if(response == null)
            {
                Logger.Error("Error getting vulnerabilities numbers");
                throw new HttpRequestException("Error getting vulnerabilities numbers");
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                _authenticationService.DiscardAuthenticationToken();
            }
            Logger.Error("Error getting vulnerabilities numbers message:{ExMessage}", ex.Message);
            throw new RestComunicationException("Error getting vulnerabilities numbers", ex);
        }
    }

    public VulnerabilityNumbersByStatus GetVulnerabilitiesNumbersByStatus()
    {
        var client = RestService.GetClient();
        
        var request = new RestRequest("/Statistics/Vulnerabilities/NumbersByStatus");

        try
        {
            var response = client.Get<VulnerabilityNumbersByStatus>(request);
            
            if(response == null)
            {
                Logger.Error("Error getting vulnerabilities numbers by status");
                throw new HttpRequestException("Error getting vulnerabilities numbers by status");
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                _authenticationService.DiscardAuthenticationToken();
            }
            Logger.Error("Error getting vulnerabilities numbers bu status message:{ExMessage}", ex.Message);
            throw new RestComunicationException("Error getting vulnerabilities numbers by status", ex);
        }
    }

    public SecurityControlsStatistics GetSecurityControlStatistics()
    {
        var client = RestService.GetClient();
        
        var request = new RestRequest("/Statistics/SecurityControls");
        
        try
        {
            var response = client.Get<SecurityControlsStatistics>(request);

            if (response == null)
            {
                Logger.Error("Error getting security control statistics");
                response = new SecurityControlsStatistics();
            }
            
            return response;
            
        }
        catch (Exception ex)
        {
            Logger.Error("Error getting security control statistics message:{ExMessage}", ex.Message);
            throw new RestComunicationException("Error getting risks over time", ex);
        }
        
    }

    public List<LabeledPoints> GetRisksVsCosts(double minRisk, double maxRisk)
    {
        var client = RestService.GetClient();
        
        var request = new RestRequest("/Statistics/RisksVsCosts");

        request.AddParameter("minRisk", minRisk);
        request.AddParameter("minRisk", minRisk);
        
        try
        {
            var response = client.Get<List<LabeledPoints>>(request);

            if (response == null)
            {
                Logger.Error("Error getting risks over time");
                throw new HttpRequestException("Error getting risks over time");
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                _authenticationService.DiscardAuthenticationToken();
            }
            Logger.Error("Error getting risks over time message:{ExMessage}", ex.Message);
            throw new RestComunicationException("Error getting risks over time", ex);
        }
    }

    public List<LabeledPoints> GetRisksImpactVsProbability(double minRisk, double maxRisk)
    {
        var client = RestService.GetClient();
        
        var request = new RestRequest("/Statistics/RisksImpactVsProbability");

        request.AddParameter("minRisk", minRisk);
        request.AddParameter("minRisk", minRisk);
        
        try
        {
            var response = client.Get<List<LabeledPoints>>(request);

            if (response == null)
            {
                Logger.Error("Error getting risks impact vs provability");
                throw new HttpRequestException("Error getting risks impact vs provability");
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                _authenticationService.DiscardAuthenticationToken();
            }
            Logger.Error("Error getting risks impact vs provability message:{ExMessage}", ex.Message);
            throw new RestComunicationException("Error getting risks impact vs provability", ex);
        }
    }

    public List<ValueNameType> GetEntitiesRiskValues(int? parentId = null)
    {
        var client = RestService.GetClient();
        
        var request = new RestRequest("/Statistics/EntitiesRiskValues");
        
        if (parentId != null)
        {
            request.AddParameter("parentId", parentId.Value);
        }
        
        try
        {
            var response = client.Get<List<ValueNameType>>(request);

            if (response == null)
            {
                Logger.Error("Error getting Entities Risk Values");
                throw new HttpRequestException("Error getting Entities Risk Values");
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                _authenticationService.DiscardAuthenticationToken();
            }
            Logger.Error("Error getting Entities Risk Values message:{ExMessage}", ex.Message);
            throw new RestComunicationException("Error getting Entities Risk Values", ex);
        }
    }
}