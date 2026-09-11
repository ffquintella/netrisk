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
    public string StrPackage { get;  } = Localizer["Package"];
    public string StrDeletePlugin { get;  } = Localizer["DeletePlugin"];
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

    private bool _isDeleting;

    /// <summary>
    /// Drives the delete buttons' enabled state. A removal deletes a directory and reloads every
    /// plugin in the API process, so two of them running over each other is worth preventing here as
    /// well as on the server.
    /// </summary>
    public bool IsDeleting
    {
        get => _isDeleting;
        set => this.RaiseAndSetIfChanged(ref _isDeleting, value);
    }

    public bool IsNotDeleting => !IsDeleting;

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

        this.WhenAnyValue(x => x.IsDeleting)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(IsNotDeleting)));
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

    /// <summary>
    /// Removes one plugin from the server, after confirmation.
    /// </summary>
    /// <remarks>
    /// The confirmation names the plugin and its version because the row is the only place the
    /// operator sees which installation they are deleting, and the server's own message is shown
    /// afterwards: a removal whose files are still locked by the API process succeeds but leaves them
    /// on disk until the next restart, and that is not something to hide behind "Deleted".
    /// </remarks>
    public async Task DeletePluginAsync(PluginInfo plugin)
    {
        if (IsDeleting) return;

        var confirm = await Dispatcher.UIThread.InvokeAsync(() => MessageBoxManager
            .GetMessageBoxStandard(new MessageBoxStandardParams
            {
                ContentTitle = Localizer["Warning"],
                ContentMessage = string.Format(Localizer["DeletePluginConfirmMSG"], plugin.Name, plugin.Version),
                Icon = Icon.Warning,
                ButtonDefinitions = ButtonEnum.YesNo
            })
            .ShowAsync());

        if (confirm != ButtonResult.Yes) return;

        IsDeleting = true;

        try
        {
            var result = await PluginsService.UninstallPluginAsync(plugin.Name);

            await LoadPluginsAsync();

            if (!result.Success)
            {
                await ShowMessageAsync(Localizer["Error"],
                    Localizer["ErrorDeletingPluginMSG"] + "\n\n" + result.Message, Icon.Error);
                return;
            }

            await ShowMessageAsync(Localizer["Information"],
                Localizer["PluginDeletedMSG"] + "\n\n" + result.Message, Icon.Info);
        }
        catch (Exception ex)
        {
            Logger.Error("Error deleting plugin: {Message}", ex.Message);
            await ShowMessageAsync(Localizer["Error"],
                Localizer["ErrorDeletingPluginMSG"] + "\n\n" + ex.Message, Icon.Error);
        }
        finally
        {
            IsDeleting = false;
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
