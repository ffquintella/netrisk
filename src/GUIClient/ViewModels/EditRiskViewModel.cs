using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive;
using Avalonia.Controls;
using ClientServices.Interfaces;
using DAL.Entities;
using GUIClient.Models;
using MessageBox.Avalonia.DTO;
using Model.DTO;
using Model.Exceptions;
using ReactiveUI;
using ReactiveUI.Validation.Extensions;
using MessageBox.Avalonia.Enums;

namespace GUIClient.ViewModels;

public class EditRiskViewModel: ViewModelBase
{
    #region LangStrings

    public string StrRisk { get; }
    public string StrOperation { get; }
    public string StrOperationType { get; }
    public string StrRiskType { get; }
    public string StrSubject { get; }
    public string StrSource { get; }
    public string StrCategory { get; }
    public string StrNotes { get; }
    public string StrOwner { get; }
    public string StrManager { get; }
    public bool ShowEditFields { get; }
    public string StrSave { get; }
    public string StrCancel { get; }
    public string StrScoring { get; }
    
    public string StrProbability { get; }
    public string StrImpact { get; }
    public string StrValue { get; }

    #endregion
    
    #region PROPERTIES
    public List<Source>? RiskSources { get; }
    
    public List<UserListing>? UserListings { get; }
    
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
    
    private List<RiskCatalog> RiskTypes { get; }

    private List<RiskCatalog> _selectedRiskTypes;

    private List<RiskCatalog> SelectedRiskTypes
    {
        get => _selectedRiskTypes;
        set => this.RaiseAndSetIfChanged(ref _selectedRiskTypes, value);
    }
    
    private RiskScoring? RiskScoring { get; set; }
    
    public ReactiveCommand<Window, Unit> BtSaveClicked { get; }
    public ReactiveCommand<Window, Unit> BtCancelClicked { get; }
    #endregion
    
    private readonly OperationType _operationType;
    private readonly IRisksService _risksService;
    private readonly IAuthenticationService _authenticationService;
    private readonly IUsersService _usersService;
    private readonly string _originalSubject = "";

    
    public EditRiskViewModel(OperationType operation, Risk? risk = null)
    {
        if (operation == OperationType.Edit && risk == null)
        {
            throw new InvalidParameterException("risk", "Risk cannot be null");
        }

        _selectedRiskTypes = new List<RiskCatalog>();
        
        
        _operationType = operation;
        StrRisk = Localizer["Risk"];
        StrOperation = Localizer["Operation"] + ": ";
        StrSubject = Localizer["Subject"] + ": ";
        StrSource = Localizer["Source"] + ": ";
        StrCategory = Localizer["Category"]+ ": ";
        StrRiskType = Localizer["RiskType"] ;
        StrOwner = Localizer["Owner"] + ":";
        StrManager = Localizer["Manager"] + ":";
        StrNotes = Localizer["Notes"] + ": ";
        StrSave= Localizer["Save"] ;
        StrCancel= Localizer["Cancel"] ;
        StrScoring = Localizer["Scoring"];
        StrProbability = Localizer["Probability"];
        StrImpact = Localizer["Impact"];
        StrValue = Localizer["Value"];
        
        StrOperationType = _operationType == OperationType.Create ? Localizer["Creation"] : Localizer["Edit"];
        
        _risksService = GetService<IRisksService>();
        
        if (_operationType == OperationType.Create)
        {
            Risk = new Risk();
            ShowEditFields = false;
        }
        else
        {
            Risk = risk!;
            RiskScoring = _risksService.GetRiskScoring(Risk.Id);
            ShowEditFields = true;
        }

        SelectedRiskTypes = new List<RiskCatalog>();

        
        _authenticationService = GetService<IAuthenticationService>();
        _usersService = GetService<IUsersService>();

        RiskSources = _risksService.GetRiskSources();
        Categories = _risksService.GetRiskCategories();
        RiskTypes = _risksService.GetRiskTypes();
        UserListings = _usersService.ListUsers();
        Probabilities = _risksService.GetProbabilities();
        Impacts = _risksService.GetImpacts();
        
        if (operation == OperationType.Edit)
        {
            IsCtrlNumVisible = true;
            RiskSubject = risk!.Subject;
            _originalSubject = risk.Subject;
            SelectedRiskSource = RiskSources!.FirstOrDefault(r => r.Value == risk.Source);
            SelectedCategory = Categories!.FirstOrDefault(c => c.Value == risk.Category);
            var ids = risk.RiskCatalogMapping.TrimEnd().Length > 0 ? risk.RiskCatalogMapping.Split(',').Select(int.Parse).ToList() : new List<int>();
            SelectedRiskTypes = ids.Count == 0 ? new List<RiskCatalog>() : RiskTypes.Where(rt => ids.Contains(rt.Id)).ToList();
            SelectedOwner = UserListings!.FirstOrDefault(ul => ul.Id == risk.Owner);
            SelectedManager = UserListings!.FirstOrDefault(ul => ul.Id == risk.Manager);
            Notes = risk.Notes;

            
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



        if (RiskSources == null) throw new Exception("Unable to load risk list");
        if (Categories == null) throw new Exception("Unable to load category list");
        if (RiskTypes == null) throw new Exception("Unable to load risk types");
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
            viewModel => viewModel.RiskSubject, 
            subject => !string.IsNullOrWhiteSpace(subject),
            Localizer["RiskMustHaveASubjectMSG"]);
        
        IObservable<bool> subjectUnique =
            this.WhenAnyValue(
                x => x.RiskSubject,
                (subject) =>
                {
                    if (_operationType == OperationType.Edit && _originalSubject == subject) return true;
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
            Risk.Category = SelectedCategory.Value;
        if (SelectedRiskSource != null)
            Risk.Source = SelectedRiskSource.Value;
        if (Notes != null)
            Risk.Notes = Notes;
        else Risk.Notes = "";

        Risk.Assessment = "";
        Risk.RiskCatalogMapping = "";
        Risk.ThreatCatalogMapping = "";
        Risk.ReferenceId = "";


        foreach (var srt in SelectedRiskTypes)
        {
            Risk.RiskCatalogMapping += srt.Id + ",";
        }

        Risk.RiskCatalogMapping = Risk.RiskCatalogMapping.TrimEnd(',');

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

                
            }

            var msgOk = MessageBox.Avalonia.MessageBoxManager
                .GetMessageBoxStandardWindow(new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Save"],
                    ContentMessage = Localizer["SaveOkMSG"],
                    Icon = Icon.Success,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });

            await msgOk.Show();

            baseWindow.Close();

        }
        catch (ErrorSavingException ex)
        {

            var errors = "";

            foreach (var error in ex.Result.Errors)
            {
                errors += error + "\n";
            }


            var msgError = MessageBox.Avalonia.MessageBoxManager
                .GetMessageBoxStandardWindow(new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Error"],
                    ContentMessage = Localizer["ErrorCreatingRiskMSG"] + "cd: " + ex.Result.Status + "\nerr: " +
                                     errors + ".",
                    Icon = Icon.Error,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });

            await msgError.Show();

        }
        catch (Exception ex)
        {
            var msgError = MessageBox.Avalonia.MessageBoxManager
                .GetMessageBoxStandardWindow(new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Error"],
                    ContentMessage = Localizer["ErrorCreatingRiskMSG"] + "ex: " + ex.Message,
                    Icon = Icon.Error,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });

            await msgError.Show();
        }

    }
    
    private void ExecuteCancel(Window baseWindow)
    {
        baseWindow.Close();
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
}