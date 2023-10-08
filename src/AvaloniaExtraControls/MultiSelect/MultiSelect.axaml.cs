using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using ReactiveUI;
using System.Reactive;
using Avalonia.Collections;
using Avalonia.Markup.Xaml.Templates;
using AvaloniaExtraControls.Models;

namespace AvaloniaExtraControls.MultiSelect;

public class  MultiSelect : TemplatedControl
{
    public new static readonly StyledProperty<IBrush?> BackgroundProperty =
        AvaloniaProperty.Register<MultiSelect, IBrush?>(nameof(Background));

    public new IBrush? Background
    {
        get => GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    public static readonly StyledProperty<IBrush?> HeaderBackgroundProperty =
        AvaloniaProperty.Register<MultiSelect, IBrush?>(nameof(HeaderBackground));

    public IBrush? HeaderBackground
    {
        get => GetValue(HeaderBackgroundProperty);
        set => SetValue(HeaderBackgroundProperty, value);
    }
    
    public static readonly StyledProperty<IBrush?> AvailableHeaderBackgroundProperty =
        AvaloniaProperty.Register<MultiSelect, IBrush?>(nameof(AvailableHeaderBackground));

    public IBrush? AvailableHeaderBackground
    {
        get => GetValue(AvailableHeaderBackgroundProperty);
        set => SetValue(AvailableHeaderBackgroundProperty, value);
    }
    
    public static readonly StyledProperty<IBrush?> SelectedHeaderBackgroundProperty =
        AvaloniaProperty.Register<MultiSelect, IBrush?>(nameof(SelectedHeaderBackground));

    public IBrush? SelectedHeaderBackground
    {
        get => GetValue(SelectedHeaderBackgroundProperty);
        set => SetValue(SelectedHeaderBackgroundProperty, value);
    }
    
    public static readonly StyledProperty<String?> TitleProperty =
        AvaloniaProperty.Register<MultiSelect, String?>(nameof(Title));

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }
    
    public static readonly StyledProperty<String?> StrAvailableProperty =
        AvaloniaProperty.Register<MultiSelect, String?>(nameof(StrAvailable), "Available");

    public string? StrAvailable
    {
        get => GetValue(StrAvailableProperty);
        set => SetValue(StrAvailableProperty, value);
    }
    
    public static readonly StyledProperty<String?> StrSelectedProperty =
        AvaloniaProperty.Register<MultiSelect, String?>(nameof(StrSelected), "Selected");

    public String? StrSelected
    {
        get { return GetValue(StrSelectedProperty); }
        set { SetValue(StrSelectedProperty, value); }
    }
    
    /*public static readonly StyledProperty<IEnumerable<String>?> ItemsSourceProperty =
        AvaloniaProperty.Register<MultiSelect, IEnumerable<String>?>(nameof(ItemsSource));

    public IEnumerable<String>? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set
        {
            SetValue(ItemsSourceProperty, value);
        }
    }*/
    
    /*public static readonly StyledProperty<IEnumerable<SelectEntity>?> AvailableItemsProperty =
        AvaloniaProperty.Register<MultiSelect, IEnumerable<SelectEntity>?>(nameof(AvailableItems));

    public IEnumerable<SelectEntity>? AvailableItems
    {
        get => GetValue(AvailableItemsProperty);
        set => SetValue(AvailableItemsProperty, value);
    }
    
    public static readonly DirectProperty<MultiSelect, IEnumerable<SelectEntity>> ItemsProperty =
        AvaloniaProperty.RegisterDirect<MultiSelect, IEnumerable<SelectEntity>>(
            nameof(Items),
            o => o.Items,
            (o, v) => o.Items = v);
*/
    
    /*private IEnumerable<SelectEntity> _items = new AvaloniaList<SelectEntity>();

    public IEnumerable<SelectEntity> Items
    {
        get { return _items; }
        set { SetAndRaise(ItemsProperty, ref _items, value); }
    }*/
    
    public static readonly DirectProperty<MultiSelect, IEnumerable<SelectEntity>> AvailableItemsProperty =
        AvaloniaProperty.RegisterDirect<MultiSelect, IEnumerable<SelectEntity>>(
            nameof(AvailableItems),
            o => o.AvailableItems,
            (o, v) => o.AvailableItems = v);

    private IEnumerable<SelectEntity> _availableItems = new AvaloniaList<SelectEntity>();

    public IEnumerable<SelectEntity> AvailableItems
    {
        get { return _availableItems; }
        set { SetAndRaise(AvailableItemsProperty, ref _availableItems, value); }
    }
    
    public static readonly DirectProperty<MultiSelect, IEnumerable<SelectEntity>> SelectedAvailableItemsProperty =
        AvaloniaProperty.RegisterDirect<MultiSelect, IEnumerable<SelectEntity>>(
            nameof(SelectedAvailableItems),
            o => o.SelectedAvailableItems,
            (o, v) => o.SelectedAvailableItems = v);

    private IEnumerable<SelectEntity> _selectedAvailableItems = new AvaloniaList<SelectEntity>();

    public IEnumerable<SelectEntity> SelectedAvailableItems
    {
        get { return _selectedAvailableItems; }
        set { SetAndRaise(SelectedAvailableItemsProperty, ref _selectedAvailableItems, value); }
    }
    
    
    public event EventHandler<SelectedItemsChangedEventHandlerArgs>? SelectedItemsChanged;
    
