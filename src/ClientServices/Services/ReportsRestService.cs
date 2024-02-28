using System.Collections.ObjectModel;
using ClientServices.Interfaces;
using DAL.Entities;
using DAL.EntitiesDto;
using Model.Exceptions;
using RestSharp;

namespace ClientServices.Services;

public class ReportsRestService(IRestService restService) : RestServiceBase(restService), IReportsService
{
    public async Task<ObservableCollection<Report>> GetReportsAsync()
    {
        var client = RestService.GetClient();
        
        var request = new RestRequest($"/Reports");

        try
        {
            var response = await client.GetAsync<List<Report>>(request);

            if (response == null)
            {
                Logger.Error("Error listing reports");
                throw new RestComunicationException($"Error listing reports ");
            }

            return new ObservableCollection<Report>(response);
            
            
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error listing reports message: {Message}", ex.Message);
            throw new RestComunicationException("Error listing reports", ex);
        }
    }
    

    public async Task<Report> CreateReportAsync(ReportDto report)
    {
        var client = RestService.GetClient();
        
        var request = new RestRequest($"/Reports");

        try
        {
            request.AddJsonBody(report);
            
            var response = await client.PostAsync<Report>(request);

            if (response == null)
            {
                Logger.Error("Error creating report");
                throw new RestComunicationException($"Error creating reports ");
            }

            return response;
            
            
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error creating report message: {Message}", ex.Message);
            throw new RestComunicationException("Error creating report", ex);
        }
    }

    public async Task DeleteReportAsync(int id)
    {
        var client = RestService.GetClient();
        
        var request = new RestRequest($"/Reports/{id}");

        try
        {
            var response = await client.DeleteAsync(request);

            if (response == null)
            {
                Logger.Error("Error deleting report");
                throw new RestComunicationException($"Error deleting report ");
            }
            
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error deleting report message: {Message}", ex.Message);
            throw new RestComunicationException("Error deleting report", ex);
        }
    }


}