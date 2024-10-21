using DAL;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Model.Exceptions;
using Serilog;
using ServerServices.Interfaces;

namespace ServerServices.Services;

public class TechnologiesService(ILogger logger, IDalService dalService)
    : ServiceBase(logger, dalService), ITechnologiesService
{
    public List<Technology> GetAll()
    {
        using var context = DalService.GetContext();
        
        return context.Technologies.ToList();
    }
    
    public async Task<List<Technology>> GetAllAsync()
    {
        await using var context = DalService.GetContext();
        
        return await context.Technologies.ToListAsync();
    }

    public async Task AddTechnologyAsync(string name)
    {
        await using var context = DalService.GetContext();
        
        var exists = await context.Technologies.AnyAsync(x => x.Name == name);
        if (exists)
        {
            Log.Error("Technology already exists: " + name);
            throw new DataAlreadyExistsException("technology", "technology", name, "Technology already exists!");
        }
        
        await context.Technologies.AddAsync(new Technology()
        {
            Name = name
        });
        
        await context.SaveChangesAsync();
    }

    public async Task RemoveTechnologyAsync(string name)
    {
        await using var context = DalService.GetContext();
        var exists = await context.Technologies.AnyAsync(x => x.Name == name);
        if (!exists)
        {
            Log.Error("Technology not found: " + name);
            throw new DataNotFoundException("technology",  name);
        }
        
        var tec = await context.Technologies.FirstAsync(x => x.Name == name);
        context.Technologies.Remove(tec);
        
        await context.SaveChangesAsync();
        
    }
}