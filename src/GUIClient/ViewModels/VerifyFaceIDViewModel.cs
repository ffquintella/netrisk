using System;
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
    private FaceTransactionData? _faceTransactionData;

    private Bitmap? _offImage;
    private Bitmap? _redImage;
    private Bitmap? _greenImage;
    private Bitmap? _blueImage;
    private Bitmap? _whiteImage;
    
    
    
    
    #endregion
    
    #region SERVICES
    
    // Add any services you need here, e.g., for camera access or face ID verification.

    private CameraManager CameraManager { get; set; } = GetService<CameraManager>();
    private IFaceIDService FaceIDService { get; set; } = GetService<IFaceIDService>();
    private IAuthenticationService AuthenticationService { get; set; } = GetService<IAuthenticationService>();

    private bool _canCaptureImage = false;

    private char _catureImageKey = 'O';
    
    private int _captureImageIndex = 0;
    

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
    

    private async Task OnPixelBufferArrivedAsync(PixelBufferScope bufferScope)
    {
        //Logger.Debug($"Buffer arrived: {bufferScope.Buffer.FrameIndex} ");

        try
        {
            _frameIndex++;
            
            Image = await CameraManager.ExtractImageAsync(bufferScope);

            if (_canCaptureImage)
            {
                if (_captureImageIndex <= _faceTransactionData!.ValidationSequence.Count + 1)
                {
                    if(_catureImageKey == 'O' && _captureImageIndex < 3)
                    {
                        _captureImageIndex++;
                        _offImage = Image;
                        FooterText = Localizer["OffImageCaptured"];
                    }
                    else
                    {
                        if (_frameIndex == 1)
                        {
                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                //_parentWindow!.WindowState = WindowState.FullScreen;
                                var screens = _parentWindow!.Screens;
                                var screen = screens.ScreenFromVisual(_parentWindow!);
                    
                                WindowHeight = screen!.Bounds.Height -20;
                                WindowWidth = screen.Bounds.Width -20;
                    
                                WindowPositioning.CenterOnScreen(_parentWindow);
                    
                                Logger.Debug($"Capturing frame: {bufferScope.Buffer.FrameIndex} ");
                            });
                        }

                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            if(_faceTransactionData!.ValidationSequence[_captureImageIndex - 1] == 'R')
                            {
                                BackgroundColor = new SolidColorBrush(Colors.Red);
                                _redImage = Image;
                                FooterText = Localizer["RedImageCaptured"];
                            }
                            else if(_faceTransactionData.ValidationSequence[_captureImageIndex - 1] == 'G')
                            {
                                BackgroundColor = new SolidColorBrush(Colors.Green);
                                _greenImage = Image;
                                FooterText = Localizer["GreenImageCaptured"];
                            }
                            else if(_faceTransactionData.ValidationSequence[_captureImageIndex - 1] == 'B')
                            {
                                BackgroundColor = new SolidColorBrush(Colors.Blue);
                                _blueImage = Image;
                                FooterText = Localizer["BlueImageCaptured"];
                            }
                            else if(_faceTransactionData.ValidationSequence[_captureImageIndex - 1] == 'W')
                            {
                                BackgroundColor = new SolidColorBrush(Colors.White);
                                _whiteImage = Image;
                                FooterText = Localizer["WhiteImageCaptured"];
                            }
                           
                        });

                    }
                }
                else
                {
                    BackgroundColor = new SolidColorBrush(Color.Parse("#282928"));
                    _canCaptureImage = false;
                }
                
                
            }
            
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