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

namespace GUIClient.ViewModels.Reports
{
    public class ReportScheduleManagerViewModel : ViewModelBase
    {
        private readonly IReportSchedulesService _reportSchedulesService;
        private readonly IDialogService _dialogService;

        private ObservableCollection<ReportSchedule> _schedules = new();
        public ObservableCollection<ReportSchedule> Schedules
        {
            get => _schedules;
            set => this.RaiseAndSetIfChanged(ref _schedules, value);
        }

        private ReportSchedule _selectedSchedule;
        public ReportSchedule SelectedSchedule
        {
            get => _selectedSchedule;
            set => this.RaiseAndSetIfChanged(ref _selectedSchedule, value);
        }

        public ReactiveCommand<Unit, Unit> CreateCommand { get; }
        public ReactiveCommand<Unit, Unit> UpdateCommand { get; }
        public ReactiveCommand<Unit, Unit> DeleteCommand { get; }
        public ReactiveCommand<Unit, Unit> TestCommand { get; }

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
            }
        }

        private async Task UpdateSchedule()
        {
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
            }
        }

        private async Task DeleteSchedule()
        {
            await _reportSchedulesService.DeleteAsync(SelectedSchedule.Id);
            await LoadSchedules();
        }

        private async Task TestSchedule()
        {
            await _reportSchedulesService.TriggerTestAsync(SelectedSchedule.Id);
        }
    }
}
