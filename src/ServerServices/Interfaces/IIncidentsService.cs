using DAL.Entities;
using Model.DTO;

namespace ServerServices.Interfaces;

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
    /// <param name="year">The year of the sequence. If left for the default the current year is used</param>
    /// <returns></returns>
    public Task<int> GetNextSequenceAsync(int year = -1);
    
    /// <summary>
    /// Get an incident by its id
    /// </summary>
    /// <returns></returns>
    public Task<Incident> GetByIdAsync(int id);
    
    /// <summary>
    /// Get all attachments for an incident
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public Task<List<FileListing>> GetAttachmentsByIdAsync(int id);
    
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
    
    /// <summary>
    /// Create a new incident
    /// </summary>
    /// <param name="incident"></param>
    /// <param name="user"></param>
    /// <returns></returns>
    public Task<Incident> CreateAsync(Incident incident, User user);
    
    /// <summary>
    /// Update an existing incident
    /// </summary>
    /// <param name="incident"></param>
    /// <param name="user"></param>
    /// <returns></returns>
    public Task<Incident> UpdateAsync(Incident incident, User user);

    
    /// <summary>
    /// Delete an incident
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public Task DeleteByIdAsync(int id);
}