using System;
using System.Collections.Generic;
using ClientServices.Interfaces;
using DAL.Entities;
using GUIClient.Models;
using ReactiveUI;

namespace GUIClient.ViewModels;

public class EditMgmtReviewViewModel: ViewModelBase
{
    #region LANGUAGE
        public string StrTitle { get; }
        public string StrSubmissionDate { get; }
        public string StrReviewDecision { get; }
        public string StrNextReview { get; }
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

        public List<Review> ReviewTypes { get; set; }
        
        private Review _selectedReviewType;
        public Review SelectedReviewType
        {
            get => _selectedReviewType;
            set => this.RaiseAndSetIfChanged(ref _selectedReviewType, value);
        }
        
    #endregion

    #region PRIVATE FIELDS

        private int? _riskId;
        private OperationType _operation;
        
        private IMgmtReviewsService _mgmtReviewsService;
    #endregion
    
    public EditMgmtReviewViewModel(OperationType operation, int? riskId)
    {
        #region LANGUAGE
            StrTitle = Localizer["Risk Review"];
            StrSubmissionDate = Localizer["SubmissionDate"] ;
            StrReviewDecision = Localizer["ReviewDecision"] ;
            StrNextReview = Localizer["NextReview"] ;
        #endregion
        
        _operation = operation;
        if (operation == OperationType.Edit)
        {
            _riskId = riskId;
        }
        else
        {
            SubmissionDate = DateTimeOffset.Now;
        }

        _mgmtReviewsService = GetService<IMgmtReviewsService>();
        
        ReviewTypes = _mgmtReviewsService.GetReviewTypes();

    }

    #region METHODS

    

    #endregion
}