using DAL.Entities;

namespace ServerServices.Interfaces;

public interface IClientRegistrationService
{
    List<ClientRegistration> GetAll();
    List<ClientRegistration> GetRequested();

    ClientRegistration? GetRegistrationById(int id);
    
    /// <summary>
    /// Finds the approved registration by id
    /// </summary>
    /// <param name="externalId"></param>
    /// <returns></returns>
    public Task<ClientRegistration?> FindApprovedRegistrationAsync(string  externalId);
    
    int Delete(ClientRegistration addonsClientRegistration);

    int Update(ClientRegistration addonsClientRegistration);
    
    int Add(ClientRegistration addonsClientRegistration);
    int IsAccepted(string externalId);

    int Approve(int id);

    int DeleteById(int id);
    
    int Reject(int id);
}