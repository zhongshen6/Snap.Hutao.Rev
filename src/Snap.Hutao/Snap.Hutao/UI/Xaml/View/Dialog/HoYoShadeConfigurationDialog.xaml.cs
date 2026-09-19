// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Snap.Hutao.Core;
using Snap.Hutao.Factory.ContentDialog;
using Snap.Hutao.Service.Game;
using Snap.Hutao.Service.Game.Island;
using Snap.Hutao.Service.Notification;

namespace Snap.Hutao.UI.Xaml.View.Dialog;

[DependencyProperty<string>("StatusText", DefaultValue = "")]
[DependencyProperty<string>("InstallButtonText", DefaultValue = "")]
[DependencyProperty<string>("ResetButtonText", DefaultValue = "")]
[DependencyProperty<string>("ProgressText", DefaultValue = "")]
[DependencyProperty<bool>("IsConfirmationPending", DefaultValue = false, NotNull = true)]
[DependencyProperty<bool>("IsConfirmationNotPending", DefaultValue = true, NotNull = true)]
[DependencyProperty<bool>("IsInstalling", DefaultValue = false, NotNull = true)]
[DependencyProperty<bool>("IsProgressIndeterminate", DefaultValue = true, NotNull = true)]
[DependencyProperty<double>("ProgressValue", DefaultValue = 0D, NotNull = true)]
internal sealed partial class HoYoShadeConfigurationDialog : ContentDialog
{
    private readonly IContentDialogFactory contentDialogFactory;
    private readonly HoYoShadeShaderService shaderService;
    private readonly LaunchOptions launchOptions;
    private readonly ITaskContext taskContext;
    private readonly IMessenger messenger;
    private bool resetConfirmationPending;
    private bool reinstallConfirmationPending;

    [GeneratedConstructor(InitializeComponent = true)]
    public partial HoYoShadeConfigurationDialog(IServiceProvider serviceProvider);

    public async ValueTask ShowConfigurationAsync()
    {
        RefreshInstallationState();
        await contentDialogFactory.EnqueueAndShowAsync(this).ShowTask.ConfigureAwait(false);
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

    private void OnResetConfigurationClick(object sender, RoutedEventArgs e)
    {
        if (!resetConfirmationPending)
        {
            resetConfirmationPending = true;
            reinstallConfirmationPending = false;
            IsConfirmationPending = true;
            IsConfirmationNotPending = false;
            ResetButtonText = SH.ViewDialogHoYoShadeConfigurationConfirmReset;
            StatusText = SH.ViewDialogHoYoShadeConfigurationResetWarning;
            return;
        }

        try
        {
            HoYoShadeRuntime.ResetConfiguration(launchOptions);
            resetConfirmationPending = false;
            ResetButtonText = SH.ViewDialogHoYoShadeConfigurationResetConfiguration;
            RefreshInstallationState();
            messenger.Send(InfoBarMessage.Success(SH.ViewDialogHoYoShadeConfigurationResetSucceeded));
        }
        catch (Exception ex)
        {
            messenger.Send(InfoBarMessage.Error(ex));
        }
    }

    private async void OnInstallClick(object sender, RoutedEventArgs e)
    {
        if (IsInstalling)
        {
            return;
        }

        if (shaderService.HasLibrary() && !reinstallConfirmationPending)
        {
            resetConfirmationPending = false;
            ResetButtonText = SH.ViewDialogHoYoShadeConfigurationResetConfiguration;
            reinstallConfirmationPending = true;
            IsConfirmationPending = true;
            IsConfirmationNotPending = false;
            InstallButtonText = SH.ViewDialogHoYoShadeConfigurationConfirmReinstall;
            StatusText = SH.ViewDialogHoYoShadeConfigurationReinstallWarning;
            return;
        }

        IsInstalling = true;
        ProgressValue = 0;
        IsProgressIndeterminate = true;
        ProgressText = SH.ViewDialogHoYoShadeConfigurationDownloadingShaderLibrary;

        try
        {
            IProgress<HoYoShadeShaderInstallProgress> progress = new Progress<HoYoShadeShaderInstallProgress>(ReportProgress);
            await taskContext.SwitchToBackgroundAsync();
            bool reinstalled = await shaderService.InstallOrReinstallAsync(progress, CancellationToken.None).ConfigureAwait(false);
            await taskContext.SwitchToMainThreadAsync();
            RefreshInstallationState();
            messenger.Send(InfoBarMessage.Success(reinstalled
                ? SH.ViewDialogHoYoShadeConfigurationReinstallSucceeded
                : SH.ViewDialogHoYoShadeConfigurationInstallSucceeded));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            messenger.Send(InfoBarMessage.Error(SH.ViewDialogHoYoShadeConfigurationInstallFailed, ex));
        }
        finally
        {
            await taskContext.SwitchToMainThreadAsync();
            IsInstalling = false;
            if (reinstallConfirmationPending)
            {
                RefreshInstallationState();
            }
        }
    }

    private void OnClosing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        if (IsInstalling)
        {
            args.Cancel = true;
        }
    }

    private void RefreshInstallationState()
    {
        bool hasLibrary = shaderService.HasLibrary();
        StatusText = hasLibrary
            ? SH.ViewDialogHoYoShadeConfigurationInstalledLibrary
            : SH.ViewDialogHoYoShadeConfigurationNotInstalled;
        InstallButtonText = hasLibrary
            ? SH.ViewDialogHoYoShadeConfigurationReinstallBaseShaderLibrary
            : SH.ViewDialogHoYoShadeConfigurationInstallBaseShaderLibrary;
        ResetButtonText = SH.ViewDialogHoYoShadeConfigurationResetConfiguration;
        IsConfirmationPending = false;
        IsConfirmationNotPending = true;
        reinstallConfirmationPending = false;
        resetConfirmationPending = false;
    }

    private void ReportProgress(HoYoShadeShaderInstallProgress progress)
    {
        ProgressText = progress.Text;
        IsProgressIndeterminate = progress.Percentage is null;
        ProgressValue = progress.Percentage ?? 0;
    }
}
