using DAL.Entities;

namespace ServerServices.Interfaces;

public interface ITeamManagementService
{
    /// <summary>
    /// Get all teams
    /// </summary>
    /// <returns>List of teams</returns>
    public List<Team> GetAll();
    
    /// <summary>
    /// Gets the teams by mitigation id
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public List<Team> GetByMitigationId(int id);
    
    /// <summary>
    /// Associates a team to a mitigation
    /// </summary>
    /// <param name="mitigationId"></param>
    /// <param name="teamId"></param>
    public void AssociateTeamToMitigation(int mitigationId, int teamId);
}