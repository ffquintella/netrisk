using System.Net;
using DAL.Entities;
using Model.Exceptions;
using RestSharp;
using System.Text.Json;
using ClientServices.Interfaces;
using Model.Rest;

namespace ClientServices.Services;

public class RisksService: ServiceBase, IRisksService
{
    private IAuthenticationService _authenticationService;

    public RisksService(IRestService restService, 
        IAuthenticationService authenticationService): base(restService)
    {
        _authenticationService = authenticationService;
    }
    
    public List<Risk> GetAllRisks(bool includeClosed = false)
    {
        var client = _restService.GetClient();
        
        var request = new RestRequest("/Risks");
        
        if(includeClosed)
            request.AddQueryParameter("includeClosed", "true");
        
        try
        {
            var response = client.Get<List<Risk>>(request);

            if (response == null)
            {
                _logger.Error("Error getting risks");
                response = new List<Risk>();
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                _authenticationService.DiscardAuthenticationToken();
            }
            _logger.Error("Error getting all risks message: {Message}", ex.Message);
            throw new RestComunicationException("Error getting all risks", ex);
        }
    }
    
    public List<Risk> GetUserRisks()
    {

        var client = _restService.GetClient();
        
        var request = new RestRequest("/Risks/MyRisks");
        
        try
        {
            var response = client.Get<List<Risk>>(request);

            if (response == null)
            {
                _logger.Error("Error getting my risks ");
                response = new List<Risk>();
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                _authenticationService.DiscardAuthenticationToken();
            }
            _logger.Error("Error getting my risks message: {Message}", ex.Message);
            throw new RestComunicationException("Error getting my risks", ex);
        }


    }

