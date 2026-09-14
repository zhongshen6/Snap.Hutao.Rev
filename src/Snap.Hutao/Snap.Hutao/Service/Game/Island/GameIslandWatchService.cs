// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.

using Snap.Hutao.Core;
using Snap.Hutao.Core.Property;
using Snap.Hutao.Factory.Process;
using Snap.Hutao.Service.Game.Launching.Invoker;
using Snap.Hutao.Service.Notification;
using Snap.Hutao.Win32.Foundation;
using System.Diagnostics;
using System.IO;

namespace Snap.Hutao.Service.Game.Island;

[BindableCustomPropertyProvider]
[Service(ServiceLifetime.Singleton)]
internal sealed partial class GameIslandWatchService : IDisposable
{
    private readonly LaunchOptions options;
    private readonly ITaskContext taskContext;
    private readonly IMessenger messenger;
    private CancellationTokenSource? cancellation;

    [GeneratedConstructor]
    public partial GameIslandWatchService(IServiceProvider serviceProvider);

    public IObservableProperty<bool> IsWaiting { get; } = Property.CreateObservable(false);

    public IObservableProperty<bool> IsActive { get; } = Property.CreateObservable(false);

    public IObservableProperty<bool> CanToggle { get; } = Property.CreateObservable(true);

    public IReadOnlyObservableProperty<string> ButtonText { get => field ??= Property.Observe(IsWaiting, static waiting => waiting ? SH.ServiceGameIslandWatchCancel : SH.ServiceGameIslandWatchStart); }

    public void Toggle()
    {
        if (IsWaiting.Value)
        {
            IsWaiting.Value = false;
            CanToggle.Value = false;
            cancellation?.Cancel();
            return;
        }

        if (IsActive.Value)
        {
            return;
        }

        if (!HutaoRuntime.IsProcessElevated)
        {
            messenger.Send(InfoBarMessage.Warning(SH.ServiceGameIslandWatchRequiresAdmin));
            return;
        }

        string? path = options.GamePathEntry.Value?.Path;
        if (!options.IsIslandEnabled.Value || !GameIslandInterop.IsIslandLibraryAvailable() || string.IsNullOrEmpty(path) || !File.Exists(path)
            || (!string.Equals(Path.GetFileName(path), "YuanShen.exe", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(Path.GetFileName(path), "GenshinImpact.exe", StringComparison.OrdinalIgnoreCase)))
        {
            messenger.Send(InfoBarMessage.Warning(SH.ServiceGameIslandWatchInvalidConfiguration));
            return;
        }

        if (AbstractLaunchExecutionInvoker.Invoking() || GameLifeCycle.IsGameRunningRequiresMainThread())
        {
            messenger.Send(InfoBarMessage.Warning(SH.ServiceGameLaunchExecutionGameIsRunning));
            return;
        }

        cancellation = new();
        IsActive.Value = true;
        IsWaiting.Value = true;
        WatchAsync(Path.GetFullPath(path), cancellation).SafeForget();
    }

    public void Dispose()
    {
        cancellation?.Cancel();
    }

    private async Task WatchAsync(string path, CancellationTokenSource source)
    {
        Process? target = null;
        try
        {
            await taskContext.SwitchToBackgroundAsync();
            using Process current = Process.GetCurrentProcess();
            int sessionId = current.SessionId;
            using PeriodicTimer timer = new(TimeSpan.FromMilliseconds(100));
            while (await timer.WaitForNextTickAsync(source.Token).ConfigureAwait(false))
            {
                target = FindTarget(path, sessionId);
                if (target is not null)
                {
                    break;
                }
            }

            source.Token.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(target);
            await taskContext.SwitchToMainThreadAsync();
            source.Token.ThrowIfCancellationRequested();
            IsWaiting.Value = false;
            CanToggle.Value = false;
            GameLifeCycle.IsGameRunningProperty.Value = true;
            GameLifeCycle.IsIslandConnected.Value = true;

            await taskContext.SwitchToBackgroundAsync();
            SentrySdk.AddBreadcrumb($"Watch injection PID={target.Id}, path={path}", category: "island.watch");
            DiagnosticsProcess process = new(target);
            GameIslandInterop interop = new(resume: false);
            await interop.WaitForExitAsync(process, options, source.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            SentrySdk.CaptureException(ex);
            await taskContext.SwitchToMainThreadAsync();
            messenger.Send(InfoBarMessage.Error(ex));
        }
        finally
        {
            target?.Dispose();
            await taskContext.SwitchToMainThreadAsync();
            GameLifeCycle.IsIslandConnected.Value = false;
            GameLifeCycle.IsGameRunningRequiresMainThread();
            IsWaiting.Value = false;
            IsActive.Value = false;
            CanToggle.Value = true;
            cancellation = null;
            source.Dispose();
        }
    }

    private static Process? FindTarget(string path, int sessionId)
    {
        foreach (string name in new[] { "YuanShen", "GenshinImpact" })
        {
            Process[] candidates = Process.GetProcessesByName(name);
            Process? result = null;
            try
            {
                foreach (Process candidate in candidates)
                {
                    try
                    {
                        if (!candidate.HasExited && candidate.SessionId == sessionId
                            && string.Equals(candidate.MainModule?.FileName, path, StringComparison.OrdinalIgnoreCase))
                        {
                            result = candidate;
                            break;
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        // The process exited between enumeration and inspection.
                    }
                    catch (Win32Exception ex) when ((WIN32_ERROR)ex.NativeErrorCode is WIN32_ERROR.ERROR_PARTIAL_COPY)
                    {
                        // The process is still loading modules. Retry on the next polling interval.
                    }
                    catch (Win32Exception) when (candidate.HasExited)
                    {
                        // The process exited during path inspection.
                    }
                }

                if (result is not null)
                {
                    return result;
                }
            }
            finally
            {
                foreach (Process candidate in candidates)
                {
                    if (!ReferenceEquals(candidate, result))
                    {
                        candidate.Dispose();
                    }
                }
            }
        }

        return null;
    }
}
