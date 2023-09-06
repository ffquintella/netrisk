﻿using System;
using System.Collections.Generic;
using System.Runtime.InteropServices.JavaScript;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using DAL.Entities;
using GUIClient.Models;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ReactiveUI;
using System.Reactive;

namespace GUIClient.ViewModels;

public class EditMgmtReviewViewModel: ViewModelBase
{
    #region LANGUAGE
        public string StrTitle { get; }
        public string StrSubmissionDate { get; }
        public string StrReviewDecision { get; }
        public string StrNextReview { get; }
        public string StrAction { get; }
        public string StrNotes { get; }
        public string StrSave { get; }
        public string StrCancel { get; }
    #endregion

    #region PROPERTIES
    
        private DateTimeOffset _submissionDate;
        public DateTimeOffset SubmissionDate
        {
            get => _submissionDate;
            set => this.RaiseAndSetIfChanged(ref _submissionDate, value);
        }
        
        private DateTimeOffset _nextReview;
        public DateTimeOffset NextReview
        {
            get => _nextReview;
            set => this.RaiseAndSetIfChanged(ref _nextReview, value);
        }

        private List<Review> _reviewTypes;

        public List<Review> ReviewTypes
        {
            get => _reviewTypes;
            set => this.RaiseAndSetIfChanged(ref _reviewTypes, value);
        }
        
        private Review _selectedReviewType;
        public Review SelectedReviewType
        {
            get => _selectedReviewType;
            set => this.RaiseAndSetIfChanged(ref _selectedReviewType, value);
        }
        
        private List<NextStep> _nextSteps;

        public List<NextStep> NextSteps
        {
            get => _nextSteps;
            set => this.RaiseAndSetIfChanged(ref _nextSteps, value);
        }

        private NextStep _selectedNextStep;
        public NextStep SelectedNextStep
        {
            get => _selectedNextStep;
            set => this.RaiseAndSetIfChanged(ref _selectedNextStep, value);
        }

        private string? _notes;
        public string? Notes
        {
            get => _notes;
            set => this.RaiseAndSetIfChanged(ref _notes, value);
        }

        private bool _saveEnabled = true;
        public bool SaveEnabled
        {
            get => _saveEnabled;
            set => this.RaiseAndSetIfChanged(ref _saveEnabled, value);
        }
        
    #endregion

    #region PRIVATE FIELDS

        private int _riskId;
        private OperationType _operation;
        
        private readonly IMgmtReviewsService _mgmtReviewsService;
        private readonly IRisksService _risksService;
        
        private ReactiveCommand<Unit, Unit> BtSaveClicked { get; }
        private ReactiveCommand<Unit, Unit> BtCancelClicked { get; }
        
    #endregion
    
    public EditMgmtReviewViewModel(OperationType operation, int riskId)
    {
        #region LANGUAGE
            StrTitle = Localizer["Risk Review"];
            StrSubmissionDate = Localizer["SubmissionDate"] ;
            StrReviewDecision = Localizer["ReviewDecision"] ;
            StrNextReview = Localizer["NextReview"] ;
            StrAction = Localizer["Action"] ;
            StrNotes = Localizer["Notes"] ;
            StrSave = Localizer["Save"] ;
            StrCancel = Localizer["Cancel"] ;
        #endregion
        
        _operation = operation;
        _riskId = riskId;
        
        if (operation == OperationType.Edit)
        {
            
        }
        else
        {
            SubmissionDate = DateTimeOffset.Now;
        }

        _mgmtReviewsService = GetService<IMgmtReviewsService>();
        _risksService = GetService<IRisksService>();
        
        BtSaveClicked = ReactiveCommand.Create(ExecuteSave);
        BtCancelClicked = ReactiveCommand.Create(ExecuteCancel);

        Task.Run(LoadData);

        

    }

    #region METHODS

    private void ExecuteSave()
    {
        
    }

    private void ExecuteCancel()
    {
        
    }
    
    private void LoadData()
    {
        ReviewTypes = _mgmtReviewsService.GetReviewTypes();
        NextSteps = _mgmtReviewsService.GetNextSteps();

        var riskLevel = _risksService.GetRiskReviewLevel(_riskId);
        NextReview = DateTimeOffset.Now.AddDays(riskLevel.Value);
    }

    #endregion
}