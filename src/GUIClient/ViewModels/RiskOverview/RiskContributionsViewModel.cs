using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using GUIClient.Tools.RiskIndex;
using Model.Exceptions;
using Model.RiskIndex;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace GUIClient.ViewModels.RiskOverview;

/// <summary>One line of the contributions; it only navigates — to the object's detail (§5.8 "cada linha só navega").</summary>
public sealed class RiskContributionRowViewModel(ContributionRow row, ReactiveCommand<RiskContributionRowViewModel, RxVoid> open)
{
    public ContributionRow Row { get; } = row;
    public string Object => Row.Object;
    public string? ObjectHint => Row.ObjectHint;
    public bool IsRedacted => Row.IsRedacted;
    public string Category => Row.Category;
    public string Points => Row.Points;
    public string Share => Row.Share;
    public string Branch => Row.Branch;
    public string Sensitivity => Row.Sensitivity;
    public string NextHolder => Row.NextHolder;
    public string? NextHolderHint => Row.NextHolderHint;
    public string Decision => Row.Decision;

    /// <summary>The sortable columns' values (§5.8: Objeto, Categoria, Pontos). The sensitivity has none.</summary>
    public string ObjectSort => Row.ObjectSort;
    public int CategoryOrder => Row.CategoryOrder;
    public double PointsValue => Row.PointsValue;

    public ReactiveCommand<RiskContributionRowViewModel, RxVoid> Open { get; } = open;
}

/// <summary>
/// "Contribuições e sensibilidade do indicador" (S40 §5.8; T255): the snapshot's top contributions of the page's node with
/// their branch ("piso: sustentado", média, presunção), the next object that would hold the floor, the index's change if
/// the object's reading cleared and the read-only decision state, under the R-1 notice. No action: a line only navigates.
/// </summary>
public class RiskContributionsViewModel : ViewModelBase
{
    #region LANGUAGE

    public string StrTitle { get; } = Localizer["RiskContributionsTitle"];
    public string StrR1Notice { get; } = Localizer["RiskR1NoticeMSG"];
    public string StrColObject { get; } = Localizer["RiskContributionsColObject"];
    public string StrColCategory { get; } = Localizer["RiskContributionsColCategory"];
    public string StrColPoints { get; } = Localizer["RiskContributionsColPoints"];
    public string StrColShare { get; } = Localizer["RiskContributionsColShare"];
    public string StrColBranch { get; } = Localizer["RiskContributionsColBranch"];
    public string StrColSensitivity { get; } = Localizer["RiskContributionsColSensitivity"];
    public string StrColNextHolder { get; } = Localizer["RiskContributionsColNextHolder"];
    public string StrColDecision { get; } = Localizer["RiskContributionsColDecision"];
    public string StrSensitivityHint { get; } = Localizer["RiskContributionsSensitivityHint"];
    public string StrEmpty { get; } = Localizer["RiskContributionsEmpty"];
    public string StrOpenDetailHint { get; } = Localizer["RiskObjectsOpenDetailHint"];
    public string StrRetry { get; } = Localizer["RiskOverviewRetry"];
    private string StrLoadError { get; } = Localizer["RiskOverviewLoadError"];

    #endregion

    private static TextLookup Text => RiskHeadlineViewModel.Text;

    private IRiskIndexService RiskIndexService { get; } = GetService<IRiskIndexService>();

    private readonly Func<RiskIndexObjectLabelDto, Task> _openDetail;
    private string? _node;
    private DateOnly _date;
    private SemaphoreSlim _gate = new(1, 1);
    private CancellationToken _token;
    private int _generation;

    public RiskContributionsViewModel(Func<RiskIndexObjectLabelDto, Task> openDetail)
    {
        _openDetail = openDetail;
        BtRetryClicked = ReactiveCommand.CreateFromTask(ReadAsync);
        OpenRow = ReactiveCommand.CreateFromTask<RiskContributionRowViewModel>(row =>
            row is null ? Task.CompletedTask : _openDetail(row.Row.Source.Object));
    }

    public ReactiveCommand<RxVoid, RxVoid> BtRetryClicked { get; }

    public ReactiveCommand<RiskContributionRowViewModel, RxVoid> OpenRow { get; }

    #region PROPERTIES

    private bool _isActive;
    public bool IsActive { get => _isActive; private set => this.RaiseAndSetIfChanged(ref _isActive, value); }

    private bool _isExpanded = true;
    public bool IsExpanded { get => _isExpanded; set => this.RaiseAndSetIfChanged(ref _isExpanded, value); }

    public ObservableCollection<RiskContributionRowViewModel> Rows { get; } = [];

    private string _registerWeightText = "";
    public string RegisterWeightText
    {
        get => _registerWeightText;
        private set => this.RaiseAndSetIfChanged(ref _registerWeightText, value);
    }

    private bool _isEmpty;
    public bool IsEmpty { get => _isEmpty; private set => this.RaiseAndSetIfChanged(ref _isEmpty, value); }

    private bool _hasTable;
    public bool HasTable { get => _hasTable; private set => this.RaiseAndSetIfChanged(ref _hasTable, value); }

    private string? _errorMessage;
    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            this.RaiseAndSetIfChanged(ref _errorMessage, value);
            this.RaisePropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    #endregion

    public void Clear()
    {
        _generation++;
        _node = null;
        IsActive = false;
        Rows.Clear();
        RegisterWeightText = "";
        IsEmpty = HasTable = false;
        ErrorMessage = null;
    }

    /// <summary>A new page: the node's contributions on the snapshot shown, through the page's gate.</summary>
    public Task LoadAsync(string node, DateOnly date, SemaphoreSlim gate, CancellationToken token)
    {
        _node = node;
        _date = date;
        _gate = gate;
        _token = token;
        IsActive = true;
        return ReadAsync();
    }

    private async Task ReadAsync()
    {
        if (_node is not { } node) return;
        var generation = ++_generation;
        var token = _token;
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await _gate.WaitAsync(token);
            RiskIndexContributionsDto contributions;
            try
            {
                contributions = await RiskIndexService.GetContributionsAsync(node, _date, null, token);
            }
            finally
            {
                _gate.Release();
            }

            if (generation != _generation || token.IsCancellationRequested) return;
            var culture = CultureInfo.CurrentCulture;
            Rows.Clear();
            foreach (var row in ContributionFormatter.Rows(contributions, Text, culture))
                Rows.Add(new RiskContributionRowViewModel(row, OpenRow));
            RegisterWeightText = ContributionFormatter.RegisterWeight(contributions, Text, culture);
            IsEmpty = Rows.Count == 0;
            HasTable = Rows.Count > 0;
        }
        catch (Exception ex) when (generation == _generation && !token.IsCancellationRequested)
        {
            Logger.Error(ex, "Error loading the risk index contributions");
            Rows.Clear();
            RegisterWeightText = "";
            IsEmpty = HasTable = false;
            ErrorMessage = ex is InvalidHttpRequestException { ErrorCode: { Length: > 0 } code } refusal
                ? RiskApiErrorText.Translate(code, refusal.Message, Text, StrLoadError)
                : StrLoadError;
        }
        catch (Exception ex)
        {
            // Superseded by a newer read or a new page: nothing to show, but the failure is still recorded.
            Logger.Debug("A superseded read of the contributions ended: {Message}", ex.Message);
        }
        finally
        {
            if (generation == _generation) IsBusy = false;
        }
    }
}
