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
using ClientServices.Interfaces;
using ReactiveUI;
using FlashCap;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;
using SkiaSharp;
using PixelFormats = FlashCap.PixelFormats;

namespace GUIClient.ViewModels.Admin;

public class AddFaceImageViewModel: ViewModelBase
{
    #region PROPERTIES
        private int UserId { get; set; }
        private Window ParentWindow { get; set; }
        
        private Bitmap _image = null!;

        public Bitmap Image
        {
            get => _image;
            set => this.RaiseAndSetIfChanged(ref _image, value);
        }
        
        private ObservableCollection<CaptureDeviceDescriptor> _deviceList = new ObservableCollection<CaptureDeviceDescriptor>();
    
        public ObservableCollection<CaptureDeviceDescriptor> DeviceList
        {
            get => _deviceList;
            set => this.RaiseAndSetIfChanged(ref _deviceList, value);
        }
        
        private CaptureDeviceDescriptor? _device;
    
        public CaptureDeviceDescriptor? Device
        {
            get => _device;
            set => this.RaiseAndSetIfChanged(ref _device, value);
        }
        
        private ObservableCollection<VideoCharacteristics> _characteristicsList = new ObservableCollection<VideoCharacteristics>();
    
        public ObservableCollection<VideoCharacteristics> CharacteristicsList
        {
            get => _characteristicsList;
            set => this.RaiseAndSetIfChanged(ref _characteristicsList, value);
        }

        private VideoCharacteristics? _characteristics;
    
        public VideoCharacteristics? Characteristics
        {
            get => _characteristics;
            set => this.RaiseAndSetIfChanged(ref _characteristics, value);
        }
    
        public int VideoWidth { get; }= 600;
        public int VideoHeight  { get; }= 600;
        
    #endregion
    
    #region SERVICES

    private IFaceIDService FaceIDService { get; } = GetService<IFaceIDService>();
    
    #endregion
    
    #region FIELDS
    // Constructed capture device.
    private CaptureDevice? _captureDevice;
    #endregion
    
    #region CONSTRUCTOR
    public AddFaceImageViewModel(int userId, Window parentWindow)
    {
        ParentWindow = parentWindow;
        
        ParentWindow.Closed += ParentWindowOnClosed;
        
        UserId = userId;
        Image = new Bitmap(AssetLoader.Open(new Uri("avares://GUIClient/Assets/placeholder.png")));
        _ = InitializeAsync();
    }

    private void ParentWindowOnClosed(object? sender, EventArgs e)
    {
        // Unsubscribe from the event to avoid memory leaks
        ParentWindow.Closed -= ParentWindowOnClosed;
        // Dispose of the view model
        // This will call the Dispose method below
        if(_captureDevice != null) _captureDevice.StopAsync().ConfigureAwait(true).GetAwaiter().GetResult();
        Dispose();
    }

    #endregion
    
    #region METHODS
    
    public new void Dispose()
    {
        // Dispose of any resources if needed
        // For example, if you have any subscriptions or event handlers, unsubscribe them here
        // Dispose of the camera if it is open
        if (_captureDevice != null)
        {
            _captureDevice.Dispose();
            _captureDevice = null;
        }
        DeviceList.Clear();
        CharacteristicsList.Clear();
    }

    private async Task InitializeAsync()
    {

        if ((await FaceIDService.GetInfo()).IsServiceAvailable == false)
        {
            var msgError = MessageBoxManager
                .GetMessageBoxStandard(new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Error"],
                    ContentMessage = Localizer["FaceIDNotAvaliableMSG"],
                    Icon = Icon.Error,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });

            await msgError.ShowAsync();

            ParentWindow.Close();
            return;
        }

        var devices = new CaptureDevices();

        try
        {
            int t = 0;

            IEnumerable<CaptureDeviceDescriptor> descriptors = new CaptureDeviceDescriptor[0];

            while (t < 10)
            {
                descriptors = devices.EnumerateDescriptors();
                if (descriptors.Any()) break;
                t++;
                Thread.Sleep(1000);
            }

            if (descriptors is null)
            {

                var msgError = MessageBoxManager
                    .GetMessageBoxStandard(new MessageBoxStandardParams
                    {
                        ContentTitle = Localizer["Error"],
                        ContentMessage = Localizer["CameraNotFoundMSG"],
                        Icon = Icon.Error,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner
                    });

                await msgError.ShowAsync();
                ParentWindow.Close();
                return;
                //throw new Exception("Could not find any devices");
            }


            Logger.Debug($"Found {descriptors.Count()} devices");

            foreach (var descriptor in descriptors.
                         // You could filter by device type and characteristics.
                         //Where(d => d.DeviceType == DeviceTypes.DirectShow).  // Only DirectShow device.
                         Where(d => d.Characteristics.Length >= 1)) // One or more valid video characteristics.
            {
                DeviceList.Add(descriptor);
            }

            Device = DeviceList.FirstOrDefault();
        }
        catch (Exception ex)
        {
            Logger.Error(ex.Message);
        }

