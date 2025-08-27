using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using ClientServices.Interfaces;
using ReactiveUI;
using FlashCap;
using FlashCap.Devices;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;
using SkiaSharp;
using System.Reactive;
using FaceONNX;
using System.Text.Json;
using System.Text.Json.Serialization;
using Model.FaceID;
using Tools.Extensions;
using PixelFormats = FlashCap.PixelFormats;

namespace GUIClient.ViewModels.Admin;

public class AddFaceImageViewModel : ViewModelBase, IAsyncDisposable
{
    #region LANGUAGE

    public string Title => Localizer["AddFaceImageTitle"];
    public string FaceImageInstructions => Localizer["FaceImageInstructions"];
    public string StrSave => Localizer["Save"];
    public string StrCancel => Localizer["Cancel"];

    #endregion
    
    #region PROPERTIES
    private int UserId { get; }
    private Window ParentWindow { get; }

    private Bitmap _image = null!;
    public Bitmap Image
    {
        get => _image;
        private set => this.RaiseAndSetIfChanged(ref _image, value);
    }

    private ObservableCollection<CaptureDeviceDescriptor> _deviceList = new();
    public ObservableCollection<CaptureDeviceDescriptor> DeviceList
    {
        get => _deviceList;
        private set => this.RaiseAndSetIfChanged(ref _deviceList, value);
    }

    private CaptureDeviceDescriptor? _device;
    public CaptureDeviceDescriptor? Device
    {
        get => _device;
        private set => this.RaiseAndSetIfChanged(ref _device, value);
    }

    private ObservableCollection<VideoCharacteristics> _characteristicsList = new();
    public ObservableCollection<VideoCharacteristics> CharacteristicsList
    {
        get => _characteristicsList;
        private set => this.RaiseAndSetIfChanged(ref _characteristicsList, value);
    }

    private VideoCharacteristics? _characteristics;
    public VideoCharacteristics? Characteristics
    {
        get => _characteristics;
        private set => this.RaiseAndSetIfChanged(ref _characteristics, value);
    }

    public int VideoWidth { get; private set; } = 600;
    public int VideoHeight { get; private set; } = 600;
    
    private int _windowWidth = 700;
    
    public int WindowWidth 
    {
        get => _windowWidth;
        set
        {
            if (value < 400) value = 400; // Minimum width
            this.RaiseAndSetIfChanged(ref _windowWidth, value);
        }
    }
    
    private int _windowHeight = 700;
    
    public int WindowHeight 
    {
        get => _windowHeight;
        set
        {
            if (value < 400) value = 400; // Minimum height
            this.RaiseAndSetIfChanged(ref _windowHeight, value);
        }
    }
    
    private Bitmap _locatorImage = null!;
    
    public Bitmap LocatorImage 
    {
        get => _locatorImage;
        set => this.RaiseAndSetIfChanged(ref _locatorImage, value);
    }
    
    public bool _btSaveEnabled = false;
    
    public bool BtSaveEnabled 
    {
        get => _btSaveEnabled;
        set => this.RaiseAndSetIfChanged(ref _btSaveEnabled, value);
    }
    
    #endregion

    #region SERVICES
    private IFaceIDService FaceIDService { get; } = GetService<IFaceIDService>();
    #endregion

    #region FIELDS
    private CaptureDevice? _captureDevice;
    private readonly PixelBufferArrivedTaskDelegate _pixelBufferDelegate;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _imageUpdateLock = new(1, 1);
    private bool _disposed;
    private int frameCount = 0;
    private FaceDetectionResult? _faceDetectionResult;
    private SKBitmap? _detectedFaceImage;
    private PixelFormats _pixelFormat = PixelFormats.BGRA32;
    #endregion
    
    #region BUTTONS
    public ReactiveCommand<Unit, Unit> BtSaveClicked { get; } 
    public ReactiveCommand<Unit, Unit> BtCancelClicked { get; } 
    #endregion

