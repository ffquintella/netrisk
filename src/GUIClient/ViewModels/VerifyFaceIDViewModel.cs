using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using ClientServices.Interfaces;
using FaceONNX;
using FlashCap;
using GUIClient.Extensions;
using GUIClient.Tools;
using GUIClient.Tools.Camera;
using GUIClient.Tools.Window;
using Model.FaceID;
using ReactiveUI;
using SkiaSharp;

namespace GUIClient.ViewModels;

public class VerifyFaceIDViewModel: ViewModelBase
{
    
    #region LANGUAGES

    public string StrTitle { get; } = Localizer["VerifyFaceImageTitle"];

    #endregion
    
    #region PROPERTIES
    
    private IBrush _backgroundColor = new SolidColorBrush(Color.Parse("#282928"));
    
    public IBrush BackgroundColor
    {
        get => _backgroundColor;
        set => this.RaiseAndSetIfChanged(ref _backgroundColor, value);
    }
    
    private int _windowWidth = 800;
    public int WindowWidth
    {
        get => _windowWidth;
        set => this.RaiseAndSetIfChanged(ref _windowWidth, value);
    }
    
    private int _windowHeight = 600;
    public int WindowHeight { 
        get => _windowHeight; 
        set => this.RaiseAndSetIfChanged(ref _windowHeight, value); 
    }
    public bool IsFaceIdVerified { get; set; } = false;
    
    private readonly object _lock = new object();
    private Bitmap _image = null!;
    public Bitmap Image
    {
        get
        {
            lock (_lock) return _image;
        }
        private set
        {
            lock (_lock)
            {
                this.RaiseAndSetIfChanged(ref _image, value);
            }
            
        } 
    }
    
    
    private string _footerText = string.Empty;
    public string FooterText 
    {
        get => _footerText;
        set => this.RaiseAndSetIfChanged(ref _footerText, value);
    }
    
    private readonly object _capLock = new object();
    private bool _canCaptureImage = false;
    
    public bool CanCaptureImage
    {
        get
        {
            lock (_capLock) return _canCaptureImage;
        }
        set
        {
            lock (_capLock) this.RaiseAndSetIfChanged(ref _canCaptureImage, value);
        } 
    }

    
    public FaceToken? FaceToken { get; set; }

    #endregion
    
    #region FIELDS
    
    private PixelBufferArrivedTaskDelegate? _pixelBufferDelegate;
    private bool _disposed;
    private Window? _parentWindow;
    private int _frameIndex = 0;
    private FaceTransactionData? _faceTransactionData;
    
    private List<ImageCaptureData> _imageCaptureData = new List<ImageCaptureData>();
    private const int CountDown = 1000; // Countdown for the frame color change
    
    

    //private char _catureImageKey = 'O';
    
    private int _captureImageIndex = 0;
    private int _oldCaptureImageIndex = 0;

    
    #endregion
    
    #region SERVICES
    
    // Add any services you need here, e.g., for camera access or face ID verification.

