using Model.Globalization;

namespace ClientServices.Interfaces;

public interface IListLocalizationService
{
    public List<LocalizableListItem> LocalizeList(List<LocalizableListItem> list);
}