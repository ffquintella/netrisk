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

    public static readonly StyledProperty<bool> ShowFilterProperty =
        AvaloniaProperty.Register<MultiSelect, bool>(nameof(ShowFilter));
    
    public bool ShowFilter
    {
        get => GetValue(ShowFilterProperty);
        set => SetValue(ShowFilterProperty, value);
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
    
    
    
    private static readonly DirectProperty<MultiSelect, string?> LeftFilterPoperty =
        AvaloniaProperty.RegisterDirect<MultiSelect, string?>(
            nameof(LeftFilter),
            o => o.LeftFilter,
            (o, v) => o.LeftFilter = v);

    private string? _leftFilter = "";

    public string? LeftFilter
    {
        get
        {
            return _leftFilter;
        }
        set
        {
            if (value == null) SetAndRaise(LeftFilterPoperty, ref _leftFilter, "");
            else
            {
                ListedAvailableItems = AvailableItems.Where(x => x.Label.Contains(value));
                SetAndRaise(LeftFilterPoperty, ref _leftFilter, value);
            }
        }
    }
    
    private static readonly DirectProperty<MultiSelect, string?> RightFilterProperty =
        AvaloniaProperty.RegisterDirect<MultiSelect, string?>(
            nameof(RightFilter),
            o => o.RightFilter,
            (o, v) => o.RightFilter = v);

    private string? _rightFilter = "";

    public string? RightFilter
    {
        get
        {
            return _rightFilter;
        }
        set
        {
            if(value == null) SetAndRaise(RightFilterProperty, ref _rightFilter, "");
            else
            {
                ListedSelectedItems = SelectedItems.Where(x => x.Label.Contains(value));
                SetAndRaise(RightFilterProperty, ref _rightFilter, value); 
            }
        }
    }


    public static readonly DirectProperty<MultiSelect, IEnumerable<SelectEntity>> ListedAvailableItemsProperty =
        AvaloniaProperty.RegisterDirect<MultiSelect, IEnumerable<SelectEntity>>(
            nameof(ListedAvailableItems),
            o => o.ListedAvailableItems,
            (o, v) => o.ListedAvailableItems = v);

    private IEnumerable<SelectEntity> _listedavailableItems = new AvaloniaList<SelectEntity>();

    public IEnumerable<SelectEntity> ListedAvailableItems
    {
        get
        {
            return _listedavailableItems;
        }
        set
        {
            SetAndRaise(ListedAvailableItemsProperty, ref _listedavailableItems, value);
        }
    }
    

    public static readonly DirectProperty<MultiSelect, IEnumerable<SelectEntity>> AvailableItemsProperty =
        AvaloniaProperty.RegisterDirect<MultiSelect, IEnumerable<SelectEntity>>(
            nameof(AvailableItems),
            o => o.AvailableItems,
            (o, v) => o.AvailableItems = v);

    

    private IEnumerable<SelectEntity> _availableItems = new AvaloniaList<SelectEntity>();

    public IEnumerable<SelectEntity> AvailableItems
    {
        get
        {
            return _availableItems;
        }
        set
        {
            ListedAvailableItems = value;
            SetAndRaise(AvailableItemsProperty, ref _availableItems, value);
        }
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
    
    
   public static readonly DirectProperty<MultiSelect, IEnumerable<SelectEntity>> SelectedItemsProperty =
        AvaloniaProperty.RegisterDirect<MultiSelect, IEnumerable<SelectEntity>>(
            nameof(SelectedItems),
            o => o.SelectedItems,
            (o, v) => o.SelectedItems = v);

   
   public static readonly DirectProperty<MultiSelect, IEnumerable<SelectEntity>> ListedSelectedItemsProperty =
       AvaloniaProperty.RegisterDirect<MultiSelect, IEnumerable<SelectEntity>>(
           nameof(ListedSelectedItems),
           o => o.ListedSelectedItems,
           (o, v) => o.ListedSelectedItems = v);

   private IEnumerable<SelectEntity> _listedSelectedItems = new AvaloniaList<SelectEntity>();

   public IEnumerable<SelectEntity> ListedSelectedItems
   {
       get
       {
           return _listedSelectedItems;
       }
       set
       {
           SetAndRaise(ListedSelectedItemsProperty, ref _listedSelectedItems, value);
       }
   }
   
   
    private IEnumerable<SelectEntity> _selectedItems = new AvaloniaList<SelectEntity>();

    public IEnumerable<SelectEntity> SelectedItems
    {
        get { return _selectedItems; }
        set
        {
            ListedSelectedItems = value;
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
        
       
    }
    
    private void ExecuteMoveLeft(Grid mainGrid)
    {
        var selected = SelectedSelectedItems.ToList();
        AvailableItems = AvailableItems!.Concat(selected);
        SelectedItems = SelectedItems!.Except(selected);
        
    }
    
    private void InitializeComponent()
    {
        
    }
}