    #region CONSTRUCTOR
    public AddFaceImageViewModel(int userId, Window parentWindow)
    {
        UserId = userId;
        ParentWindow = parentWindow ?? throw new ArgumentNullException(nameof(parentWindow));
        ParentWindow.Closed += ParentWindowOnClosed;

        _pixelBufferDelegate = OnPixelBufferArrivedAsync;
        Image = new Bitmap(AssetLoader.Open(new Uri("avares://GUIClient/Assets/placeholder.png")));
        LocatorImage = new Bitmap(AssetLoader.Open(new Uri("avares://GUIClient/Assets/facemask.png")));
        
        BtSaveClicked = ReactiveCommand.CreateFromTask(ExecuteSaveAsync);
        BtCancelClicked = ReactiveCommand.CreateFromTask(ExecuteCancelAsync);

        _ = InitializeAsync();
    }
    #endregion

    #region METHODS

    private async Task ExecuteSaveAsync()
    {
        if (_detectedFaceImage == null) return;
        

        var imageData = _detectedFaceImage.ToBase64Png();
        
        var response = await FaceIDService.SaveAsync(UserId, imageData, "png");
        
        Logger.Debug(response);
        ParentWindow.Close();

    }
    
    private Task ExecuteCancelAsync()
    {
        ParentWindow.Close();
        return Task.CompletedTask;
    }
    
    

