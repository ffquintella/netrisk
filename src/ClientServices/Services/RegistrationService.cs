using System.Net;
using ClientServices.Interfaces;
using Microsoft.Extensions.Logging;
using Model.Registration;
using Model.Rest;
using RestSharp;
using Tools.Identification;

namespace ClientServices.Services;

public class RegistrationService: IRegistrationService
{
    private ILogger<RegistrationService> _logger;
    private IMutableConfigurationService _mutableConfigurationService;
    private IRestService _restService;

    
    public RegistrationService(ILoggerFactory loggerFactory, 
        IMutableConfigurationService mutableConfigurationService,
        IRestService restService
    )
    {
        _logger = loggerFactory.CreateLogger<RegistrationService>();
        _mutableConfigurationService = mutableConfigurationService;
        _restService = restService;

    }

    public bool IsRegistered
    {
        get
        {
            var isRegistredVal = _mutableConfigurationService.GetConfigurationValue("IsRegistered");
            return isRegistredVal == "true";
        }
    }

    public bool IsAccepted
    {
        get
        {
            var isRegistredVal = _mutableConfigurationService.GetConfigurationValue("IsAccepted");

            return isRegistredVal == "true";
        }
    }

    public bool CheckAcceptance(string Id, bool force = false)
    {
        if (!IsRegistered) return false;
        
        if (!force)
        {
            if (IsAccepted) return true;
        }
        var client = _restService.GetClient();

        client.AddDefaultHeader("clientId", Id);
        
        var request = new RestRequest("/Registration/IsAccepted");
        try
        {
            var response = client.Get(request);
            
            if (response.IsSuccessful && response.StatusCode == HttpStatusCode.OK)
            {
                
                _mutableConfigurationService.SetConfigurationValue("IsAccepted", "true");
                NotifyRegistrationSucceeded();
                return true;
            }
            else
            {
                return false;
            }
        }
        catch (Exception ex)
        {
            //var logger = Splat.Locator.Current.GetService<ILogger>();
            
            _logger.LogCritical($"Unhandled application error: {ex}");
        }


        return false;

    }

    public RegistrationSolicitationResult Register(string Id, bool force = false)
    {
        string hashCode = String.Format("{0:X}", Id.GetHashCode());
        if (!force)
        {
            if (IsRegistered)
                return new RegistrationSolicitationResult
                {
                    RequestID = hashCode,
                    Result = RequestResult.AlreadyExists
                };
        }

        var client = _restService.GetClient();

        var reqData = new RegistrationRequest
        {
            Id = Id,
            Hostname = ComputerInfo.GetComputerName(),
            LoggedAccount = ComputerInfo.GetLoggedUser()
            
        };
        
        RegistrationSolicitationResult? result = null;
        var request = new RestRequest("Registration").AddJsonBody(reqData);
        try
        {
            var response = client.Post(request);
            
            if (response.IsSuccessful && response.StatusCode == HttpStatusCode.OK)
            {
                result = new RegistrationSolicitationResult
                {
                    Result = RequestResult.Success,
                    RequestID = response.Content
                };
                
                _mutableConfigurationService.SetConfigurationValue("IsRegistered", "true");
                _mutableConfigurationService.SetConfigurationValue("RegistrationID", result!.RequestID!);
                
                return result;
            }

            result = new RegistrationSolicitationResult
            {
                Result = RequestResult.Failure,
                RequestID = ""
            };
            return result;
        }
        catch (Exception ex)
        {
            //var logger = Splat.Locator.Current.GetService<ILogger>();
            
            _logger.LogCritical($"Unhandled application error: {ex}");
        }

        result = new RegistrationSolicitationResult
        {
            Result = RequestResult.Failure,
            RequestID = ""
        };
        return result;


    }
    
    private void NotifyRegistrationSucceeded()
    {
        if(RegistrationSucceeded != null) RegistrationSucceeded(this, new EventArgs());
    }
    public event EventHandler? RegistrationSucceeded;
    
}