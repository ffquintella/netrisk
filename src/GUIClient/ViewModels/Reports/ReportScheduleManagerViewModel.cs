using GUIClient.Tools;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using DAL.Entities;
using DAL.EntitiesDto;
using GUIClient.ViewModels;
using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Parameters;
using GUIClient.ViewModels.Dialogs.Reports;
using GUIClient.ViewModels.Dialogs.Results;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace GUIClient.ViewModels.Reports
{
    public class ReportScheduleManagerViewModel : ViewModelBase
    {
        #region LANGUAGE

        public string StrTitle { get; } = Localizer["ReportScheduleManager"];
        public string StrSchedules { get; } = Localizer["Schedules"];
        public string StrDetails { get; } = Localizer["Details"];
        public string StrTemplate { get; } = Localizer["Template"];
        public string StrFrequencyCron { get; } = Localizer["FrequencyCron"];
        public string StrTimezone { get; } = Localizer["Timezone"];
        public string StrRecipients { get; } = Localizer["Recipients"];
        public string StrEnabled { get; } = Localizer["Enabled"];
        public string StrLastRun { get; } = Localizer["LastRun"];
        public string StrWhen { get; } = Localizer["WhenLabel"];
        public string StrStatus { get; } = Localizer["Status"];
        public string StrNeverRun { get; } = Localizer["NeverRun"];
        public string StrCreate { get; } = Localizer["Create"];
        public string StrUpdate { get; } = Localizer["Update"];
        public string StrTest { get; } = Localizer["Test"];
        public string StrDelete { get; } = Localizer["Delete"];

        #endregion

        private readonly IReportSchedulesService _reportSchedulesService;
        private readonly IDialogService _dialogService;

        private ObservableCollection<ReportSchedule> _schedules = new();
        public ObservableCollection<ReportSchedule> Schedules
        {
            get => _schedules;
            set => this.RaiseAndSetIfChanged(ref _schedules, value);
        }

        private ReportSchedule? _selectedSchedule;
        public ReportSchedule? SelectedSchedule
        {
            get => _selectedSchedule;
            set => this.RaiseAndSetIfChanged(ref _selectedSchedule, value);
        }

        public ReactiveCommand<RxVoid, RxVoid> CreateCommand { get; }
        public ReactiveCommand<RxVoid, RxVoid> UpdateCommand { get; }
        public ReactiveCommand<RxVoid, RxVoid> DeleteCommand { get; }
        public ReactiveCommand<RxVoid, RxVoid> TestCommand { get; }

        public ReportScheduleManagerViewModel()
        {
            _reportSchedulesService = GetService<IReportSchedulesService>();
            _dialogService = GetService<IDialogService>();

            CreateCommand = ReactiveCommand.CreateFromTask(CreateSchedule);
            UpdateCommand = ReactiveCommand.CreateFromTask(UpdateSchedule, this.WhenAnyValue(x => x.SelectedSchedule).Select(schedule => schedule != null));
            DeleteCommand = ReactiveCommand.CreateFromTask(DeleteSchedule, this.WhenAnyValue(x => x.SelectedSchedule).Select(schedule => schedule != null));
            TestCommand = ReactiveCommand.CreateFromTask(TestSchedule, this.WhenAnyValue(x => x.SelectedSchedule).Select(schedule => schedule != null));

            _ = LoadSchedules();
        }

        private async Task LoadSchedules()
        {
            Schedules = new ObservableCollection<ReportSchedule>(await _reportSchedulesService.GetAllAsync());
        }

        private async Task CreateSchedule()
        {
            var result = await _dialogService.ShowDialogAsync<EditReportScheduleDialogResult>(nameof(EditReportScheduleDialogViewModel));
    
            if (result != null && result.Action == ResultActions.Save)
            {
                var dto = new ReportScheduleCreateDto
                {
                    ReportTemplateVersionId = result.ReportTemplateVersionId,
                    FrequencyCron = result.FrequencyCron,
                    Timezone = result.Timezone,
                    RecipientsJson = result.RecipientsJson,
                    IsEnabled = result.IsEnabled
                };
                await _reportSchedulesService.CreateAsync(dto);
                await LoadSchedules();
                Toasts.Success(Localizer["ScheduleSavedMSG"]);
            }
        }

        private async Task UpdateSchedule()
        {
            if (SelectedSchedule == null) return;

            var parameter = new ReportScheduleNavigationParameter(SelectedSchedule);
            var result = await _dialogService.ShowDialogAsync<EditReportScheduleDialogResult, ReportScheduleNavigationParameter>(nameof(EditReportScheduleDialogViewModel), parameter);

            if (result != null && result.Action == ResultActions.Save)
            {
                var dto = new ReportScheduleUpdateDto
                {
                    ReportTemplateVersionId = result.ReportTemplateVersionId,
                    FrequencyCron = result.FrequencyCron,
                    Timezone = result.Timezone,
                    RecipientsJson = result.RecipientsJson,
                    IsEnabled = result.IsEnabled
                };
                await _reportSchedulesService.UpdateAsync(SelectedSchedule.Id, dto);
                await LoadSchedules();
                Toasts.Success(Localizer["ScheduleSavedMSG"]);
            }
        }

        private async Task DeleteSchedule()
        {
            if (SelectedSchedule == null) return;

            if (!await ConfirmationDialog.ConfirmDeleteAsync(
                    SelectedSchedule.ReportTemplateVersion?.Template?.Name)) return;

            await _reportSchedulesService.DeleteAsync(SelectedSchedule.Id);
            await LoadSchedules();
            Toasts.Success(Localizer["ScheduleDeletedMSG"]);
        }

        private async Task TestSchedule()
        {
            if (SelectedSchedule == null) return;

            await _reportSchedulesService.TriggerTestAsync(SelectedSchedule.Id);
            // Reload so the just-recorded run status / timestamp surface in the list.
            await LoadSchedules();
            Toasts.Success(Localizer["ScheduleTestTriggeredMSG"]);
        }
    }
}
