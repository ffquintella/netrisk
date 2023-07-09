using DAL.Entities;

namespace ServerServices.Interfaces;

public interface IMitigationsService
{
    
    /// <summary>
    /// Gets one mitigation by it´s ID
    /// </summary>
    /// <param name="id">Mitigaiton ID</param>
    /// <returns>Mitigation Object</returns>
    public Mitigation GetById(int id);

    /// <summary>
    /// Gets one mitigation by it´s RiskId
    /// </summary>
    /// <param name="id">Risk Id</param>
    /// <returns>Mitigation Object</returns>
    public Mitigation GetByRiskId(int id);
    
    /// <summary>
    /// List the mitigation strategies
    /// </summary>
    /// <returns></returns>
    public List<PlanningStrategy> ListStrategies();
    
    /// <summary>
    /// List the mitigation efforts
    /// </summary>
    /// <returns></returns>
    public List<MitigationEffort> ListEfforts();
    
    /// <summary>
    /// List the mitigation costs
    /// </summary>
    /// <returns>List of mitigation costs</returns>
    public List<MitigationCost> ListCosts();
    
    /// <summary>
    /// Creates a new mitigation
    /// </summary>
    /// <param name="mitigation"></param>
    /// <returns></returns>
    public Mitigation Create(Mitigation mitigation);
    
    /// <summary>
    /// Saves an existing mitigation to the database
    /// </summary>
    /// <param name="mitigation"></param>
    public void Save(Mitigation mitigation);
    
    /// <summary>
    /// Deletes all teams associations from a mitigation
    /// </summary>
    /// <param name="mitigationId"></param>
    public void DeleteTeamsAssociations(int mitigationId);
}