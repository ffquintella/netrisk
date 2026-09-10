using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ClientServices.Interfaces;
using Contracts;
using GUIClient.Tools;
using Model.Plugins;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;
using ReactiveUI;


namespace GUIClient.ViewModels.Admin;

public class PluginsViewModel: ViewModelBase
{
    #region LANGUAGE
    public string StrTitle { get;  } = Localizer["Plugins"];
    public string StrName { get;  } = Localizer["Name"];
    public string StrDescription { get;  } = Localizer["Description"];
    public string StrEnabled { get;  } = Localizer["Enabled"];
    public string StrVersion { get;  } = Localizer["Version"];
    public string StrUploadPlugin { get;  } = Localizer["UploadPlugin"];
    public string StrSelectPluginPackage { get;  } = Localizer["SelectPluginPackageMSG"];
    public string StrPluginPackages { get;  } = Localizer["PluginPackages"];
    #endregion
    
    #region PROPERTIES
    private ObservableCollection<PluginInfo> _pluginsList = new();
    
    public ObservableCollection<PluginInfo> PluginsList
    {
        get => _pluginsList;
        set => this.RaiseAndSetIfChanged(ref _pluginsList, value);
    }

    private bool _isUploading;

    /// <summary>
    /// Drives the upload button's enabled state. An install unpacks an archive and reloads every
    /// plugin in the API process, so a second one started on top of the first is worth preventing
    /// in the UI rather than only on the server.
    /// </summary>
    public bool IsUploading
    {
        get => _isUploading;
        set => this.RaiseAndSetIfChanged(ref _isUploading, value);
    }

    public bool IsNotUploading => !IsUploading;

    #endregion

    #region SERVICES
    private IPluginsService _pluginsService = null!;
    public IPluginsService PluginsService
    {
        get => _pluginsService;
        set => this.RaiseAndSetIfChanged(ref _pluginsService, value);
    }

    

    #endregion
    
    #region CONSTRUCTOR
    public PluginsViewModel(IPluginsService pluginsService)
    {
        PluginsService = pluginsService;

        this.WhenAnyValue(x => x.IsUploading)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(IsNotUploading)));
    }
    #endregion
    
    #region EVENTS
    
    
    #endregion
    
    #region BUTTONS

    public async Task ReloadPluginsCommand()
    {
        await PluginsService.RequestPluginsReloadAsync();
        await LoadPluginsAsync();
    }

    /// <summary>
    /// Picks a plugin package (.zip) and hands it to the server, which unpacks it into its plugins
    /// directory and reloads.
    /// </summary>
    public async Task UploadPluginCommand()
    {
        if (IsUploading) return;

        var storageProvider = StorageProviderAccessor.Current;
        if (storageProvider == null) return;

        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = StrSelectPluginPackage,
            AllowMultiple = false,
            FileTypeFilter = new List<FilePickerFileType>
            {
                new(StrPluginPackages) { Patterns = new[] { "*.zip" } }
            }
        });

        if (files.Count == 0) return;

        await UploadPluginAsync(files[0].Path.LocalPath);
    }

    #endregion
    
    #region METHODS

    public void Initialize()
    {
        _ = LoadPluginsAsync();
    }
    
    private  Task LoadPluginsAsync()
    {
        return Task.Run(async () =>
        {
            var plugins = await PluginsService.GetPluginsAsync();
            PluginsList = new ObservableCollection<PluginInfo>(plugins);
        });
    }

    /// <summary>
    /// Sends one package and reports the outcome.
    /// </summary>
    /// <remarks>
    /// The server's own message is shown verbatim on refusal. Every rejection it produces names
    /// something the operator can change — the archive has no <c>*Plugin.dll</c> at its top level,
    /// it ships <c>Contracts.dll</c>, the file name gives no usable directory name — and replacing
    /// those with a house error string would leave nothing to act on.
    /// </remarks>
    public async Task UploadPluginAsync(string filePath)
    {
        IsUploading = true;

        try
        {
            var result = await PluginsService.UploadPluginAsync(filePath);

            if (!result.Success)
            {
                await ShowMessageAsync(Localizer["Error"],
                    Localizer["ErrorUploadingPluginMSG"] + "\n\n" + result.Message, Icon.Error);
                return;
            }

            await LoadPluginsAsync();

            await ShowMessageAsync(Localizer["Information"],
                Localizer["PluginInstalledMSG"] + "\n\n" + result.Message, Icon.Info);
        }
        catch (Exception ex)
        {
            Logger.Error("Error uploading plugin: {Message}", ex.Message);
            await ShowMessageAsync(Localizer["Error"],
                Localizer["ErrorUploadingPluginMSG"] + "\n\n" + ex.Message, Icon.Error);
        }
        finally
        {
            IsUploading = false;
        }
    }

    private static Task ShowMessageAsync(string title, string message, Icon icon)
    {
        return Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await MessageBoxManager
                .GetMessageBoxStandard(new MessageBoxStandardParams
                {
                    ContentTitle = title,
                    ContentMessage = message,
                    Icon = icon
                })
                .ShowAsync();
        });
    }

    public void SetPluginEnabledStatus(string pluginName, bool enabled)
    {
       _= PluginsService.SetPluginEnabledAsync(pluginName, enabled);
    }
    #endregion
}
