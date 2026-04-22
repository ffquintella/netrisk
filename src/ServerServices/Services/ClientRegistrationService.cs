using DAL;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Model.Exceptions;
using Serilog;
using ServerServices.Interfaces;

namespace ServerServices.Services;

public class ClientRegistrationService: IClientRegistrationService
{

    private IDalService _dalService;
    private ILogger _logger;
    public ClientRegistrationService(ILogger logger, IDalService dalService)
    {
        _logger = logger;
        _dalService = dalService;
    }
    
    public List<ClientRegistration> GetAll()
    {
      var result = new List<ClientRegistration>();
      
      using var context = _dalService.GetContext();
      var registrations = context.ClientRegistrations.ToList();
      if (registrations != null) result = registrations;
      
      return result;
    }

    /// <summary>
    /// Approves a request with specific ID
    /// </summary>
    /// <param name="id"></param>
    /// <returns>-1 if error; 0 if ok; 1 if not found; 2 if already approved;</returns>
    public int Approve(int id)
    {
        using var context = _dalService.GetContext();
        try
        {
            var client = context.ClientRegistrations.Find(id);
            if (client == null)
            {
                _logger.Warning("Not found client id: {Id}", id);
                return 1;
            }

            if (client.Status == "approved")
            {
                _logger.Warning("Trying to approve already approved client id:{Id}", id);
                return 2;
            }

            _logger.Information("Approving registration id: {Id} name: {Name}", id, client.Name);
            client.Status = "approved";
            context.ClientRegistrations.Update(client);
            context.SaveChanges();

            return 0;
        }
        catch (Exception ex)
        {
            throw new DatabaseException(ex.Message);
            //return -1;
        }
    }
    
    public int DeleteById(int id)
    {
        using var context = _dalService.GetContext();
        try
        {
            var client = context.ClientRegistrations.Find(id);
            if (client == null)
            {
                _logger.Warning("Not found client id: {Id}", id);
                return 1;
            }

            _logger.Information("Deleting registration id: {Id} name: {Name}", id, client.Name);
            context.ClientRegistrations.Remove(client);
            context.SaveChanges();

            return 0;
        }
        catch (Exception ex)
        {
            throw new DatabaseException(ex.Message);
            //return -1;
        }
    }

    /// <summary>
    /// Rejects a request with specific ID
    /// </summary>
    /// <param name="id"></param>
    /// <returns>-1 if error; 0 if ok; 1 if not found; 2 if already approved;</returns>
    public int Reject(int id)
    {
        using var context = _dalService.GetContext();
        try
        {
            var client = context.ClientRegistrations.Find(id);
            if (client == null)
            {
                _logger.Warning("Not found user id: {Id}", id);
                return 1;
            }

            if (client.Status == "rejected")
            {
                _logger.Warning("Trying to reject already rejected client id: {Id}", id);
                return 2;
            }
            _logger.Information("Rejecting registration id: {Id} name: {Name}", id, client.Name);
            client.Status = "rejected";
            context.ClientRegistrations.Update(client);
            context.SaveChanges();

            return 0;
        }
        catch (Exception ex)
        {
            throw new DatabaseException(ex.Message);
            //return -1;
        }
    }
    
    public List<ClientRegistration> GetRequested()
    {
        var result = new List<ClientRegistration>();
      
        using var context = _dalService.GetContext();
        var registrations = context.ClientRegistrations.Where(ad => ad.Status == "requested").ToList();
        if (registrations is { Count: > 0 })
        {
            _logger.Debug("Loading all registrations rg.02");
            result = registrations;
            return result;
        }
        _logger.Warning("No registrations found");
        return result;
    }
    /// <summary>
    /// Updates a client registration data
    /// </summary>
    /// <param name="addonsClientRegistration"></param>
    /// <returns>0 if success; -1 if failure</returns>
    public int Update(ClientRegistration addonsClientRegistration)
    {
        var result = 0;
        try
        {
            using var context = _dalService.GetContext();
            _logger.Information("Updating registration id: {Id} name: {Name}", addonsClientRegistration.Id, addonsClientRegistration.Name);
            context.ClientRegistrations.Update(addonsClientRegistration);
            context.SaveChanges();
        }catch (Exception ex)
        {
            _logger.Error("Error updating a registration ex: {Message}", ex.Message);
            result = -1;
        }
        return result;
    }