    private CameraManager CameraManager { get; set; } = GetService<CameraManager>();
    private IFaceIDService FaceIDService { get; set; } = GetService<IFaceIDService>();
    private IAuthenticationService AuthenticationService { get; set; } = GetService<IAuthenticationService>();

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
        await GetFaceTransactionDataAsync();
    }

    private async Task IdentifyFaceAsync(Bitmap bitmap)
    {

        var skImage = bitmap.ToSKImage();

        await Task.Run(async () =>
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
        
    }
    
    private async Task GetFaceTransactionDataAsync()
    {
        try
        {
            
            if(!AuthenticationService.IsAuthenticated) 
            {
                FooterText = Localizer["UserNotAuthenticated"];
                return;
            }
            
            var userId = AuthenticationService.AuthenticatedUserInfo!.UserId;
            
            _faceTransactionData = await FaceIDService.GetFaceTransactionDataAsync(userId!.Value);
            
            if (_faceTransactionData != null)
            {
                FooterText = Localizer["FaceTransactionDataRetrieved"];

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    //_parentWindow!.WindowState = WindowState.FullScreen;
                    var screens = _parentWindow!.Screens;
                    var screen = screens.ScreenFromVisual(_parentWindow!);

                    WindowHeight = screen!.Bounds.Height - 20;
                    WindowWidth = screen.Bounds.Width - 20;

                    WindowPositioning.CenterOnScreen(_parentWindow);

                    _= StartFaceCaptureSequenceAsync();

                });
                    
                _canCaptureImage = true;
            }
            else
            {
                FooterText = Localizer["NoFaceTransactionData"];
            }
        }
        catch (Exception ex)
        {
            FooterText = Localizer["ErrorRetrievingFaceTransactionData"];
            Logger.Error(ex, "Error retrieving face transaction data");
        }
    }
    
    
    private async Task StartFaceCaptureSequenceAsync()
    {
        if (_faceTransactionData == null)
        {
            FooterText = Localizer["NoFaceTransactionData"];
            return;
        }

        CanCaptureImage = false;
        _captureImageIndex = 0;
        _oldCaptureImageIndex = 0;

        FooterText = Localizer["StartingFaceCaptureSequence"];
        
        _imageCaptureData.Clear();
        
        char _catureImageKey = 'O';
        //_catureImageKey = _faceTransactionData!.ValidationSequence[_captureImageIndex - 1];

        await ChangeBackgroundColorAsync(_catureImageKey);
        
        CanCaptureImage = true;
        
    }
    
    private async Task ChangeBackgroundColorAsync(char detKey)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (detKey == 'R')
            {
                BackgroundColor = new SolidColorBrush(Colors.Red);
            }
            else if (detKey == 'G')
            {
                BackgroundColor = new SolidColorBrush(Colors.Green);
            }
            else if (detKey == 'B')
            {
                BackgroundColor = new SolidColorBrush(Colors.Blue);
            }
            else if (detKey == 'W')
            {
                BackgroundColor = new SolidColorBrush(Colors.White);
            }
            else
            {
                BackgroundColor = new SolidColorBrush(Color.Parse("#282928"));
            }
        });
    }
    

    private async Task ProcessImageColor(Bitmap bitmap)
    {

        if (_captureImageIndex < _faceTransactionData!.ValidationSequence.Count + 1)
        {
            
            var _catureImageKey = 'O';
            
            if(_captureImageIndex == 0)
            {
                
                
                var data = new ImageCaptureData
                {
                    UserId = AuthenticationService.AuthenticatedUserInfo!.UserId!.Value,
                    CaptureSequenceIndex = _captureImageIndex  ,
                    PngImageData = bitmap.ToPngByteArray(),
                    CaptureImageLight = _catureImageKey
                };
                
                _imageCaptureData.Add(data);
                
                FooterText = Localizer["OffImageCaptured"];
                
                _captureImageIndex++;
            }
            else
            {
                
                var idx = _captureImageIndex - 1;
            
                _catureImageKey = _faceTransactionData!.ValidationSequence[idx];
                
                if (_captureImageIndex != _oldCaptureImageIndex)
                {
                    var data = new ImageCaptureData
                    {
                        UserId = AuthenticationService.AuthenticatedUserInfo!.UserId!.Value,
                        CaptureSequenceIndex = _captureImageIndex  - 1,
                        PngImageData = Image.ToPngByteArray(),
                        CaptureImageLight = _catureImageKey
                    };
                        
                    _imageCaptureData.Add(data);
                    
                    _oldCaptureImageIndex = _captureImageIndex;
                    _captureImageIndex++;
                    
                }
                
            }
            
            await ChangeBackgroundColorAsync(_catureImageKey);
        }
        else
        {
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                BackgroundColor = new SolidColorBrush(Color.Parse("#282928"));
                _canCaptureImage = false;
                _captureImageIndex = 0;
                FooterText = Localizer["AllImagesCaptured"];

                WindowHeight = 600;
                WindowWidth = 800;
                
                _= ConvertImagesToJsonAndPostAsync();

                WindowPositioning.CenterOnScreen(_parentWindow);
                
            });
        }
            
            
        
    }

    private async Task OnPixelBufferArrivedAsync(PixelBufferScope bufferScope)
    {
        try
        {
            _frameIndex++;
            
            Image = await CameraManager.ExtractImageAsync(bufferScope);
            
            if(_frameIndex % 10 == 0)
            {
               _= IdentifyFaceAsync(Image);
            }

            if (CanCaptureImage)
            {
               _= ProcessImageColor(Image);
            }

        }
        catch (Exception ex)
        {
            FooterText = Localizer["Camera error"];
        }

    }
    
    private async Task ConvertImagesToJsonAndPostAsync()
    {
        try
        {
            
            var validationSequence = "";
            foreach (var valChar in _faceTransactionData.ValidationSequence)   
            {
                validationSequence += valChar;
            }
            
           /* await using (var fileWriter = new StreamWriter("/Users/felipe/tmp/faceid_sequence.txt", true))
            {
                fileWriter.WriteLine(validationSequence);
            }
            
            var userId = AuthenticationService.AuthenticatedUserInfo!.UserId;

            foreach (var imageCaptureData in _imageCaptureData)
            {
                ImageTools.SaveBitmapArrayAsPng(imageCaptureData.PngImageData, $"/Users/felipe/tmp/{userId}_{imageCaptureData.CaptureImageLight}_{imageCaptureData.CaptureSequenceIndex}.png");
            }*/
           
           
            var json = System.Text.Json.JsonSerializer.Serialize(_imageCaptureData);

            _faceTransactionData.SequenceImages = json;
            var userId = AuthenticationService.AuthenticatedUserInfo!.UserId;

            try
            {
                FaceToken = await FaceIDService.CommitTransactionAsync(userId!.Value, _faceTransactionData);
                FooterText = Localizer["FaceTokenCreated"];
            }
            catch (Exception ex)
            {
                FooterText = Localizer["ErrorGettingToken"];
            }
            
            

        }
        catch (Exception ex)
        {
            FooterText = Localizer["ErrorSavingImages"];
            Logger.Error(ex, "Error converting images");
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