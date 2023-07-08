using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GUIClient.Views;

public partial class AssessmentQuestionView : Window
{
    public AssessmentQuestionView()
    {
        //DataContext = new AssessmentQuestionViewModel(this, selectedAssessment);
        
        InitializeComponent();
#if DEBUG
        this.AttachDevTools();
#endif
    }
    
    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
    
}