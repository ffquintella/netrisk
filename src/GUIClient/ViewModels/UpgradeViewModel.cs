using ReactiveUI;

namespace GUIClient.ViewModels;

public class UpgradeViewModel: ViewModelBase
{
    private int _progressBarValue = 0;
    public int ProgressBarValue
    {
        get => _progressBarValue;
        set
        {
            this.RaiseAndSetIfChanged(ref _progressBarValue, value);
        }
    }
    public int ProgressBarMaxValue { get; set; } = 100;
    
    private string _operation = " --- ";
    public string Operation
    {
        get => _operation;
        set
        {
            this.RaiseAndSetIfChanged(ref _operation, value);
        }
    }
}