using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ClientServices.Interfaces;
using FlashCap;
using GUIClient.Exceptions;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Serilog;
using SkiaSharp;
using ILogger = Serilog.ILogger;

namespace GUIClient.Tools.Camera;

public class CameraManager : IDisposable
{
    private ILogger Logger { get; }
    private IFaceIDService FaceIDService { get; }
    private IStringLocalizer Localizer { get; }

    private PixelFormats _pixelFormat = PixelFormats.BGRA32;

    private List<CaptureDeviceDescriptor> DeviceList { get; set; } = new List<CaptureDeviceDescriptor>();
    private List<VideoCharacteristics> CharacteristicsList { get; set; } = new List<VideoCharacteristics>();

    private CaptureDeviceDescriptor? Device { get; set; }

    public VideoCharacteristics? Characteristics;

    private CaptureDevice? _captureDevice;

    private PixelBufferArrivedTaskDelegate _pixelBufferDelegate;

    public CameraManager(ILoggerFactory loggerFactory, IFaceIDService faceIDService, IStringLocalizer localizer)
    {
        Logger = Log.Logger;
        FaceIDService = faceIDService;
        Localizer = localizer;
    }

    public async Task StartCameraAsync(PixelBufferArrivedTaskDelegate pixelBufferDelegate)
    {
        _pixelBufferDelegate = pixelBufferDelegate ?? throw new ArgumentNullException(nameof(pixelBufferDelegate));

        Logger.Information("Starting camera...");

        if (_captureDevice != null)
        {
            Logger.Warning("Camera is already running.");
            return;
        }

        try
        {
            await InitializeAsync();
            await EnableCameraAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.Error($"Error starting camera: {ex.Message}");
            throw new CameraError(ex.Message);
        }
    }

    public async Task StopCameraAsync()
    {
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
    }

    public async Task CleanResourcesAsync()
    {
        await StopCameraAsync();

        _pixelBufferDelegate = null;
        DeviceList.Clear();
        CharacteristicsList.Clear();
        Device = null;
        Characteristics = null;
    }

    private async Task InitializeAsync()
    {
        try
        {
            var faceIdInfo = await FaceIDService.GetInfo().ConfigureAwait(false);
            if (!faceIdInfo.IsServiceAvailable)
            {
                throw new CameraError(Localizer["FaceIDNotAvaliableMSG"]);
            }

            var devices = new CaptureDevices();
            IEnumerable<CaptureDeviceDescriptor> descriptors = Enumerable.Empty<CaptureDeviceDescriptor>();

            for (int t = 0; t < 10; t++)
            {
                descriptors = devices.EnumerateDescriptors();
                if (descriptors.Any()) break;
                if (t < 9) await Task.Delay(1000).ConfigureAwait(false);
            }

            if (!descriptors.Any())
            {
                throw new CameraError(Localizer["CameraNotFoundMSG"]);
            }

            DeviceList = new List<CaptureDeviceDescriptor>(
                descriptors.Where(d => d.Characteristics.Length >= 1));
            Device = DeviceList.FirstOrDefault();

            if (Device == null)
            {
                throw new CameraError("No suitable camera device found.");
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                CharacteristicsList = new List<VideoCharacteristics>(
                    Device.Characteristics.Where(c => c.PixelFormat == PixelFormats.JPEG));
                _pixelFormat = PixelFormats.JPEG;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                CharacteristicsList = new List<VideoCharacteristics>(
                    Device.Characteristics.Where(c => c.PixelFormat == PixelFormats.BGRA32));
                _pixelFormat = PixelFormats.BGRA32;
            }

            if (!CharacteristicsList.Any())
            {
                throw new CameraError("No ARGB32 characteristics found for the selected camera.");
            }

            Characteristics = CharacteristicsList.FirstOrDefault(c => c.Width > 900) ?? CharacteristicsList.FirstOrDefault();

            if (Characteristics == null)
            {
                throw new CameraError("No suitable video characteristics found.");
            }
        }
        catch (OperationCanceledException)
        {
            Logger.Debug("Initialization was canceled.");
        }
        catch (Exception ex)
        {
            Logger.Error($"Initialization error: {ex.Message}");
            throw new CameraError(ex.Message);
        }
    }

