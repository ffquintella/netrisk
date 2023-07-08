using Model;

namespace ClientServices.Interfaces;

public interface IClientService
{
    List<Client> GetAll();
    
    /// <summary>
    /// Approves the client with Id
    /// </summary>
    /// <param name="id"></param>
    /// <returns>-1 if error; 0 if ok;</returns>
    int Approve(int id);
    
    /// <summary>
    /// Rejects the client with Id
    /// </summary>
    /// <param name="id"></param>
    /// <returns>-1 if error; 0 if ok;</returns>
    int Reject(int id);
    
    /// <summary>
    /// Deletes the client with Id
    /// </summary>
    /// <param name="id"></param>
    /// <returns>-1 if error; 0 if ok;</returns>
    int Delete(int id);
}