    /// <summary>
    /// Checks if a cliente is registred and authorized
    /// </summary>
    /// <param name="externalId"></param>
    /// <returns>-1 if client not found, 0 if not authorized, 1 if authorized</returns>
    public int IsAccepted(string externalId)
    { 
        var clientRegistration = GetByExternalId(externalId);
        if (clientRegistration == null)
        {
            _logger.Information("Client not found externalID: {Id}", externalId);
            return -1;
        }
        
        return clientRegistration.Status != "approved" ? 0 : 1;
    }
    
    private ClientRegistration? GetByExternalId(string externalId)
    {
        using var context = _dalService.GetContext();
        var request = context.ClientRegistrations.Where(cr => cr.ExternalId == externalId).FirstOrDefault();
        return request;
    }

    public async Task<ClientRegistration?> FindApprovedRegistrationAsync(string  externalId)
    {
        await using var context = _dalService.GetContext();
        var client = await context!.ClientRegistrations!
            .FirstOrDefaultAsync(cl => cl.ExternalId == externalId && cl.Status == "approved");
        return client;
    }
    
    public ClientRegistration? GetRegistrationById(int id)
    {
        using var context = _dalService.GetContext();
        var request = context.ClientRegistrations.Where(cr => cr.Id == id).FirstOrDefault();
        return request;
    }

    /// <summary>
    /// Deletes a client registration from db
    /// </summary>
    /// <param name="addonsClientRegistration"></param>
    /// <returns>0 if success; -1 if failure</returns>
    public int Delete(ClientRegistration addonsClientRegistration)
    {
        var result = 0;
        try
        {
            _logger.Information("Deleting registration id: {Id} name: {Name}", addonsClientRegistration.Id, addonsClientRegistration.Name);
            using var context = _dalService.GetContext();
            context.ClientRegistrations.Remove(addonsClientRegistration);
            context.SaveChanges();
        }catch (Exception ex)
        {
            _logger.Error("Error deleting a registration ex: {Message}", ex.Message);
            result = -1;
        }
        return result;
    }

    /// <summary>
    /// Adds a new Client Registration
    /// </summary>
    /// <param name="addonsClientRegistration"></param>
    /// <returns>0 if success; 1 if client already exists; -1 if failure</returns>
    public int Add(ClientRegistration addonsClientRegistration)
    {
        var result = 0;
        try
        {
            var now = DateTime.Now;
            if (addonsClientRegistration.RegistrationDate <= DateTime.MinValue.AddYears(1))
                addonsClientRegistration.RegistrationDate = now;
            if (addonsClientRegistration.LastVerificationDate <= DateTime.MinValue.AddYears(1))
                addonsClientRegistration.LastVerificationDate = now;
            if (string.IsNullOrWhiteSpace(addonsClientRegistration.Status))
                addonsClientRegistration.Status = "requested";

            using var context = _dalService.GetContext();

            var nfound = context.ClientRegistrations
                .Count(cr => cr.ExternalId == addonsClientRegistration.ExternalId);

            if (nfound > 0)
            {
                _logger.Warning("Trying to add an already existing client");
                return 1;
            }
            
            context.ClientRegistrations.Add(addonsClientRegistration);
            context.SaveChanges();
            _logger.Information("Adding a client Registration request with name {Name}", addonsClientRegistration.Name);
            
        }
        catch (Exception ex)
        {
            _logger.Error("Error adding new client registration ex: {Message}", ex.Message);
            result = -1;
        }
        return result;
    }
}
