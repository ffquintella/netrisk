using DAL.Entities;
using Model.DTO;

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
    
    /// <summary>
    /// Get an incident by its id
    /// </summary>
    /// <param name="incidentId"></param>
    /// <returns></returns>
    public Task<List<FileListing>> GetAttachmentsAsync(int incidentId);
    
    /// <summary>
    /// Get all incident response plans for an incident
    /// </summary>
    /// <param name="id">The incident Id</param>
    /// <returns></returns>
    public Task<List<int>> GetIncidentResponsPlanIdsByIdAsync(int id);
    
    /// <summary>
    /// Associate incident response plans to an incident
    /// </summary>
    /// <param name="id">Incident Id</param>
    /// <param name="ids">IncidentResponsePlan Ids</param>
    /// <returns></returns>
    public Task AssociateIncidentResponsPlanIdsByIdAsync(int id, List<int> ids);
    
}