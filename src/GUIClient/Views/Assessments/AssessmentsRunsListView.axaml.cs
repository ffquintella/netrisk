using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using DAL.Entities;
using GUIClient.ViewModels.Assessments;

namespace GUIClient.Views.Assessments;

public partial class AssessmentsRunsListView : UserControl
{
    
    /*public static readonly StyledProperty<Assessment?> AssessmentProperty =
        AvaloniaProperty.Register<AssessmentsRunsListView, Assessment?>(nameof(Assessment));

    public Assessment? Assessment
    {
        get => GetValue(AssessmentProperty);
        set => SetValue(AssessmentProperty, value);
    }*/
    
    public AssessmentsRunsListView()
    {
        InitializeComponent();
    }
}