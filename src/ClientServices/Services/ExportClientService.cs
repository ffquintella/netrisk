using System.Net.Http;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using Model.Exceptions;
using RestSharp;

namespace ClientServices.Services
{
    public class ExportClientService : RestServiceBase, IExportClientService
    {
        public ExportClientService(IRestService restService) : base(restService)
        {
        }

        public async Task<byte[]> ExportAsync(string entityType, ExportFormat format, string? filter = null, string? sort = null, string? reportTitle = null)
        {
            var client = RestService.GetClient();
            var request = new RestRequest($"/Export/{format.ToString().ToLower()}");
            request.AddQueryParameter("entityType", entityType);

            if (!string.IsNullOrEmpty(filter))
                request.AddQueryParameter("filters", filter);
            if (!string.IsNullOrEmpty(sort))
                request.AddQueryParameter("sorts", sort);
            if (!string.IsNullOrEmpty(reportTitle))
                request.AddQueryParameter("reportTitle", reportTitle);

            try
            {
                var response = await client.GetAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    Logger.Error("Error exporting {EntityType} as {Format}", entityType, format);
                    throw new RestComunicationException($"Error exporting {entityType} as {format}");
                }
                return response.RawBytes!;
            }
            catch (HttpRequestException ex)
            {
                Logger.Error("Error exporting {EntityType} as {Format}: {Message}", entityType, format, ex.Message);
                throw new RestComunicationException($"Error exporting {entityType} as {format}", ex);
            }
        }
    }
}
