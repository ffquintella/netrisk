using DAL.Entities;

namespace ServerServices.Interfaces;

public interface ITeamsService
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
    
    
    /// <summary>
    /// Get the user ids associated to a team
    /// </summary>
    /// <returns></returns>
    public List<int> GetUsersIds(int teamId);


    /// <summary>
    /// Updates the team users
    /// </summary>
    /// <param name="id"></param>
    /// <param name="userIds"></param>
    public void UpdateTeamUsers(int id, List<int> userIds);
    
    
    /// <summary>
    /// Creates a new Team
    /// </summary>
    /// <param name="team"></param>
    /// <returns></returns>
    public Team Create(Team team);
    
    /// <summary>
    /// Deletes a team
    /// </summary>
    /// <param name="teamId"></param>
    public void Delete(int teamId);

    /// <summary>
    ///  Gets a team by id
    /// </summary>
    /// <param name="teamId"></param>
    /// <returns></returns>
    public Team GetById(int teamId);
}