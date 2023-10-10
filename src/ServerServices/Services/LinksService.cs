using DAL;
using DAL.Entities;
using Microsoft.Extensions.Configuration;
using Model.Exceptions;
using ServerServices.Interfaces;
using Tools;
using Tools.Security;
using ILogger = Serilog.ILogger;

namespace ServerServices.Services;

public class LinksService: ServiceBase, ILinksService
{
    private readonly IConfiguration _configuration;

    public LinksService(ILogger logger, DALService dalService, IConfiguration configuration): base(logger, dalService)
    {
        _configuration = configuration;
    }


    public string CreateLink(string type, DateTime expirationDate, byte[]? data)
    {
        CleanLinks();
        
        var key = RandomGenerator.RandomString(40);
        //var hash = HashPassword(key, 5);
        var hash = HashTool.CreateMD5(key);
        
        using var context = DalService.GetContext();
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
            Logger.Error(ex, "Error creating link");
            throw new Exception("Error creating link");
        }
        
    }

    public bool LinkExists(string type, string key)
    {
        CleanLinks();
        using var context = DalService.GetContext();
        var hash = HashTool.CreateMD5(key);
        var link = context.Links.FirstOrDefault(l => l.Type == type && l.KeyHash == hash);
        return link != null;
    }

    public byte[] GetLinkData(string type, string key)
    {
        if(!LinkExists(type,key)) throw new DataNotFoundException("link", key);
        using var context = DalService.GetContext();
        var hash = HashTool.CreateMD5(key);
        var link = context.Links.FirstOrDefault(l => l.Type == type && l.KeyHash == hash);
        if(link!.Data == null) throw new DataNotFoundException("link", key, new Exception("Link data is null"));
        return link.Data;
    }

    public void DeleteLink(string type, string key)
    {
        if(!LinkExists(type,key)) throw new DataNotFoundException("link", key);
        using var context = DalService.GetContext();
        var hash = HashTool.CreateMD5(key);
        var link = context.Links.FirstOrDefault(l => l.Type == type && l.KeyHash == hash);
        context.Links.Remove(link!);
        context.SaveChanges();
    }
    
    private void CleanLinks()
    {
        using var context = DalService.GetContext();
        var links = context.Links.Where(l => l.ExpirationDate < DateTime.Now);
        context.Links.RemoveRange(links);
        context.SaveChanges();
    }
}