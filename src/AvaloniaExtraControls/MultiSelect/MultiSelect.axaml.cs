using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using ReactiveUI;
using System.Reactive;

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
    
    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty =
        AvaloniaProperty.Register<MultiSelect, IEnumerable?>(nameof(ItemsSource));

    public IEnumerable? ItemsSource
    {
        get { return GetValue(ItemsSourceProperty); }
        set
        {
            SetValue(SelectedSelectedItemsProperty, null);
            SetValue(AvailableItemsProperty, value);
            
            SetValue(ItemsSourceProperty, value);
        }
    }
    
    public static readonly StyledProperty<IEnumerable?> AvailableItemsProperty =
        AvaloniaProperty.Register<MultiSelect, IEnumerable?>(nameof(AvailableItems));

    public IEnumerable? AvailableItems
    {
        get { return GetValue(AvailableItemsProperty); }
        set { SetValue(AvailableItemsProperty, value); }
    }
    
    public static readonly StyledProperty<IEnumerable?> SelectedAvailableItemsProperty =
        AvaloniaProperty.Register<MultiSelect, IEnumerable?>(nameof(SelectedAvailableItems));

    public IEnumerable? SelectedAvailableItems
    {
        get { return GetValue(SelectedAvailableItemsProperty); }
        set { SetValue(SelectedAvailableItemsProperty, value); }
    }
    
    
    public static readonly StyledProperty<IEnumerable?> SelectedItemsProperty =
        AvaloniaProperty.Register<MultiSelect, IEnumerable?>(nameof(SelectedItems));

    public IEnumerable? SelectedItems
    {
        get { return GetValue(SelectedItemsProperty); }
        set { SetValue(SelectedItemsProperty, value); }
    }
    
    public static readonly StyledProperty<IEnumerable?> SelectedSelectedItemsProperty =
        AvaloniaProperty.Register<MultiSelect, IEnumerable?>(nameof(SelectedSelectedItems));

    public IEnumerable? SelectedSelectedItems
    {
        get { return GetValue(SelectedSelectedItemsProperty); }
        set { SetValue(SelectedSelectedItemsProperty, value); }
    }
    
    public static readonly StyledProperty<IReactiveCommand?> BtMoveRightClickedProperty =
        AvaloniaProperty.Register<MultiSelect, IReactiveCommand?>(nameof(BtMoveRightClicked));
    
    public IReactiveCommand? BtMoveRightClicked 
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
        BtMoveRightClicked = ReactiveCommand.Create(ExecuteMoveRight);
        BtMoveLeftClicked = ReactiveCommand.Create(ExecuteMoveLeft);
        
        InitializeComponent();
    }

    private void ExecuteMoveRight()
    {
        
    }
    
    private void ExecuteMoveLeft()
    {
        
    }
    
    private void InitializeComponent()
    {
        
    }
}