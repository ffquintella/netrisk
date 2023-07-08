using DAL.Entities;

namespace ServerServices.Interfaces;

public interface IClientRegistrationService
{
    List<AddonsClientRegistration> GetAll();
    List<AddonsClientRegistration> GetRequested();

    AddonsClientRegistration? GetRegistrationById(int id);
    
    int Delete(AddonsClientRegistration addonsClientRegistration);

    int Update(AddonsClientRegistration addonsClientRegistration);
    
    int Add(AddonsClientRegistration addonsClientRegistration);
    int IsAccepted(string externalId);

    int Approve(int id);

    int DeleteById(int id);
    
    int Reject(int id);
}