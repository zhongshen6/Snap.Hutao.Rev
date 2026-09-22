// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Snap.Hutao.Factory.ContentDialog;
using Snap.Hutao.Service.Game;
using Snap.Hutao.Service.Game.Island;
using Snap.Hutao.Service.Notification;
using System.Collections.ObjectModel;
using System.Globalization;

namespace Snap.Hutao.UI.Xaml.View.Dialog;

[DependencyProperty<string>("StatusText", DefaultValue = "")]
[DependencyProperty<string>("BaseButtonText", DefaultValue = "")]
[DependencyProperty<string>("FullButtonText", DefaultValue = "")]
[DependencyProperty<string>("SelectedButtonText", DefaultValue = "")]
[DependencyProperty<string>("ResetButtonText", DefaultValue = "")]
[DependencyProperty<string>("SelectedCountText", DefaultValue = "")]
[DependencyProperty<string>("ProgressText", DefaultValue = "")]
[DependencyProperty<bool>("IsConfirmationPending", DefaultValue = false, NotNull = true)]
[DependencyProperty<bool>("IsBusy", DefaultValue = false, NotNull = true)]
[DependencyProperty<bool>("IsPicker", DefaultValue = false, NotNull = true)]
[DependencyProperty<bool>("HasCatalogError", DefaultValue = false, NotNull = true)]
[DependencyProperty<bool>("HasNoMatches", DefaultValue = false, NotNull = true)]
[DependencyProperty<bool>("CanInstallSelected", DefaultValue = false, NotNull = true)]
[DependencyProperty<bool>("CanInstallBase", DefaultValue = true, NotNull = true)]
[DependencyProperty<bool>("CanInstallFull", DefaultValue = true, NotNull = true)]
[DependencyProperty<bool>("IsProgressIndeterminate", DefaultValue = true, NotNull = true)]
[DependencyProperty<double>("ProgressValue", DefaultValue = 0D, NotNull = true)]
internal sealed partial class HoYoShadeConfigurationDialog : ContentDialog
{
    private readonly IContentDialogFactory contentDialogFactory;
    private readonly HoYoShadeShaderService shaderService;
    private readonly LaunchOptions launchOptions;
    private readonly ITaskContext taskContext;
    private readonly IMessenger messenger;
    private IReadOnlyList<HoYoShadeEffectPackage> packages = [];
    private IReadOnlyList<HoYoShadeAddon> addons = [];
    private bool effectCatalogLoaded;
    private bool addonCatalogLoaded;
    private PendingAction pendingAction;
    private PendingAction installingAction;
    private CancellationTokenSource? installationCancellation;
    private bool canCancelInstallation;
    private int operationVersion;

    [GeneratedConstructor(InitializeComponent = true)]
    public partial HoYoShadeConfigurationDialog(IServiceProvider serviceProvider);

    public ObservableCollection<HoYoShadeEffectPackage> VisiblePackages { get; } = [];

    public ObservableCollection<HoYoShadeAddon> VisibleAddons { get; } = [];

    public async ValueTask ShowConfigurationAsync()
    {
        await taskContext.SwitchToMainThreadAsync();
        ClearConfirmation();
        UpdateSelection();
        try
        {
            await contentDialogFactory.EnqueueAndShowAsync(this).ShowTask.ConfigureAwait(false);
        }
        finally
        {
            await taskContext.SwitchToMainThreadAsync();
            foreach (INotifyPropertyChanged resource in packages.Cast<INotifyPropertyChanged>().Concat(addons))
            {
                resource.PropertyChanged -= OnResourcePropertyChanged;
            }

            packages = [];
            addons = [];
            VisiblePackages.Clear();
            VisibleAddons.Clear();
            effectCatalogLoaded = false;
            addonCatalogLoaded = false;
            IsPicker = false;
            HasCatalogError = false;
            HasNoMatches = false;
            SearchBox.Text = string.Empty;
        }
    }

    private async void OnOpenRuntimeDirectoryClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await Windows.System.Launcher.LaunchFolderPathAsync(HoYoShadeRuntime.GetRuntimeDirectory());
        }
        catch (Exception ex)
        {
            messenger.Send(InfoBarMessage.Error(ex));
        }
    }

    private async void OnResetConfigurationClick(object sender, RoutedEventArgs e)
    {
        if (IsBusy || !ConfirmAction(PendingAction.Reset))
        {
            return;
        }

        BeginOperation(SH.ViewDialogHoYoShadeConfigurationResetConfiguration);
        try
        {
            await taskContext.SwitchToBackgroundAsync();
            HoYoShadeRuntime.ResetConfiguration(launchOptions);
            await taskContext.SwitchToMainThreadAsync();
            StatusText = SH.ViewDialogHoYoShadeConfigurationResetSucceeded;
            messenger.Send(InfoBarMessage.Success(StatusText));
        }
        catch (Exception ex)
        {
            await taskContext.SwitchToMainThreadAsync();
            StatusText = ex.Message;
            messenger.Send(InfoBarMessage.Error(ex));
        }
        finally
        {
            await taskContext.SwitchToMainThreadAsync();
            EndOperation();
        }
    }

    private async void OnInstallBaseClick(object sender, RoutedEventArgs e)
    {
        await InstallAsync(PendingAction.Base);
    }

    private async void OnInstallFullClick(object sender, RoutedEventArgs e)
    {
        await InstallAsync(PendingAction.Full);
    }

    private async void OnInstallSelectedClick(object sender, RoutedEventArgs e)
    {
        await InstallAsync(PendingAction.Selected);
    }

    private async void OnSelectPackagesClick(object sender, RoutedEventArgs e)
    {
        if (IsBusy)
        {
            return;
        }

        ClearConfirmation();
        IsPicker = true;
        await LoadPickerAsync();
    }

    private async void OnRetryCatalogClick(object sender, RoutedEventArgs e)
    {
        if (!IsBusy)
        {
            await LoadPickerAsync();
        }
    }

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        if (!IsBusy)
        {
            ClearConfirmation();
            IsPicker = false;
        }
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyFilter();
    }

    private void OnResourcePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(HoYoShadeEffectPackage.IsSelected))
        {
            ClearConfirmation();
            UpdateSelection();
        }
    }

    private async ValueTask LoadPickerAsync()
    {
        BeginOperation(SH.ViewDialogHoYoShadeConfigurationLoadingCatalog);
        try
        {
            await EnsureCatalogAsync(includeAddons: true);
        }
        catch (Exception ex)
        {
            await taskContext.SwitchToMainThreadAsync();
            HasCatalogError = true;
            StatusText = SH.ViewDialogHoYoShadeConfigurationCatalogFailed;
            messenger.Send(InfoBarMessage.Error(StatusText, ex));
        }
        finally
        {
            await taskContext.SwitchToMainThreadAsync();
            EndOperation();
        }
    }

    private async ValueTask EnsureCatalogAsync(bool includeAddons, CancellationToken token = default)
    {
        if (effectCatalogLoaded && (!includeAddons || addonCatalogLoaded))
        {
            return;
        }

        HasCatalogError = false;
        await taskContext.SwitchToBackgroundAsync();
        IReadOnlyList<HoYoShadeEffectPackage> effectResult = effectCatalogLoaded
            ? packages
            : await shaderService.GetEffectPackagesAsync(token).ConfigureAwait(false);
        IReadOnlyList<HoYoShadeAddon>? addonResult = includeAddons && !addonCatalogLoaded
            ? await shaderService.GetAddonsAsync(token).ConfigureAwait(false)
            : null;
        await taskContext.SwitchToMainThreadAsync();
        token.ThrowIfCancellationRequested();
        if (!effectCatalogLoaded)
        {
            packages = effectResult;
            foreach (INotifyPropertyChanged resource in packages)
            {
                resource.PropertyChanged += OnResourcePropertyChanged;
            }

            effectCatalogLoaded = true;
        }

        if (includeAddons && !addonCatalogLoaded)
        {
            addons = addonResult!;
            foreach (INotifyPropertyChanged resource in addons)
            {
                resource.PropertyChanged += OnResourcePropertyChanged;
            }

            addonCatalogLoaded = true;
        }

        ApplyFilter();
    }

    private async ValueTask InstallAsync(PendingAction action)
    {
        if (IsBusy)
        {
            if (installingAction == action && canCancelInstallation && installationCancellation is { } cancellation)
            {
                canCancelInstallation = false;
                UpdateSelection();
                SetInstallationButtonText(SH.ViewModelGamePackageOperationCancelling);
                cancellation.Cancel();
            }

            return;
        }

        if ((action is PendingAction.Selected && !CanInstallSelected) || !ConfirmAction(action))
        {
            return;
        }

        BeginOperation(SH.ViewDialogHoYoShadeConfigurationLoadingCatalog);
        using CancellationTokenSource source = new();
        installationCancellation = source;
        installingAction = action;
        canCancelInstallation = true;
        SetInstallationButtonText(SH.ContentDialogCancelCloseButtonText);
        UpdateSelection();
        int version = operationVersion;
        IProgress<HoYoShadeShaderInstallProgress> progress = new Progress<HoYoShadeShaderInstallProgress>(value =>
        {
            if (version == operationVersion && IsBusy)
            {
                ProgressText = value.Text;
                IsProgressIndeterminate = value.Percentage is null;
                ProgressValue = value.Percentage ?? 0;
            }
        });

        try
        {
            await EnsureCatalogAsync(action is not PendingAction.Base, source.Token);
            await taskContext.SwitchToMainThreadAsync();
            HoYoShadeEffectPackage[] selected = packages.Where(package => action switch
            {
                PendingAction.Base => package.IsRequired || package.IsDefaultEnabled,
                PendingAction.Selected => package.IsRequired || package.IsSelected,
                _ => true,
            }).ToArray();
            HoYoShadeAddon[] selectedAddons = action switch
            {
                PendingAction.Full => addons.ToArray(),
                PendingAction.Selected => addons.Where(static addon => addon.IsSelected).ToArray(),
                _ => [],
            };
            await taskContext.SwitchToBackgroundAsync();
            await shaderService.InstallAsync(selected, selectedAddons, action is PendingAction.Full, progress, BeforeCommitAsync, source.Token).ConfigureAwait(false);
            await taskContext.SwitchToMainThreadAsync();
            StatusText = SH.ViewDialogHoYoShadeConfigurationResourcesInstalled;
            messenger.Send(InfoBarMessage.Success(StatusText));
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested)
        {
            await taskContext.SwitchToMainThreadAsync();
            StatusText = SH.ViewModelGamePackageOperationCanceled;
        }
        catch (Exception ex)
        {
            await taskContext.SwitchToMainThreadAsync();
            HasCatalogError = packages.Count is 0;
            StatusText = HasCatalogError
                ? SH.ViewDialogHoYoShadeConfigurationCatalogFailed
                : SH.ViewDialogHoYoShadeConfigurationResourcesInstallFailed;
            messenger.Send(InfoBarMessage.Error(StatusText, ex));
        }
        finally
        {
            await taskContext.SwitchToMainThreadAsync();
            installationCancellation = null;
            canCancelInstallation = false;
            installingAction = PendingAction.None;
            BaseButtonText = SH.ViewDialogHoYoShadeConfigurationInstall;
            FullButtonText = SH.ViewDialogHoYoShadeConfigurationInstall;
            SelectedButtonText = SH.ViewDialogHoYoShadeConfigurationInstallSelected;
            EndOperation();
        }
    }

    private async ValueTask BeforeCommitAsync()
    {
        // Serialize the cancellation boundary with button clicks on the UI thread.
        await taskContext.SwitchToMainThreadAsync();
        canCancelInstallation = false;
        UpdateSelection();
        await taskContext.SwitchToBackgroundAsync();
    }

    private void SetInstallationButtonText(string text)
    {
        switch (installingAction)
        {
            case PendingAction.Base:
                BaseButtonText = text;
                break;
            case PendingAction.Full:
                FullButtonText = text;
                break;
            case PendingAction.Selected:
                SelectedButtonText = text;
                break;
        }
    }

    private bool ConfirmAction(PendingAction action)
    {
        if (pendingAction == action)
        {
            ClearConfirmation();
            return true;
        }

        ClearConfirmation();
        pendingAction = action;
        IsConfirmationPending = true;
        StatusText = action switch
        {
            PendingAction.Reset => SH.ViewDialogHoYoShadeConfigurationResetWarning,
            PendingAction.Full => SH.ViewDialogHoYoShadeConfigurationFullInstallWarning,
            _ => SH.ViewDialogHoYoShadeConfigurationInstallWarning,
        };
        switch (action)
        {
            case PendingAction.Base:
                BaseButtonText = SH.ViewDialogHoYoShadeConfigurationConfirmInstall;
                break;
            case PendingAction.Full:
                FullButtonText = SH.ViewDialogHoYoShadeConfigurationConfirmInstall;
                break;
            case PendingAction.Selected:
                SelectedButtonText = SH.ViewDialogHoYoShadeConfigurationConfirmInstall;
                break;
            case PendingAction.Reset:
                ResetButtonText = SH.ViewDialogHoYoShadeConfigurationConfirmReset;
                break;
        }

        return false;
    }

    private void ClearConfirmation()
    {
        pendingAction = PendingAction.None;
        IsConfirmationPending = false;
        BaseButtonText = SH.ViewDialogHoYoShadeConfigurationInstall;
        FullButtonText = SH.ViewDialogHoYoShadeConfigurationInstall;
        SelectedButtonText = SH.ViewDialogHoYoShadeConfigurationInstallSelected;
        ResetButtonText = SH.ViewDialogHoYoShadeConfigurationResetConfiguration;
        StatusText = string.Empty;
    }

    private void BeginOperation(string text)
    {
        ClearConfirmation();
        operationVersion++;
        IsBusy = true;
        UpdateSelection();
        ProgressText = text;
        ProgressValue = 0;
        IsProgressIndeterminate = true;
    }

    private void EndOperation()
    {
        operationVersion++;
        IsBusy = false;
        UpdateSelection();
    }

    private void ApplyFilter()
    {
        string query = SearchBox?.Text.Trim() ?? string.Empty;
        VisiblePackages.Clear();
        VisibleAddons.Clear();
        foreach (HoYoShadeEffectPackage package in packages)
        {
            if (package.PackageName.Contains(query, StringComparison.OrdinalIgnoreCase)
                || package.PackageDescription.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                VisiblePackages.Add(package);
            }
        }

        foreach (HoYoShadeAddon addon in addons)
        {
            if (addon.PackageName.Contains(query, StringComparison.OrdinalIgnoreCase)
                || addon.PackageDescription.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                VisibleAddons.Add(addon);
            }
        }

        HasNoMatches = packages.Count + addons.Count > 0 && VisiblePackages.Count + VisibleAddons.Count is 0;
        UpdateSelection();
    }

    private void UpdateSelection()
    {
        int count = packages.Count(static package => package.IsSelected) + addons.Count(static addon => addon.IsSelected);
        SelectedCountText = string.Format(CultureInfo.CurrentCulture, SH.ViewDialogHoYoShadeConfigurationSelectedCount, count);
        CanInstallBase = !IsBusy || (installingAction is PendingAction.Base && canCancelInstallation);
        CanInstallFull = !IsBusy || (installingAction is PendingAction.Full && canCancelInstallation);
        CanInstallSelected = IsBusy
            ? installingAction is PendingAction.Selected && canCancelInstallation
            : count > 0;
    }

    private void OnClosing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        args.Cancel = IsBusy;
    }

    private enum PendingAction
    {
        None,
        Base,
        Full,
        Selected,
        Reset,
    }
}
