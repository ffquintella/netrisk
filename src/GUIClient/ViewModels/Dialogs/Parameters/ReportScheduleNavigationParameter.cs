using DAL.Entities;

namespace GUIClient.ViewModels.Dialogs.Parameters
{
    public class ReportScheduleNavigationParameter : NavigationParameterBase
    {
        public ReportSchedule Schedule { get; }

        public ReportScheduleNavigationParameter(ReportSchedule schedule)
        {
            Schedule = schedule;
        }
    }
}
