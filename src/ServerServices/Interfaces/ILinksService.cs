using DAL.Entities;

namespace ServerServices.Interfaces;

/// <summary>
/// Manages the http links created by the application
/// </summary>
public interface ILinksService
{

    /// <summary>
    /// Creates a new link and returns it´s url
    /// </summary>
    /// <param name="type"></param>
    /// <param name="expirationDate"></param>
    /// <param name="data">The data to be encoded with the link</param>
    /// <returns>The link url</returns>
    public string CreateLink(string type, DateTime expirationDate, byte[]? data);
    
    /// <summary>
    /// Check if the specified link exists
    /// </summary>
    /// <param name="type"></param>
    /// <param name="key"></param>
    /// <returns></returns>
    public bool LinkExists(string type, string key);
    
    /// <summary>
    /// Gets the link data
    /// </summary>
    /// <param name="type"></param>
    /// <param name="key"></param>
    /// <returns></returns>
    public byte[] GetLinkData(string type, string key);
}