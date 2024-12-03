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
using Material.Icons;
using Model.DTO;
using Model.Entities;
using Model.Exceptions;
using Model.Statistics;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;
using ReactiveUI;
using ReactiveUI.Validation.Extensions;

namespace GUIClient.ViewModels;

public class EditRiskViewModel: ViewModelBase
{
    #region LANGUAGE

    public string StrRisk { get; }
    public string StrOperation { get; }
    public string StrOperationType { get; }
    public string StrImpactTypes { get; }
    public string StrSubject { get; }
    public string StrSource { get; }
    public string StrCategory { get; }
    public string StrNotes { get; }
    public string StrOwner { get; }
    public string StrEntity { get; }
    public string StrManager { get; }
    public bool ShowEditFields { get; set; }
    public string StrSave { get; }
    public string StrCancel { get; }
    public string StrScoring { get; }
    
    public string StrProbability { get; }
    public string StrImpact { get; }
    public string StrValue { get; }

    #endregion
    
    #region PROPERTIES
    public List<Source>? RiskSources { get; }
    
    private List<UserListing> _userListings = new List<UserListing>();
    public List<UserListing> UserListings
    {
        get => _userListings;
        set => this.RaiseAndSetIfChanged(ref _userListings, value);
    }
    private List<Entity>? Entities { get; set; }
    private List<ListNode> EntityNodes { get; set; } = new List<ListNode>();
    public List<Likelihood>? Probabilities { get; }
    public List<Impact>? Impacts { get; }
    
    private Source? _selectedRiskSource;
    public Source? SelectedRiskSource
    {
        get => _selectedRiskSource;
        set => this.RaiseAndSetIfChanged(ref _selectedRiskSource, value);
    }
    public List<Category>? Categories { get; }

    
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
            CalculateValue();
        }
    }

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
    
    private ObservableCollection<RiskCatalog> RiskCatalogs { get; } 
    
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
    public ReactiveCommand<Window, Unit> BtSaveClicked { get; }
    public ReactiveCommand<Window, Unit> BtCancelClicked { get; }
    
    #endregion
    
    #region FIELDS
    
    private readonly OperationType _operationType;
    private readonly IRisksService _risksService;
    private readonly IEntitiesService _entitiesService;
    private readonly IAuthenticationService _authenticationService;
    private readonly IUsersService _usersService;
    private string _originalSubject = "";
    private EntitiesConfiguration? _entitiesConfiguration;
    
    #endregion

    #region METHODS
    public EditRiskViewModel(OperationType operation, Risk? risk = null)
    {
        if (operation == OperationType.Edit && risk == null)
        {
            throw new InvalidParameterException("risk", "Risk cannot be null");
        }
        
        _operationType = operation;
        StrRisk = Localizer["Risk"];
        StrOperation = Localizer["Operation"] + ": ";
        StrSubject = Localizer["Subject"] + ": ";
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
        StrEntity = Localizer["Entity"];
        
        StrOperationType = _operationType == OperationType.Create ? Localizer["Creation"] : Localizer["Edit"];
        
        _risksService = GetService<IRisksService>();
        _entitiesService = GetService<IEntitiesService>();
        _authenticationService = GetService<IAuthenticationService>();
        _usersService = GetService<IUsersService>();
        
        
        if (_operationType == OperationType.Create)
        {
            Risk = new Risk();
            ShowEditFields = false;
        }
        else
        {
            Risk = risk!;
            ShowEditFields = true;
        }
        
        
        RiskSources = _risksService.GetRiskSources();
        Categories = _risksService.GetRiskCategories();
        RiskCatalogs =  new ObservableCollection<RiskCatalog>(_risksService.GetRiskTypes());
        //UserListings = usersService.ListUsers();
        Probabilities = _risksService.GetProbabilities();
        Impacts = _risksService.GetImpacts();
        
        //Entities = _entitiesService.GetAll();


        if (operation == OperationType.Edit)
        {
            LoadDataAsync(Risk.Id);
        }
        else
        {
            LoadDataAsync();
        }
        
        if (RiskSources == null) throw new Exception("Unable to load risk list");
        if (Categories == null) throw new Exception("Unable to load category list");
        if (RiskCatalogs == null) throw new Exception("Unable to load risk types");
        if (UserListings == null) throw new Exception("Unable to load user listing");
        if (Probabilities == null) throw new Exception("Unable to load probability list");
        if (Impacts == null) throw new Exception("Unable to load impact list");
        
        BtSaveClicked = ReactiveCommand.Create<Window>(ExecuteSave);
        BtCancelClicked = ReactiveCommand.Create<Window>(ExecuteCancel);
        
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

    private async void LoadDataAsync(int riskId = -1)
    {
        
        UserListings = await _usersService.GetAllAsync();
        Entities = await _entitiesService.GetAllAsync();
        
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
            SelectedCategory = Categories!.FirstOrDefault(c => c.Value == Risk.Category);
            
            foreach (var riskCatalog in Risk.RiskCatalogs)
            {
                SelectedCatalogs.Add(RiskCatalogs.FirstOrDefault(r => r.Id == riskCatalog.Id));
            }
            
            SelectedOwner = UserListings!.FirstOrDefault(ul => ul.Id == Risk.Owner);
            SelectedManager = UserListings!.FirstOrDefault(ul => ul.Id == Risk.Manager);
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
            var sowner = UserListings!.FirstOrDefault(ul => ul.Id == _authenticationService.AuthenticatedUserInfo!.UserId);
            if (sowner != null) SelectedOwner = sowner;
        }
        
        
    }
    
    private void CalculateValue()
    {
        if (_selectedImpact != null && _selectedProbability != null)
            Value = _risksService.GetRiskScore(SelectedProbability!.Value, SelectedImpact!.Value ).ToString("0.00");
        else Value = "0.00";
    }
    
    private async void ExecuteSave(Window baseWindow)
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

                if (SelectedEntityName != "")
                {
                    var strId = SelectedEntityName.Split('(')[1].TrimEnd(')');
                    var entityId = int.Parse(strId);
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
                
                if (SelectedEntityName != "")
                {
                    var strId = SelectedEntityName.Split('(')[1].TrimEnd(')');
                    var entityId = int.Parse(strId);
                    _risksService.AssociateEntityToRisk( Risk.Id, entityId);
                }
                
            }

            var msgOk = MessageBoxManager
                .GetMessageBoxStandard(new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Save"],
                    ContentMessage = Localizer["SaveOkMSG"],
                    Icon = Icon.Success,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });

            await msgOk.ShowAsync();

            baseWindow.Close();

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
    
    private void ExecuteCancel(Window baseWindow)
    {
        baseWindow.Close();
    }
    
    #endregion


}