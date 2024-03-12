using System.Collections.Generic;
using System.Net.Http;
using ClientServices.Interfaces;
using Model.Exceptions;
using Model.Globalization;
using RestSharp;

namespace ClientServices.Services;

public class ImpactsRestService: RestServiceBase, IImpactsService
{
    private List<LocalizableListItem>? _impacts;
    private IListLocalizationService ListLocalizationService { get; }
    public ImpactsRestService(IRestService restService, IListLocalizationService listLocalizationService) : base(restService)
    {
        ListLocalizationService = listLocalizationService;
    }
    
    public List<LocalizableListItem> GetAll()
    {
        if(_impacts != null) return _impacts;
        
        using var client = RestService.GetClient();
        
        var request = new RestRequest($"/Impacts");
        try
        {
            var response = client.Get<List<LocalizableListItem>>(request);

            if (response == null)
            {
                Logger.Error("Error listing impacts");
                throw new InvalidHttpRequestException("Error listing impacts", "/Impacts", "GET");
            }
            
            _impacts = ListLocalizationService.LocalizeList(response);
            return _impacts;
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error listing impacts message:{Message}", ex.Message);
            throw new RestComunicationException("Error listing impacts", ex);
        }
    }

    public async Task<List<LocalizableListItem>> GetAllAsync()
    {
        if(_impacts != null) return _impacts;
        
        using var client = RestService.GetClient();
        
        var request = new RestRequest($"/Impacts");
        try
        {
            var response = await client.GetAsync<List<LocalizableListItem>>(request);

            if (response == null)
            {
                Logger.Error("Error listing impacts");
                throw new InvalidHttpRequestException("Error listing impacts", "/Impacts", "GET");
            }
            
            _impacts = ListLocalizationService.LocalizeList(response);
            return _impacts;
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error listing impacts message:{Message}", ex.Message);
            throw new RestComunicationException("Error listing impacts", ex);
        } 
    }


}