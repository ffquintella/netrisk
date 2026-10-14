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
    /// Creates a new link and returns its raw key, for a caller that builds its own URL. The key is
    /// <c>RandomGenerator.RandomToken(32)</c> — 256 bits of CSPRNG output, URL-safe — and only its
    /// <c>LinkKeyHash.Primary</c> digest is stored, so the key exists nowhere but in the value returned.
    /// </summary>
    public string CreateLinkKey(string type, DateTime expirationDate, byte[]? data);

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
    
    /// <summary>
    /// Deletes the specified link
    /// </summary>
    /// <param name="type"></param>
    /// <param name="key"></param>
    public void DeleteLink(string type, string key);

    /// <summary>
    /// Returns all non-expired links of the given type (used by the website sync fast lane).
    /// </summary>
    public List<Link> GetLinks(string type);
}