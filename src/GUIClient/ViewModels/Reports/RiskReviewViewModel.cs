﻿using System.Collections.ObjectModel;
using GUIClient.Models;
using ReactiveUI;
using System.Reactive;

namespace GUIClient.ViewModels.Reports;

public class RiskReviewViewModel: ViewModelBase
{
    #region LANGUAGE
    public string StrFilters { get; }
    public string StrDaysSinceLastReview { get; }
    public string StrGenerate { get; }
    public string StrData { get; }
    public string StrSubject { get; }
    public string StrStatus { get; }
    public string StrSubmissionDate { get; }
    
    #endregion

    #region PROPERTIES

    private int _daysSinceLastReview = 30;
    public int DaysSinceLastReview {
        get => _daysSinceLastReview;
        set => this.RaiseAndSetIfChanged(ref _daysSinceLastReview, value);
    }
    
    private ObservableCollection<RiskReviewReportItem> _risks;

    public ObservableCollection<RiskReviewReportItem> Risks
    {
        get => _risks;
        set => this.RaiseAndSetIfChanged(ref _risks, value);
    }

    public ReactiveCommand<Unit, Unit> BtGenerateClicked { get; }
    
    #endregion

    public RiskReviewViewModel()
    {
        StrFilters = Localizer["Filters"];
        StrDaysSinceLastReview = Localizer["Days since last review"] + ":";
        StrGenerate = Localizer["Generate"];
        StrData = Localizer["Data"];
        StrSubject = Localizer["Subject"];
        StrStatus = Localizer["Status"];
        StrSubmissionDate = Localizer["SubmissionDate"];
        
        BtGenerateClicked = ReactiveCommand.Create(ExecuteGenerate);
        
    }
    
#region METHODS

    public void ExecuteGenerate()
    {

        return;

    }

#endregion
    
}

