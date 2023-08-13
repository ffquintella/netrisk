using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using ReactiveUI;
using System.Reactive;
using Avalonia.Markup.Xaml.Templates;
using AvaloniaExtraControls.Models;

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
    
    public static readonly StyledProperty<IBrush?> AvailableHeaderBackgroundProperty =
        AvaloniaProperty.Register<MultiSelect, IBrush?>(nameof(AvailableHeaderBackground));

    public IBrush? AvailableHeaderBackground
    {
        get { return GetValue(AvailableHeaderBackgroundProperty); }
        set { SetValue(AvailableHeaderBackgroundProperty, value); }
    }
    
    public static readonly StyledProperty<IBrush?> SelectedHeaderBackgroundProperty =
        AvaloniaProperty.Register<MultiSelect, IBrush?>(nameof(SelectedHeaderBackground));

    public IBrush? SelectedHeaderBackground
    {
        get { return GetValue(SelectedHeaderBackgroundProperty); }
        set { SetValue(SelectedHeaderBackgroundProperty, value); }
    }
    
    public static readonly StyledProperty<String?> TitleProperty =
        AvaloniaProperty.Register<MultiSelect, String?>(nameof(Title));

    public String? Title
    {
        get { return GetValue(TitleProperty); }
        set { SetValue(TitleProperty, value); }
    }
    
    public static readonly StyledProperty<String?> StrAvailableProperty =
        AvaloniaProperty.Register<MultiSelect, String?>(nameof(StrAvailable), "Available");

    public String? StrAvailable
    {
        get { return GetValue(StrAvailableProperty); }
        set { SetValue(StrAvailableProperty, value); }
    }
    
    public static readonly StyledProperty<String?> StrSelectedProperty =
        AvaloniaProperty.Register<MultiSelect, String?>(nameof(StrSelected), "Selected");

    public String? StrSelected
    {
        get { return GetValue(StrSelectedProperty); }
        set { SetValue(StrSelectedProperty, value); }
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
    
    public static readonly StyledProperty<IEnumerable<SelectEntity>?> AvailableItemsProperty =
        AvaloniaProperty.Register<MultiSelect, IEnumerable<SelectEntity>?>(nameof(AvailableItems));

    public IEnumerable<SelectEntity>? AvailableItems
    {
        get { return GetValue(AvailableItemsProperty); }
        set { SetValue(AvailableItemsProperty, value); }
    }
    

    
    
    public static readonly StyledProperty<IEnumerable<SelectEntity>?> SelectedItemsProperty =
        AvaloniaProperty.Register<MultiSelect, IEnumerable<SelectEntity>?>(nameof(SelectedItems));

    public IEnumerable<SelectEntity>? SelectedItems
    {
        get { return GetValue(SelectedItemsProperty); }
        set { SetValue(SelectedItemsProperty, value); }
    }
    

    
    public static readonly StyledProperty<ReactiveCommand<Grid, Unit>> BtMoveRightClickedProperty =
        AvaloniaProperty.Register<MultiSelect, ReactiveCommand<Grid, Unit>>(nameof(BtMoveRightClicked));
    
    public ReactiveCommand<Grid, Unit> BtMoveRightClicked 
    {
        get { return GetValue(BtMoveRightClickedProperty); }
        set { SetValue(BtMoveRightClickedProperty, value); }
    }
    
    public static readonly StyledProperty<ReactiveCommand<Grid, Unit>> BtMoveLeftClickedProperty =
        AvaloniaProperty.Register<MultiSelect, ReactiveCommand<Grid, Unit>>(nameof(BtMoveLeftClicked));
    public ReactiveCommand<Grid, Unit> BtMoveLeftClicked     
    {
        get { return GetValue(BtMoveLeftClickedProperty); }
        set { SetValue(BtMoveLeftClickedProperty, value); }
    }
    
    public MultiSelect()
    {
        BtMoveRightClicked = ReactiveCommand.Create<Grid>(ExecuteMoveRight);
        BtMoveLeftClicked = ReactiveCommand.Create<Grid>(ExecuteMoveLeft);
        
        InitializeComponent();
    }

    private void ExecuteMoveRight(Grid mainGrid)
    {
       
        var avaliableList =  mainGrid.Children.OfType<ListBox>().FirstOrDefault(c => c.Name == "MsLstAvailable");
        
        //var selectedList =  mainGrid.Children.OfType<ListBox>().FirstOrDefault(c => c.Name == "MsLstSelected");

        
        var selectedItems = avaliableList.SelectedItems?.Cast<SelectEntity>().ToList();
        if (selectedItems == null || selectedItems.Count == 0)
            return;

        if (SelectedItems == null) SelectedItems = new List<SelectEntity>();
        
        SelectedItems = SelectedItems?.Concat(selectedItems);
        AvailableItems = AvailableItems?.Except(selectedItems);
    }
    
    private void ExecuteMoveLeft(Grid mainGrid)
    {
        var selectedList =  mainGrid.Children.OfType<ListBox>().FirstOrDefault(c => c.Name == "MsLstSelected");
        
        var selectedItems = selectedList.SelectedItems?.Cast<SelectEntity>().ToList();
        if (selectedItems == null || selectedItems.Count == 0)
            return;

        if (AvailableItems == null) AvailableItems = new List<SelectEntity>();

        AvailableItems = AvailableItems?.Concat(selectedItems);
        SelectedItems = SelectedItems?.Except(selectedItems);
    }
    
    private void InitializeComponent()
    {
        
    }
}