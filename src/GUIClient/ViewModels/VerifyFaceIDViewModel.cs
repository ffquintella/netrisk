using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Channels;
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
        set
        {
            this.RaiseAndSetIfChanged(ref _backgroundColor, value);
            //_= ConsumeCaptureQueueAsync();
        }
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
    
    public bool IsFaceDetected { get; set; } = false;
    
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
    

    
    public FaceToken? FaceToken { get; set; }

    #endregion
    
    #region FIELDS
    
    private PixelBufferArrivedTaskDelegate? _pixelBufferDelegate;
    private bool _disposed;
    private Window? _parentWindow;
    private int _frameIndex = 0; // Initialize frame index
    private FaceTransactionData? _faceTransactionData;
    
    private List<ImageCaptureData> _imageCaptureData = new List<ImageCaptureData>();
    
    // Queue mechanism for captures
    //private readonly Channel<char> _captureQueue = Channel.CreateUnbounded<char>();
    
    private static ConcurrentQueue<string> _captureQueue = new ConcurrentQueue<string>();
    private bool _convertImagesCalled = false;
    private object _lockConvert = new object();
    
    
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
                    //FooterText = Localizer["FaceDetected"];
                    //IsFaceIdVerified = true;
                    IsFaceDetected = true;
                }
                else
                {
                    //FooterText = Localizer["NoFaceDetected"];
                    //IsFaceIdVerified = false;
                    IsFaceDetected = false;
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

                });

                foreach (var c in _faceTransactionData.ValidationSequence)
                {
                    _captureQueue.Enqueue(c.ToString());
                }
                _ = ConsumeCaptureQueueAsync();

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
    
    
    private async Task ConsumeCaptureQueueAsync()
    {
        if (!IsFaceDetected)
        {
            await Task.Delay(500);
            await ConsumeCaptureQueueAsync();
        }
        
        if (_captureQueue.TryDequeue(out string colorKey))
        {
            var detKey = colorKey.ToUpperInvariant()[0];
            await ChangeBackgroundColorAsync(detKey);

            
            await Task.Delay(500);

            var data = new ImageCaptureData
            {
                UserId = AuthenticationService.AuthenticatedUserInfo!.UserId!.Value,
                CaptureSequenceIndex = _imageCaptureData.Count,
                PngImageData = Image.ToPngByteArray(),
                CaptureImageLight = detKey
            };

            _imageCaptureData.Add(data);

            await ConsumeCaptureQueueAsync();

        }
        else
        {
            // When the queue is exhausted
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                BackgroundColor = new SolidColorBrush(Color.Parse("#282928"));
            });
            
            
            bool shouldCallConvert = false;
            lock (_lockConvert)
            {
                if (!_convertImagesCalled)
                {
                    _convertImagesCalled = true;
                    shouldCallConvert = true;
                }
            }
            if (shouldCallConvert)
            {
                await ConvertImagesToJsonAndPostAsync();
            }
        }
        
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
    

    // Method to be called when the background color changes
    // Removed RequestSequentialCapture method and associated logic

    private async Task OnPixelBufferArrivedAsync(PixelBufferScope bufferScope)
    {
        try
        {
            _frameIndex++; // Increment frame index
            Image = await CameraManager.ExtractImageAsync(bufferScope); // Extract image from buffer
    
            if (_frameIndex % 10 == 0) // Perform action every 10 frames
            {
                _ = IdentifyFaceAsync(Image);
            }
            
        }
        catch (Exception ex)
        {
            FooterText = Localizer["Camera error"]; // Update footer text on error
            Logger.Error(ex, "Error in OnPixelBufferArrivedAsync"); // Log error
        }
    }
    
    private async Task ConvertImagesToJsonAndPostAsync()
    {
        try
        {
            var userId = AuthenticationService.AuthenticatedUserInfo!.UserId;
            
            
            
            var validationSequence = "";
            foreach (var valChar in _faceTransactionData.ValidationSequence)   
            {
                validationSequence += valChar;
            }
            
            await using (var fileWriter = new StreamWriter("/Users/felipe/tmp/faceid_sequence.txt", true))
            {
                fileWriter.WriteLine(validationSequence);
            }
            
            foreach (var imageCaptureData in _imageCaptureData)
            {
                GUIImageTools.SaveBitmapArrayAsPng(imageCaptureData.PngImageData, $"/Users/felipe/tmp/{userId}_{imageCaptureData.CaptureImageLight}_{imageCaptureData.CaptureSequenceIndex}.png");
            }
           
           
            var json = System.Text.Json.JsonSerializer.Serialize(_imageCaptureData);

            _faceTransactionData.SequenceImages = json;

            try
            {
                FaceToken = await FaceIDService.CommitTransactionAsync(userId!.Value, _faceTransactionData);

                await AuthenticationService.RegisterFaceAuthenticationTokenAsync(FaceToken);
                
                FooterText = Localizer["FaceTokenCreated"];

                if (FaceToken != null)
                {
                    IsFaceIdVerified = true;
                }
                
                _parentWindow.Close();
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
