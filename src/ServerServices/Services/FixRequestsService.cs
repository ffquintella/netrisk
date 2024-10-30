using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Model.Exceptions;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace ServerServices.Services;

public class FixRequestsService: ServiceBase, IFixRequestsService
{
    public FixRequestsService(ILogger logger, IDalService dalService) : base(logger, dalService)
    {
    }

    public async Task<FixRequest> CreateFixRequestAsync(FixRequest fixRequest)
    {
        await using var db = DalService.GetContext();
        
        var result =  await db.FixRequests.AddAsync(fixRequest);
        
        await db.SaveChangesAsync();
        
        return result.Entity;
    }

    public async Task<FixRequest> GetFixRequestAsync(string identifier)
    {
        await using var db = DalService.GetContext();
        
        var result = await db.FixRequests
            .Include(fr => fr.Vulnerability).ThenInclude(v => v.Host)
            .FirstOrDefaultAsync(fr => fr.Identifier == identifier);

        if (result == null)
        {
            Logger.Warning("FixRequest with identifier {identifier} not found", identifier);
            throw new DataNotFoundException("FixRequest", $"FixRequest with identifier {identifier} not found");
        }
            
        
        return result;
    }

    public async Task<FixRequest> SaveFixRequestAsync(FixRequest fixRequest)
    {
        await using var db = DalService.GetContext();
        
        var result = db.FixRequests.Update(fixRequest);
        await db.SaveChangesAsync();
        return result.Entity;
    }

    public async Task<FixRequest> GetByIdAsync(int id)
    {
        await using var db = DalService.GetContext();

        var result = await db.FixRequests
            .Include(fr => fr.Vulnerability).ThenInclude(v => v.Host)
            .FirstOrDefaultAsync(fr => fr.Id == id);

        if (result == null)
        {
            Logger.Warning("FixRequest with identifier {id} not found", id);
            throw new DataNotFoundException("FixRequest", $"FixRequest with identifier {id} not found");
        }
            
        
        return result; 
    }
    
    public async Task<List<FixRequest>> GetAllFixRequestAsync()
    {
        await using var db = DalService.GetContext();

        var result = await db.FixRequests
            .Include(fr => fr.Vulnerability).ThenInclude(v => v.Host).ToListAsync();

        if (result == null)
        {
            Logger.Warning("FixRequests not found");
            throw new DataNotFoundException("FixRequest", $"FixRequests not found");
        }
            
        
        return result; 
    }

    public async Task<List<FixRequest>> GetVulnerabilitiesFixRequestAsync(List<int> vulnerabilitiesIds)
    {
        await using var db = DalService.GetContext();
        
        var result = await db.FixRequests
            .Include(fr => fr.Vulnerability)
            .Where(fr => vulnerabilitiesIds.Contains(fr.VulnerabilityId))
            .ToListAsync();
        
        if (result == null) 
        {
            Logger.Warning("FixRequests not found");
            throw new DataNotFoundException("FixRequest", $"FixRequests not found");
        }

        return result;

    }
}