using DAL.Entities;

namespace ClientServices.Interfaces;

public interface IIncidentsService
{
    /// <summary>
    /// Get all incidents
    /// </summary>
    /// <returns></returns>
    public Task<List<Incident>> GetAllAsync();
}