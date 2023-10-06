using Model.Globalization;

namespace ServerServices.Interfaces;

public interface IImpactsService
{
    
    /// <summary>
    /// Get all impacts
    /// </summary>
    /// <returns></returns>
    public List<LocalizableListItem> GetAll();
}