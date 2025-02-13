﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices.JavaScript;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using DAL.Entities;
using GUIClient.Models;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ReactiveUI;
using System.Reactive;
using Avalonia.Controls;
using Model.DTO;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;
using GUIClient.Extensions;
using GUIClient.Models.Events;

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

        private List<Review>? _reviewTypes;

        public List<Review>? ReviewTypes
        {
            get => _reviewTypes;
            set => this.RaiseAndSetIfChanged(ref _reviewTypes, value);
        }
        
        private Review? _selectedReviewType;
        public Review? SelectedReviewType
        {
            get => _selectedReviewType;
            set => this.RaiseAndSetIfChanged(ref _selectedReviewType, value);
        }
        
        private List<NextStep>? _nextSteps;

        public List<NextStep>? NextSteps
        {
            get => _nextSteps;
            set => this.RaiseAndSetIfChanged(ref _nextSteps, value);
        }

        private NextStep? _selectedNextStep;
        public NextStep? SelectedNextStep
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
        
        public event EventHandler<MgmtReviewSavedEventHandlerArgs>? MgmtReviewSaved;
        
    #endregion

    #region PRIVATE FIELDS

        private int _riskId;
        private OperationType _operation;
        
        private readonly IMgmtReviewsService _mgmtReviewsService;
        private readonly IRisksService _risksService;
        private readonly IUsersService _usersService;

        private MgmtReview? _review;
        
        private ReactiveCommand<Unit, Unit> BtSaveClicked { get; }
        private ReactiveCommand<Unit, Unit> BtCancelClicked { get; }

        private Window _baseWindow;
        
    #endregion
    
    public EditMgmtReviewViewModel(OperationType operation, int riskId, Window window)
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
        
        _baseWindow = window;

        _mgmtReviewsService = GetService<IMgmtReviewsService>();
        _risksService = GetService<IRisksService>();
        _usersService = GetService<IUsersService>();
        
        BtSaveClicked = ReactiveCommand.Create(ExecuteSave);
        BtCancelClicked = ReactiveCommand.Create(ExecuteCancel);

        Task.Run(LoadData);
        
    }

    #region METHODS

    private void ExecuteSave()
    {
        Notes ??= "";
        
        var reviewDto = new MgmtReviewDto()
        {
            Comments = Notes,
            Id = 0,
            NextStep = SelectedNextStep!.Value,
            Review = SelectedReviewType!.Value,
            RiskId = _riskId,
            SubmissionDate = SubmissionDate.DateTime,
            NextReview = new DateOnly(NextReview.DateTime.Year, NextReview.DateTime.Month, NextReview.DateTime.Day),
            Reviewer = 0
        };

        try
        {
            var result = _mgmtReviewsService.Create(reviewDto);
            OnReviewSaved(result);
            _baseWindow.Close();
        }
        catch (Exception ex)
        {
            ErrorMsg("100-01:"+ ex.Message);
        }
        

    }

    private void ExecuteCancel()
    {
        _baseWindow.Close();
    }
    
    private void LoadData()
    {
        ReviewTypes = _mgmtReviewsService.GetReviewTypes();
        NextSteps = _mgmtReviewsService.GetNextSteps();

        var riskLevel = _risksService.GetRiskReviewLevel(_riskId);
        NextReview = DateTimeOffset.Now.AddDays(riskLevel.Value);
        
        SubmissionDate = DateTimeOffset.Now;
        
        if(_operation == OperationType.Edit)
            LoadDataForEdit();
    }

    private void LoadDataForEdit()
    {
        var reviews = _risksService.GetRiskMgmtReviews(_riskId);
        _review = reviews.OrderBy(r => r.SubmissionDate).LastOrDefault();

        if (_review == null)
        {
            ErrorMsg(Localizer["ErrorLoadingReviewMSG"]);
            return;
        }
        
        SelectedNextStep = NextSteps!.FirstOrDefault(ns => ns.Value == _review.NextStep)!;
        SelectedReviewType = ReviewTypes!.FirstOrDefault(rt => rt.Value == _review.Review)!;

        var user = _usersService.GetUserName(_review.Reviewer);
        
        Notes = "\n--- " + user + ": " + _review.SubmissionDate.ToString(CultureInfo.InvariantCulture) + " ---\n" + _review.Comments;


    }

    private async void ErrorMsg(string text)
    {
        var msgSelect = MessageBoxManager
            .GetMessageBoxStandard(   new MessageBoxStandardParams
            {
                ContentTitle = Localizer["Error"],
                ContentMessage = Localizer["Error"] + " :" + text ,
                Icon = Icon.Error,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            });

        await msgSelect.ShowWindowDialogAsync(_baseWindow);
    }
    
    protected virtual void OnReviewSaved(MgmtReview review)
    {
        EventHandler<MgmtReviewSavedEventHandlerArgs>? handler = MgmtReviewSaved;
        if (handler != null)
        {
            handler(this, new MgmtReviewSavedEventHandlerArgs(review));
        }

    }

    #endregion
}