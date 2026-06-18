using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Platform;
using ClientServices.Interfaces;
using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Results;
using Model.Assessments;
using ReactiveUI;

namespace GUIClient.ViewModels.Assessments;

public class AssessmentImportDialogViewModel : DialogViewModelBase<AssessmentImportResult>
{
    #region LANGUAGE

    public string StrTitle => Localizer["ImportAssessmentTemplate"];
    public string StrFile => Localizer["File"] + ":";
    public string StrBrowse => Localizer["Browse"];
    public string StrName => Localizer["Name"] + ":";
    public string StrImport => Localizer["Import"];
    public string StrImportTemplateHelpMSG => Localizer["ImportTemplateHelpMSG"];
    public string StrStarterPacks => Localizer["StarterPacks"] + ":";
    public string StrNistStarterPack => Localizer["NistStarterPack"];
    public string StrIsoStarterPack => Localizer["IsoStarterPack"];
    public string StrWarnings => Localizer["Warnings"];
    public string StrErrors => Localizer["Errors"];

    #endregion

    #region SERVICES

    private IAssessmentsService AssessmentsService { get; } = GetService<IAssessmentsService>();

    #endregion

    #region PROPERTIES

    private string? _selectedFilePath;
    public string? SelectedFilePath
    {
        get => _selectedFilePath;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedFilePath, value);
            this.RaisePropertyChanged(nameof(CanImport));
            if (!string.IsNullOrWhiteSpace(value) && string.IsNullOrWhiteSpace(AssessmentName))
                AssessmentName = Path.GetFileNameWithoutExtension(value);
            if (!string.IsNullOrWhiteSpace(value))
                _ = RunPreviewAsync();
        }
    }

    private string? _assessmentName;
    public string? AssessmentName
    {
        get => _assessmentName;
        set => this.RaiseAndSetIfChanged(ref _assessmentName, value);
    }

    private string _statusMessage = string.Empty;
    public string StatusMessage
    {
        get => _statusMessage;
        set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
    }

    private bool _hasPreview;
    public bool HasPreview
    {
        get => _hasPreview;
        set => this.RaiseAndSetIfChanged(ref _hasPreview, value);
    }

    private bool _previewValid;
    public bool PreviewValid
    {
        get => _previewValid;
        set
        {
            this.RaiseAndSetIfChanged(ref _previewValid, value);
            this.RaisePropertyChanged(nameof(CanImport));
        }
    }

    private ObservableCollection<string> _warnings = new();
    public ObservableCollection<string> Warnings
    {
        get => _warnings;
        set => this.RaiseAndSetIfChanged(ref _warnings, value);
    }

    private ObservableCollection<string> _errors = new();
    public ObservableCollection<string> Errors
    {
        get => _errors;
        set => this.RaiseAndSetIfChanged(ref _errors, value);
    }

    public bool HasWarnings => Warnings.Count > 0;
    public bool HasErrors => Errors.Count > 0;

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            this.RaiseAndSetIfChanged(ref _isBusy, value);
            this.RaisePropertyChanged(nameof(CanImport));
        }
    }

    public bool CanImport => !IsBusy && PreviewValid && !string.IsNullOrWhiteSpace(SelectedFilePath);

    #endregion

    #region COMMANDS

    public ReactiveCommand<Unit, Unit> ImportCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }
    public ReactiveCommand<string, Unit> LoadStarterPackCommand { get; }

    #endregion

    public AssessmentImportDialogViewModel()
    {
        ImportCommand = ReactiveCommand.CreateFromTask(ImportAsync);
        CancelCommand = ReactiveCommand.Create(() => Close(new AssessmentImportResult { Action = ResultActions.Cancel }));
        LoadStarterPackCommand = ReactiveCommand.CreateFromTask<string>(LoadStarterPackAsync);
    }

    private async Task LoadStarterPackAsync(string pack)
    {
        try
        {
            var (resource, displayName) = pack switch
            {
                "iso" => ("avares://GUIClient/Assets/AssessmentTemplates/iso-27001-2022-annex-a.json",
                          "ISO/IEC 27001:2022 Annex A Self-Assessment"),
                _ => ("avares://GUIClient/Assets/AssessmentTemplates/nist-csf-2.0.json",
                      "NIST CSF 2.0 Self-Assessment")
            };

            await using var stream = AssetLoader.Open(new Uri(resource));
            using var reader = new StreamReader(stream);
            var json = await reader.ReadToEndAsync();

            // Materialise the bundled pack to a temp .json file so it reuses the regular
            // file-based import/preview path (the server dispatches on the extension).
            var tempPath = Path.Combine(Path.GetTempPath(), $"netrisk-starter-{pack}-{Guid.NewGuid():N}.json");
            await File.WriteAllTextAsync(tempPath, json);

            AssessmentName = displayName;
            SelectedFilePath = tempPath;
        }
        catch (Exception ex)
        {
            Logger.Error("Error loading starter pack {0}: {1}", pack, ex.Message);
            StatusMessage = Localizer["ImportFailedMSG"];
        }
    }

    private async Task RunPreviewAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedFilePath)) return;

        var extension = Path.GetExtension(SelectedFilePath).ToLowerInvariant();
        if (extension != ".json" && extension != ".xlsx")
        {
            HasPreview = true;
            PreviewValid = false;
            Warnings = new ObservableCollection<string>();
            Errors = new ObservableCollection<string> { Localizer["UnsupportedTemplateFormatMSG"] };
            this.RaisePropertyChanged(nameof(HasWarnings));
            this.RaisePropertyChanged(nameof(HasErrors));
            StatusMessage = Localizer["ImportInvalidMSG"];
            return;
        }

        IsBusy = true;
        StatusMessage = Localizer["ValidatingMSG"];

        try
        {
            var preview = await AssessmentsService.PreviewTemplateAsync(SelectedFilePath, AssessmentName);

            if (preview is null)
            {
                HasPreview = false;
                PreviewValid = false;
                StatusMessage = Localizer["ImportFailedMSG"];
                return;
            }

            Warnings = new ObservableCollection<string>(preview.Warnings);
            Errors = new ObservableCollection<string>(
                preview.Errors.Select(e => e.Row > 0 ? $"{Localizer["Row"]} {e.Row}: {e.Message}" : e.Message));
            this.RaisePropertyChanged(nameof(HasWarnings));
            this.RaisePropertyChanged(nameof(HasErrors));

            HasPreview = true;
            PreviewValid = preview.Valid;

            StatusMessage = preview.Valid
                ? string.Format(Localizer["ImportPreviewSummaryMSG"], preview.PageCount, preview.QuestionCount, preview.AnswerCount)
                : Localizer["ImportInvalidMSG"];
        }
        catch (Exception ex)
        {
            Logger.Error("Error validating assessment template: {0}", ex.Message);
            HasPreview = false;
            PreviewValid = false;
            StatusMessage = Localizer["ImportFailedMSG"];
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ImportAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedFilePath) || !PreviewValid) return;

        IsBusy = true;
        StatusMessage = Localizer["ImportingMSG"];

        try
        {
            var (code, assessment) = await AssessmentsService.ImportTemplateAsync(SelectedFilePath, AssessmentName);

            if (code != 0 || assessment is null)
            {
                StatusMessage = Localizer["ImportFailedMSG"];
                IsBusy = false;
                return;
            }

            Close(new AssessmentImportResult
            {
                Action = ResultActions.Ok,
                ImportedAssessment = assessment
            });
        }
        catch (Exception ex)
        {
            Logger.Error("Error importing assessment template: {0}", ex.Message);
            StatusMessage = Localizer["ImportFailedMSG"];
            IsBusy = false;
        }
    }
}
