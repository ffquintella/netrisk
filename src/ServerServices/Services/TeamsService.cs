using DAL;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Model.Exceptions;
using Serilog;
using ServerServices.Interfaces;

namespace ServerServices.Services;

public class TeamsService: ITeamsService
{
    private DALManager _dalManager;
    private ILogger _log;


    public TeamsService(
        ILogger logger, 
        DALManager dalManager
    )
    {
        _dalManager = dalManager;
        _log = logger;
    }
    
    public List<Team> GetAll()
    {
        using var context = _dalManager.GetContext();
        var teams = context.Teams.ToList();
        if (teams == null)
        {
            Log.Error("Error Listing teams");
            throw new DataNotFoundException("Team", "No teams found");
        }

        return teams;
    }

    public List<Team> GetByMitigationId(int id)
    {
        using var context = _dalManager.GetContext();
        var teams = context.Teams
            .Where(t => context.MitigationToTeams.Where(m => m.MitigationId == id).Select(m => m.TeamId)
                .Contains(t.Value))
            .ToList();
            
        if (teams == null)
        {
            Log.Error("Error Listing teams");
            throw new DataNotFoundException("Team", "No teams found");
        }

        return teams;
    }

    public void AssociateTeamToMitigation(int mitigationId, int teamId)
    {
        using var context = _dalManager.GetContext();
        var mitigation = context.Mitigations.FirstOrDefault(m => m.Id == mitigationId);
        var team = context.Teams.FirstOrDefault(t => t.Value == teamId);
        
        if(mitigation == null || team == null) throw new DataNotFoundException("Mitigation or Team", "Mitigation or Team not found");
        
        var mitigationToTeam = new MitigationToTeam
        {
            MitigationId = mitigation.Id,
            TeamId = team.Value
        };
        
        context.MitigationToTeams.Add(mitigationToTeam);

        context.SaveChanges();
    }

    public List<int> GetUsersIds(int teamId)
    {
        using var context = _dalManager.GetContext();

        var team = context.Teams.Include(t => t.Users).FirstOrDefault(t=> t.Value == teamId);
        
        if(team == null) throw new DataNotFoundException("Team", "Team not found");

        var userIds = team.Users.Select(u => u.Value).ToList();
        
        //var userIds = context.UserToTeams.Where(t => t.TeamId == teamId).Select(t => t.UserId).ToList();

        return userIds;
    }
}