using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using FaceONNX;
using FlashCap;
using GUIClient.Extensions;
using GUIClient.Tools;
using GUIClient.Tools.Camera;
using ReactiveUI;
using SkiaSharp;

namespace GUIClient.ViewModels;

public class VerifyFaceIDViewModel: ViewModelBase
{
    
    #region LANGUAGES

    public string StrTitle { get; } = Localizer["VerifyFaceImageTitle"];

    #endregion
    
    #region PROPERTIES
    
    public int WindowWidth { get; set; } = 800;
    public int WindowHeight { get; set; } = 600;
    public bool IsFaceIdVerified { get; set; } = false;
    
    private Bitmap _image = null!;
    public Bitmap Image
    {
        get => _image;
        private set => this.RaiseAndSetIfChanged(ref _image, value);
    }
    
    
    private string _footerText = string.Empty;
    public string FooterText 
    {
        get => _footerText;
        set => this.RaiseAndSetIfChanged(ref _footerText, value);
    }

    #endregion
    
    #region FIELDS
    
    private PixelBufferArrivedTaskDelegate? _pixelBufferDelegate;
    private bool _disposed;
    private Window? _parentWindow;
    private int _frameIndex = 0;
    
    
    #endregion
    
    #region SERVICES
    
    // Add any services you need here, e.g., for camera access or face ID verification.

    private CameraManager CameraManager { get; set; } = GetService<CameraManager>();

    #endregion
    
    #region CONSTRUCTORS
    
    public VerifyFaceIDViewModel(Window? parentWindow)
    {
        _parentWindow = parentWindow ?? throw new ArgumentNullException(nameof(parentWindow));
        _parentWindow.Closed += ParentWindowOnClosed;
        
        Image = new Bitmap(AssetLoader.Open(new Uri("avares://GUIClient/Assets/placeholder.png")));
        
        FooterText = Localizer["InitializingFaceID"];
        
        _pixelBufferDelegate = OnPixelBufferArrivedAsync;
        
        _=  InitializeAsync();
    }
    
    #endregion
    
    #region METHODS
    
    private async void ParentWindowOnClosed(object? sender, EventArgs e)
    {
        await DisposeAsync().ConfigureAwait(false);
    }

    public async Task InitializeAsync()
    {
        await CameraManager.StartCameraAsync(_pixelBufferDelegate!);
        FooterText = Localizer["CameraInitialized"];
    }

    private Task IdentifyFace()
    {

        var skImage = Image.ToSKImage();

        _= Task.Run(async () =>
        {
            if(skImage == null)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    FooterText = Localizer["ImageConversionError"];
                });
                return;
            }
            
            using var dnnDetector = new FaceDetector();
            using var bitmap = SKBitmap.FromImage(skImage);
            skImage.Dispose();
            var faces = dnnDetector.Forward(new SkiaDrawing.Bitmap(bitmap));

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (faces.Length > 0)
                {
                    FooterText = Localizer["FaceDetected"];
                    IsFaceIdVerified = true;
                }
                else
                {
                    FooterText = Localizer["NoFaceDetected"];
                    IsFaceIdVerified = false;
                }
            });
        });

        return Task.CompletedTask;
    }

    private async Task OnPixelBufferArrivedAsync(PixelBufferScope bufferScope)
    {
        //Logger.Debug($"Buffer arrived: {bufferScope.Buffer.FrameIndex} ");

        try
        {
            _frameIndex++;
            
            Image = await CameraManager.ExtractImageAsync(bufferScope);
            
            if(_frameIndex % 90 == 0)
            {
               await IdentifyFace();
            }

        }
        catch (Exception ex)
        {
            FooterText = Localizer["Camera error"];
        }

    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        
        _pixelBufferDelegate = null;
        
        await CameraManager.StopCameraAsync();
        
        await CameraManager.CleanResourcesAsync();
        
        if (_parentWindow != null)
        {
            _parentWindow.Closed -= ParentWindowOnClosed;
        }
    }

    #endregion
}