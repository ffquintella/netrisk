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
        using var client = RestService.GetClient();
        
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

    public async Task<List<RisksOnDay>> GetRisksOverTimeAsync()
    {
        using var client = RestService.GetClient();
        
        var request = new RestRequest("/Statistics/RisksOverTime");

        request.AddParameter("daysSpan", 90);
        
        try
        {
            var response = await client.GetAsync<List<RisksOnDay>>(request);

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

    public async Task<List<ImportSeverity>> GetVulnerabilitiesServerityByImportAsync()
    {
        using var client = RestService.GetClient();
        
        var request = new RestRequest("/Statistics/VulnerabilitiesSeverityByImport");

        request.AddParameter("itemCount", 120);
        
        try
        {
            var response = await client.GetAsync<List<ImportSeverity>>(request);

            if (response == null)
            {
                Logger.Error("Error getting vulnerabilities severity by import");
                response = new List<ImportSeverity>();
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                _authenticationService.DiscardAuthenticationToken();
            }
            Logger.Error("Error getting vulnerabilities severity by import message:{ExMessage}", ex.Message);
            throw new RestComunicationException("Error getting vulnerabilities severity by import", ex);
        }
    }

    

    public List<ValueName> GetVulnerabilitiesDistribution()
    {
        using var client = RestService.GetClient();
        
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
        using var client = RestService.GetClient();
        
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

    public async Task<float> GetVulnerabilitiesVerifiedPercentageAsync()
    {
        using var client = RestService.GetClient();
        
        var request = new RestRequest("/Statistics/Vulnerabilities/VerifiedPercentage");

        try
        {
            var response = await client.GetAsync<float>(request);
            
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

    public async Task<RisksNumbers> GetRisksNumbersAsync()
    {
        using var client = RestService.GetClient();
        
        var request = new RestRequest("/Statistics/Risks/Numbers");
        
        try
        {
            var response = await client.GetAsync<RisksNumbers>(request);
            
            if(response == null)
            {
                Logger.Error("Error getting risks numbers");
                throw new HttpRequestException("Error getting risks numbers");
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                _authenticationService.DiscardAuthenticationToken();
            }
            Logger.Error("Error getting risks numbers message:{ExMessage}", ex.Message);
            throw new RestComunicationException("Error getting risks numbers", ex);
        }
        
    }

    public async Task<List<RiskGroup>> GetRisksTopGroupsAsync()
    {
        using var client = RestService.GetClient();
        
        var request = new RestRequest("/Statistics/Risks/TopGroups");
        
        try
        {
            var response = await client.GetAsync<List<RiskGroup>>(request);
            
            if(response == null)
            {
                Logger.Error("Error getting risks top groups");
                throw new HttpRequestException("Error getting risks top groups");
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                _authenticationService.DiscardAuthenticationToken();
            }
            Logger.Error("Error getting risks top groups message:{ExMessage}", ex.Message);
            throw new RestComunicationException("Error getting risks top groups", ex);
        }
    }

    public async Task<List<RiskEntity>> GetRisksTopEntitiesAsync(int count = 10, string? entityType = null)
    {
        using var client = RestService.GetClient();
        
        var request = new RestRequest("/Statistics/Risks/TopEntities");
        
        request.AddParameter("count", count);
        if(entityType != null)
        {
            request.AddParameter("entityType", entityType);
        }

        try
        {
            var response = await client.GetAsync<List<RiskEntity>>(request);
            
            if(response == null)
            {
                Logger.Error("Error getting risks top entities");
                throw new HttpRequestException("Error getting risks top entities");
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                _authenticationService.DiscardAuthenticationToken();
            }
            Logger.Error("Error getting risks top entities message:{ExMessage}", ex.Message);
            throw new RestComunicationException("Error getting risks top entities", ex);
        }
    }
    
    public VulnerabilityNumbers GetVulnerabilityNumbers()
    {
        using var client = RestService.GetClient();
        
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

    public async Task<VulnerabilityNumbersByStatus> GetVulnerabilitiesNumbersByStatusAsync()
    {
        using var client = RestService.GetClient();
        
        var request = new RestRequest("/Statistics/Vulnerabilities/NumbersByStatus");

        try
        {
            var response = await client.GetAsync<VulnerabilityNumbersByStatus>(request);
            
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
        using var client = RestService.GetClient();
        
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

    public async Task<SecurityControlsStatistics> GetSecurityControlStatisticsAsync()
    {
        using var client = RestService.GetClient();
        
        var request = new RestRequest("/Statistics/SecurityControls");
        
        try
        {
            var response = await client.GetAsync<SecurityControlsStatistics>(request);

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
            throw new RestComunicationException("Error getting security control statistics", ex);
        }
    }

    public List<LabeledPoints> GetRisksVsCosts(double minRisk, double maxRisk)
    {
        using var client = RestService.GetClient();
        
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
        using var client = RestService.GetClient();
        
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

    public List<ValueNameType> GetEntitiesRiskValues(int? parentId = null, int topCount = 10)
    {
        using var client = RestService.GetClient();
        
        var request = new RestRequest("/Statistics/EntitiesRiskValues");
        
        request.AddParameter("topCount", topCount);
        
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