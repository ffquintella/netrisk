using DAL.Entities;

namespace ClientServices.Interfaces;

public interface ITeamsService
{
    /// <summary>
    /// List all teams
    /// </summary>
    /// <returns></returns>
    public List<Team> GetAll();
    
    
    /// <summary>
    /// Gets all teams by mitigation id
    /// </summary>
    /// <param name="id">Mitigation id</param>
    /// <returns></returns>
    public List<Team> GetByMitigationId(int id);
}