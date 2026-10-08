using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using DAL.Entities;
using GUIClient.Tools.Track9;
using Model.Authentication;
using ReactiveUI;

namespace GUIClient.ViewModels.Track9;

public sealed class Track9Choice(object value, string name)
{
    public object Value { get; } = value;
    public string Name { get; } = name;
}

public class Track9RiskChoice : ReactiveObject
{
    private bool _isSelected;

    public Track9RiskChoice(int id, string referenceId, string subject, int? mitigationId = null)
    {
        Id = id;
        ReferenceId = referenceId;
        Subject = subject;
        MitigationId = mitigationId;
    }

    public int Id { get; }
    public string ReferenceId { get; }
    public string Subject { get; }
    public int? MitigationId { get; }
    public string DisplayName => string.IsNullOrWhiteSpace(ReferenceId) ? Subject : $"{ReferenceId} — {Subject}";

    public bool IsSelected
    {
        get => _isSelected;
        set => this.RaiseAndSetIfChanged(ref _isSelected, value);
    }
}

/// <summary>Common error, permission and selector behavior for the Stage 9.5–9.7 panels.</summary>
public abstract class Track9RiskViewModelBase : ViewModelBase
{
    protected IRisksService RisksService { get; } = GetService<IRisksService>();

    private string? _loadError;
    public string? LoadError
    {
        get => _loadError;
        protected set
        {
            this.RaiseAndSetIfChanged(ref _loadError, value);
            this.RaisePropertyChanged(nameof(HasLoadError));
        }
    }

    public bool HasLoadError => !string.IsNullOrWhiteSpace(LoadError);

    private string? _validationMessage;
    public string? ValidationMessage
    {
        get => _validationMessage;
        protected set
        {
            this.RaiseAndSetIfChanged(ref _validationMessage, value);
            this.RaisePropertyChanged(nameof(HasValidationMessage));
        }
    }

    public bool HasValidationMessage => !string.IsNullOrWhiteSpace(ValidationMessage);

    public AuthenticatedUserInfo? User => AuthenticationService.AuthenticatedUserInfo;
    public bool IsThirdLine => Track9RiskPresentation.IsThirdLine(User);
    public string WriteDisabledReason => Localizer[IsThirdLine ? "Track9ThirdLineReadOnly" : "Track9ReadOnly"].Value;

    public string StrReload { get; } = Localizer["Reload"];
    public string StrSaveChanges { get; } = Localizer["Save"];
    public string StrDelete { get; } = Localizer["Delete"];
    public string StrCalculate { get; } = Localizer["Track9Calculate"];
    public string StrNoData { get; } = Localizer["Track9NoData"];
    public string StrNotAvailable { get; } = Localizer["Track9NotAvailable"];

    protected async Task ReadAsync(string operation, Func<Task> read, Func<bool>? isCurrent = null)
    {
        try
        {
            if (isCurrent?.Invoke() is not false) LoadError = null;
            await WithBusyAsync(read);
        }
        catch (Exception ex)
        {
            if (isCurrent?.Invoke() == false) return;
            Logger.Warning(ex, "Could not load {Track9Operation}", operation);
            LoadError = ExplainError(ex);
            Toasts.Error(LoadError);
        }
    }

    protected static List<Track9RiskChoice> ToRiskChoices(IEnumerable<Risk> risks) => risks
        .OrderBy(r => r.ReferenceId, StringComparer.CurrentCultureIgnoreCase)
        .ThenBy(r => r.Subject, StringComparer.CurrentCultureIgnoreCase)
        .Select(r => new Track9RiskChoice(r.Id, r.ReferenceId ?? string.Empty, r.Subject, r.MitigationId))
        .ToList();

    protected string LocalizedEnum<T>(T value) where T : struct, Enum =>
        Localizer[Track9RiskPresentation.EnumKey(value)].Value;
}
