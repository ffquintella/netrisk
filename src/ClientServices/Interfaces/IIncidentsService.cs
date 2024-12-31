using DAL.Entities;

namespace ClientServices.Interfaces;

public interface IIncidentsService
{
    /// <summary>
    /// Get all incidents
    /// </summary>
    /// <returns></returns>
    public Task<List<Incident>> GetAllAsync();
    
    /// <summary>
    /// Gets the next sequence number for an incident
    /// </summary>
    /// <param name="year"></param>
    /// <returns></returns>
    public Task<int> GetNextSequenceAsync(int year = -1);
    
    
    /// <summary>
    /// Create a new incident
    /// </summary>
    /// <param name="incident"></param>
    /// <returns></returns>
    public Task<Incident> CreateAsync(Incident incident);
    
    /// <summary>
    /// Update an incident
    /// </summary>
    /// <param name="incident"></param>
    /// <returns></returns>
    public Task<Incident> UpdateAsync(Incident incident);
}