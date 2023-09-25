using DAL.Entities;

namespace ServerServices.Interfaces;

public interface IClientRegistrationService
{
    List<ClientRegistration> GetAll();
    List<ClientRegistration> GetRequested();

    ClientRegistration? GetRegistrationById(int id);
    
    int Delete(ClientRegistration addonsClientRegistration);

    int Update(ClientRegistration addonsClientRegistration);
    
    int Add(ClientRegistration addonsClientRegistration);
    int IsAccepted(string externalId);

    int Approve(int id);

    int DeleteById(int id);
    
    int Reject(int id);
}