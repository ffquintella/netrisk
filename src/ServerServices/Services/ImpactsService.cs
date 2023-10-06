using DAL;
using Model.Globalization;
using Serilog;
using ServerServices.Interfaces;

namespace ServerServices.Services;

public class ImpactsService: ServiceBase, IImpactsService
{
    public ImpactsService(ILogger logger, DALManager dalManager) : base(logger, dalManager)
    {
    }

    public List<LocalizableListItem> GetAll()
    {
        var impacts = new List<LocalizableListItem>
        {
            new LocalizableListItem {Key = 1, Value = "Low" },
            new LocalizableListItem {Key = 2, Value = "Medium"},
            new LocalizableListItem {Key = 3, Value = "High"}
        };

        return impacts;
    }
}