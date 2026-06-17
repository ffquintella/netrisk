using System.Linq;
using System.Reactive;
using Avalonia.Controls;
using DAL.Entities;
using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Parameters;
using GUIClient.ViewModels.Dialogs.Results;
using ReactiveUI;

namespace GUIClient.ViewModels.Dialogs.Reports
{
    public class EditReportTemplateDialogViewModel : ParameterizedDialogViewModelBase<EditReportTemplateDialogResult, ReportTemplateNavigationParameter>
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string LayoutJson { get; set; }
        public string BrandingJson { get; set; }

        public ReactiveCommand<Unit, Unit> SaveCommand { get; }

        public EditReportTemplateDialogViewModel()
        {
            SaveCommand = ReactiveCommand.Create(Save);
        }

        public override void Activate(ReportTemplateNavigationParameter parameter)
        {
            var template = parameter.Template;
            Name = template.Name;
            Description = template.Description;

            var latestVersion = template.Versions.OrderByDescending(v => v.Version).FirstOrDefault();
            if (latestVersion != null)
            {
                LayoutJson = latestVersion.LayoutJson;
                BrandingJson = latestVersion.BrandingJson;
            }
        }

        private void Save()
        {
            var result = new EditReportTemplateDialogResult
            {
                Name = Name,
                Description = Description,
                LayoutJson = LayoutJson,
                BrandingJson = BrandingJson,
                Action = ResultActions.Save
            };
            Close(result);
        }
    }
}
}
