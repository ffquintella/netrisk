using System.Collections.ObjectModel;
using System.Reactive;
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
    public class ReportTemplateManagerViewModel : ViewModelBase
    {
        private readonly IReportTemplatesService _reportTemplatesService;
        private readonly IDialogService _dialogService;

        private ObservableCollection<ReportTemplate> _templates = new();
        public ObservableCollection<ReportTemplate> Templates
        {
            get => _templates;
            set => this.RaiseAndSetIfChanged(ref _templates, value);
        }

        private ReportTemplate _selectedTemplate;
        public ReportTemplate SelectedTemplate
        {
            get => _selectedTemplate;
            set => this.RaiseAndSetIfChanged(ref _selectedTemplate, value);
        }

        public ReactiveCommand<Unit, Unit> CreateCommand { get; }
        public ReactiveCommand<Unit, Unit> UpdateCommand { get; }
        public ReactiveCommand<Unit, Unit> DeleteCommand { get; }

        public ReportTemplateManagerViewModel()
        {
            _reportTemplatesService = GetService<IReportTemplatesService>();
            _dialogService = GetService<IDialogService>();

            CreateCommand = ReactiveCommand.CreateFromTask(CreateTemplate);
            UpdateCommand = ReactiveCommand.CreateFromTask(UpdateTemplate, this.WhenAnyValue(x => x.SelectedTemplate, (template) => template != null));
            DeleteCommand = ReactiveCommand.CreateFromTask(DeleteTemplate, this.WhenAnyValue(x => x.SelectedTemplate, (template) => template != null));

            _ = LoadTemplates();
        }

        private async Task LoadTemplates()
        {
            var templates = await _reportTemplatesService.GetAllAsync();
            Templates = new ObservableCollection<ReportTemplate>(templates);
        }

        private async Task CreateTemplate()
        {
            var result = await _dialogService.ShowDialogAsync<EditReportTemplateDialogResult>(nameof(EditReportTemplateDialogViewModel));
    
            if (result != null && result.Action == ResultActions.Save)
            {
                var dto = new ReportTemplateCreateDto
                {
                    Name = result.Name,
                    Description = result.Description,
                    LayoutJson = result.LayoutJson,
                    BrandingJson = result.BrandingJson
                };
                await _reportTemplatesService.CreateAsync(dto);
                await LoadTemplates();
            }
        }

        private async Task UpdateTemplate()
        {
            var parameter = new ReportTemplateNavigationParameter(SelectedTemplate);
            var result = await _dialogService.ShowDialogAsync<EditReportTemplateDialogResult, ReportTemplateNavigationParameter>(nameof(EditReportTemplateDialogViewModel), parameter);

            if (result != null && result.Action == ResultActions.Save)
            {
                var dto = new ReportTemplateUpdateDto
                {
                    Name = result.Name,
                    Description = result.Description,
                    LayoutJson = result.LayoutJson,
                    BrandingJson = result.BrandingJson
                };
                await _reportTemplatesService.UpdateAsync(SelectedTemplate.Id, dto);
                await LoadTemplates();
            }
        }

        private async Task DeleteTemplate()
        {
            await _reportTemplatesService.DeleteAsync(SelectedTemplate.Id);
            await LoadTemplates();
        }
    }
}
