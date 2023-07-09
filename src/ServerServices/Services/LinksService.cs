using System.Text;
using DAL;
using DAL.Entities;
using Microsoft.Extensions.Configuration;
using Serilog;
using ServerServices.Interfaces;
using Tools;
using ILogger = Serilog.ILogger;
using static BCrypt.Net.BCrypt;

namespace ServerServices.Services;

public class LinksService: ILinksService
{
    //private SRDbContext? _dbContext = null;
    private readonly DALManager _dalManager;
    private ILogger _log;
    private IConfiguration _configuration;



    public LinksService(DALManager dalManager,
        IConfiguration configuration,
        ILogger logger)
    {
        _configuration = configuration;
        _dalManager = dalManager;
        _log = logger;
       
    }
    
    
    public string CreateLink(string type, DateTime expirationDate, byte[]? data)
    {
        CleanLinks();
        
        var key = RandomGenerator.RandomString(40);
        var hash = HashPassword(key, 5);
        
        using var context = _dalManager.GetContext();
        var link = new Link()
        {
            Type = type,
            ExpirationDate = expirationDate,
            CreationDate = DateTime.Now,
            Data = data,
            KeyHash = hash
        };
        try
        {
            context.Links.Add(link);
            context.SaveChanges();
            
            return _configuration["website:protocol"] + "://" + _configuration["website:host"] + ":" + _configuration["website:port"] + "/password/ResetPassword?key=" + key;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Error creating link");
            throw new Exception("Error creating link");
        }
        
    }
    
    private void CleanLinks()
    {
        using var context = _dalManager.GetContext();
        var links = context.Links.Where(l => l.ExpirationDate < DateTime.Now);
        context.Links.RemoveRange(links);
        context.SaveChanges();
    }
}