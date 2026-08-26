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

    public LinksService(ILogger logger, IDalService dalService, IConfiguration configuration): base(logger, dalService)
    {
        _configuration = configuration;
    }


    public string CreateLink(string type, DateTime expirationDate, byte[]? data)
    {
        CleanLinks();
        
        var key = RandomGenerator.RandomString(40);
        var hash = LinkKeyHash.Primary(key);
        
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
        return Find(context, type, key) != null;
    }

    public byte[] GetLinkData(string type, string key)
    {
        if(!LinkExists(type,key)) throw new DataNotFoundException("link", key);
        using var context = DalService.GetContext();
        var link = Find(context, type, key);
        if(link?.Data == null) throw new DataNotFoundException("link", key, new Exception("Link data is null"));
        return link.Data;
    }

    public void DeleteLink(string type, string key)
    {
        if(!LinkExists(type,key)) throw new DataNotFoundException("link", key);
        using var context = DalService.GetContext();
        var link = Find(context, type, key);
        if (link == null) throw new DataNotFoundException("link", key);
        context.Links.Remove(link);
        context.SaveChanges();
    }

    /// <summary>
    /// Resolves a link by its key. See <see cref="LinkKeyHash"/> for why the digest choice lives in a
    /// shared helper: the WebSite resolves the very same rows, pushed to it verbatim over
    /// <c>/sync</c>, and a disagreement between the two would silently break every password reset.
    /// </summary>
    private static Link? Find(DAL.Context.AuditableContext context, string type, string key)
    {
        var primary = LinkKeyHash.Primary(key);
        var link = context.Links.FirstOrDefault(l => l.Type == type && l.KeyHash == primary);
        if (link != null) return link;

        var legacy = LinkKeyHash.Legacy(key);
        return context.Links.FirstOrDefault(l => l.Type == type && l.KeyHash == legacy);
    }
    
    public List<Link> GetLinks(string type)
    {
        CleanLinks();
        using var context = DalService.GetContext();
        return context.Links.Where(l => l.Type == type).ToList();
    }

    private void CleanLinks()
    {
        using var context = DalService.GetContext();
        var links = context.Links.Where(l => l.ExpirationDate < DateTime.Now);
        context.Links.RemoveRange(links);
        context.SaveChanges();
    }
}