﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using DAL.Entities;
using Model.DTO;
using Model.Exceptions;
using ReliableRestClient.Exceptions;
using RestSharp;
using Tools.Helpers;
using DateTime = System.DateTime;

namespace ClientServices.Services;

public class VulnerabilitiesRestService: RestServiceBase, IVulnerabilitiesService
{
    
    private IMemoryCacheService MemoryCacheService { get; } = GetService<IMemoryCacheService>();
    
    public VulnerabilitiesRestService(IRestService restService) : base(restService)
    {
    }

    public List<Vulnerability> GetAll()
    {
        using var client = RestService.GetClient();
        
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

    public List<Vulnerability> GetFiltered(int pageSize, int pageNumber, string filter, out int totalRecords, out bool validFilter)
    {
        using var client = RestService.GetClient();
        string cultureCode = CultureInfo.CurrentCulture.Name;
        
        var request = new RestRequest("/Vulnerabilities/Filtered");
        try
        {
            request.AddParameter("pageSize", pageSize);
            request.AddParameter("page", pageNumber);
            request.AddParameter("culture", cultureCode);
            
            if (filter.Length > 0) request.AddParameter("filters", filter);


            var response = client.Get(request);

            if (response.StatusCode == HttpStatusCode.Conflict || response.StatusCode == HttpStatusCode.BadRequest)
            {
                validFilter = false;
            }

            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                throw new BadFilterException(filter, response.Content!);
            }


            if (response.StatusCode != HttpStatusCode.OK)
            {
                Logger.Error("Error listing vulnerabilities");
                throw new InvalidHttpRequestException("Error listing vulnerabilities", "/Vulnerabilities", "GET");
            }

            var vulnerabilities = JsonSerializer.Deserialize<List<Vulnerability>>(response.Content!,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

            var recordHeader = response.Headers!.FirstOrDefault(x => x.Name == "X-Total-Count");

            totalRecords = recordHeader!.Value is not null ? int.Parse(recordHeader.Value.ToString()!) : 0;
            validFilter = true;

            if (vulnerabilities == null) throw new Exception("Null vulnerabilities list");

            return vulnerabilities;

        }
        catch (RestException ex)
        {
            Logger.Error("Error listing vulnerabilities message:{Message}", ex.Message);
            throw new RestComunicationException("Error listing vulnerabilities", ex);
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.BadRequest || ex.StatusCode == HttpStatusCode.Conflict)
            {
                validFilter = false;
                throw new BadFilterException(filter, ex.Message);
            }
            
            validFilter = true;
            
            Logger.Error("Error listing vulnerabilities message:{Message}", ex.Message);
            throw new RestComunicationException("Error listing vulnerabilities", ex);
        }
    }