    public string GetRiskCategory(int id)
    {
        var client = _restService.GetClient();
        
        var request = new RestRequest($"/Risks/Categories/{id}");
        
        try
        {
            var response = client.Get<Category>(request);

            if (response == null)
            {
                _logger.Error("Error getting category ");
                return "ERROR";
            }
            
            return response.Name;
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                _authenticationService.DiscardAuthenticationToken();
            }
            _logger.Error("Error getting risk category message:{Message}", ex.Message);
            throw new RestComunicationException("Error getting risk category", ex);
        }
    }

    public RiskScoring GetRiskScoring(int id)
    {
        var client = _restService.GetClient();
        
        var request = new RestRequest($"/Risks/{id}/Scoring");
        try
        {
            var response = client.Get<RiskScoring>(request);

            if (response == null)
            {
                _logger.Error("Error getting scoring for risk {Id}", id);
                throw new RestComunicationException($"Error getting scoring for risk {id}");
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                _authenticationService.DiscardAuthenticationToken();
            }
            _logger.Error("Error getting risk scoring message:{Message}", ex.Message);
            throw new RestComunicationException("Error getting risk scoring", ex);
        }
    }

    public Closure? GetRiskClosure(int riskId)
    {
        var client = _restService.GetClient();
        
        var request = new RestRequest($"/Risks/{riskId}/Closure");
        try
        {
            var response = client.Get<Closure>(request);

            if (response == null)
            {
                _logger.Error("Error getting closure ");
                return null;
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                _authenticationService.DiscardAuthenticationToken();
            }
            _logger.Error("Error getting risk closure message: {Message}", ex.Message);
            throw new RestComunicationException("Error getting risk closure", ex);
        }

    }

    public List<CloseReason> GetRiskCloseReasons()
    {
        var client = _restService.GetClient();
        
        var request = new RestRequest($"/Risks/CloseReasons");
        try
        {
            var response = client.Get<List<CloseReason>>(request);

            if (response == null)
            {
                _logger.Error("Error getting closure reasons");
                return null;
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                _authenticationService.DiscardAuthenticationToken();
            }
            _logger.Error("Error getting risk closure reasons message: {Message}", ex.Message);
            throw new RestComunicationException("Error getting risk closure reasons", ex);
        }
    }


    public void CloseRisk(Closure closure)
    {
        var client = _restService.GetClient();
        
        var request = new RestRequest($"/Risks/{closure.RiskId}/Closure");
        
        request.AddJsonBody(closure);
        
        try
        {
            var response = client.Post<Closure>(request);

            if (response == null)
            {
                _logger.Error("Error closing risk");
                throw new RestComunicationException("Error closing risk");
            }
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                _authenticationService.DiscardAuthenticationToken();
            }
            _logger.Error("Error closing risk: {Message}", ex.Message);
            throw new RestComunicationException("Error closing risk", ex);
        }
    }
    
    public List<Category>? GetRiskCategories()
    {
        var client = _restService.GetClient();
        
        var request = new RestRequest($"/Risks/Categories");
        
        try
        {
            var response = client.Get<List<Category>>(request);

            if (response == null)
            {
                _logger.Error("Error getting categories ");
                return null;
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                _authenticationService.DiscardAuthenticationToken();
            }
            _logger.Error("Error getting risk categories message: {Message}", ex.Message);
            throw new RestComunicationException("Error getting risk categories", ex);
        }
    }
    
    public string GetRiskSource(int id)
    {
        var client = _restService.GetClient();
        
        var request = new RestRequest($"/Risks/Sources/{id}");
        
        try
        {
            var response = client.Get<Source>(request);

            if (response == null)
            {
                _logger.Error("Error getting source ");
                return "ERROR";
            }
            
            return response.Name;
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                _authenticationService.DiscardAuthenticationToken();
            }
            _logger.Error("Error getting risk source message: {Message}", ex.Message);
            throw new RestComunicationException("Error getting risk source", ex);
        }
    }

    public Risk? CreateRisk(Risk risk)
    {
        risk.Id = 0;
        using (var client = _restService.GetClient())
        {
            var request = new RestRequest($"/Risks");

            request.AddJsonBody(risk);
        
            try
            {
                var response = client.Post(request);

                if (response.StatusCode != HttpStatusCode.Created)
                {
                    _logger.Error("Error creating risk ");
                    
                    var opResult = JsonSerializer.Deserialize<OperationError>(response!.Content!);

                    throw new ErrorSavingException("Error creating risk", opResult!);
                    

                }
                
                var newRisk = JsonSerializer.Deserialize<Risk?>(response!.Content!, new JsonSerializerOptions 
                {
                    PropertyNameCaseInsensitive = true
                });

                return newRisk;

            }
            catch (HttpRequestException ex)
            {
                if (ex.StatusCode == HttpStatusCode.Unauthorized)
                {
                    _authenticationService.DiscardAuthenticationToken();
                }
                _logger.Error("Error creating risk message: {Message}", ex.Message);
                throw new RestComunicationException("Error creating risk", ex);
            }
        }

    }
    
    public void SaveRisk(Risk risk)
    {

        using (var client = _restService.GetClient())
        {
            var request = new RestRequest($"/Risks/{risk.Id}");

            request.AddJsonBody(risk);
        
            try
            {
                var response = client.Put(request);

                if (response.StatusCode != HttpStatusCode.OK)
                {
                    _logger.Error("Error saving risk with id: {Id}", risk.Id);
                    
                    var opResult = JsonSerializer.Deserialize<OperationError>(response!.Content!);

                    throw new ErrorSavingException("Error saving risk", opResult!);
                    
                }
                

            }
            catch (HttpRequestException ex)
            {
                if (ex.StatusCode == HttpStatusCode.Unauthorized)
                {
                    _authenticationService.DiscardAuthenticationToken();
                }
                _logger.Error("Error saving risk {Id} message:{ExMessage}", risk.Id, ex.Message);
                throw new RestComunicationException("Error saving risk", ex);
            }
        }

    }

    public void DeleteRisk(Risk risk)
    {

        using (var client = _restService.GetClient())
        {
            var request = new RestRequest($"/Risks/{risk.Id}");

            try
            {
                var response = client.Delete(request);

                if (response.StatusCode != HttpStatusCode.OK)
                {
                    _logger.Error("Error deleting risk with id: {Id}", risk.Id);
                    
                    //var opResult = JsonSerializer.Deserialize<OperationError>(response!.Content!);

                    throw new Exception("Error deleting risk");
                    
                }
                

            }
            catch (HttpRequestException ex)
            {
                if (ex.StatusCode == HttpStatusCode.Unauthorized)
                {
                    _authenticationService.DiscardAuthenticationToken();
                }
                _logger.Error("Error deleting risk {Id} message:{ExMessage}", risk.Id, ex.Message);
                throw new RestComunicationException("Error deleting risk", ex);
            }
        }

    }
    
    public RiskScoring? CreateRiskScoring(RiskScoring scoring)
    {
        scoring.CvssAuthentication = "N";
        scoring.CvssAccessVector = "N";
        scoring.CvssAccessComplexity = "L";
        scoring.CvssConfImpact = "C";
        scoring.CvssIntegImpact = "C";
        scoring.CvssAvailImpact = "C";
        scoring.CvssExploitability = "ND";
        scoring.CvssRemediationLevel = "ND";
        scoring.CvssReportConfidence = "ND";
        scoring.CvssCollateralDamagePotential = "ND";
        scoring.CvssTargetDistribution = "ND";
        scoring.CvssConfidentialityRequirement = "ND";
        scoring.CvssIntegrityRequirement = "ND";
        scoring.CvssAvailabilityRequirement = "ND";
        
        
        
        using (var client = _restService.GetClient())
        {
            var request = new RestRequest($"/Risks/{scoring.Id}/Scoring");

            request.AddJsonBody(scoring);
        
            try
            {
                var response = client.Post(request);

                if (response.StatusCode != HttpStatusCode.Created)
                {
                    _logger.Error("Error creating risk scoring");
                    
                    var opResult = JsonSerializer.Deserialize<OperationError>(response!.Content!);

                    throw new ErrorSavingException("Error creating risk scoring", opResult!);

                }
            
                return  JsonSerializer.Deserialize<RiskScoring?>(response!.Content!, new JsonSerializerOptions 
                {
                    PropertyNameCaseInsensitive = true
                });
            
            }
            catch (HttpRequestException ex)
            {
                if (ex.StatusCode == HttpStatusCode.Unauthorized)
                {
                    _authenticationService.DiscardAuthenticationToken();
                }
                _logger.Error("Error creating risk scoring message: {Message}", ex.Message);
                throw new RestComunicationException("Error creating risk score", ex);
            }
        }

    }

    public void SaveRiskScoring(RiskScoring scoring)
    {
        using (var client = _restService.GetClient())
        {
            var request = new RestRequest($"/Risks/{scoring.Id}/Scoring");

            request.AddJsonBody(scoring);
        
            try
            {
                var response = client.Put(request);

                if (response.StatusCode != HttpStatusCode.OK)
                {
                    _logger.Error("Error saving risk scoring with id: {Id}", scoring.Id);
                    
                    var opResult = JsonSerializer.Deserialize<OperationError>(response!.Content!);

                    throw new ErrorSavingException("Error saving risk scoring", opResult!);
                    
                }
                

            }
            catch (HttpRequestException ex)
            {
                if (ex.StatusCode == HttpStatusCode.Unauthorized)
                {
                    _authenticationService.DiscardAuthenticationToken();
                }
                _logger.Error("Error saving risk {Id} message:{ExMessage}", scoring.Id, ex.Message);
                throw new RestComunicationException("Error saving risk", ex);
            }
        }
    }

    public void DeleteRiskScoring(int scoringId)
    {
        using (var client = _restService.GetClient())
        {
            var request = new RestRequest($"/Risks/{scoringId}/Scoring");

            try
            {
                var response = client.Delete(request);

                if (response.StatusCode != HttpStatusCode.OK)
                {
                    _logger.Error("Error deleting risk scoring with id: {Id}", scoringId);
                    
                    throw new Exception("Error deleting risk scoring");
                    
                }
                

            }
            catch (HttpRequestException ex)
            {
                if (ex.StatusCode == HttpStatusCode.Unauthorized)
                {
                    _authenticationService.DiscardAuthenticationToken();
                }
                _logger.Error("Error deleting risk scoring: {Id} message:{ExMessage}", scoringId, ex.Message);
                throw new RestComunicationException("Error deleting risk scoring", ex);
            }
        }
    }
    
    public bool RiskSubjectExists(string? subject)
    {
        if (subject == null || subject == "") return false;
        
        var client = _restService.GetClient();
        
        var request = new RestRequest($"/Risks/Exists");

        request.AddParameter("subject", subject);
        
        try
        {
            var response = client.Get(request);

            if (response == null)
            {
                _logger.Error("Error getting risk subject status ");
                return false;
            }

            if (response.StatusCode == HttpStatusCode.OK ) return true;
            return false;

        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                _authenticationService.DiscardAuthenticationToken();
            }
            _logger.Error("Error getting risk subject status message:{0}", ex.Message);
            throw new RestComunicationException("Error getting risk subject", ex);
        }
        
    }
    
    public List<Source>? GetRiskSources()
    {
        var client = _restService.GetClient();
        
        var request = new RestRequest($"/Risks/Sources");
        
        try
        {
            var response = client.Get<List<Source>>(request);

            if (response == null)
            {
                _logger.Error("Error getting sources ");
                return null;
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                _authenticationService.DiscardAuthenticationToken();
            }
            _logger.Error("Error getting risk source message:{0}", ex.Message);
            throw new RestComunicationException("Error getting risk source", ex);
        }
    }

    public List<Likelihood>? GetProbabilities()
    {
        var client = _restService.GetClient();
        
        var request = new RestRequest($"/Risks/Probabilities");
        
        try
        {
            var response = client.Get<List<Likelihood>>(request);

            if (response == null)
            {
                _logger.Error("Error getting probabilities ");
                return null;
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                _authenticationService.DiscardAuthenticationToken();
            }
            _logger.Error("Error getting risk probabilities message:{Message}", ex.Message);
            throw new RestComunicationException("Error getting risk probabilities", ex);
        }
    }

    public List<Impact>? GetImpacts()
    {
        var client = _restService.GetClient();
        
        var request = new RestRequest($"/Risks/Impacts");
        
        try
        {
            var response = client.Get<List<Impact>>(request);

            if (response == null)
            {
                _logger.Error("Error getting impacts ");
                return null;
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                _authenticationService.DiscardAuthenticationToken();
            }
            _logger.Error("Error getting risk impacts message:{Message}", ex.Message);
            throw new RestComunicationException("Error getting risk impacts", ex);
        }
    }

    public float GetRiskScore(int probabilityId, int impactId)
    {
        var client = _restService.GetClient();
        
        var request = new RestRequest($"/Risks/ScoreValue-{probabilityId}-{impactId}");
        
        try
        {
            var response = client.Get<float?>(request);

            if (response == null)
            {
                _logger.Error("Error getting score value ");
                return 0;
            }
            
            return response.Value;
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                _authenticationService.DiscardAuthenticationToken();
            }
            _logger.Error("Error getting risk score value message:{Message}", ex.Message);
            throw new RestComunicationException("Error getting risk score value", ex);
        }
    }
    
    public List<RiskCatalog> GetRiskTypes()
    {
        return GetRiskTypes("", true);
    }
    public List<RiskCatalog> GetRiskTypes(string ids, bool all = false)
    {
        //var all = false;
        //if (ids == "") all = true;

        ids = ids.TrimEnd(',');
        
        var client = _restService.GetClient();
        
        
        var request = new RestRequest($"/Risks/Catalogs");

        if(!all) request.AddParameter("list", ids);
        
        try
        {
            var response = client.Get<List<RiskCatalog>>(request);

            if (response == null)
            {
                _logger.Error("Error getting risk catalogs ");
                return new List<RiskCatalog>();
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                _authenticationService.DiscardAuthenticationToken();
            }
            _logger.Error("Error getting risk catalogs message:{0}", ex.Message);
            throw new RestComunicationException("Error getting risk catalogs", ex);
        }
    }
}