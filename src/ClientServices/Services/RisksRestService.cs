using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using DAL.Entities;
using Model.Exceptions;
using RestSharp;
using System.Text.Json;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using Model.DTO;
using Model.Rest;
using Tools.Helpers;

namespace ClientServices.Services;

public class RisksRestService(
    IRestService restService,
    IAuthenticationService authenticationService)
    : RestServiceBase(restService), IRisksService
{
    public async Task<List<Risk>> GetAllRisksAsync(bool includeClosed = false)
    {
        using var client = RestService.GetClient();
        
        var request = new RestRequest("/Risks");
        
        if(includeClosed)
            request.AddQueryParameter("includeClosed", "true");
        
        try
        {
            var response = await client.GetAsync<List<Risk>>(request);

            if (response == null)
            {
                Logger.Error("Error getting risks");
                response = new List<Risk>();
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                authenticationService.DiscardAuthenticationToken();
            }
            Logger.Error("Error getting all risks message: {Message}", ex.Message);
            throw new RestComunicationException("Error getting all risks", ex);
        }
    }

    public async Task<List<Risk>> GetUserRisksAsync()
    {
        using var client = RestService.GetClient();
        
        var request = new RestRequest("/Risks/MyRisks");
        
        try
        {
            var response = await client.GetAsync<List<Risk>>(request);

            if (response == null)
            {
                Logger.Error("Error getting my risks ");
                response = new List<Risk>();
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                authenticationService.DiscardAuthenticationToken();
            }
            Logger.Error("Error getting my risks message: {Message}", ex.Message);
            throw new RestComunicationException("Error getting my risks", ex);
        }
    }

    public async Task<string> GetRiskCategoryAsync(int id)
    {
        using var client = RestService.GetClient();
        
        var request = new RestRequest($"/Risks/Categories/{id}");
        
        try
        {
            var response = await client.GetAsync<Category>(request);

            if (response == null)
            {
                Logger.Error("Error getting category ");
                return "ERROR";
            }
            
            return response.Name;
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                authenticationService.DiscardAuthenticationToken();
            }
            Logger.Error("Error getting risk category message:{Message}", ex.Message);
            throw new RestComunicationException("Error getting risk category", ex);
        }  
    }

    public async Task<RiskScoring> GetRiskScoringAsync(int id)
    {
        using var client = RestService.GetClient();
        
        var request = new RestRequest($"/Risks/{id}/Scoring");
        try
        {
            var response = await client.GetAsync<RiskScoring>(request);

            if (response == null)
            {
                Logger.Error("Error getting scoring for risk {Id}", id);
                throw new RestComunicationException($"Error getting scoring for risk {id}");
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                authenticationService.DiscardAuthenticationToken();
            }
            Logger.Error("Error getting risk scoring message:{Message}", ex.Message);
            throw new RestComunicationException("Error getting risk scoring", ex);
        }
    }
    

    public void AssociateEntityToRisk(int riskId, int entityId)
    {
        using var client = RestService.GetClient();
        
        var request = new RestRequest($"/Risks/{riskId}/Entity");
        try
        {
            Object eid = entityId;
            
            request.AddJsonBody(eid);
            
            var response = client.Put<string>(request);

            if (response == null)
            {
                Logger.Error("Error adding entity {EntityId} for risk {Id}", entityId, riskId);
                throw new RestComunicationException($"Error adding entity {entityId} for risk {riskId}");
            }
            
            
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                authenticationService.DiscardAuthenticationToken();
            }
            Logger.Error("Error adding entity for risk message:{Message}", ex.Message);
            throw new RestComunicationException("Error adding entity for risk", ex);
        }
    }

    public Int32? GetEntityIdFromRisk(int riskId)
    {
        using var client = RestService.GetClient();
        
        var request = new RestRequest($"/Risks/{riskId}/Entity");
        try
        {
           
            var response = client.Get(request);

            if(response.StatusCode == HttpStatusCode.NotFound) return null;
            
            if (response.StatusCode != HttpStatusCode.OK)
            {
                Logger.Error("Error getting entity for risk {Id}",  riskId);
                throw new RestComunicationException($"Error getting entity for risk {riskId}");
            }

            
            return response.Content != null ? Int32.Parse(response.Content) : null;
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                authenticationService.DiscardAuthenticationToken();
            }
            Logger.Error("Error adding getting for risk message:{Message}", ex.Message);
            throw new RestComunicationException("Error getting entity for risk", ex);
        }
    }
    
    public List<FileListing> GetRiskFiles(int riskId)
    {
        using var client = RestService.GetClient();
        
        var request = new RestRequest($"/Risks/{riskId}/Files");
        try
        {
            var response = client.Get<List<FileListing>>(request);

            if (response == null)
            {
                Logger.Error("Error getting files for risk: {Id}", riskId);
                throw new RestComunicationException($"Error getting files for risk: {riskId}");
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                authenticationService.DiscardAuthenticationToken();
            }
            Logger.Error("Error getting risk files message:{Message}", ex.Message);
            throw new RestComunicationException("Error getting risk files", ex);
        }
    }

    public async Task<List<FileListing>> GetRiskFilesAsync(int riskId)
    {
        using var client = RestService.GetClient();
        
        var request = new RestRequest($"/Risks/{riskId}/Files");
        try
        {
            var response = await client.GetAsync<List<FileListing>>(request);

            if (response == null)
            {
                Logger.Error("Error getting files for risk: {Id}", riskId);
                throw new RestComunicationException($"Error getting files for risk: {riskId}");
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                authenticationService.DiscardAuthenticationToken();
            }
            Logger.Error("Error getting risk files message:{Message}", ex.Message);
            throw new RestComunicationException("Error getting risk files", ex);
        }
    }

    public List<MgmtReview> GetRiskMgmtReviews(int riskId)
    {
        using var client = RestService.GetClient();
        
        var request = new RestRequest($"/Risks/{riskId}/MgmtReviews");
        try
        {
            var response = client.Get<List<MgmtReview>>(request);

            if (response == null)
            {
                Logger.Error("Error getting reviews for risk: {Id}", riskId);
                throw new RestComunicationException($"Error getting reviews for risk: {riskId}");
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                authenticationService.DiscardAuthenticationToken();
            }
            Logger.Error("Error getting risk reviews message:{Message}", ex.Message);
            throw new RestComunicationException("Error getting risk reviews", ex);
        }
    }

    public ReviewLevel GetRiskReviewLevel(int riskId)
    {
        using var client = RestService.GetClient();
        
        var request = new RestRequest($"/Risks/{riskId}/ReviewLevel");
        try
        {
            var response = client.Get<ReviewLevel>(request);

            if (response == null)
            {
                Logger.Error("Error getting review level for risk: {Id}", riskId);
                throw new RestComunicationException($"Error getting review level for risk: {riskId}");
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                authenticationService.DiscardAuthenticationToken();
            }
            Logger.Error("Error getting risk review level message:{Message}", ex.Message);
            throw new RestComunicationException("Error getting risk review level", ex);
        }
    }

    public MgmtReview? GetRiskLastMgmtReview(int riskId)
    {
        using var client = RestService.GetClient();
        
        var request = new RestRequest($"/Risks/{riskId}/LastMgmtReview");
        try
        {
            var response = client.Get(request);

            if(response.StatusCode == HttpStatusCode.NotFound) return null;
            
            
            var review = response.Content != null ? JsonSerializer.Deserialize<MgmtReview>(response.Content, new JsonSerializerOptions 
            {
                PropertyNameCaseInsensitive = true
            }) : null;
            
            return review;

        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                authenticationService.DiscardAuthenticationToken();
            }
            Logger.Error("Error getting last risk review message:{Message}", ex.Message);
            throw new RestComunicationException("Error getting last risk reviews", ex);
        }
    }

    public async Task<MgmtReview?> GetRiskLastMgmtReviewAsync(int riskId)
    {
        using var client = RestService.GetClient();
        
        var request = new RestRequest($"/Risks/{riskId}/LastMgmtReview");
        try
        {
            var response = await client.GetAsync(request);

            if(response.StatusCode == HttpStatusCode.NotFound) return null;
            
            
            var review = response.Content != null ? JsonSerializer.Deserialize<MgmtReview>(response.Content, new JsonSerializerOptions 
            {
                PropertyNameCaseInsensitive = true
            }) : null;
            
            return review;

        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                authenticationService.DiscardAuthenticationToken();
            }
            Logger.Error("Error getting last risk review message:{Message}", ex.Message);
            throw new RestComunicationException("Error getting last risk reviews", ex);
        }
    }

    public Closure? GetRiskClosure(int riskId)
    {
        using  var client = RestService.GetClient();
        
        var request = new RestRequest($"/Risks/{riskId}/Closure");
        try
        {
            var response = client.Get<Closure>(request);

            if (response == null)
            {
                Logger.Error("Error getting closure ");
                return null;
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                authenticationService.DiscardAuthenticationToken();
            }
            Logger.Error("Error getting risk closure message: {Message}", ex.Message);
            throw new RestComunicationException("Error getting risk closure", ex);
        }

    }

    public List<CloseReason> GetRiskCloseReasons()
    {
        using var client = RestService.GetClient();
        
        var request = new RestRequest($"/Risks/CloseReasons");
        try
        {
            var response = client.Get<List<CloseReason>>(request);

            if (response == null)
            {
                Logger.Error("Error getting closure reasons");
                throw new RestComunicationException("Error getting closure reasons");
                
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                authenticationService.DiscardAuthenticationToken();
            }
            Logger.Error("Error getting risk closure reasons message: {Message}", ex.Message);
            throw new RestComunicationException("Error getting risk closure reasons", ex);
        }
    }


    public void CloseRisk(Closure closure)
    {
        using var client = RestService.GetClient();
        
        var request = new RestRequest($"/Risks/{closure.RiskId}/Closure");
        
        request.AddJsonBody(closure);
        
        try
        {
            var response = client.Post<Closure>(request);

            if (response == null)
            {
                Logger.Error("Error closing risk");
                throw new RestComunicationException("Error closing risk");
            }
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                authenticationService.DiscardAuthenticationToken();
            }
            Logger.Error("Error closing risk: {Message}", ex.Message);
            throw new RestComunicationException("Error closing risk", ex);
        }
    }

    public async Task<List<Category>> GetRiskCategoriesAsync()
    {
        using var client = RestService.GetClient();
        var request = new RestRequest($"/Risks/Categories");
        
        try
        {
            var response = await client.GetAsync<List<Category>>(request);

            if (response == null)
            {
                Logger.Error("Error getting categories ");
                return new List<Category>();
            }
            
            return response.OrderBy(cat => cat.Name).ToList();
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                authenticationService.DiscardAuthenticationToken();
            }
            Logger.Error("Error getting risk categories message: {Message}", ex.Message);
            throw new RestComunicationException("Error getting risk categories", ex);
        }
    }
    

    public async Task<string> GetRiskSourceAsync(int id)
    {
        using var client = RestService.GetClient();
        
        var request = new RestRequest($"/Risks/Sources/{id}");
        
        try
        {
            var response = await client.GetAsync<Source>(request);

            if (response == null)
            {
                Logger.Error("Error getting source ");
                return "ERROR";
            }
            
            return response.Name;
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                authenticationService.DiscardAuthenticationToken();
            }
            Logger.Error("Error getting risk source message: {Message}", ex.Message);
            throw new RestComunicationException("Error getting risk source", ex);
        }
    }

    public List<Risk> GetToReview(int daysSinceLastReview, string? status = null, bool includeNew = false)
    {
        using var client = RestService.GetClient();
        var request = new RestRequest($"/Risks/ToReview");
        
        request.AddQueryParameter("daysSinceLastReview", daysSinceLastReview.ToString());
        
        if(status!= null) request.AddQueryParameter("status", status);
        request.AddQueryParameter("includeNew", includeNew.ToString());
        
        try
        {
            var response = client.Get<List<Risk>>(request);

            if (response != null) return response;
            Logger.Error("Error getting risks to review ");
            throw new RestComunicationException("Error getting risks to review"); 


        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                authenticationService.DiscardAuthenticationToken();
            }
            Logger.Error("Error getting risks to review message:{ExMessage}",  ex.Message);
            throw new RestComunicationException("Error getting risks to review", ex);
        }
        
    }
    
    public Risk? CreateRisk(Risk risk)
    {
        risk.Id = 0;
        risk.Mitigation = null;
        risk.MitigationId = null;
        
        using var client = RestService.GetClient();
        var request = new RestRequest($"/Risks");

        request.AddJsonBody(risk);
        
        try
        {
            var response = client.Post(request);

            if (response.StatusCode != HttpStatusCode.Created)
            {
                Logger.Error("Error creating risk ");
                    
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
                authenticationService.DiscardAuthenticationToken();
            }
            Logger.Error("Error creating risk message: {Message}", ex.Message);
            throw new RestComunicationException("Error creating risk", ex);
        }
    }
    
    public void SaveRisk(Risk risk)
    {
        using var client = RestService.GetClient();
        var request = new RestRequest($"/Risks/{risk.Id}");

        request.AddJsonBody(risk);
        
        try
        {
            var response = client.Put(request);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                Logger.Error("Error saving risk with id: {Id}", risk.Id);
                    
                var opResult = JsonSerializer.Deserialize<OperationError>(response!.Content!);

                throw new ErrorSavingException("Error saving risk", opResult!);
                    
            }
                

        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                authenticationService.DiscardAuthenticationToken();
            }
            Logger.Error("Error saving risk {Id} message:{ExMessage}", risk.Id, ex.Message);
            throw new RestComunicationException("Error saving risk", ex);
        }
    }

    public async Task<List<Vulnerability>> GetVulnerabilitiesAsync(int riskId)
    {
        using var client = RestService.GetReliableClient();
        
        var request = new RestRequest($"/Risks/{riskId}/Vulnerabilities");
        
        try
        {
            var response = await client.GetAsync<List<Vulnerability>>(request);

            if (response == null)
            {
                Logger.Error("Error getting vulnerabilities for risk: {Id}", riskId);
                throw new RestComunicationException("Error getting vulnerabilities for risk");
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                authenticationService.DiscardAuthenticationToken();
            }
            Logger.Error("Error getting vulnerabilities for risk message: {Message}", ex.Message);
            throw new RestComunicationException("Error getting vulnerabilities for risk", ex);
        }
    }

    public async Task<List<Vulnerability>> GetOpenVulnerabilitiesAsync(int riskId)
    {
        using var client = RestService.GetReliableClient();
        
        var request = new RestRequest($"/Risks/{riskId}/Vulnerabilities/Open");
        
        try
        {
            var response = await client.GetAsync<List<Vulnerability>>(request);

            if (response == null)
            {
                Logger.Error("Error getting vulnerabilities for risk: {Id}", riskId);
                throw new RestComunicationException("Error getting vulnerabilities for risk");
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                authenticationService.DiscardAuthenticationToken();
            }
            Logger.Error("Error getting vulnerabilities for risk message: {Message}", ex.Message);
            throw new RestComunicationException("Error getting vulnerabilities for risk", ex);
        }
    }

    public void DeleteRisk(Risk risk)
    {
        using var client = RestService.GetClient();
        var request = new RestRequest($"/Risks/{risk.Id}");

        try
        {
            var response = client.Delete(request);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                Logger.Error("Error deleting risk with id: {Id}", risk.Id);
                    
                //var opResult = JsonSerializer.Deserialize<OperationError>(response!.Content!);

                throw new Exception("Error deleting risk");
                    
            }
                

        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                authenticationService.DiscardAuthenticationToken();
            }
            Logger.Error("Error deleting risk {Id} message:{ExMessage}", risk.Id, ex.Message);
            throw new RestComunicationException("Error deleting risk", ex);
        }
    }
    
    public RiskScoring? CreateRiskScoring(RiskScoring scoring)
    {



        using var client = RestService.GetClient();
        var request = new RestRequest($"/Risks/{scoring.Id}/Scoring");

        request.AddJsonBody(scoring);
        
        try
        {
            var response = client.Post(request);

            if (response.StatusCode != HttpStatusCode.Created)
            {
                Logger.Error("Error creating risk scoring");
                    
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
                authenticationService.DiscardAuthenticationToken();
            }
            Logger.Error("Error creating risk scoring message: {Message}", ex.Message);
            throw new RestComunicationException("Error creating risk score", ex);
        }
    }

    public void SaveRiskScoring(RiskScoring scoring)
    {
        using var client = RestService.GetClient();
        var request = new RestRequest($"/Risks/{scoring.Id}/Scoring");

        request.AddJsonBody(scoring);
        
        try
        {
            var response = client.Put(request);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                Logger.Error("Error saving risk scoring with id: {Id}", scoring.Id);
                    
                var opResult = JsonSerializer.Deserialize<OperationError>(response!.Content!);

                if (opResult != null) throw new ErrorSavingException("Error saving risk scoring", opResult);
            }
                

        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                authenticationService.DiscardAuthenticationToken();
            }
            Logger.Error("Error saving risk {Id} message:{ExMessage}", scoring.Id, ex.Message);
            throw new RestComunicationException("Error saving risk", ex);
        }
    }

    public void DeleteRiskScoring(int scoringId)
    {
        using var client = RestService.GetClient();
        var request = new RestRequest($"/Risks/{scoringId}/Scoring");

        try
        {
            var response = client.Delete(request);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                Logger.Error("Error deleting risk scoring with id: {Id}", scoringId);
                    
                throw new Exception("Error deleting risk scoring");
                    
            }
                

        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                authenticationService.DiscardAuthenticationToken();
            }
            Logger.Error("Error deleting risk scoring: {Id} message:{ExMessage}", scoringId, ex.Message);
            throw new RestComunicationException("Error deleting risk scoring", ex);
        }
    }
    
    public bool RiskSubjectExists(string? subject)
    {
        if (string.IsNullOrEmpty(subject)) return false;
        
        using var client = RestService.GetClient();
        
        var request = new RestRequest($"/Risks/Exists");

        request.AddParameter("subject", subject);
        
        try
        {
            var response = client.Get(request);

            return response.StatusCode == HttpStatusCode.OK;
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                authenticationService.DiscardAuthenticationToken();
            }
            Logger.Error("Error getting risk subject status message:{Message}", ex.Message);
            throw new RestComunicationException("Error getting risk subject", ex);
        }
        
    }
    
    public List<Source>? GetRiskSources()
    {
        using var client = RestService.GetClient();
        
        var request = new RestRequest($"/Risks/Sources");
        
        try
        {
            var response = client.Get<List<Source>>(request);

            if (response != null) return response.OrderBy(r => r.Name).ToList();
            Logger.Error("Error getting sources ");
            return null;

        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                authenticationService.DiscardAuthenticationToken();
            }
            Logger.Error("Error getting risk source message:{Message}", ex.Message);
            throw new RestComunicationException("Error getting risk source", ex);
        }
    }

    public async Task<List<Likelihood>?> GetProbabilitiesAsync()
    {
        using var client = RestService.GetClient();
        
        var request = new RestRequest($"/Risks/Probabilities");
        
        try
        {
            var response = await client.GetAsync<List<Likelihood>>(request);

            if (response != null) return response;
            Logger.Error("Error getting probabilities ");
            return null;

        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                authenticationService.DiscardAuthenticationToken();
            }
            Logger.Error("Error getting risk probabilities message:{Message}", ex.Message);
            throw new RestComunicationException("Error getting risk probabilities", ex);
        }
    }

    public List<Impact>? GetImpacts()
    {
        using var client = RestService.GetClient();
        
        var request = new RestRequest($"/Risks/Impacts");
        
        try
        {
            var response = client.Get<List<Impact>>(request);

            if (response != null) return response;
            Logger.Error("Error getting impacts ");
            return null;

        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                authenticationService.DiscardAuthenticationToken();
            }
            Logger.Error("Error getting risk impacts message:{Message}", ex.Message);
            throw new RestComunicationException("Error getting risk impacts", ex);
        }
    }

    public async Task<List<Impact>?> GetImpactsAsync()
    {
        using var client = RestService.GetClient();
        
        var request = new RestRequest($"/Risks/Impacts");
        
        try
        {
            var response = await client.GetAsync<List<Impact>>(request);

            if (response != null) return response;
            Logger.Error("Error getting impacts ");
            return null;

        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                authenticationService.DiscardAuthenticationToken();
            }
            Logger.Error("Error getting risk impacts message:{Message}", ex.Message);
            throw new RestComunicationException("Error getting risk impacts", ex);
        }
    }

    public float GetRiskScore(int probabilityId, int impactId)
    {
        using var client = RestService.GetClient();
        
        var request = new RestRequest($"/Risks/ScoreValue-{probabilityId}-{impactId}");
        
        try
        {
            var response = client.Get<float?>(request);

            if (response != null) return response.Value;
            Logger.Error("Error getting score value ");
            return 0;

        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                authenticationService.DiscardAuthenticationToken();
            }
            Logger.Error("Error getting risk score value message:{Message}", ex.Message);
            throw new RestComunicationException("Error getting risk score value", ex);
        }
    }

    public async Task<List<RiskCatalog>> GetRiskTypesAsync()
    {
        return await GetRiskTypesAsync("", true);
    }

    public async Task<List<RiskCatalog>> GetRiskTypesAsync(string ids, bool all = false)
    {
        ids = ids.TrimEnd(',');
        
        using var client = RestService.GetClient();
        
        var request = new RestRequest($"/Risks/Catalogs");

        if(!all) request.AddParameter("list", ids);
        
        try
        {
            var response = await client.GetAsync<List<RiskCatalog>>(request);

            if (response != null) return response;
            Logger.Error("Error getting risk catalogs ");
            return new List<RiskCatalog>();

        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                authenticationService.DiscardAuthenticationToken();
            }
            Logger.Error("Error getting risk catalogs message:{0}", ex.Message);
            throw new RestComunicationException("Error getting risk catalogs", ex);
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
        
        using var client = RestService.GetClient();
        
        
        var request = new RestRequest($"/Risks/Catalogs");

        if(!all) request.AddParameter("list", ids);
        
        try
        {
            var response = client.Get<List<RiskCatalog>>(request);

            if (response != null) return response;
            Logger.Error("Error getting risk catalogs ");
            return new List<RiskCatalog>();

        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                authenticationService.DiscardAuthenticationToken();
            }
            Logger.Error("Error getting risk catalogs message:{0}", ex.Message);
            throw new RestComunicationException("Error getting risk catalogs", ex);
        }
    }

    public async Task<IncidentResponsePlan?> GetIncidentResponsePlanAsync(int riskId)
    {
        using var client = RestService.GetReliableClient();
        var request = new RestRequest($"/Risks/{riskId}/IncidentResponsePlan");
        
        try
        {
            var response = await client.GetAsync<IncidentResponsePlan>(request);
            return response;
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                authenticationService.DiscardAuthenticationToken();
            }
            Logger.Error("Error getting incident response plan message:{0}", ex.Message);
            throw new RestComunicationException("Error getting risk incident response plan ", ex);
        }
    }

    public async Task AssociateRiskToIncidentResponsePlanAsync(int riskId, int planId)
    {
        using var client = RestService.GetReliableClient();
        var request = new RestRequest($"/Risks/{riskId}/IncidentResponsePlan/{planId}");
        
        try
        {
            var response = await client.PatchAsync(request);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                Logger.Warning("Risk of plan not found");
                throw new DataNotFoundException("---","---");
            }
            
            if (response.StatusCode != HttpStatusCode.OK)
            {
                Logger.Error("Error associating an irp to a risk message");
                throw new Exception("Unexpected error");
            }
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                authenticationService.DiscardAuthenticationToken();
            }
            Logger.Error("Error associating an irp to a risk message:{0}", ex.Message);
            throw new RestComunicationException("Error associating an irp to a ris ", ex);
        }
    }
}