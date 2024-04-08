using System;
using System.Threading;
using System.Threading.Tasks;
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
    
    #endregion
    
    #region SERVICES
    
    #endregion
    
    #region METHODS
    public override Task ActivateAsync(FixRequestDialogParameter parameter, CancellationToken cancellationToken = default)
    {
        return Task.Run( () =>
        {
            if(parameter.Vulnerability == null)
                throw new ArgumentNullException(nameof(parameter.Vulnerability));
            Vulnerability = parameter.Vulnerability;
            Score = parameter.Score;
            if(parameter.Solution != null)
                Solution = parameter.Solution;
            if(parameter.Comments != null)
                Comments = parameter.Comments;
        }, cancellationToken);
        
    }
    
    #endregion
}