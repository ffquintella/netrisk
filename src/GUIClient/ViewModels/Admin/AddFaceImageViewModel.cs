using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
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
using PixelFormats = FlashCap.PixelFormats;

namespace GUIClient.ViewModels.Admin;

public class AddFaceImageViewModel : ViewModelBase, IAsyncDisposable
{
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
    #endregion

    #region CONSTRUCTOR
    public AddFaceImageViewModel(int userId, Window parentWindow)
    {
        UserId = userId;
        ParentWindow = parentWindow ?? throw new ArgumentNullException(nameof(parentWindow));
        ParentWindow.Closed += ParentWindowOnClosed;

        _pixelBufferDelegate = OnPixelBufferArrivedAsync;
        Image = new Bitmap(AssetLoader.Open(new Uri("avares://GUIClient/Assets/placeholder.png")));
        _ = InitializeAsync();
    }
    #endregion

    #region METHODS

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

            CharacteristicsList = new ObservableCollection<VideoCharacteristics>(
                Device.Characteristics.Where(c => c.PixelFormat == PixelFormats.ARGB32));

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

        _captureDevice = await Device.OpenAsync(Characteristics, _pixelBufferDelegate).ConfigureAwait(false);
        if (_captureDevice == null)
        {
            throw new InvalidOperationException("Error initializing camera: Failed to open device.");
        }
        await _captureDevice.StartAsync().ConfigureAwait(false);
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
            ArraySegment<byte> imageSegment = bufferScope.Buffer.ReferImage();
            SKImageInfo desiredInfo = new SKImageInfo(VideoWidth, VideoHeight, SKColorType.Bgra8888, SKAlphaType.Premul);

            const int headerSize = 54; // Assuming BMP header for ARGB32 from FlashCap
            if (imageSegment.Array == null || imageSegment.Count - headerSize < desiredInfo.BytesSize)
            {
                return; // Buffer too small or invalid
            }

            byte[] rgbaBytes = new byte[desiredInfo.BytesSize];
            Buffer.BlockCopy(imageSegment.Array, imageSegment.Offset + headerSize, rgbaBytes, 0, desiredInfo.BytesSize);

            if (!await _imageUpdateLock.WaitAsync(0, _cts.Token).ConfigureAwait(false)) // Try to acquire lock immediately
            {
                 return; // Skip frame if update is busy or cancelled
            }

            try
            {
                if (_cts.IsCancellationRequested) return;

                GCHandle handle = GCHandle.Alloc(rgbaBytes, GCHandleType.Pinned);
                SKImage? skImage = null;
                try
                {
                    IntPtr ptr = handle.AddrOfPinnedObject();
                    using SKPixmap pixmap = new SKPixmap(desiredInfo, ptr, desiredInfo.RowBytes);
                    skImage = SKImage.FromPixels(pixmap);
                }
                finally
                {
                    handle.Free();
                }

                if (skImage == null) return;

                using (skImage)
                using (SKData? encodedData = skImage.Encode(SKEncodedImageFormat.Jpeg, 90))
                {
                    if (encodedData == null) return;
                    using MemoryStream memoryStream = new MemoryStream();
                    encodedData.SaveTo(memoryStream);
                    memoryStream.Position = 0;

                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (!_cts.IsCancellationRequested) // Double check before UI update
                        {
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
            // Log if necessary, but often just means processing is stopping
        }
        catch (Exception ex)
        {
            Logger.Error($"Error in pixel buffer processing: {ex.Message}");
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
        
        _imageUpdateLock.Dispose();
        _cts.Dispose();
        
        DeviceList.Clear();
        CharacteristicsList.Clear();
        Logger.Debug("AddFaceImageViewModel disposed.");
    }
    #endregion
}