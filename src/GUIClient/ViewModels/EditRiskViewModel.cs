using GUIClient.ViewModels.Dialogs.Results;
using GUIClient.ViewModels.Dialogs.Parameters;
using GUIClient.ViewModels.Dialogs;
using GUIClient.Validation;
using GUIClient.Interfaces;
using System.Windows.Input;
using System.Threading;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using Avalonia.Controls;
using ClientServices.Interfaces;
using DAL.Entities;
using GUIClient.Models;
using GUIClient.Tools;
using Tools.String;
using Material.Icons;
using Model.DTO;
using Model.Entities;
using Model.Exceptions;
using Model.Statistics;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace GUIClient.ViewModels;

/// <summary>
/// Creates or edits a risk. Migrated onto the single dialog stack (IX-2), so Esc/Ctrl+S come from
/// <see cref="DialogWindowBase{TResult}"/> instead of the window's own KeyBindings, the size is
/// declared in XAML only (IX-1), and the saved risk travels back as a typed result.
/// </summary>
public class EditRiskViewModel
    : ParameterizedDialogViewModelBaseAsync<RiskDialogResult, RiskDialogParameter>, ISaveableDialog
{
    #region LANGUAGE

    public string StrRisk { get; }
    public string StrOperation { get; }
    private string _strOperationType = "";

    /// <summary>Set at activation time, once the operation is known.</summary>
    public string StrOperationType
    {
        get => _strOperationType;
        private set => this.RaiseAndSetIfChanged(ref _strOperationType, value);
    }
    public string StrImpactTypes { get; }
    public string StrSubject { get; }
    public string StrCtrlNumber { get; }
    public string StrSource { get; }
    public string StrCategory { get; }
    public string StrNotes { get; }
    public string StrOwner { get; }
    public string StrEntity { get; }
    public string StrManager { get; }
    public bool ShowEditFields { get; set; }
    public new string StrSave { get; }
    public new string StrCancel { get; }
    public string StrScoring { get; }
    
    public string StrProbability { get; }
    public string StrImpact { get; }
    public string StrValue { get; }

    /// <summary>Header for the anchor text under each scale choice (Track 8 milestone 8.7.1).</summary>
    public string StrWhatThisLevelMeans { get; }

    #endregion
    
    #region PROPERTIES
    
    private bool _loading;
    
    public bool Loading
    {
        get => _loading;
        set => this.RaiseAndSetIfChanged(ref _loading, value);
    }
    
    private ObservableCollection<Source>? _riskSources;
    
    public ObservableCollection<Source>? RiskSources 
    {
        get => _riskSources;
        set => this.RaiseAndSetIfChanged(ref _riskSources, value);
    }
    
    //public List<Source>? RiskSources { get; }
    
    private ObservableCollection<UserListing> _userListings = new ObservableCollection<UserListing>();
    public ObservableCollection<UserListing> UserListings
    {
        get => _userListings;
        set => this.RaiseAndSetIfChanged(ref _userListings, value);
    }

    private ObservableCollection<Entity>? _entities;

    private ObservableCollection<Entity>? Entities
    {
        get => _entities;
        set => this.RaiseAndSetIfChanged(ref _entities, value);
    }
    public List<ListNode> EntityNodes { get; set; } = new List<ListNode>();
    
    private ObservableCollection<Likelihood>? _probabilities;
    
    public ObservableCollection<Likelihood>? Probabilities
    {
        get => _probabilities;
        set => this.RaiseAndSetIfChanged(ref _probabilities, value);
    }
    
    //public List<Likelihood>? Probabilities { get; set; }
    //public List<Impact>? Impacts { get; }
    
    private ObservableCollection<Impact>? _impacts;
    
    public ObservableCollection<Impact>? Impacts
    {
        get => _impacts;
        set => this.RaiseAndSetIfChanged(ref _impacts, value);
    }
    
    
    
    
    private Source? _selectedRiskSource;
    public Source? SelectedRiskSource
    {
        get => _selectedRiskSource;
        set => this.RaiseAndSetIfChanged(ref _selectedRiskSource, value);
    }
    
    private ObservableCollection<Category> _categories = new ObservableCollection<Category>();

    public ObservableCollection<Category> Categories
    {
        get => _categories;
        set => this.RaiseAndSetIfChanged(ref _categories, value);
    }

    
    private Category? _selectedCategory;
    public Category? SelectedCategory
    {
        get => _selectedCategory;
        set => this.RaiseAndSetIfChanged(ref _selectedCategory, value);
    }

    private bool _isCtrlNumVisible;
    public bool IsCtrlNumVisible
    {
        get => _isCtrlNumVisible;
        set => this.RaiseAndSetIfChanged(ref _isCtrlNumVisible, value);
    }

    private UserListing? _selectedOwner;
    public UserListing? SelectedOwner
    {
        get => _selectedOwner;
        set => this.RaiseAndSetIfChanged(ref _selectedOwner, value);
    }
    
    private ListNode? _selectedEntityNode;
    public ListNode? SelectedEntityNode
    {
        get => _selectedEntityNode;
        set => this.RaiseAndSetIfChanged(ref _selectedEntityNode, value);
    }
    
    private Likelihood? _selectedProbability;
    public Likelihood? SelectedProbability
    {
        get => _selectedProbability;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedProbability, value);
            this.RaisePropertyChanged(nameof(SelectedProbabilityDefinition));
            this.RaisePropertyChanged(nameof(HasProbabilityDefinition));
            CalculateValue();
        }
    }

    private Impact? _selectedImpact;
    public Impact? SelectedImpact
    {
        get => _selectedImpact;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedImpact, value);
            this.RaisePropertyChanged(nameof(SelectedImpactDefinition));
            this.RaisePropertyChanged(nameof(HasImpactDefinition));
            CalculateValue();
        }
    }

    /// <summary>
    /// What the selected likelihood level means, shown next to the choice (Track 8 milestone 8.7.1).
    ///
    /// A five-point scale labelled only "Low/Medium/High" is read differently by different raters —
    /// the finding behind Budescu's work on verbal probability and Cox's 2008 critique of risk
    /// matrices — and two raters who mean different things by "Medium" produce a register that cannot
    /// be aggregated or compared. The anchors live on the scale rows themselves
    /// (<c>likelihood.definition</c>, seeded in version 80), so an installation that rewrites them for
    /// its own risk appetite gets its own wording here with no code change.
    /// </summary>
    public string SelectedProbabilityDefinition => ScaleAnchorFormatter.Describe(
        SelectedProbability?.Definition, SelectedProbability?.ProbabilityMin,
        SelectedProbability?.ProbabilityMax, isProbability: true);

    public bool HasProbabilityDefinition => !string.IsNullOrWhiteSpace(SelectedProbabilityDefinition);

    /// <summary>What the selected impact level means, in words and in money.</summary>
    public string SelectedImpactDefinition => ScaleAnchorFormatter.Describe(
        SelectedImpact?.Definition, SelectedImpact?.ImpactMin, SelectedImpact?.ImpactMax,
        isProbability: false);

    public bool HasImpactDefinition => !string.IsNullOrWhiteSpace(SelectedImpactDefinition);


    private UserListing? _selectedManager;
    public UserListing? SelectedManager
    {
        get => _selectedManager;
        set => this.RaiseAndSetIfChanged(ref _selectedManager, value);
    }
    
    private string? _notes;
    public string? Notes
    {
        get => _notes;
        set => this.RaiseAndSetIfChanged(ref _notes, value);
    }
    
    private string? _value;
    public string? Value
    {
        get => _value;
        set => this.RaiseAndSetIfChanged(ref _value, value);
    }
    
    private ObservableCollection<string> _entitiesNames = new ObservableCollection<string>();
    public ObservableCollection<string> EntitiesNames
    {
        get => _entitiesNames;
        set => this.RaiseAndSetIfChanged(ref _entitiesNames, value);
    }
    
    private string _selectedEntityName = "";
    public string SelectedEntityName
    {
        get => _selectedEntityName;
        set => this.RaiseAndSetIfChanged(ref _selectedEntityName, value);
    }

    private ObservableCollection<RiskCatalog> _riskCatalogs = new ObservableCollection<RiskCatalog>();
    public ObservableCollection<RiskCatalog> RiskCatalogs
    {
        get => _riskCatalogs;
        set => this.RaiseAndSetIfChanged(ref _riskCatalogs, value);
    }
    
    //private ObservableCollection<RiskCatalog> RiskCatalogs { get; } 
    
    private ObservableCollection<RiskCatalog?> _selectedCatalogs = new();
    public ObservableCollection<RiskCatalog?> SelectedCatalogs
    {
        get => _selectedCatalogs;
        set => this.RaiseAndSetIfChanged(ref _selectedCatalogs, value);
    }
    
    private bool _saveEnabled;
    public bool SaveEnabled
    {
        get => _saveEnabled;
        set => this.RaiseAndSetIfChanged(ref _saveEnabled, value);
    }
    
    private string _riskSubject = "";
    public string RiskSubject
    {
        get => _riskSubject;
        set
        {
            Risk.Subject = value;
            this.RaiseAndSetIfChanged(ref _riskSubject, value);
        }
    }
    
    private Risk _risk = new Risk();
    public Risk Risk
    {
        get => _risk;
        set => this.RaiseAndSetIfChanged(ref _risk, value);
    }
    
    #endregion
    
    #region BUTTONS
    
    private RiskScoring? RiskScoring { get; set; }
    public ReactiveCommand<RxVoid, RxVoid> BtSaveClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtCancelClicked { get; }

    /// <inheritdoc />
    public ICommand? SaveCommand => BtSaveClicked;
    
    #endregion
    
    #region FIELDS
    
    private OperationType _operationType;
    private readonly IRisksService _risksService;
    private readonly IEntitiesService _entitiesService;
    private readonly IAuthenticationService _authenticationService;
    private readonly IUsersService _usersService;
    private string _originalSubject = "";
    private EntitiesConfiguration? _entitiesConfiguration;
    
    #endregion

    #region METHODS
    public EditRiskViewModel()
    {
        StrRisk = Localizer["Risk"];
        StrOperation = Localizer["Operation"] + ": ";
        StrSubject = Localizer["Subject"] + ": ";
        StrCtrlNumber = Localizer["CtrlNumber"] + ": ";
        StrSource = Localizer["Source"] + ": ";
        StrCategory = Localizer["Category"]+ ": ";
        StrImpactTypes = Localizer["ImpactTypes"] ;
        StrOwner = Localizer["Owner"] + ":";
        StrManager = Localizer["Manager"] + ":";
        StrNotes = Localizer["Notes"] + ": ";
        StrSave= Localizer["Save"] ;
        StrCancel= Localizer["Cancel"] ;
        StrScoring = Localizer["Scoring"];
        StrProbability = Localizer["Probability"];
        StrImpact = Localizer["Impact"];
        StrValue = Localizer["Value"];
        StrWhatThisLevelMeans = Localizer["WhatThisLevelMeans"];
        StrEntity = Localizer["Entity"];
        
        _risksService = GetService<IRisksService>();
        _entitiesService = GetService<IEntitiesService>();
        _authenticationService = GetService<IAuthenticationService>();
        _usersService = GetService<IUsersService>();
        
        //RiskSources = _risksService.GetRiskSources();
        //Categories = _risksService.GetRiskCategories();
        //RiskCatalogs =  new ObservableCollection<RiskCatalog>(_risksService.GetRiskTypes());
        //UserListings = usersService.ListUsers();
        //Probabilities = _risksService.GetProbabilities();
        //Impacts = _risksService.GetImpacts();
        
        //Entities = _entitiesService.GetAll();



        //if (RiskSources == null) throw new Exception("Unable to load risk list");
        //if (Categories == null) throw new Exception("Unable to load category list");
        //if (RiskCatalogs == null) throw new Exception("Unable to load risk types");
        //if (UserListings == null) throw new Exception("Unable to load user listing");
        //if (Probabilities == null) throw new Exception("Unable to load probability list");
        //if (Impacts == null) throw new Exception("Unable to load impact list");
        
        BtSaveClicked = ReactiveCommand.CreateFromTask(ExecuteSave,
            this.WhenAnyValue(x => x.SaveEnabled));
        BtCancelClicked = ReactiveCommand.Create(ExecuteCancel);
        
        this.ValidationRule(
            viewModel => viewModel.SelectedProbability, 
            prob => prob != null,
            Localizer["PleaseSelectOneMSG"]);
        
        this.ValidationRule(
            viewModel => viewModel.SelectedImpact, 
            impact => impact != null,
            Localizer["PleaseSelectOneMSG"]);
        
        this.ValidationRule(
            viewModel => viewModel.SelectedRiskSource, 
            source => source != null,
            Localizer["PleaseSelectOneMSG"]);
        
        this.ValidationRule(
            viewModel => viewModel.SelectedCategory, 
            category => category != null,
            Localizer["PleaseSelectOneMSG"]);
        
        this.ValidationRule(
            viewModel => viewModel.SelectedOwner, 
            owner => owner != null,
            Localizer["PleaseSelectOneMSG"]);
        
        this.ValidationRule(
            viewModel => viewModel.SelectedManager, 
            manager => manager != null,
            Localizer["PleaseSelectOneMSG"]);
        

        this.ValidationRule(
            viewModel => viewModel.SelectedEntityName,
            name =>
            {
                if (name == null) return false;
                if (name == "") return false;
                return true;
            },
            Localizer["PleaseSelectOneMSG"]
        );
        
        this.ValidationRule(
            viewModel => viewModel.RiskSubject, 
            subject => !string.IsNullOrWhiteSpace(subject),
            Localizer["RiskMustHaveASubjectMSG"]);
        
        IObservable<bool> subjectUnique =
            this.WhenAnyValue(
                x => x.RiskSubject,
                (subject) =>
                {
                    if (_operationType == OperationType.Edit && _originalSubject.TrimEnd() == subject.TrimEnd()) return true;
                    return !_risksService.RiskSubjectExists(subject);
                });
        
        this.ValidationRule(
            vm => vm.RiskSubject,
            subjectUnique,
            "Subject already exists.");
        
        
        this.IsValid()
            .Subscribe(x =>
            {
                SaveEnabled = x;
            });
    }

    /// <inheritdoc />
    public override async Task ActivateAsync(RiskDialogParameter parameter,
        CancellationToken cancellationToken = default)
    {
        if (parameter.Operation == OperationType.Edit && parameter.Risk == null)
        {
            throw new InvalidParameterException("risk", "Risk cannot be null");
        }

        _operationType = parameter.Operation;
        StrOperationType = _operationType == OperationType.Create ? Localizer["Creation"] : Localizer["Edit"];

        if (_operationType == OperationType.Create)
        {
            Risk = new Risk();
            ShowEditFields = false;

            await LoadDataAsync();
        }
        else
        {
            Risk = parameter.Risk!;
            ShowEditFields = true;

            await LoadDataAsync(Risk.Id);
        }
    }

    private async Task LoadDataAsync(int riskId = -1)
    {
        Loading = true;
        
        UserListings = new ObservableCollection<UserListing>(await _usersService.GetAllAsync());
        Entities = new ObservableCollection<Entity>(await _entitiesService.GetAllAsync());
        Categories = new ObservableCollection<Category>(await _risksService.GetRiskCategoriesAsync());
        Probabilities = new ObservableCollection<Likelihood>((await _risksService.GetProbabilitiesAsync())!);
        Impacts = new ObservableCollection<Impact>((await _risksService.GetImpactsAsync())!);
        RiskCatalogs =  new ObservableCollection<RiskCatalog>(await _risksService.GetRiskTypesAsync());
        RiskSources = new ObservableCollection<Source>( (await _risksService.GetRiskSourcesAsync()!)!);
        
        if (riskId != -1)
        {
            RiskScoring = await _risksService.GetRiskScoringAsync(Risk.Id);
        }
        
        _entitiesConfiguration = await _entitiesService.GetEntitiesConfigurationAsync();

        foreach (var entity in Entities!)
        {
            var icon = _entitiesConfiguration!.Definitions[entity.DefinitionName].GetIcon();
            var node = new ListNode(entity.EntitiesProperties.FirstOrDefault(ep => ep.Type == "name")!.Value, entity.Id, icon);
            EntityNodes.Add(node);
        }

        foreach (var node in EntityNodes.OrderBy(en => en.Name))
        {
            EntitiesNames.Add(node.Name + " (" + node.RelatedObjectId + ")");
        }
        
        var emptyNode = new ListNode("---", -1, MaterialIconKind.Error);
        EntityNodes.Add(emptyNode);
        
        if (riskId > 0)
        {
            
            var entityId = _risksService.GetEntityIdFromRisk(riskId);
            if (entityId != null)
            {
                SelectedEntityNode = EntityNodes.FirstOrDefault(en => en.RelatedObjectId == entityId);
                SelectedEntityName = SelectedEntityNode!.Name + " (" + SelectedEntityNode.RelatedObjectId + ")";
            }
            else
            {
                SelectedEntityNode = EntityNodes.FirstOrDefault(en => en.RelatedObjectId == -1);
            }
        }
        else
        {
            SelectedEntityNode = EntityNodes.FirstOrDefault(en => en.RelatedObjectId == -1);
        }
            
        if (riskId != -1)
        {
            IsCtrlNumVisible = true;
            _originalSubject = Risk.Subject;
            RiskSubject = Risk.Subject;
            SelectedRiskSource = RiskSources!.FirstOrDefault(r => r.Value == Risk.Source);
            SelectedCategory = Categories.FirstOrDefault(c => c.Value == Risk.Category);
            
            foreach (var riskCatalog in Risk.RiskCatalogs)
            {
                SelectedCatalogs.Add(RiskCatalogs.FirstOrDefault(r => r.Id == riskCatalog.Id));
            }
            
            SelectedOwner = UserListings.FirstOrDefault(ul => ul.Id == Risk.Owner);
            SelectedManager = UserListings.FirstOrDefault(ul => ul.Id == Risk.Manager);
            Notes = Risk.Notes;

            
            var sp = Probabilities!.FirstOrDefault(p => Math.Abs(p.Value - RiskScoring!.ClassicLikelihood) < 0.01);
            if (sp != null) SelectedProbability = sp;
            var imp = Impacts!.FirstOrDefault(i => Math.Abs(i.Value - RiskScoring!.ClassicImpact) < 0.01);
            if (imp != null) SelectedImpact = imp;
        }
        else
        {
            SelectedImpact = Impacts!.FirstOrDefault(i => i.Value == 1);
            SelectedProbability = Probabilities!.FirstOrDefault(p => p.Value == 1);
            var sowner = UserListings.FirstOrDefault(ul => ul.Id == _authenticationService.AuthenticatedUserInfo!.UserId);
            if (sowner != null) SelectedOwner = sowner;
        }
        
        Loading = false;
        
    }
    
    private void CalculateValue()
    {
        if (_selectedImpact != null && _selectedProbability != null)
            Value = _risksService.GetRiskScore(SelectedProbability!.Value, SelectedImpact!.Value ).ToString("0.00");
        else Value = "0.00";
    }
    
    private async Task ExecuteSave()
    {

        if(SelectedOwner != null)
            Risk.Owner = SelectedOwner.Id;
        if (SelectedManager != null)
            Risk.Manager = SelectedManager.Id;

        if (_operationType == OperationType.Create)
        {
            Risk.Status = "New";
            Risk.SubmissionDate = DateTime.Now;
            if(_authenticationService.AuthenticatedUserInfo!.UserId.HasValue)
                Risk.SubmittedBy = _authenticationService.AuthenticatedUserInfo.UserId.Value;
        }

        Risk.LastUpdate = DateTime.Now;

        if (SelectedCategory != null)
        {
            Risk.Category = SelectedCategory.Value;
            Risk.CategoryNavigation = SelectedCategory;
        }

        if (SelectedRiskSource != null)
        {
            Risk.Source = SelectedRiskSource.Value;
            Risk.SourceNavigation = SelectedRiskSource;
        }

        
        Risk.Notes = Notes ?? "";

        Risk.Assessment = "";
        Risk.RiskCatalogMapping = "";
        Risk.ThreatCatalogMapping = "";
        Risk.ReferenceId = "";
        
        /*foreach (var srt in SelectedRiskTypes)
        {
            Risk.RiskCatalogMapping += srt.Id + ",";
        }*/
        
        Risk.RiskCatalogs.Clear();

        foreach (var riskCatalog in SelectedCatalogs)
        {
            if(riskCatalog!= null) Risk.RiskCatalogs.Add(riskCatalog);
        }

        //Risk.RiskCatalogMapping = Risk.RiskCatalogMapping.TrimEnd(',');

        var riskScoring = new RiskScoring
        {
            ScoringMethod = 1,
            ClassicImpact = SelectedImpact!.Value,
            ClassicLikelihood = SelectedProbability!.Value,
            CalculatedRisk =_risksService.GetRiskScore(SelectedProbability!.Value, SelectedImpact!.Value),
        };

        try
        {
            if (_operationType == OperationType.Create)
            {
                var newRisk = _risksService.CreateRisk(Risk);
                Debug.Assert(newRisk != null, nameof(newRisk) + " != null");
                riskScoring.Id = newRisk.Id;
                _risksService.CreateRiskScoring(riskScoring);

                if (LabelIdParser.TryParseTrailingId(SelectedEntityName, out var entityId))
                {
                    _risksService.AssociateEntityToRisk( newRisk.Id, entityId);
                }
                
            }


            if (_operationType == OperationType.Edit)
            {
                _risksService.SaveRisk(Risk);
                if (RiskScoring != null && Risk.Id != RiskScoring.Id)
                {
                    riskScoring.Id = Risk.Id;
                    _risksService.CreateRiskScoring(riskScoring);
                }
                else
                {
                    if (RiskScoring != null)
                    {
                        RiskScoring.ClassicImpact = SelectedImpact!.Value;
                        RiskScoring.ClassicLikelihood = SelectedProbability!.Value;
                        RiskScoring.CalculatedRisk =
                            _risksService.GetRiskScore(SelectedProbability!.Value, SelectedImpact!.Value);
                        _risksService.SaveRiskScoring(RiskScoring);
                    }
                }
                
                if (LabelIdParser.TryParseTrailingId(SelectedEntityName, out var entityId))
                {
                    _risksService.AssociateEntityToRisk( Risk.Id, entityId);
                }
                
            }

            // IX-4: the dialog closing and the refreshed list are the confirmation; the toast is
            // the optional transient note, not a box the user has to dismiss.
            Toasts.Success(Localizer["SaveOkMSG"]);

            Close(new RiskDialogResult { Action = ResultActions.Ok, SavedRisk = Risk });
        }
        catch (ErrorSavingException ex)
        {

            var errors = "";

            foreach (var error in ex.Result.Errors)
            {
                errors += error + "\n";
            }


            var msgError = MessageBoxManager
                .GetMessageBoxStandard(new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Error"],
                    ContentMessage = Localizer["ErrorCreatingRiskMSG"] + "cd: " + ex.Result.Status + "\nerr: " +
                                     errors + ".",
                    Icon = Icon.Error,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });

            await msgError.ShowAsync();

        }
        catch (Exception ex)
        {
            var msgError = MessageBoxManager
                .GetMessageBoxStandard(new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Error"],
                    ContentMessage = Localizer["ErrorCreatingRiskMSG"] + "ex: " + ex.Message,
                    Icon = Icon.Error,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });

            await msgError.ShowAsync();
        }

    }
    
    private void ExecuteCancel() =>
        Close(new RiskDialogResult { Action = ResultActions.Cancel });
    
    #endregion


}
