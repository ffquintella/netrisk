using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using DAL.Entities;
using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Parameters;
using GUIClient.ViewModels.Dialogs.Results;
using ReactiveUI;

namespace GUIClient.ViewModels;

public class FixRequestDialogViewModel: ParameterizedDialogViewModelBaseAsync<FixRequestDialogResult, FixRequestDialogParameter>
{
    
    #region LANGUAGES
    public string StrVulnerabilitiesFixRequest { get;  } = Localizer["Vulnerability Fix Request"];
    public string StrCommunicationType { get;  } = Localizer["Communication Type"];
    public string StrAutomatic { get;  } = Localizer["Automatic"];
    public string StrManual { get;  } = Localizer["Manual"];
    public string StrVulnerability { get;  } = Localizer["Vulnerability"];
    public string StrTitle { get;  } = Localizer["Title"];
    public string StrScore { get;  } = Localizer["Score"];
    public string StrSolution { get;  } = Localizer["Solution"];
    public string StrComments { get;  } = Localizer["Comments"];
    public string StrFixTeam { get;  } = Localizer["FixTeam"];
    public string StrDestination { get;  } = Localizer["Destination"];
    public string StrSend { get;  } = Localizer["Send"];
    public string StrCancel { get;  } = Localizer["Cancel"];
    #endregion
    
    #region PROPERTIES

    private bool _isAutomatic = true;
    public bool IsAutomatic
    {
        get => _isAutomatic;
        set => this.RaiseAndSetIfChanged(ref _isAutomatic, value);
    }
    private bool _isManual;
    public bool IsManual
    {
        get => _isManual;
        set => this.RaiseAndSetIfChanged(ref _isManual, value);
    }
    private string _vulnerability = String.Empty;
    public string Vulnerability
    {
        get => _vulnerability;
        set => this.RaiseAndSetIfChanged(ref _vulnerability, value);
    }
    private float _score;
    public float Score
    {
        get => _score;
        set => this.RaiseAndSetIfChanged(ref _score, value);
    }
    private string _solution = String.Empty;
    public string Solution
    {
        get => _solution;
        set => this.RaiseAndSetIfChanged(ref _solution, value);
    }
    private string _comments = String.Empty;
    public string Comments
    {
        get => _comments;
        set => this.RaiseAndSetIfChanged(ref _comments, value);
    }
    
    private ObservableCollection<Team> _teams = new();
    public ObservableCollection<Team> Teams
    {
        get => _teams;
        set => this.RaiseAndSetIfChanged(ref _teams, value);
    }
    
    private Team? _selectedTeam;
    public Team? SelectedTeam
    {
        get => _selectedTeam;
        set => this.RaiseAndSetIfChanged(ref _selectedTeam, value);
    }
    private string _destination = String.Empty;
    public string Destination
    {
        get => _destination;
        set => this.RaiseAndSetIfChanged(ref _destination, value);
    }
    
    #endregion
    
    #region SERVICES

    private ITeamsService TeamsService { get; } = GetService<ITeamsService>();
    
    #endregion
    
    #region CONSTRUCTOR

    public FixRequestDialogViewModel()
    {
        this.ValidationRule(
            viewModel => viewModel.Destination, 
            email =>
            {
                if(IsAutomatic)
                    return true;
                return !string.IsNullOrWhiteSpace(email);
            },
            Localizer["PleaseEnterAValueMSG"]);
        
        IObservable<bool> emailValid =
            this.WhenAnyValue(
                x => x.Destination,
                (email) =>
                {
                    if(IsAutomatic)
                        return true;
                    Regex regex = new Regex(@"^([\w\.\-]+)@([\w\-]+)((\.(\w){2,3})+)$");
                    Match match = regex.Match(email);
                    if (match.Success)
                        return true;
                    else
                        return false;

                });
        
        this.ValidationRule(
            viewModel => viewModel.Destination,
            emailValid,
            Localizer["EnterAValidEmailMSG"]);
    }
    
    #endregion
    
    #region METHODS
    public override Task ActivateAsync(FixRequestDialogParameter parameter, CancellationToken cancellationToken = default)
    {
        return Task.Run( async () =>
        {
            if(parameter.Vulnerability == null)
                throw new ArgumentNullException(nameof(parameter.Vulnerability));
            Vulnerability = parameter.Vulnerability;
            Score = parameter.Score;
            if(parameter.Solution != null)
                Solution = parameter.Solution;
            if(parameter.Comments != null)
                Comments = parameter.Comments;
            
            Teams = new ObservableCollection<Team>(await TeamsService.GetAllAsync());
            
            SelectedTeam = Teams.FirstOrDefault(t => t.Value == parameter.FixTeamId);
            
        }, cancellationToken);
        
    }
    public void Send()
    {
        var result = new FixRequestDialogResult()
        {
            Action = ResultActions.Send,
            Comments = Comments,
            FixTeamId = SelectedTeam?.Value,
        };
        
        if(IsAutomatic)
            result.SendToAll = true;
        else
        {
            result.SendToAll = false;
            result.SendTo = Destination;
        }
        
        Close(result);
    }
    public void Cancel()
    {
        Close(new FixRequestDialogResult()
        {
            Action = ResultActions.Cancel
        });
    }
    
    #endregion
}