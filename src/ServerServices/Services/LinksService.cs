using System.Text;
using BCrypt.Net;
using DAL;
using DAL.Entities;
using Microsoft.Extensions.Configuration;
using Model.Exceptions;
using Serilog;
using ServerServices.Interfaces;
using Tools;
using Tools.Security;
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
        //var hash = HashPassword(key, 5);
        var hash = HashTool.CreateMD5(key);
        
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

    public bool LinkExists(string type, string key)
    {
        CleanLinks();
        using var context = _dalManager.GetContext();
        var hash = HashTool.CreateMD5(key);
        var link = context.Links.FirstOrDefault(l => l.Type == type && l.KeyHash == hash);
        return link != null;
    }

    public byte[] GetLinkData(string type, string key)
    {
        if(!LinkExists(type,key)) throw new DataNotFoundException("link", key);
        using var context = _dalManager.GetContext();
        var hash = HashTool.CreateMD5(key);
        var link = context.Links.FirstOrDefault(l => l.Type == type && l.KeyHash == hash);
        if(link!.Data == null) throw new DataNotFoundException("link", key, new Exception("Link data is null"));
        return link.Data;
    }
    
    private void CleanLinks()
    {
        using var context = _dalManager.GetContext();
        var links = context.Links.Where(l => l.ExpirationDate < DateTime.Now);
        context.Links.RemoveRange(links);
        context.SaveChanges();
    }
}