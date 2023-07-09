using DAL.Entities;
using ServerServices.Interfaces;

namespace ServerServices.Services;

public class LinksService: ILinksService
{
    public string CreateLink(string type, DateTime expirationDate, byte[]? data)
    {
        throw new NotImplementedException();
    }
}