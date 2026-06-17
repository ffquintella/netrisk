using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using DAL.Entities;
using DAL.EntitiesDto;
using Model.Exceptions;
using RestSharp;

namespace ClientServices.Services;

public class ReportTemplatesRestService(IRestService restService) : RestServiceBase(restService), IReportTemplatesService
{
    public async Task<List<ReportTemplate>> GetAllAsync()
    {
        var client = RestService.GetClient();
        var request = new RestRequest("/ReportTemplates");

        try
        {
            var response = await client.GetAsync<List<ReportTemplate>>(request);
            if (response == null)
            {
                Logger.Error("Error listing report templates");
                throw new RestComunicationException("Error listing report templates");
            }
            return response;
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error listing report templates message: {Message}", ex.Message);
            throw new RestComunicationException("Error listing report templates", ex);
        }
    }

    public async Task<ReportTemplate> GetByIdAsync(int id)
    {
        var client = RestService.GetClient();
        var request = new RestRequest($"/ReportTemplates/{id}");

        try
        {
            var response = await client.GetAsync<ReportTemplate>(request);
            if (response == null)
            {
                Logger.Error("Error getting report template {Id}", id);
                throw new RestComunicationException($"Error getting report template {id}");
            }
            return response;
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error getting report template {Id} message: {Message}", id, ex.Message);
            throw new RestComunicationException($"Error getting report template {id}", ex);
        }
    }

    public async Task<ReportTemplate> CreateAsync(ReportTemplateCreateDto template)
    {
        var client = RestService.GetClient();
        var request = new RestRequest("/ReportTemplates");

        try
        {
            request.AddJsonBody(template);
            var response = await client.PostAsync<ReportTemplate>(request);
            if (response == null)
            {
                Logger.Error("Error creating report template");
                throw new RestComunicationException("Error creating report template");
            }
            return response;
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error creating report template message: {Message}", ex.Message);
            throw new RestComunicationException("Error creating report template", ex);
        }
    }

    public async Task<ReportTemplate> UpdateAsync(int id, ReportTemplateUpdateDto template)
    {
        var client = RestService.GetClient();
        var request = new RestRequest($"/ReportTemplates/{id}");

        try
        {
            request.AddJsonBody(template);
            var response = await client.PutAsync<ReportTemplate>(request);
            if (response == null)
            {
                Logger.Error("Error updating report template {Id}", id);
                throw new RestComunicationException($"Error updating report template {id}");
            }
            return response;
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error updating report template {Id} message: {Message}", id, ex.Message);
            throw new RestComunicationException($"Error updating report template {id}", ex);
        }
    }

    public async Task DeleteAsync(int id)
    {
        var client = RestService.GetClient();
        var request = new RestRequest($"/ReportTemplates/{id}");

        try
        {
            var response = await client.DeleteAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                Logger.Error("Error deleting report template {Id}", id);
                throw new RestComunicationException($"Error deleting report template {id}");
            }
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error deleting report template {Id} message: {Message}", id, ex.Message);
            throw new RestComunicationException($"Error deleting report template {id}", ex);
        }
    }
}