    public async Task<Tuple<List<Vulnerability>,int,bool>> GetFilteredAsync(int pageSize, int pageNumber, string filter, bool includeFixRequests = false)
    {
        using var client = RestService.GetClient();
        var validFilter = false;
        var totalRecords = 0;
        
        var request = new RestRequest("/Vulnerabilities/Filtered");
        try
        {
            request.AddParameter("pageSize", pageSize);
            request.AddParameter("page", pageNumber);
            request.AddParameter("includeFixRequests", includeFixRequests);
            if (filter.Length > 0) request.AddParameter("filters", filter);


            var response = await client.GetAsync(request);

            if (response.StatusCode == HttpStatusCode.Conflict || response.StatusCode == HttpStatusCode.BadRequest)
            {
                validFilter = false;
            }

            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                throw new BadFilterException(filter, response.Content!);
            }


            if (response.StatusCode != HttpStatusCode.OK)
            {
                Logger.Error("Error listing vulnerabilities");
                throw new InvalidHttpRequestException("Error listing vulnerabilities", "/Vulnerabilities", "GET");
            }

            var vulnerabilities = JsonSerializer.Deserialize<List<Vulnerability>>(response.Content!,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

            var recordHeader = response.Headers!.FirstOrDefault(x => x.Name == "X-Total-Count");

            totalRecords = recordHeader!.Value is not null ? int.Parse(recordHeader.Value.ToString()!) : 0;
            validFilter = true;

            if (vulnerabilities == null) throw new Exception("Null vulnerabilities list");

            return new Tuple<List<Vulnerability>, int, bool>(vulnerabilities, totalRecords, validFilter) ;

        }
        catch (RestException ex)
        {
            Logger.Error("Error listing vulnerabilities message:{Message}", ex.Message);
            throw new RestComunicationException("Error listing vulnerabilities", ex);
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.BadRequest || ex.StatusCode == HttpStatusCode.Conflict)
            {
                validFilter = false;
                throw new BadFilterException(filter, ex.Message);
            }
            
            validFilter = true;
            
            Logger.Error("Error listing vulnerabilities message:{Message}", ex.Message);
            throw new RestComunicationException("Error listing vulnerabilities", ex);
        }
    }

    public Vulnerability GetOne(int id)
    {
        using var client = RestService.GetClient();
        
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

    public async Task<Vulnerability> GetOneAsync(int id)
    {
        using var client = RestService.GetClient();
        
        var request = new RestRequest($"/Vulnerabilities/{id}");

        request.AddParameter("includeDetails", "true");
        
        try
        {
            var response = await client.GetAsync<Vulnerability>(request);

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
        using var client = RestService.GetClient();
        
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

    public async Task<List<RiskScoring>> GetRisksScoresAsync(int vulnerabilityId)
    {
        using var client = RestService.GetClient();
        
        var request = new RestRequest($"/Vulnerabilities/{vulnerabilityId}/RisksScores");
        
        try
        {
            var response = await client.GetAsync<List<RiskScoring>>(request);

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

    public async Task<Vulnerability> CreateAsync(Vulnerability vulnerability)
    {
        using var client = RestService.GetReliableClient();
        
        var request = new RestRequest($"/Vulnerabilities");

        request.AddJsonBody(vulnerability);
        
        try
        {
            var response = await client.PostAsync<Vulnerability>(request);

            if (response == null)
            {
                Logger.Error("Error creating vulnerability ");
                throw new InvalidHttpRequestException("Error creating vulnerability", $"/Vulnerabilities", "POST");
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error creating vulnerability  message:{Message}", ex.Message);
            throw new RestComunicationException("Error creating vulnerability ", ex);
        } 
    }

    public async Task<Tuple<bool,Vulnerability?>> FindAsync(string hash)
    {
        using var client = RestService.GetReliableClient();
        
        var request = new RestRequest($"/Vulnerabilities/Find");

        request.AddParameter("hash", hash);
        
        try
        {
            var response = await client.GetAsync(request);

            
            if(response.StatusCode == HttpStatusCode.NotFound)
                return new Tuple<bool, Vulnerability?>(false,null);
            
            if (response.StatusCode != HttpStatusCode.OK)
            {
                Logger.Error("Error searching vulnerability ");
                throw new InvalidHttpRequestException("Error searching vulnerability", $"/Vulnerabilities/Find", "GET");
            }
            
            var vulnerability = JsonSerializer.Deserialize<Vulnerability>(response.Content!, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            
            return new Tuple<bool, Vulnerability?>(true,vulnerability);
            
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error creating vulnerability  message:{Message}", ex.Message);
            throw new RestComunicationException("Error creating vulnerability ", ex);
        } 
    }

    public async void Update(Vulnerability vulnerability)
    {
        List<int> riskIds = new List<int>();
        if (vulnerability.Risks is not null)
        {
            riskIds.AddRange(vulnerability.Risks.Select(risk => risk.Id));
            vulnerability.Risks = new List<Risk>();
        }

        vulnerability.FixRequests.Clear();
        
        using var client = RestService.GetReliableClient();
        
        var request = new RestRequest($"/Vulnerabilities/{vulnerability.Id}");

        request.AddJsonBody(vulnerability);
        
        try
        {
            var response = await client.PutAsync(request);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                Logger.Error("Error updating vulnerability ");
                throw new InvalidHttpRequestException("Error updating vulnerability", $"/Vulnerabilities/{vulnerability.Id}", "PUT");
            }
            
            AssociateRisks(vulnerability.Id, riskIds);
            
            
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error updating vulnerability  message:{Message}", ex.Message);
            throw new RestComunicationException("Error updating vulnerability ", ex);
        } 
    }

    public void Delete(Vulnerability vulnerability)
    {
        using var client = RestService.GetClient();
        
        var request = new RestRequest($"/Vulnerabilities/{vulnerability.Id}");
       
        try
        {
            
            var response = client.Delete(request);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                Logger.Error("Error deleting vulnerability ");
                throw new InvalidHttpRequestException("Error deleting vulnerability", $"/Vulnerabilities/{vulnerability.Id}", "DELETE");
            }
            
            
            
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error updating deleting  message:{Message}", ex.Message);
            throw new RestComunicationException("Error deleting vulnerability ", ex);
        } 
    }

    public async void AssociateRisks(int vulnerabilityId, List<int> riskIds)
    {
        using var client = RestService.GetReliableClient();
        
        var request = new RestRequest($"/Vulnerabilities/{vulnerabilityId}/RisksAssociate");

        request.AddJsonBody(riskIds);
        
        try
        {
            var response = await client.PostAsync(request);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                Logger.Error("Error associating vulnerability to risks");
                throw new InvalidHttpRequestException("Error associating vulnerability to risks", $"/Vulnerabilities/{vulnerabilityId}/RisksAssociate", "POST");
            }
            
            
            
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error associating vulnerability to risks message:{Message}", ex.Message);
            throw new RestComunicationException("Error associating vulnerability to risks", ex);
        } 
    }

    public void UpdateStatus(int id, ushort status)
    {
        AsyncHelper.RunSync(  () => UpdateStatusAsync(id, status));
    }

    public async Task UpdateStatusAsync(int id, ushort status)
    {
        using var client = RestService.GetClient();
        
        var request = new RestRequest($"/Vulnerabilities/{id}/Status");
        request.AddJsonBody(status.ToString());
       
        try
        {
            var response = await client.PutAsync(request);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                Logger.Error("Error updating vulnerability status ");
                throw new InvalidHttpRequestException("Error updating vulnerability status", $"/Vulnerabilities/{id}", "PUT");
            }
            
            
            
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error updating vulnerability status  message:{Message}", ex.Message);
            throw new RestComunicationException("Error updating vulnerability ", ex);
        } 
    }

    public async void UpdateCommentsAsync(int id, string comments)
    {
        using var client = RestService.GetClient();
        
        var request = new RestRequest($"/Vulnerabilities/{id}/Comments");

        var dto = new CommentDto()
        {
            Text = comments
        };
        
        request.AddJsonBody(dto);

       
        try
        {
            var response = await client.PutAsync(request);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                Logger.Error("Error updating vulnerability comments ");
                throw new InvalidHttpRequestException("Error updating vulnerability comments", $"/Vulnerabilities/{id}", "PUT");
            }
            
            
            
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error updating vulnerability comments message:{Message}", ex.Message);
            throw new RestComunicationException("Error updating vulnerability ", ex);
        } 
    }

    public async Task<NrAction> AddActionAsync(int id, int userId, NrAction action)
    {
        using var client = RestService.GetReliableClient();
        
        var request = new RestRequest($"/Vulnerabilities/{id}/Actions");
        request.AddJsonBody(action);
       
        try
        {
            var response = await client.PostAsync(request);

            if (response.StatusCode != HttpStatusCode.Created)
            {
                Logger.Error("Error updating vulnerability status ");
                throw new InvalidHttpRequestException("Error updating vulnerability status", $"/Vulnerabilities/{id}", "PUT");
            }

            var resultingAction = JsonSerializer.Deserialize<NrAction>(response.Content!, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            return resultingAction!;

        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error updating vulnerability status  message:{Message}", ex.Message);
            throw new RestComunicationException("Error updating vulnerability ", ex);
        }
    }

    public async Task ImportNessusAsync(string id)
    {
        using var client = RestService.GetReliableClient();
        
        var request = new RestRequest($"/Vulnerabilities/import/nessus/{id}");
 
        try
        {
            var response = await client.PostAsync(request);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                Logger.Error("Error stating nessus import process ");
                throw new InvalidHttpRequestException("Error stating nessus import process", $"/Vulnerabilities/import/nessus/{id}", "POST");
            }

 
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error stating nessus import process  message:{Message}", ex.Message);
            throw new RestComunicationException("Error stating nessus import process ", ex);
        }
    }

    public async Task<DateTime> GetLastScanDateAsync()
    {
        if(MemoryCacheService.HasCache<DateTime>("lastScanDate"))
            return MemoryCacheService.Get<DateTime>("lastScanDate");
        
        using var client = RestService.GetReliableClient();
        
        var request = new RestRequest("/Vulnerabilities/LastScanDate");
        
        try
        {
            var response = await client.GetAsync(request);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                Logger.Error("Error getting last scan date ");
                throw new InvalidHttpRequestException("Error getting last scan date", $"/Vulnerabilities/LastScanDate", "GET");
            }

            var date = JsonSerializer.Deserialize<DateTime>(response.Content!, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            
            MemoryCacheService.Set("lastScanDate",date,TimeSpan.FromMinutes(15));
            
            return date;

        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error getting last scan date  message:{Message}", ex.Message);
            throw new RestComunicationException("Error getting last scan date ", ex);
        }
        
    }
}