        if (Device == null) throw new Exception("No Device Found");

        CharacteristicsList = new ObservableCollection<VideoCharacteristics>();
        foreach (var characteristics in Device.Characteristics.Where(c => c.PixelFormat == PixelFormats.ARGB32))
        {
            if (characteristics.PixelFormat != PixelFormats.Unknown)
            {
                this.CharacteristicsList.Add(characteristics);
            }
        }

        if (CharacteristicsList is null or { Count: <= 0 }) throw new Exception("Invalid camera");

        Characteristics = CharacteristicsList.FirstOrDefault(c => c is { Width: > 900 });

        await EnableCamera().ConfigureAwait(true);

        

    }
    
    
    private async Task EnableCamera()
    {
        
        try 
        {
            if (this.Device is { } descriptor && Characteristics is { })
            {
                this._captureDevice = await descriptor.OpenAsync(
                    Characteristics,
                    this.OnPixelBufferArrivedAsync);
        
                if (this._captureDevice == null)
                {
                    throw new InvalidOperationException("Error initializing camera");
                }
        
                await this._captureDevice.StartAsync().ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Error initializing camera: {ex.Message}");
            throw;
        }
        
    }

    private async Task OnPixelBufferArrivedAsync(PixelBufferScope bufferScope)
    {
        try
        {
            Console.WriteLine("Buffer arrived");
            return;
            ArraySegment<byte> imageSegment = bufferScope.Buffer.ReferImage();

            SKImageInfo desiredInfo =
                new SKImageInfo(VideoWidth, VideoHeight, SKColorType.Bgra8888, SKAlphaType.Premul);

            if (imageSegment.Count - 54 < desiredInfo.BytesSize)
                throw new ArgumentException("RGB buffer is too small for the given dimensions.");

            if (imageSegment.Array == null || imageSegment.Count <= 54)
            {
                return;
            }

            // Allocate RGBA output buffer
            byte[] rgbaBytes = new byte[desiredInfo.BytesSize];

            int offset = imageSegment.Offset + 54; // Skip BMP header

            for (int i = 0; i < desiredInfo.BytesSize; i += 4)
            {

                // This is in ARGB format
                if (imageSegment.Array != null)
                {
                    var X = imageSegment.Array[offset + i + 0];
                    var Y = imageSegment.Array[offset + i + 1];
                    var Z = imageSegment.Array[offset + i + 2];
                    var W = imageSegment.Array[offset + i + 3];


                    rgbaBytes[i + 0] = X; // B
                    rgbaBytes[i + 1] = Y; // G
                    rgbaBytes[i + 2] = Z; // R
                    rgbaBytes[i + 3] = W; // A
                }
            }

            SKImage? image;
            GCHandle handle = GCHandle.Alloc(rgbaBytes, GCHandleType.Pinned);
            try
            {
                IntPtr ptr = handle.AddrOfPinnedObject();
                using (SKPixmap pixmap = new SKPixmap(desiredInfo, ptr, desiredInfo.RowBytes))
                {
                    image = SKImage.FromPixels(pixmap);
                }
            }
            finally
            {
                handle.Free();
            }
            
            if(image == null) throw new System.Exception("No image");
        
            await ProcessImageAsync(image);
        }
        catch (Exception ex)
        {
            Logger.Error($"Error in buffer processing: {ex.Message}");
        }
        finally
        {
            bufferScope.ReleaseNow();
        }

        // `bitmap` is copied, so we can release pixel buffer now.
        //bufferScope.ReleaseNow();
        

    }

    private Task ProcessImageAsync(SKImage skImage)
    {
        var encodedData = skImage.Encode(SKEncodedImageFormat.Jpeg, 90);
            
        using MemoryStream memoryStream = new MemoryStream();
        // Write to memory stream
        encodedData.SaveTo(memoryStream);
        memoryStream.Position = 0;

        Image = new Bitmap(memoryStream);
        encodedData?.Dispose();
        
        return Task.CompletedTask;
    }

    #endregion
    


    
}