using System.Collections.Generic;
using DAL.Entities;

namespace ClientServices.Interfaces;

public interface ITeamsService
{
    /// <summary>
    /// List all teams
    /// </summary>
    /// <returns></returns>
    [Obsolete("Use GetAllAsync instead")]
    public List<Team> GetAll();
    
    /// <summary>
    /// Get all teams
    /// </summary>
    /// <returns></returns>
    public Task<List<Team>> GetAllAsync();
    
    /// <summary>
    /// Gets all teams by mitigation id
    /// </summary>
    /// <param name="id">Mitigation id</param>
    /// <returns></returns>
    public List<Team> GetByMitigationId(int id);
    
    /// <summary>
    /// Gets team by id
    /// </summary>
    /// <param name="teamId"></param>
    /// <returns></returns>
    public Team GetById(int teamId, bool fullGet = false);
    
    /// <summary>
    /// Gets users ids by team id
    /// </summary>
    /// <param name="teamId"></param>
    /// <returns></returns>
    public List<int> GetUsersIds(int teamId);
    
    /// <summary>
    ///  Updates users in team
    /// </summary>
    /// <param name="teamId"></param>
    /// <param name="usersIds"></param>
    public void UpdateUsers(int teamId, List<int> usersIds);
    
    /// <summary>
    ///  Deletes team
    /// </summary>
    /// <param name="teamId"></param>
    public void Delete(int teamId);

    /// <summary>
    ///  Creates a team
    /// </summary>
    /// <param name="team"></param>
    /// <returns></returns>
    public Team Create(Team team);
}


