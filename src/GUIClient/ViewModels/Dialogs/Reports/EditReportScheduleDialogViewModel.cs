using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using DAL.Entities;
using GUIClient.ViewModels.Dialogs.Parameters;
using GUIClient.ViewModels.Dialogs.Results;
using ReactiveUI;
using Serilog;

namespace GUIClient.ViewModels.Dialogs.Reports
{
    public enum ScheduleFrequency
    {
        Daily,
        Weekly,
        Monthly
    }

    public class EditReportScheduleDialogViewModel : ParameterizedDialogViewModelBase<EditReportScheduleDialogResult, ReportScheduleNavigationParameter>
    {
        private readonly IReportTemplatesService _reportTemplatesService;

        #region LANGUAGE
        public string StrReportTemplate => Localizer["Report Template"];
        public string StrVersion => Localizer["Version"];
        public string StrFrequency => Localizer["Frequency"];
        public string StrTime => Localizer["Time"];
        public string StrDayOfWeek => Localizer["Day of week"];
        public string StrDayOfMonth => Localizer["Day of month"];
        public string StrTimezone => Localizer["Timezone"];
        public string StrRecipients => Localizer["Recipients"];
        public string StrAdd => Localizer["Add"];
        public string StrRemove => Localizer["Remove"];
        public string StrEnabled => Localizer["Enabled"];
        #endregion

        #region PROPERTIES
        public ObservableCollection<ReportTemplate> ReportTemplates { get; set; } = new();

        private ReportTemplate? _selectedReportTemplate;
        public ReportTemplate? SelectedReportTemplate
        {
            get => _selectedReportTemplate;
            set => this.RaiseAndSetIfChanged(ref _selectedReportTemplate, value);
        }

        public ObservableCollection<ReportTemplateVersion> ReportTemplateVersions { get; set; } = new();

        private ReportTemplateVersion? _selectedReportTemplateVersion;
        public ReportTemplateVersion? SelectedReportTemplateVersion
        {
            get => _selectedReportTemplateVersion;
            set => this.RaiseAndSetIfChanged(ref _selectedReportTemplateVersion, value);
        }

        public ObservableCollection<ScheduleFrequency> Frequencies { get; } =
            new(Enum.GetValues<ScheduleFrequency>());

        private ScheduleFrequency _frequency = ScheduleFrequency.Daily;
        public ScheduleFrequency Frequency
        {
            get => _frequency;
            set
            {
                this.RaiseAndSetIfChanged(ref _frequency, value);
                this.RaisePropertyChanged(nameof(IsWeekly));
                this.RaisePropertyChanged(nameof(IsMonthly));
            }
        }

        public bool IsWeekly => Frequency == ScheduleFrequency.Weekly;
        public bool IsMonthly => Frequency == ScheduleFrequency.Monthly;

        public ObservableCollection<int> Hours { get; } = new(Enumerable.Range(0, 24));
        public ObservableCollection<int> Minutes { get; } = new(Enumerable.Range(0, 60));

        private int _hour = 8;
        public int Hour { get => _hour; set => this.RaiseAndSetIfChanged(ref _hour, value); }

        private int _minute;
        public int Minute { get => _minute; set => this.RaiseAndSetIfChanged(ref _minute, value); }

        public ObservableCollection<DayOfWeek> DaysOfWeek { get; } = new(Enum.GetValues<DayOfWeek>());

        private DayOfWeek _selectedDayOfWeek = DayOfWeek.Monday;
        public DayOfWeek SelectedDayOfWeek { get => _selectedDayOfWeek; set => this.RaiseAndSetIfChanged(ref _selectedDayOfWeek, value); }

        // 1..28 only, so a monthly schedule fires every month (no skipped 29-31).
        public ObservableCollection<int> DaysOfMonth { get; } = new(Enumerable.Range(1, 28));

        private int _selectedDayOfMonth = 1;
        public int SelectedDayOfMonth { get => _selectedDayOfMonth; set => this.RaiseAndSetIfChanged(ref _selectedDayOfMonth, value); }

        public ObservableCollection<string> Timezones { get; } =
            new(TimeZoneInfo.GetSystemTimeZones().Select(t => t.Id));

        private string _timezone = "UTC";
        public string Timezone { get => _timezone; set => this.RaiseAndSetIfChanged(ref _timezone, value); }

        public ObservableCollection<string> Recipients { get; } = new();

        private string? _newRecipient;
        public string? NewRecipient { get => _newRecipient; set => this.RaiseAndSetIfChanged(ref _newRecipient, value); }

        private string? _selectedRecipient;
        public string? SelectedRecipient { get => _selectedRecipient; set => this.RaiseAndSetIfChanged(ref _selectedRecipient, value); }

        private bool _isEnabled = true;
        public bool IsEnabled { get => _isEnabled; set => this.RaiseAndSetIfChanged(ref _isEnabled, value); }
        #endregion

