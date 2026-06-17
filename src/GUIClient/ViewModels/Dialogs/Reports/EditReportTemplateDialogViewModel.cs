using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using ClientServices.Interfaces;
using GUIClient.Models.Reports;
using GUIClient.Tools;
using GUIClient.ViewModels.Dialogs.Parameters;
using GUIClient.ViewModels.Dialogs.Results;
using ReactiveUI;
using Serilog;

namespace GUIClient.ViewModels.Dialogs.Reports
{
    public class EditReportTemplateDialogViewModel : ParameterizedDialogViewModelBase<EditReportTemplateDialogResult, ReportTemplateNavigationParameter>
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
        private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = false };

        private readonly IReportTemplatesService _reportTemplatesService;

        #region LANGUAGE
        public string StrName => Localizer["Name"];
        public string StrDescription => Localizer["Description"];
        public string StrSections => Localizer["Sections"];
        public string StrBranding => Localizer["Branding"];
        public string StrType => Localizer["Type"];
        public string StrContent => Localizer["Content"];
        public string StrColumns => Localizer["Columns"];
        public string StrPrimaryColor => Localizer["Primary color"];
        public string StrSecondaryColor => Localizer["Secondary color"];
        public string StrFont => Localizer["Font"];
        public string StrLogo => Localizer["Logo"];
        public string StrUploadLogo => Localizer["Upload logo"];
        public string StrClear => Localizer["Clear"];
        public string StrPreview => Localizer["Preview"];
        public string StrAddSection => Localizer["Add section"];
        public string StrRemove => Localizer["Remove"];
        public string StrMoveUp => Localizer["Move up"];
        public string StrMoveDown => Localizer["Move down"];
        public string StrPreset => Localizer["Preset"];
        public string StrApplyPreset => Localizer["Apply preset"];
        public string StrSaveAsCopy => Localizer["Save as copy"];
        #endregion

        #region PROPERTIES

        private string _name = "";
        public string Name { get => _name; set => this.RaiseAndSetIfChanged(ref _name, value); }

        private string? _description;
        public string? Description { get => _description; set => this.RaiseAndSetIfChanged(ref _description, value); }

        public ObservableCollection<ReportSectionEditModel> Sections { get; } = new();

        private ReportSectionEditModel? _selectedSection;
        public ReportSectionEditModel? SelectedSection { get => _selectedSection; set => this.RaiseAndSetIfChanged(ref _selectedSection, value); }

        private string _primaryColor = "#2A3F54";
        public string PrimaryColor { get => _primaryColor; set => this.RaiseAndSetIfChanged(ref _primaryColor, value); }

        private string _secondaryColor = "#1ABB9C";
        public string SecondaryColor { get => _secondaryColor; set => this.RaiseAndSetIfChanged(ref _secondaryColor, value); }

        private string _fontName = "Arial";
        public string FontName { get => _fontName; set => this.RaiseAndSetIfChanged(ref _fontName, value); }

        public ObservableCollection<string> AvailableFonts { get; } =
            new() { "Arial", "Helvetica", "Times New Roman", "Calibri", "Verdana", "Courier New" };

        private string? _logoBase64;
        public string? LogoBase64 { get => _logoBase64; set => this.RaiseAndSetIfChanged(ref _logoBase64, value); }

        private Bitmap? _logoPreview;
        public Bitmap? LogoPreview { get => _logoPreview; set => this.RaiseAndSetIfChanged(ref _logoPreview, value); }

        public ObservableCollection<ReportTemplatePreset> Presets { get; } =
            new(ReportTemplatePresets.All);

        private ReportTemplatePreset? _selectedPreset;
        public ReportTemplatePreset? SelectedPreset { get => _selectedPreset; set => this.RaiseAndSetIfChanged(ref _selectedPreset, value); }

        private Bitmap? _previewImage;
        public Bitmap? PreviewImage { get => _previewImage; set => this.RaiseAndSetIfChanged(ref _previewImage, value); }

        private bool _isPreviewLoading;
        public bool IsPreviewLoading { get => _isPreviewLoading; set => this.RaiseAndSetIfChanged(ref _isPreviewLoading, value); }

        private string? _statusMessage;
        public string? StatusMessage { get => _statusMessage; set => this.RaiseAndSetIfChanged(ref _statusMessage, value); }

        #endregion

        #region COMMANDS
        public ReactiveCommand<Unit, Unit> AddSectionCommand { get; }
        public ReactiveCommand<Unit, Unit> RemoveSectionCommand { get; }
        public ReactiveCommand<Unit, Unit> MoveSectionUpCommand { get; }
        public ReactiveCommand<Unit, Unit> MoveSectionDownCommand { get; }
        public ReactiveCommand<Unit, Unit> UploadLogoCommand { get; }
        public ReactiveCommand<Unit, Unit> ClearLogoCommand { get; }
        public ReactiveCommand<Unit, Unit> ApplyPresetCommand { get; }
        public ReactiveCommand<Unit, Unit> PreviewCommand { get; }
        public ReactiveCommand<Unit, Unit> SaveCommand { get; }
        public ReactiveCommand<Unit, Unit> SaveAsCopyCommand { get; }
        #endregion

        public EditReportTemplateDialogViewModel()
        {
            _reportTemplatesService = GetService<IReportTemplatesService>();

            AddSectionCommand = ReactiveCommand.Create(AddSection);
            RemoveSectionCommand = ReactiveCommand.Create(RemoveSection);
            MoveSectionUpCommand = ReactiveCommand.Create(MoveSectionUp);
            MoveSectionDownCommand = ReactiveCommand.Create(MoveSectionDown);
            UploadLogoCommand = ReactiveCommand.CreateFromTask(UploadLogo);
            ClearLogoCommand = ReactiveCommand.Create(ClearLogo);
            ApplyPresetCommand = ReactiveCommand.Create(ApplyPreset);
            PreviewCommand = ReactiveCommand.CreateFromTask(RefreshPreview);
            SaveCommand = ReactiveCommand.Create(() => Save(false));
            SaveAsCopyCommand = ReactiveCommand.Create(() => Save(true));
        }

        public override void Activate(ReportTemplateNavigationParameter parameter)
        {
            var template = parameter.Template;
            Name = template.Name;
            Description = template.Description;

            var latestVersion = template.Versions.OrderByDescending(v => v.Version).FirstOrDefault();
            if (latestVersion != null)
            {
                LoadLayout(latestVersion.LayoutJson);
                LoadBranding(latestVersion.BrandingJson);
            }
        }

        private void LoadLayout(string? layoutJson)
        {
            Sections.Clear();
            if (string.IsNullOrWhiteSpace(layoutJson)) return;

            try
            {
                var layout = JsonSerializer.Deserialize<ReportLayoutDto>(layoutJson, JsonOptions);
                if (layout?.Sections == null) return;

                foreach (var s in layout.Sections)
                {
                    Sections.Add(new ReportSectionEditModel
                    {
                        Type = NormalizeType(s.Type),
                        Content = s.Content,
                        TableColumnsText = s.TableColumns != null ? string.Join(", ", s.TableColumns) : null
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not parse report template layout JSON; starting empty");
            }
        }

        private void LoadBranding(string? brandingJson)
        {
            if (string.IsNullOrWhiteSpace(brandingJson)) return;

            try
            {
                var branding = JsonSerializer.Deserialize<ReportBrandingDto>(brandingJson, JsonOptions);
                if (branding == null) return;

                PrimaryColor = branding.PrimaryColor;
                SecondaryColor = branding.SecondaryColor;
                FontName = branding.FontName;
                LogoBase64 = branding.LogoBase64;
                LoadLogoPreviewFromBase64();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not parse report template branding JSON; using defaults");
            }
        }

        private static string NormalizeType(string? type)
        {
            if (string.IsNullOrWhiteSpace(type)) return "Text";
            return type.ToLowerInvariant() switch
            {
                "title" => "Title",
                "table" => "Table",
                _ => "Text"
            };
        }

        private void AddSection()
        {
            var section = new ReportSectionEditModel { Type = "Text", Content = "" };
            Sections.Add(section);
            SelectedSection = section;
        }

        private void RemoveSection()
        {
            if (SelectedSection == null) return;
            var index = Sections.IndexOf(SelectedSection);
            Sections.Remove(SelectedSection);
            SelectedSection = Sections.Count == 0 ? null : Sections[Math.Min(index, Sections.Count - 1)];
        }

        private void MoveSectionUp()
        {
            if (SelectedSection == null) return;
            var index = Sections.IndexOf(SelectedSection);
            if (index <= 0) return;
            Sections.Move(index, index - 1);
        }

        private void MoveSectionDown()
        {
            if (SelectedSection == null) return;
            var index = Sections.IndexOf(SelectedSection);
            if (index < 0 || index >= Sections.Count - 1) return;
            Sections.Move(index, index + 1);
        }

        private void ApplyPreset()
        {
            if (SelectedPreset == null) return;

            if (string.IsNullOrWhiteSpace(Name))
                Name = SelectedPreset.Name;
            if (string.IsNullOrWhiteSpace(Description))
                Description = SelectedPreset.Description;

            Sections.Clear();
            foreach (var s in SelectedPreset.Layout.Sections)
            {
                Sections.Add(new ReportSectionEditModel
                {
                    Type = NormalizeType(s.Type),
                    Content = s.Content,
                    TableColumnsText = s.TableColumns != null ? string.Join(", ", s.TableColumns) : null
                });
            }

            PrimaryColor = SelectedPreset.Branding.PrimaryColor;
            SecondaryColor = SelectedPreset.Branding.SecondaryColor;
            FontName = SelectedPreset.Branding.FontName;
        }

        private async Task UploadLogo()
        {
            var window = WindowsManager.AllWindows.Find(w => w is Views.Reports.EditReportTemplateDialog);
            var topLevel = window is null ? null : TopLevel.GetTopLevel(window);
            if (topLevel is null) return;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = StrUploadLogo,
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Images") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif" } }
                }
            });

            if (files.Count == 0) return;

            try
            {
                await using var stream = await files[0].OpenReadAsync();
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms);
                var bytes = ms.ToArray();
                LogoBase64 = Convert.ToBase64String(bytes);
                LoadLogoPreviewFromBase64();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error reading logo image for report template");
            }
        }

        private void ClearLogo()
        {
            LogoBase64 = null;
            LogoPreview = null;
        }

        private void LoadLogoPreviewFromBase64()
        {
            if (string.IsNullOrWhiteSpace(LogoBase64))
            {
                LogoPreview = null;
                return;
            }

            try
            {
                var bytes = Convert.FromBase64String(LogoBase64);
                using var ms = new MemoryStream(bytes);
                LogoPreview = new Bitmap(ms);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not decode logo preview");
                LogoPreview = null;
            }
        }

        private async Task RefreshPreview()
        {
            try
            {
                IsPreviewLoading = true;
                StatusMessage = null;

                var bytes = await _reportTemplatesService.RenderPreviewAsync(
                    BuildLayoutJson(),
                    BuildBrandingJson(),
                    string.IsNullOrWhiteSpace(Name) ? "Report Preview" : Name);

                if (bytes.Length == 0)
                {
                    StatusMessage = Localizer["Preview unavailable"];
                    return;
                }

                using var ms = new MemoryStream(bytes);
                PreviewImage = new Bitmap(ms);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error generating report template preview");
                StatusMessage = Localizer["Preview unavailable"];
            }
            finally
            {
                IsPreviewLoading = false;
            }
        }

        private string BuildLayoutJson()
        {
            var layout = new ReportLayoutDto
            {
                Sections = Sections.Select(s => new ReportSectionDto
                {
                    Type = s.Type,
                    Content = s.Content,
                    TableColumns = s.ToTableColumns()
                }).ToList()
            };
            return JsonSerializer.Serialize(layout, WriteOptions);
        }

        private string BuildBrandingJson()
        {
            var branding = new ReportBrandingDto
            {
                LogoBase64 = LogoBase64,
                PrimaryColor = PrimaryColor,
                SecondaryColor = SecondaryColor,
                FontName = FontName
            };
            return JsonSerializer.Serialize(branding, WriteOptions);
        }

        private void Save(bool asCopy)
        {
            var result = new EditReportTemplateDialogResult
            {
                Name = Name,
                Description = Description ?? "",
                LayoutJson = BuildLayoutJson(),
                BrandingJson = BuildBrandingJson(),
                SaveAsCopy = asCopy,
                Action = ResultActions.Save
            };
            Close(result);
        }
    }
}
