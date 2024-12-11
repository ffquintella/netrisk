using System.Collections.Generic;
using Model.Globalization;

namespace ClientServices.Interfaces;

public interface IImpactsService
{
    /// <summary>
    /// Get all impacts
    /// </summary>
    /// <returns></returns>
    [Obsolete("Use GetAllAsync instead")]
    public List<LocalizableListItem> GetAll();
    
    /// <summary>
    /// Get all impacts
    /// </summary>
    /// <returns></returns>
    public Task<List<LocalizableListItem>> GetAllAsync();
}