        #region COMMANDS
        public ReactiveCommand<Unit, Unit> AddRecipientCommand { get; }
        public ReactiveCommand<Unit, Unit> RemoveRecipientCommand { get; }
        public ReactiveCommand<Unit, Unit> SaveCommand { get; }
        #endregion

        public EditReportScheduleDialogViewModel()
        {
            _reportTemplatesService = GetService<IReportTemplatesService>();

            AddRecipientCommand = ReactiveCommand.Create(AddRecipient);
            RemoveRecipientCommand = ReactiveCommand.Create(RemoveRecipient);
            SaveCommand = ReactiveCommand.Create(Save, this.WhenAnyValue(x => x.SelectedReportTemplateVersion).Select(v => v != null));

            this.WhenAnyValue(x => x.SelectedReportTemplate).Subscribe(template =>
            {
                if (template != null)
                {
                    ReportTemplateVersions = new ObservableCollection<ReportTemplateVersion>(
                        template.Versions.OrderByDescending(v => v.Version));
                    this.RaisePropertyChanged(nameof(ReportTemplateVersions));
                    SelectedReportTemplateVersion = ReportTemplateVersions.FirstOrDefault();
                }
            });

            _ = LoadReportTemplates();
        }

        private async Task LoadReportTemplates()
        {
            var templates = await _reportTemplatesService.GetAllAsync();
            ReportTemplates = new ObservableCollection<ReportTemplate>(templates);
            this.RaisePropertyChanged(nameof(ReportTemplates));
        }

        public override void Activate(ReportScheduleNavigationParameter parameter)
        {
            var schedule = parameter.Schedule;
            Timezone = string.IsNullOrWhiteSpace(schedule.Timezone) ? "UTC" : schedule.Timezone;
            IsEnabled = schedule.IsEnabled;

            ParseCron(schedule.FrequencyCron);
            ParseRecipients(schedule.RecipientsJson);

            SelectedReportTemplate = ReportTemplates.FirstOrDefault(t => t.Id == schedule.ReportTemplateVersion.TemplateId);
            SelectedReportTemplateVersion = ReportTemplateVersions.FirstOrDefault(v => v.Id == schedule.ReportTemplateVersionId)
                                            ?? ReportTemplateVersions.FirstOrDefault();
        }

        private void AddRecipient()
        {
            var email = NewRecipient?.Trim();
            if (string.IsNullOrWhiteSpace(email)) return;
            if (!Recipients.Contains(email)) Recipients.Add(email);
            NewRecipient = "";
        }

        private void RemoveRecipient()
        {
            if (SelectedRecipient != null) Recipients.Remove(SelectedRecipient);
        }

        // Builds a 5-field cron expression (min hour day-of-month month day-of-week)
        // matching the Hangfire/NCrontab format the scheduler consumes.
        private string BuildCron()
        {
            return Frequency switch
            {
                ScheduleFrequency.Weekly => $"{Minute} {Hour} * * {(int)SelectedDayOfWeek}",
                ScheduleFrequency.Monthly => $"{Minute} {Hour} {SelectedDayOfMonth} * *",
                _ => $"{Minute} {Hour} * * *"
            };
        }

        private void ParseCron(string? cron)
        {
            if (string.IsNullOrWhiteSpace(cron)) return;

            var parts = cron.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 5) return;

            if (int.TryParse(parts[0], out var min)) Minute = Math.Clamp(min, 0, 59);
            if (int.TryParse(parts[1], out var hour)) Hour = Math.Clamp(hour, 0, 23);

            var dom = parts[2];
            var dow = parts[4];

            if (dow != "*" && int.TryParse(dow, out var dowVal))
            {
                Frequency = ScheduleFrequency.Weekly;
                if (Enum.IsDefined(typeof(DayOfWeek), dowVal)) SelectedDayOfWeek = (DayOfWeek)dowVal;
            }
            else if (dom != "*" && int.TryParse(dom, out var domVal))
            {
                Frequency = ScheduleFrequency.Monthly;
                SelectedDayOfMonth = Math.Clamp(domVal, 1, 28);
            }
            else
            {
                Frequency = ScheduleFrequency.Daily;
            }
        }

        private void ParseRecipients(string? recipientsJson)
        {
            Recipients.Clear();
            if (string.IsNullOrWhiteSpace(recipientsJson)) return;

            try
            {
                var list = JsonSerializer.Deserialize<List<string>>(recipientsJson);
                if (list != null)
                    foreach (var r in list.Where(r => !string.IsNullOrWhiteSpace(r)))
                        Recipients.Add(r.Trim());
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not parse report schedule recipients JSON");
            }
        }

        private void Save()
        {
            if (SelectedReportTemplateVersion == null) return;

            var result = new EditReportScheduleDialogResult
            {
                ReportTemplateVersionId = SelectedReportTemplateVersion.Id,
                FrequencyCron = BuildCron(),
                Timezone = Timezone,
                RecipientsJson = JsonSerializer.Serialize(Recipients.ToList()),
                IsEnabled = IsEnabled,
                Action = ResultActions.Save
            };
            Close(result);
        }
    }
}
