using System.Net;
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
}