    private async Task InitializeAsync()
    {
        try
        {
            var faceIdInfo = await FaceIDService.GetInfo().ConfigureAwait(false);
            if (!faceIdInfo.IsServiceAvailable)
            {
                await ShowErrorMessageAsync(Localizer["FaceIDNotAvaliableMSG"]);
                ParentWindow.Close();
                return;
            }

            var devices = new CaptureDevices();
            IEnumerable<CaptureDeviceDescriptor> descriptors = Enumerable.Empty<CaptureDeviceDescriptor>();

            for (int t = 0; t < 10; t++)
            {
                if (_cts.Token.IsCancellationRequested) return;
                descriptors = devices.EnumerateDescriptors();
                if (descriptors.Any()) break;
                if (t < 9) await Task.Delay(1000, _cts.Token).ConfigureAwait(false);
            }

            if (!descriptors.Any())
            {
                await ShowErrorMessageAsync(Localizer["CameraNotFoundMSG"]);
                ParentWindow.Close();
                return;
            }

            DeviceList = new ObservableCollection<CaptureDeviceDescriptor>(
                descriptors.Where(d => d.Characteristics.Length >= 1));
            Device = DeviceList.FirstOrDefault();

            if (Device == null)
            {
                await ShowErrorMessageAsync("No suitable camera device found.");
                ParentWindow.Close();
                return;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                CharacteristicsList = new ObservableCollection<VideoCharacteristics>(
                    Device.Characteristics.Where(c => c.PixelFormat == PixelFormats.JPEG));
                _pixelFormat = PixelFormats.JPEG;
            }
            
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                CharacteristicsList = new ObservableCollection<VideoCharacteristics>(
                    Device.Characteristics.Where(c => c.PixelFormat == PixelFormats.BGRA32));
                _pixelFormat = PixelFormats.BGRA32;
            }
            

            if (!CharacteristicsList.Any())
            {
                await ShowErrorMessageAsync("No ARGB32 characteristics found for the selected camera.");
                ParentWindow.Close();
                return;
            }
            
            Characteristics = CharacteristicsList.FirstOrDefault(c => c.Width > 900) ?? CharacteristicsList.FirstOrDefault();

            if (Characteristics == null)
            {
                await ShowErrorMessageAsync("No suitable video characteristics found.");
                ParentWindow.Close();
                return;
            }

            VideoHeight = Characteristics.Height;
            VideoWidth = Characteristics.Width;
            //VideoHeight = 960;
            //VideoWidth = 1280;
            this.RaisePropertyChanged(nameof(VideoHeight));
            this.RaisePropertyChanged(nameof(VideoWidth));

            await EnableCameraAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Logger.Debug("Initialization was canceled.");
        }
        catch (Exception ex)
        {
            Logger.Error($"Initialization error: {ex.Message}");
            await ShowErrorMessageAsync($"Initialization error: {ex.Message}");
            ParentWindow.Close();
        }
    }

    private async Task EnableCameraAsync()
    {
        if (Device == null || Characteristics == null || _cts.Token.IsCancellationRequested) return;

        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            _captureDevice = await Device.OpenAsync(Characteristics, _pixelBufferDelegate).ConfigureAwait(false);
            if (_captureDevice == null)
            {
                throw new InvalidOperationException("Error initializing camera: Failed to open device.");
            }
            await _captureDevice.StartAsync().ConfigureAwait(false);
        });

    }
    
    /// <summary>
    /// Try to identify the image format based on the byte data.
    /// </summary>
    /// <param name="data">The image bytes.</param>
    /// <returns>A string with (ex: "PNG", "JPEG", "BMP") ou "Unknown".</returns>
    private static string IdentifyImageFormat(byte[] data)
    {
        if (data == null) return "Unknown";

        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (data.Length >= 8 &&
            data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47 &&
            data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A)
        {
            return "PNG";
        }

        // JPEG: FF D8 FF
        if (data.Length >= 3 &&
            data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
        {
            // Pode-se adicionar verificações mais específicas para JPEG (E0 para JFIF, E1 para Exif)
            // if (data.Length >= 4 && (data[3] == 0xE0 || data[3] == 0xE1))
            return "JPEG";
        }

        // GIF: 47 49 46 38 37 61 ou 47 49 46 38 39 61
        if (data.Length >= 6 &&
            data[0] == 0x47 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x38 &&
            (data[4] == 0x37 || data[4] == 0x39) && data[5] == 0x61)
        {
            return "GIF";
        }

        // BMP: 42 4D
        if (data.Length >= 2 &&
            data[0] == 0x42 && data[1] == 0x4D)
        {
            return "BMP";
        }

        // TIFF: II*. ou MM.* (Little Endian ou Big Endian)
        if (data.Length >= 4 &&
            ((data[0] == 0x49 && data[1] == 0x49 && data[2] == 0x2A && data[3] == 0x00) ||
             (data[0] == 0x4D && data[1] == 0x4D && data[2] == 0x00 && data[3] == 0x2A)))
        {
            return "TIFF";
        }
        
        // WebP: RIFF ???? WEBP
        if (data.Length >= 12 &&
            data[0] == 'R' && data[1] == 'I' && data[2] == 'F' && data[3] == 'F' && /* File size */
            data[8] == 'W' && data[9] == 'E' && data[10] == 'B' && data[11] == 'P')
        {
            return "WebP";
        }

        return "Unknown";
    }
    
    // TODO: Refactor this to use cameraManager
    private async Task<SKImage> ExtractImageFromBufferAsync(ArraySegment<byte> imageSegment)
    {
        if (imageSegment.Array == null || imageSegment.Count == 0)
        {
            throw new ArgumentException("Invalid image segment.");
        }

        if (_pixelFormat == PixelFormats.JPEG)
        {
            using var skData = SKData.CreateCopy(imageSegment);
            using var skCodec = SKCodec.Create(skData);

            if (skCodec == null)
            {
                throw new InvalidOperationException("Failed to create SKCodec from image data.");
            }

            var info = skCodec.Info;
            var imageInfo = new SKImageInfo(info.Width, info.Height, SKColorType.Bgra8888, SKAlphaType.Premul);

            using var bitmap = new SKBitmap(imageInfo);
            var result = skCodec.GetPixels(bitmap.Info, bitmap.GetPixels());

            if (result != SKCodecResult.Success)
            {
                throw new InvalidOperationException($"Failed to decode image: {result}");
            }

            return SKImage.FromBitmap(bitmap);
        }

        if (_pixelFormat == PixelFormats.BGRA32)
        {
             byte[] bgraBytesCorrected = new byte[0];
            
            // SKColorType.Bgra8888 implies 4 bytes per pixel (B, G, R, A)
            // Use ViewModel properties for dimensions
            SKImageInfo desiredInfo = new SKImageInfo(this.VideoWidth, this.VideoHeight, SKColorType.Bgra8888,
                SKAlphaType.Premul);
            int bytesPerPixel = 4;
            
            // Width of a row in bytes.
            int rowWidthBytes = desiredInfo.Width * bytesPerPixel;
            long expectedPixelDataSize = (long)desiredInfo.Height * rowWidthBytes;
            
                
            // headerSize is 54 for a standard BMP file header.
            // If FlashCap provides raw pixel data (e.g., for ARGB32 that we treat as BGRA)
            // without a BMP file structure, this should be 0.
            const int headerSize = 54;
        
            if (imageSegment.Array == null || imageSegment.Count < headerSize + expectedPixelDataSize)
            {
                Logger.Error(
                    $"Image segment invalid or too small. Needed after header: {expectedPixelDataSize}, available: {imageSegment.Count - headerSize}. Total segment count: {imageSegment.Count}");
                throw new Exception("Image segment invalid or too small.");

            }

            bgraBytesCorrected = new byte[expectedPixelDataSize];

            int sourcePixelDataStartOffset = imageSegment.Offset + headerSize;

            // Copy row by row, and flip pixels horizontally within each row.
            // Assumes source is top-down BGRA after the header.
            for (int y = 0; y < desiredInfo.Height; y++)
            {
                int sourceRowStart = sourcePixelDataStartOffset + (y * rowWidthBytes);
                int destRowStart = y * rowWidthBytes;

                for (int x = 0; x < desiredInfo.Width; x++)
                {
                    // Source pixel (from right to left)
                    int sourcePixelOffset = sourceRowStart + ((desiredInfo.Width - 1 - x) * bytesPerPixel);
                    // Destination pixel (from left to right)
                    int destPixelOffset = destRowStart + (x * bytesPerPixel);

                    // Check bounds (important if source stride might differ or calculations are off)
                    if (sourcePixelOffset + bytesPerPixel > imageSegment.Offset + imageSegment.Count ||
                        destPixelOffset + bytesPerPixel > bgraBytesCorrected.Length)
                    {
                        Logger.Error(
                            $"Pixel access out of bounds. Y: {y}, X_dest: {x}, X_src: {desiredInfo.Width - 1 - x}. SourceOffset: {sourcePixelOffset}, DestOffset: {destPixelOffset}");
                        throw new Exception("Pixel access out of bounds.");
                    }

                    // Copy BGRA pixel (4 bytes)
                    bgraBytesCorrected[destPixelOffset + 0] = imageSegment.Array[sourcePixelOffset + 0]; // B
                    bgraBytesCorrected[destPixelOffset + 1] = imageSegment.Array[sourcePixelOffset + 1]; // G
                    bgraBytesCorrected[destPixelOffset + 2] = imageSegment.Array[sourcePixelOffset + 2]; // R
                    bgraBytesCorrected[destPixelOffset + 3] = imageSegment.Array[sourcePixelOffset + 3]; // A
                }
            }
            
            GCHandle handle = GCHandle.Alloc(bgraBytesCorrected, GCHandleType.Pinned);
            try
            {
                IntPtr ptr = handle.AddrOfPinnedObject();
                // The stride for SKPixmap is rowWidthBytes, as bgraBytesCorrected is compact.
                using SKPixmap pixmap = new SKPixmap(desiredInfo, ptr, rowWidthBytes);
                var skImage = SKImage.FromPixels(pixmap);
                
                return skImage;
            }
            finally
            {
                handle.Free();
            }
            
            
        }
        
        throw new NotSupportedException("Unsupported pixel format.");

    }

    private async Task OnPixelBufferArrivedAsync(PixelBufferScope bufferScope)
    {
        if (_cts.IsCancellationRequested)
        {
            bufferScope.ReleaseNow();
            return;
        }

        try
        {
            frameCount++;
            var identifyFace = false;
            
            if (frameCount % 50 == 0)
            {
                Logger.Debug($"Processing frame {frameCount} at {DateTime.Now}");
            }

            if (frameCount % 100 == 0)
            {
                identifyFace = true;
            }

            if (frameCount > 10000000)
            {
                frameCount = 0;
            }

            
            ArraySegment<byte> imageSegment = bufferScope.Buffer.ReferImage();
            
            var skImage = await ExtractImageFromBufferAsync(imageSegment);

            if (!await _imageUpdateLock.WaitAsync(0, _cts.Token))
            {
                bufferScope.ReleaseNow();
                return;
            }

            try
            {
                if (_cts.IsCancellationRequested)
                {
                    bufferScope.ReleaseNow();
                    return;
                }
                
                using (skImage)
                using (SKData? encodedData = skImage.Encode(SKEncodedImageFormat.Jpeg, 90))
                {
                    if (identifyFace)
                    {
                        var faceAligned = false;
                        var bitmap = SKBitmap.FromImage(skImage);
                        using var dnnDetector = new FaceDetector();
                        var faces = dnnDetector.Forward(new SkiaDrawing.Bitmap(bitmap));
                        if (faces.Any())
                        {
                            
                            var biggerFace = faces.OrderByDescending(f => f.Box.Width * f.Box.Height).FirstOrDefault();
                            
                            if(biggerFace == null) throw new Exception("No face found.");

                            if (biggerFace.Box.Left > VideoWidth / 4 && biggerFace.Box.Right < VideoWidth * 3 / 4 &&
                                biggerFace.Box.Top > VideoHeight / 8  && biggerFace.Box.Bottom < VideoHeight * 7 / 8 )
                            {
                                faceAligned = true;
                                Logger.Debug($"Face detected at {biggerFace.Box.Left}, {biggerFace.Box.Top}, {biggerFace.Box.Right}, {biggerFace.Box.Bottom}");
                                _faceDetectionResult = biggerFace;
                                _detectedFaceImage = bitmap;

                            }
                            else
                            {
                                faceAligned = false;
                            }
                            
                            LocatorImage = faceAligned ? new Bitmap(AssetLoader.Open(new Uri("avares://GUIClient/Assets/face-mask-green.png"))) : new Bitmap(AssetLoader.Open(new Uri("avares://GUIClient/Assets/facemask.png")));
                            BtSaveEnabled = faceAligned;
                            
                            
                            Logger.Debug($"Detected {faces.Length} face(s) in the image.");
                        }
                        else
                        {
                            Logger.Debug("No faces detected in the image.");
                        }
                    }
                    
                    if (encodedData == null)
                    {
                        Logger.Error("Failed to encode SKImage to JPEG.");
                        bufferScope.ReleaseNow();
                        return;
                    }
                    using MemoryStream memoryStream = new MemoryStream();
                    encodedData.SaveTo(memoryStream);
                    memoryStream.Position = 0;

                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (!_cts.IsCancellationRequested)
                        {
                            (_image as IDisposable)?.Dispose();
                            Image = new Bitmap(memoryStream);
                        }
                    }, DispatcherPriority.Background);
                }
            }
            finally
            {
                _imageUpdateLock.Release();
            }
        }
        catch (OperationCanceledException)
        {
            Logger.Debug("Pixel buffer processing canceled.");
        }
        catch (Exception ex)
        {
            Logger.Error($"Error processing pixel buffer: {ex.Message} StackTrace: {ex.StackTrace}");
        }
        finally
        {
            bufferScope.ReleaseNow();
        }
    }
    
    private async Task ShowErrorMessageAsync(string message)
    {
        var msgError = MessageBoxManager.GetMessageBoxStandard(new MessageBoxStandardParams
        {
            ContentTitle = Localizer["Error"],
            ContentMessage = message,
            Icon = Icon.Error,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        });
        await msgError.ShowAsync();
    }

    private async void ParentWindowOnClosed(object? sender, EventArgs e)
    {
        await DisposeAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _cts.Cancel();

        if (ParentWindow != null)
        {
            ParentWindow.Closed -= ParentWindowOnClosed;
        }

        if (_captureDevice != null)
        {
            if (_captureDevice.IsRunning)
            {
                try
                {
                    await _captureDevice.StopAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error stopping capture device: {ex.Message}");
                }
            }
            _captureDevice.Dispose();
            _captureDevice = null;
        }
        
        
        _cts.Dispose();
        
        DeviceList.Clear();
        CharacteristicsList.Clear();
        Logger.Debug("AddFaceImageViewModel disposed.");
        _imageUpdateLock.Dispose();
    }
    #endregion
}