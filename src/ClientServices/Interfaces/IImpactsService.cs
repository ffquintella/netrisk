using System.Collections.Generic;
using Model.Globalization;

namespace ClientServices.Interfaces;

public interface IImpactsService
{
    /// <summary>
    /// Get all impacts
    /// </summary>
    /// <returns></returns>
    public List<LocalizableListItem> GetAll();
}