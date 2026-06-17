using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using DAL.Entities;
using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Parameters;
using GUIClient.ViewModels.Dialogs.Results;
using ReactiveUI;

namespace GUIClient.ViewModels.Dialogs.Reports
{
    public class EditReportScheduleDialogViewModel : ParameterizedDialogViewModelBase<EditReportScheduleDialogResult, ReportScheduleNavigationParameter>
    {
        private readonly IReportTemplatesService _reportTemplatesService;

        public string FrequencyCron { get; set; }
        public string Timezone { get; set; }
        public string RecipientsJson { get; set; }
        public bool IsEnabled { get; set; }

        public ObservableCollection<ReportTemplate> ReportTemplates { get; set; } = new();
        public ReportTemplate SelectedReportTemplate { get; set; }

        public ObservableCollection<ReportTemplateVersion> ReportTemplateVersions { get; set; } = new();
        public ReportTemplateVersion SelectedReportTemplateVersion { get; set; }

        public ReactiveCommand<Unit, Unit> SaveCommand { get; }

        public EditReportScheduleDialogViewModel()
        {
            _reportTemplatesService = GetService<IReportTemplatesService>();
            SaveCommand = ReactiveCommand.Create(Save);

            this.WhenAnyValue(x => x.SelectedReportTemplate).Subscribe(template =>
            {
                if (template != null)
                {
                    ReportTemplateVersions = new ObservableCollection<ReportTemplateVersion>(template.Versions);
                    SelectedReportTemplateVersion = ReportTemplateVersions.FirstOrDefault();
                }
            });
            
            _ = LoadReportTemplates();
        }

        private async Task LoadReportTemplates()
        {
            var templates = await _reportTemplatesService.GetAllAsync();
            ReportTemplates = new ObservableCollection<ReportTemplate>(templates);
        }

        public override void Activate(ReportScheduleNavigationParameter parameter)
        {
            var schedule = parameter.Schedule;
            FrequencyCron = schedule.FrequencyCron;
            Timezone = schedule.Timezone;
            RecipientsJson = schedule.RecipientsJson;
            IsEnabled = schedule.IsEnabled;

            SelectedReportTemplate = ReportTemplates.FirstOrDefault(t => t.Id == schedule.ReportTemplateVersion.TemplateId);
            SelectedReportTemplateVersion = ReportTemplateVersions.FirstOrDefault(v => v.Id == schedule.ReportTemplateVersionId);
        }

        private void Save()
        {
            var result = new EditReportScheduleDialogResult
            {
                ReportTemplateVersionId = SelectedReportTemplateVersion.Id,
                FrequencyCron = FrequencyCron,
                Timezone = Timezone,
                RecipientsJson = RecipientsJson,
                IsEnabled = IsEnabled,
                Action = ResultActions.Save
            };
            Close(result);
        }
    }
}
