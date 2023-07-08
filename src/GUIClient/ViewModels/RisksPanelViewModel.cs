using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reactive;
using DAL.Entities;
using ClientServices.Interfaces;
using ReactiveUI;

namespace GUIClient.ViewModels;

public class RisksPanelViewModel: ViewModelBase
{
    private bool _initialized = false;

    private string StrSubject { get;  }
    private string StrStatus { get;  }
    private string StrSubmissionDate { get;  }
    
    private ObservableCollection<Risk> _risks;

    public ObservableCollection<Risk> Risks
    {
        get => _risks;
        set => this.RaiseAndSetIfChanged(ref _risks, value);
    }
    
    private IRisksService _risksService;
    
    public ReactiveCommand<Unit, Unit> BtTesteClicked { get; }
    
    public RisksPanelViewModel()
    {
        _risksService = GetService<IRisksService>();
        _risks =  new ObservableCollection<Risk>(new List<Risk>());
        BtTesteClicked = ReactiveCommand.Create(ExecuteTest);
        
        StrSubject= Localizer["Subject"];
        StrStatus = Localizer["Status"];
        StrSubmissionDate = Localizer["SubmissionDate"];
    }
    
    public void ExecuteTest()
    {
        //var a = 1;
        return;
        //Environment.Exit(0);
    }
    
    public void Initialize()
    {
        if (!_initialized)
        {
            Risks =  new ObservableCollection<Risk>(_risksService.GetUserRisks());
            //Risks = _risksService.GetUserRisks();
            _initialized = true;
        }
    }
}