    protected virtual void OnSelectedItemsChanged(SelectedItemsChangedEventHandlerArgs e)
    {
        EventHandler<SelectedItemsChangedEventHandlerArgs>? handler = SelectedItemsChanged;
        if (handler != null)
        {
            handler(this, e);
        }
    }
    
    /*public static readonly StyledProperty<IEnumerable<SelectEntity>?> SelectedItemsProperty =
        AvaloniaProperty.Register<MultiSelect, IEnumerable<SelectEntity>?>(nameof(SelectedItems));

    public IEnumerable<SelectEntity>? SelectedItems
    {
        get => GetValue(SelectedItemsProperty);
        set
        {
            OnSelectedItemsChanged(new SelectedItemsChangedEventHandlerArgs(){SelectedItems = value});
            SetValue(SelectedItemsProperty, value);
        }
    }
    */
    
    public static readonly DirectProperty<MultiSelect, IEnumerable<SelectEntity>> SelectedItemsProperty =
        AvaloniaProperty.RegisterDirect<MultiSelect, IEnumerable<SelectEntity>>(
            nameof(SelectedItems),
            o => o.SelectedItems,
            (o, v) => o.SelectedItems = v);

    private IEnumerable<SelectEntity> _selectedItems = new AvaloniaList<SelectEntity>();

    public IEnumerable<SelectEntity> SelectedItems
    {
        get { return _selectedItems; }
        set
        {
            //OnSelectedItemsChanged(new SelectedItemsChangedEventHandlerArgs(){SelectedItems = value});
            SetAndRaise(SelectedItemsProperty, ref _selectedItems, value);
        }
    }

    private static readonly DirectProperty<MultiSelect, IEnumerable<SelectEntity>> SelectedSelectedItemsProperty =
        AvaloniaProperty.RegisterDirect<MultiSelect, IEnumerable<SelectEntity>>(
            nameof(SelectedSelectedItems),
            o => o.SelectedSelectedItems,
            (o, v) => o.SelectedSelectedItems = v);

    private IEnumerable<SelectEntity> _selectedSelectedItems = new AvaloniaList<SelectEntity>();

    public IEnumerable<SelectEntity> SelectedSelectedItems
    {
        get => _selectedSelectedItems;
        set => SetAndRaise(SelectedSelectedItemsProperty, ref _selectedSelectedItems, value);
        
    }
    
    public static readonly StyledProperty<ReactiveCommand<Grid, Unit>> BtMoveRightClickedProperty =
        AvaloniaProperty.Register<MultiSelect, ReactiveCommand<Grid, Unit>>(nameof(BtMoveRightClicked));
    
    public ReactiveCommand<Grid, Unit> BtMoveRightClicked 
    {
        get => GetValue(BtMoveRightClickedProperty);
        set => SetValue(BtMoveRightClickedProperty, value);
    }
    
    public static readonly StyledProperty<ReactiveCommand<Grid, Unit>> BtMoveLeftClickedProperty =
        AvaloniaProperty.Register<MultiSelect, ReactiveCommand<Grid, Unit>>(nameof(BtMoveLeftClicked));
    public ReactiveCommand<Grid, Unit> BtMoveLeftClicked     
    {
        get => GetValue(BtMoveLeftClickedProperty);
        set => SetValue(BtMoveLeftClickedProperty, value);
    }
    
    public MultiSelect()
    {
        BtMoveRightClicked = ReactiveCommand.Create<Grid>(ExecuteMoveRight);
        BtMoveLeftClicked = ReactiveCommand.Create<Grid>(ExecuteMoveLeft);
        
        InitializeComponent();
    }

    private void ExecuteMoveRight(Grid mainGrid)
    {
        
        var selected = SelectedAvailableItems.ToList();

        SelectedItems = SelectedItems.Concat(selected);
        
        AvailableItems = AvailableItems.Except(selected);
        
        /*var availableList =  mainGrid.Children.OfType<ListBox>().FirstOrDefault(c => c.Name == "MsLstAvailable");
        if(availableList == null) return;
        
        var selectedItems = availableList.SelectedItems?.Cast<SelectEntity>().ToList();
        if (selectedItems == null || selectedItems.Count == 0)
            return;

        if (SelectedItems == null) SelectedItems = new List<SelectEntity>();
        
        SelectedItems = SelectedItems?.Concat(selectedItems);
        AvailableItems = AvailableItems?.Except(selectedItems);*/
    }
    
    private void ExecuteMoveLeft(Grid mainGrid)
    {
        var selected = SelectedSelectedItems.ToList();
        
        AvailableItems = AvailableItems!.Concat(selected);
        SelectedItems = SelectedItems!.Except(selected);
        
        
        /*
        var selectedList =  mainGrid.Children.OfType<ListBox>().FirstOrDefault(c => c.Name == "MsLstSelected");

        if (selectedList == null) return;
        var selectedItems = selectedList.SelectedItems?.Cast<SelectEntity>().ToList();
        if (selectedItems == null || selectedItems.Count == 0)
            return;

        if (AvailableItems == null) AvailableItems = new List<SelectEntity>();

        AvailableItems = AvailableItems?.Concat(selectedItems);
        SelectedItems = SelectedItems?.Except(selectedItems);
        */
    }
    
    private void InitializeComponent()
    {
        
    }
}