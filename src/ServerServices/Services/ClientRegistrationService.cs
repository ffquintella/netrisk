using DAL;
using DAL.Entities;
using Model.Exceptions;
using Serilog;
using ServerServices.Interfaces;

namespace ServerServices.Services;

public class ClientRegistrationService: IClientRegistrationService
{

    private DALManager _dalManager;
    private ILogger _logger;
    public ClientRegistrationService(ILogger logger, DALManager dalManager)
    {
        _logger = logger;
        _dalManager = dalManager;
    }
    
    public List<AddonsClientRegistration> GetAll()
    {
      var result = new List<AddonsClientRegistration>();
      
      var context = _dalManager.GetContext();
      var registrations = context.AddonsClientRegistrations.ToList();
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
        var context = _dalManager.GetContext();
        try
        {
            var client = context.AddonsClientRegistrations.Find(id);
            if (client == null)
            {
                _logger.Warning("Not found client id: {0}", id);
                return 1;
            }

            if (client.Status == "approved")
            {
                _logger.Warning("Trying to approve already approved client id:{0}", id);
                return 2;
            }

            _logger.Information("Approving registration id: {0} name: {1}", id, client.Name);
            client.Status = "approved";
            context.AddonsClientRegistrations.Update(client);
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
        var context = _dalManager.GetContext();
        try
        {
            var client = context.AddonsClientRegistrations.Find(id);
            if (client == null)
            {
                _logger.Warning("Not found client id: {0}", id);
                return 1;
            }

            _logger.Information("Deleting registration id: {0} name: {1}", id, client.Name);
            context.AddonsClientRegistrations.Remove(client);
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
        var context = _dalManager.GetContext();
        try
        {
            var client = context.AddonsClientRegistrations.Find(id);
            if (client == null)
            {
                _logger.Warning("Not found user id: {0}", id);
                return 1;
            }

            if (client.Status == "rejected")
            {
                _logger.Warning("Trying to reject already rejected client id:{0}", id);
                return 2;
            }
            _logger.Information("Rejecting registration id: {0} name: {1}", id, client.Name);
            client.Status = "rejected";
            context.AddonsClientRegistrations.Update(client);
            context.SaveChanges();

            return 0;
        }
        catch (Exception ex)
        {
            throw new DatabaseException(ex.Message);
            //return -1;
        }
    }
    
    public List<AddonsClientRegistration> GetRequested()
    {
        var result = new List<AddonsClientRegistration>();
      
        var context = _dalManager.GetContext();
        var registrations = context.AddonsClientRegistrations.Where(ad => ad.Status == "requested").ToList();
        if (registrations != null)
        {
            _logger.Debug("Loading all registrations rg.02");
            result = registrations;
        }
        _logger.Warning("No registrations found");
        return result;
    }
    /// <summary>
    /// Updates a client registration data
    /// </summary>
    /// <param name="addonsClientRegistration"></param>
    /// <returns>0 if success; -1 if failure</returns>
    public int Update(AddonsClientRegistration addonsClientRegistration)
    {
        var result = 0;
        try
        {
            var context = _dalManager.GetContext();
            _logger.Information("Updating registration id: {0} name: {1}", addonsClientRegistration.Id, addonsClientRegistration.Name);
            context.AddonsClientRegistrations.Update(addonsClientRegistration);
            context.SaveChanges();
        }catch (Exception ex)
        {
            _logger.Error("Error updating a registration ex: {0}", ex.Message);
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
            _logger.Information("Client not found externalID: {0}", externalId);
            return -1;
        }
        
        return clientRegistration.Status != "approved" ? 0 : 1;
    }
    
    private AddonsClientRegistration? GetByExternalId(string externalId)
    {
        var context = _dalManager.GetContext();
        var request = context.AddonsClientRegistrations.Where(cr => cr.ExternalId == externalId).FirstOrDefault();
        return request;
    }

    public AddonsClientRegistration? GetRegistrationById(int id)
    {
        var context = _dalManager.GetContext();
        var request = context.AddonsClientRegistrations.Where(cr => cr.Id == id).FirstOrDefault();
        return request;
    }

    /// <summary>
    /// Deletes a client registration from db
    /// </summary>
    /// <param name="addonsClientRegistration"></param>
    /// <returns>0 if success; -1 if failure</returns>
    public int Delete(AddonsClientRegistration addonsClientRegistration)
    {
        var result = 0;
        try
        {
            _logger.Information("Deleting registration id: {0} name: {1}", addonsClientRegistration.Id, addonsClientRegistration.Name);
            var context = _dalManager.GetContext();
            context.AddonsClientRegistrations.Remove(addonsClientRegistration);
            context.SaveChanges();
        }catch (Exception ex)
        {
            _logger.Error("Error deleting a registration ex: {0}", ex.Message);
            result = -1;
        }
        return result;
    }

    /// <summary>
    /// Adds a new Client Registration
    /// </summary>
    /// <param name="addonsClientRegistration"></param>
    /// <returns>0 if success; 1 if client already exists; -1 if failure</returns>
    public int Add(AddonsClientRegistration addonsClientRegistration)
    {
        var result = 0;
        try
        {
            var context = _dalManager.GetContext();

            var nfound = context.AddonsClientRegistrations
                .Count(cr => cr.ExternalId == addonsClientRegistration.ExternalId);

            if (nfound > 0)
            {
                _logger.Warning("Trying to add an already existing client");
                return 1;
            }
            
            context.AddonsClientRegistrations.Add(addonsClientRegistration);
            context.SaveChanges();
            _logger.Information("Adding a client Registration request with name {0}", addonsClientRegistration.Name);
            
        }
        catch (Exception ex)
        {
            _logger.Error("Error adding new client registration ex: {0}", ex.Message);
            result = -1;
        }
        return result;
    }
}