    private async Task EnableCameraAsync()
    {
        if (Device == null || Characteristics == null) return;

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

    public async Task<SKImage> ExtractImageFromBufferAsync(PixelBufferScope bufferScope)
    {
        if (bufferScope == null || bufferScope.Buffer == null)
        {
            throw new CameraError("Invalid pixel buffer scope.");
        }

        try
        {
            ArraySegment<byte> imageSegment = bufferScope.Buffer.ReferImage();
            if (imageSegment.Array == null || imageSegment.Count == 0)
            {
                throw new ArgumentException("Invalid image segment.");
            }

            if (_pixelFormat == PixelFormats.BGRA32)
            {
                byte[] bgraBytesCorrected = new byte[0];

                if (Characteristics == null) throw new Exception("Invalid characteristics.");
                SKImageInfo desiredInfo = new SKImageInfo(Characteristics.Width, Characteristics.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
                int bytesPerPixel = 4;

                int rowWidthBytes = desiredInfo.Width * bytesPerPixel;
                long expectedPixelDataSize = (long)desiredInfo.Height * rowWidthBytes;

                const int headerSize = 54;

                if (imageSegment.Array == null || imageSegment.Count < headerSize + expectedPixelDataSize)
                {
                    Logger.Error($"Image segment invalid or too small. Needed after header: {expectedPixelDataSize}, available: {imageSegment.Count - headerSize}. Total segment count: {imageSegment.Count}");
                    throw new Exception("Image segment invalid or too small.");
                }

                bgraBytesCorrected = new byte[expectedPixelDataSize];

                int sourcePixelDataStartOffset = imageSegment.Offset + headerSize;

                for (int y = 0; y < desiredInfo.Height; y++)
                {
                    int sourceRowStart = sourcePixelDataStartOffset + (y * rowWidthBytes);
                    int destRowStart = y * rowWidthBytes;

                    for (int x = 0; x < desiredInfo.Width; x++)
                    {
                        int sourcePixelOffset = sourceRowStart + ((desiredInfo.Width - 1 - x) * bytesPerPixel);
                        int destPixelOffset = destRowStart + (x * bytesPerPixel);

                        if (sourcePixelOffset + bytesPerPixel > imageSegment.Offset + imageSegment.Count ||
                            destPixelOffset + bytesPerPixel > bgraBytesCorrected.Length)
                        {
                            Logger.Error($"Pixel access out of bounds. Y: {y}, X_dest: {x}, X_src: {desiredInfo.Width - 1 - x}. SourceOffset: {sourcePixelOffset}, DestOffset: {destPixelOffset}");
                            throw new Exception("Pixel access out of bounds.");
                        }

                        bgraBytesCorrected[destPixelOffset + 0] = imageSegment.Array[sourcePixelOffset + 0];
                        bgraBytesCorrected[destPixelOffset + 1] = imageSegment.Array[sourcePixelOffset + 1];
                        bgraBytesCorrected[destPixelOffset + 2] = imageSegment.Array[sourcePixelOffset + 2];
                        bgraBytesCorrected[destPixelOffset + 3] = imageSegment.Array[sourcePixelOffset + 3];
                    }
                }

                GCHandle handle = GCHandle.Alloc(bgraBytesCorrected, GCHandleType.Pinned);
                try
                {
                    IntPtr ptr = handle.AddrOfPinnedObject();
                    using SKPixmap pixmap = new SKPixmap(desiredInfo, ptr, rowWidthBytes);
                    return SKImage.FromPixels(pixmap);
                }
                finally
                {
                    handle.Free();
                }
            }

            throw new NotSupportedException("Unsupported pixel format.");
        }
        catch (Exception ex)
        {
            Logger.Error($"Error extracting image: {ex.Message}");
            throw new CameraError(ex.Message);
        }
    }

    public async Task<Bitmap> ExtractImageAsync(PixelBufferScope bufferScope)
    {
        if (bufferScope == null || bufferScope.Buffer == null)
        {
            throw new CameraError("Invalid pixel buffer scope.");
        }
        using var skImage = await ExtractImageFromBufferAsync(bufferScope).ConfigureAwait(false);
        using (SKData? encodedData = skImage.Encode(SKEncodedImageFormat.Jpeg, 100))
        {
            using MemoryStream memoryStream = new MemoryStream();
            encodedData.SaveTo(memoryStream);
            memoryStream.Position = 0;
            return new Bitmap(memoryStream);
        }
    }

    public void Dispose()
    {
        try
        {
            if (_captureDevice != null)
            {
                if (_captureDevice.IsRunning)
                {
                    _captureDevice.StopAsync().GetAwaiter().GetResult();
                }
                _captureDevice.Dispose();
                _captureDevice = null;
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Error disposing capture device: {ex.Message}");
        }

        DeviceList.Clear();
        CharacteristicsList.Clear();
        Device = null;
        Characteristics = null;
        _pixelBufferDelegate = null;
    }
}