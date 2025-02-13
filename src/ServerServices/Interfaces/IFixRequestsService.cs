﻿using DAL.Entities;

namespace ServerServices.Interfaces;

public interface IFixRequestsService
{
    /// <summary>
    /// Creates a new FixRequest
    /// </summary>
    /// <param name="fixRequest"></param>
    /// <returns></returns>
    public Task<FixRequest> CreateFixRequestAsync(FixRequest fixRequest);
    
    /// <summary>
    /// Gets a FixRequest by its identifier
    /// </summary>
    /// <param name="identifier"></param>
    /// <returns></returns>
    public Task<FixRequest> GetFixRequestAsync(string identifier);
    
    /// <summary>
    /// Get a FixRequest by its id
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public Task<FixRequest> GetByIdAsync(int id);

    /// <summary>
    /// Get all fix requests
    /// </summary>
    /// <returns></returns>
    public Task<List<FixRequest>> GetAllFixRequestAsync();
    
    
    /// <summary>
    /// Get all fix requests by vulnerabilities
    /// </summary>
    /// <param name="vulnerabilitiesIds"></param>
    /// <returns></returns>
    public Task<List<FixRequest>> GetVulnerabilitiesFixRequestAsync(List<int> vulnerabilitiesIds);
    
    /// <summary>
    /// Saves the fix request to the database
    /// </summary>
    /// <param name="fixRequest"></param>
    /// <returns></returns>
    public Task<FixRequest> SaveFixRequestAsync(FixRequest fixRequest);
}