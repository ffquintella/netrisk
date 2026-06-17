using DAL.Entities;

namespace GUIClient.ViewModels.Dialogs.Parameters
{
    public class ReportTemplateNavigationParameter : NavigationParameterBase
    {
        public ReportTemplate Template { get; }

        public ReportTemplateNavigationParameter(ReportTemplate template)
        {
            Template = template;
        }
    }
}
