﻿using System.Collections.Generic;
using DAL.Entities;
using Model.DTO;

namespace ClientServices.Interfaces;

public interface IMitigationService
{
    /// <summary>
    /// Gets mitigation by Risk Id
    /// </summary>
    /// <param name="id">Risk Id</param>
    /// <returns></returns>
    public Mitigation? GetByRiskId(int id);
    
    /// <summary>
    /// Gets the mitigation Async by Risk Id
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public Task<Mitigation?> GetByRiskIdAsync(int id);
    
    /// <summary>
    /// Gets mitigation by id
    /// </summary>
    /// <param name="id">Mitigation Id</param>
    /// <returns></returns>
    public Mitigation? GetById(int id);
    
    /// <summary>
    /// Returns a list of files associated to a specific mitigation
    /// </summary>
    /// <param name="mitigationId"></param>
    /// <returns></returns>
    [Obsolete("Use GetFilesAsync instead")]
    public List<FileListing> GetFiles(int mitigationId);
    
    
    /// <summary>
    /// Returns a list of files associated to a specific mitigation
    /// </summary>
    /// <param name="mitigationId"></param>
    /// <returns></returns>
    public Task<List<FileListing>> GetFilesAsync(int mitigationId);
    
    /// <summary>
    /// Gets team by mitigation id
    /// </summary>
    /// <param name="id">Mitigation Id</param>
    /// <returns>Team</returns>
    public List<Team>? GetTeamsById(int id);
    
    /// <summary>
    /// Gets the mitigations strategies avaliable
    /// </summary>
    /// <returns>List of PlaningStrategy</returns>
    [Obsolete("Use GetStrategiesAsync instead")]
    public List<PlanningStrategy>? GetStrategies();
    
    /// <summary>
    /// Gets the mitigations strategies avaliable
    /// </summary>
    /// <returns>List of PlaningStrategy</returns>
    public Task<List<PlanningStrategy>?> GetStrategiesAsync();
    
    /// <summary>
    /// Gets the possible list of mitigation costs
    /// </summary>
    /// <returns></returns>
    [Obsolete("Use GetCostsAsync instead")]
    public List<MitigationCost>? GetCosts();
    
    
    /// <summary>
    /// Gets the possible list of mitigation costs
    /// </summary>
    /// <returns></returns>
    public Task<List<MitigationCost>?> GetCostsAsync();
    
    
    /// <summary>
    /// Gets the possible list of mitigation efforts
    /// </summary>
    /// <returns></returns>
    [Obsolete("Use GetEffortsAsync instead")]
    public List<MitigationEffort>? GetEfforts();
    
    /// <summary>
    /// Gets the possible list of mitigation efforts
    /// </summary>
    /// <returns></returns>
    public Task<List<MitigationEffort>?> GetEffortsAsync();
    
    /// <summary>
    /// Creates a new mitigation
    /// </summary>
    /// <param name="mitigation"></param>
    /// <returns>Mitigation object</returns>
    public Mitigation? Create(MitigationDto mitigation);
    
    /// <summary>
    /// Saves an existing mitigation to the database
    /// </summary>
    /// <param name="mitigation"></param>
    public void Save(MitigationDto mitigation);
    
    /// <summary>
    /// Deletes all teams associations for a mitigation
    /// </summary>
    /// <param name="mitigationId"></param>
    public void DeleteTeamsAssociations(int mitigationId);
    
    
    /// <summary>
    /// Associates a mitigation to a team
    /// </summary>
    /// <param name="mitigationId"></param>
    /// <param name="teamId"></param>
    public void AssociateMitigationToTeam(int mitigationId, int teamId);
}