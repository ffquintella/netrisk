using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using GUIClient.ViewModels;

namespace GUIClient.Views;

public partial class AssessmentView : UserControl
{
    public static readonly StyledProperty<Window> ParentWindowProperty =
        AvaloniaProperty.Register<AssessmentView, Window>(nameof(MainWindow));

    public Window ParentWindow
    {
        get { return GetValue(ParentWindowProperty); }
        set { SetValue(ParentWindowProperty, value); }
    }
    
    public AssessmentView()
    {
        DataContext = new AssessmentViewModel();
        
        InitializeComponent();
        
        /*var lstBox = this.FindControl<ListBox>("LstAssessments");
        var cntx = DataContext as AssessmentViewModel;
        cntx.ListBox = lstBox;*/
        //DataContext = cntx;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}