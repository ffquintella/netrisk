using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using DAL.Entities;
using DAL.EntitiesDto;
using Model.Exceptions;
using RestSharp;

namespace ClientServices.Services
{
    public class ReportSchedulesRestService : RestServiceBase, IReportSchedulesService
    {
        public ReportSchedulesRestService(IRestService restService) : base(restService)
        {
        }

        public async Task<List<ReportSchedule>> GetAllAsync()
        {
            var client = RestService.GetClient();
            var request = new RestRequest("/ReportSchedules");

            try
            {
                var response = await client.GetAsync<List<ReportSchedule>>(request);
                if (response == null)
                {
                    Logger.Error("Error listing report schedules");
                    throw new RestComunicationException("Error listing report schedules");
                }
                return response;
            }
            catch (HttpRequestException ex)
            {
                Logger.Error("Error listing report schedules message: {Message}", ex.Message);
                throw new RestComunicationException("Error listing report schedules", ex);
            }
        }

        public async Task<ReportSchedule> GetByIdAsync(int id)
        {
            var client = RestService.GetClient();
            var request = new RestRequest($"/ReportSchedules/{id}");

            try
            {
                var response = await client.GetAsync<ReportSchedule>(request);
                if (response == null)
                {
                    Logger.Error("Error getting report schedule {Id}", id);
                    throw new RestComunicationException($"Error getting report schedule {id}");
                }
                return response;
            }
            catch (HttpRequestException ex)
            {
                Logger.Error("Error getting report schedule {Id} message: {Message}", id, ex.Message);
                throw new RestComunicationException($"Error getting report schedule {id}", ex);
            }
        }

        public async Task<ReportSchedule> CreateAsync(ReportScheduleCreateDto schedule)
        {
            var client = RestService.GetClient();
            var request = new RestRequest("/ReportSchedules");

            try
            {
                request.AddJsonBody(schedule);
                var response = await client.PostAsync<ReportSchedule>(request);
                if (response == null)
                {
                    Logger.Error("Error creating report schedule");
                    throw new RestComunicationException("Error creating report schedule");
                }
                return response;
            }
            catch (HttpRequestException ex)
            {
                Logger.Error("Error creating report schedule message: {Message}", ex.Message);
                throw new RestComunicationException("Error creating report schedule", ex);
            }
        }

        public async Task<ReportSchedule> UpdateAsync(int id, ReportScheduleUpdateDto schedule)
        {
            var client = RestService.GetClient();
            var request = new RestRequest($"/ReportSchedules/{id}");

            try
            {
                request.AddJsonBody(schedule);
                var response = await client.PutAsync<ReportSchedule>(request);
                if (response == null)
                {
                    Logger.Error("Error updating report schedule {Id}", id);
                    throw new RestComunicationException($"Error updating report schedule {id}");
                }
                return response;
            }
            catch (HttpRequestException ex)
            {
                Logger.Error("Error updating report schedule {Id} message: {Message}", id, ex.Message);
                throw new RestComunicationException($"Error updating report schedule {id}", ex);
            }
        }

        public async Task DeleteAsync(int id)
        {
            var client = RestService.GetClient();
            var request = new RestRequest($"/ReportSchedules/{id}");

            try
            {
                var response = await client.DeleteAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    Logger.Error("Error deleting report schedule {Id}", id);
                    throw new RestComunicationException($"Error deleting report schedule {id}");
                }
            }
            catch (HttpRequestException ex)
            {
                Logger.Error("Error deleting report schedule {Id} message: {Message}", id, ex.Message);
                throw new RestComunicationException($"Error deleting report schedule {id}", ex);
            }
        }

        public async Task TriggerTestAsync(int id)
        {
            var client = RestService.GetClient();
            var request = new RestRequest($"/ReportSchedules/{id}/test");

            try
            {
                var response = await client.PostAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    Logger.Error("Error triggering test for report schedule {Id}", id);
                    throw new RestComunicationException($"Error triggering test for report schedule {id}");
                }
            }
            catch (HttpRequestException ex)
            {
                Logger.Error("Error triggering test for report schedule {Id} message: {Message}", id, ex.Message);
                throw new RestComunicationException($"Error triggering test for report schedule {id}", ex);
            }
        }
    }
}
