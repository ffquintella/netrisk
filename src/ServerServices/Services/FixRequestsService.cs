﻿using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Model.Exceptions;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace ServerServices.Services;

public class FixRequestsService: ServiceBase, IFixRequestsService
{
    public FixRequestsService(ILogger logger, DALService dalService) : base(logger, dalService)
    {
    }

    public async Task<FixRequest> CreateFixRequestAsync(FixRequest fixRequest)
    {
        await using var db = DalService.GetContext();
        
        var result = await db.FixRequests.AddAsync(fixRequest);
        await db.SaveChangesAsync();
        
        return result.Entity;
    }

    public async Task<FixRequest> GetFixRequestAsync(string identifier)
    {
        await using var db = DalService.GetContext();
        
        var result = await db.FixRequests.FirstOrDefaultAsync(fr => fr.Identifier == identifier);

        if (result == null)
        {
            Logger.Warning("FixRequest with identifier {identifier} not found", identifier);
            throw new DataNotFoundException("FixRequest", $"FixRequest with identifier {identifier} not found");
        }
            
        
        return result;
    }

    public async Task<FixRequest> GetFixRequestAsync(int id)
    {
        await using var db = DalService.GetContext();

        var result = await db.FixRequests.FindAsync(id);

        if (result == null)
        {
            Logger.Warning("FixRequest with identifier {id} not found", id);
            throw new DataNotFoundException("FixRequest", $"FixRequest with identifier {id} not found");
        }
            
        
        return result; 
    }
}