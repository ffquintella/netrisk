using System;
using System.Collections.Generic;
using System.Runtime.InteropServices.JavaScript;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using DAL.Entities;
using GUIClient.Models;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ReactiveUI;

namespace GUIClient.ViewModels;

public class EditMgmtReviewViewModel: ViewModelBase
{
    #region LANGUAGE
        public string StrTitle { get; }
        public string StrSubmissionDate { get; }
        public string StrReviewDecision { get; }
        public string StrNextReview { get; }
        public string StrAction { get; }
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
        
    #endregion

    #region PRIVATE FIELDS

        private int _riskId;
        private OperationType _operation;
        
        private readonly IMgmtReviewsService _mgmtReviewsService;
        private readonly IRisksService _risksService;
    #endregion
    
    public EditMgmtReviewViewModel(OperationType operation, int riskId)
    {
        #region LANGUAGE
            StrTitle = Localizer["Risk Review"];
            StrSubmissionDate = Localizer["SubmissionDate"] ;
            StrReviewDecision = Localizer["ReviewDecision"] ;
            StrNextReview = Localizer["NextReview"] ;
            StrAction = Localizer["Action"] ;
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

        Task.Run(LoadData);

        

    }

    #region METHODS

    private void LoadData()
    {
        ReviewTypes = _mgmtReviewsService.GetReviewTypes();

        var riskLevel = _risksService.GetRiskReviewLevel(_riskId);
        NextReview = DateTimeOffset.Now.AddDays(riskLevel.Value);
    }

    #endregion
}