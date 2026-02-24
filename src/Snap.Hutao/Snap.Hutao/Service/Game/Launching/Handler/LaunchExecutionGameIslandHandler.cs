// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.

using Snap.Hutao.Service.Game.Island;
using Snap.Hutao.Service.Game.Launching.Context;
using Snap.Hutao.Service.Notification;
using Snap.Hutao.Core.ExceptionService;
using System.IO;

namespace Snap.Hutao.Service.Game.Launching.Handler;

internal sealed class LaunchExecutionGameIslandHandler : AbstractLaunchExecutionHandler
{
    private readonly bool resume;
    private GameIslandInterop? interop;

    public LaunchExecutionGameIslandHandler(bool resume)
    {
        this.resume = resume;
    }

    public override ValueTask BeforeAsync(BeforeLaunchExecutionContext context)
    {
        bool islandRequested = context.LaunchOptions.IsIslandEnabled.Value;
        context.LaunchOptions.RefreshIslandCapability();
        if (!resume && islandRequested && !context.LaunchOptions.IsIslandCapabilityAvailable.Value)
        {
            string islandPath = Path.Combine(AppContext.BaseDirectory, GameIslandInterop.IslandLibraryName);
            throw HutaoException.InvalidOperation($"Island injection file missing: {islandPath}");
        }

        if (islandRequested)
        {
            interop = new(resume);
            return interop.BeforeAsync(context);
        }

        return ValueTask.CompletedTask;
    }

    public override async ValueTask ExecuteAsync(LaunchExecutionContext context)
    {
        if (!context.LaunchOptions.IsIslandEnabled.Value)
        {
            return;
        }

        ArgumentNullException.ThrowIfNull(interop);

        if (!resume && !context.IsProcessStarted)
        {
            context.Process.Start();
            context.IsProcessStarted = true;
        }

        ExecuteCoreAsync(context).SafeForget();
        if (!resume)
        {
            await interop.WaitForInjectionReadyAsync().ConfigureAwait(false);
        }
    }

    private async ValueTask ExecuteCoreAsync(LaunchExecutionContext context)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(interop);

            await context.TaskContext.SwitchToMainThreadAsync();
            GameLifeCycle.IsIslandConnected.Value = true;

            await context.TaskContext.SwitchToBackgroundAsync();
            await interop.WaitForExitAsync(context).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            context.Messenger.Send(InfoBarMessage.Error(ex));
            context.Process.Kill();
        }
        finally
        {
            await context.TaskContext.SwitchToMainThreadAsync();
            GameLifeCycle.IsIslandConnected.Value = false;
        }
    }

}

