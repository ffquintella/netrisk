using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using ReactiveUI;
using System.Reactive;
using Avalonia.Markup.Xaml.Templates;

namespace AvaloniaExtraControls.MultiSelect;

public class MultiSelect : TemplatedControl
{
    public static readonly StyledProperty<IBrush?> BackgroundProperty =
        AvaloniaProperty.Register<MultiSelect, IBrush?>(nameof(Background));

    public IBrush? Background
    {
        get { return GetValue(BackgroundProperty); }
        set { SetValue(BackgroundProperty, value); }
    }

    public static readonly StyledProperty<IBrush?> HeaderBackgroundProperty =
        AvaloniaProperty.Register<MultiSelect, IBrush?>(nameof(HeaderBackground));

    public IBrush? HeaderBackground
    {
        get { return GetValue(HeaderBackgroundProperty); }
        set { SetValue(HeaderBackgroundProperty, value); }
    }
    
    public static readonly StyledProperty<String?> TitleProperty =
        AvaloniaProperty.Register<MultiSelect, String?>(nameof(Title));

    public String? Title
    {
        get { return GetValue(TitleProperty); }
        set { SetValue(TitleProperty, value); }
    }
    
    public static readonly StyledProperty<IEnumerable<String>?> ItemsSourceProperty =
        AvaloniaProperty.Register<MultiSelect, IEnumerable<String>?>(nameof(ItemsSource));

    public IEnumerable<String>? ItemsSource
    {
        get { return GetValue(ItemsSourceProperty); }
        set
        {
            //SelectedItems = new List<string>();
            //AvailableItems = value;
            
            SetValue(ItemsSourceProperty, value);
        }
    }
    
    public static readonly StyledProperty<IEnumerable<String>?> AvailableItemsProperty =
        AvaloniaProperty.Register<MultiSelect, IEnumerable<String>?>(nameof(AvailableItems));

    public IEnumerable<String>? AvailableItems
    {
        get { return GetValue(AvailableItemsProperty); }
        set { SetValue(AvailableItemsProperty, value); }
    }
    
    public static readonly StyledProperty<IEnumerable<String>?> SelectedAvailableItemsProperty =
        AvaloniaProperty.Register<MultiSelect, IEnumerable<String>?>(nameof(SelectedAvailableItems));

    public IEnumerable<String>? SelectedAvailableItems
    {
        get { return GetValue(SelectedAvailableItemsProperty); }
        set { SetValue(SelectedAvailableItemsProperty, value); }
    }
    
    
    public static readonly StyledProperty<IEnumerable<String>?> SelectedItemsProperty =
        AvaloniaProperty.Register<MultiSelect, IEnumerable<String>?>(nameof(SelectedItems));

    public IEnumerable<String>? SelectedItems
    {
        get { return GetValue(SelectedItemsProperty); }
        set { SetValue(SelectedItemsProperty, value); }
    }
    
    public static readonly StyledProperty<IEnumerable<String>?> SelectedSelectedItemsProperty =
        AvaloniaProperty.Register<MultiSelect, IEnumerable<String>?>(nameof(SelectedSelectedItems));

    public IEnumerable<String>? SelectedSelectedItems
    {
        get { return GetValue(SelectedSelectedItemsProperty); }
        set { SetValue(SelectedSelectedItemsProperty, value); }
    }
    
    public static readonly StyledProperty<ReactiveCommand<Grid, Unit>> BtMoveRightClickedProperty =
        AvaloniaProperty.Register<MultiSelect, ReactiveCommand<Grid, Unit>>(nameof(BtMoveRightClicked));
    
    public ReactiveCommand<Grid, Unit> BtMoveRightClicked 
    {
        get { return GetValue(BtMoveRightClickedProperty); }
        set { SetValue(BtMoveRightClickedProperty, value); }
    }
    
    public static readonly StyledProperty<IReactiveCommand?> BtMoveLeftClickedProperty =
        AvaloniaProperty.Register<MultiSelect, IReactiveCommand?>(nameof(BtMoveLeftClicked));
    public IReactiveCommand? BtMoveLeftClicked     
    {
        get { return GetValue(BtMoveLeftClickedProperty); }
        set { SetValue(BtMoveLeftClickedProperty, value); }
    }
    
    public MultiSelect()
    {
        BtMoveRightClicked = ReactiveCommand.Create<Grid>(ExecuteMoveRight);
        BtMoveLeftClicked = ReactiveCommand.Create(ExecuteMoveLeft);
        
        InitializeComponent();
    }

    private void ExecuteMoveRight(Grid mainGrid)
    {
       
        var avaliableList =  mainGrid.Children.OfType<ListBox>().FirstOrDefault(c => c.Name == "MsLstAvailable");
        
        //var selectedList =  mainGrid.Children.OfType<ListBox>().FirstOrDefault(c => c.Name == "MsLstSelected");

        
        var selectedItems = avaliableList.SelectedItems?.Cast<string>().ToList();
        if (selectedItems == null || selectedItems.Count == 0)
            return;

        if (SelectedItems == null) SelectedItems = new List<string>();
        
        SelectedItems = SelectedItems?.Concat(selectedItems);
        
        AvailableItems = AvailableItems?.Except(selectedItems);
    }
    
    private void ExecuteMoveLeft()
    {
        var selectedItems = SelectedSelectedItems?.ToList();
        if (selectedItems == null || selectedItems.Count == 0)
            return;
        SelectedAvailableItems = new List<string>();
        AvailableItems = AvailableItems?.Concat(selectedItems);
        
        SelectedSelectedItems = new List<string>();
        SelectedItems = SelectedItems?.Except(selectedItems);
    }
    
    private void InitializeComponent()
    {
        
    }
}