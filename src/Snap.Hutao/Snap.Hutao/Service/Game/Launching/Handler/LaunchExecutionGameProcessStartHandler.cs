// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.

using Snap.Hutao.Service.Game.Launching.Context;
using Snap.Hutao.Win32.Foundation;

namespace Snap.Hutao.Service.Game.Launching.Handler;

internal sealed class LaunchExecutionGameProcessStartHandler : AbstractLaunchExecutionHandler
{
    public override ValueTask BeforeAsync(BeforeLaunchExecutionContext context)
    {
        return ValueTask.CompletedTask;
    }

    public override async ValueTask ExecuteAsync(LaunchExecutionContext context)
    {
        try
        {
            if (!context.IsProcessStarted)
            {
                context.Process.Start();
                context.IsProcessStarted = true;
            }

            // Only suspended processes support resume.
            try
            {
                context.Process.ResumeMainThread();
            }
            catch (NotSupportedException)
            {
            }

            await context.TaskContext.SwitchToMainThreadAsync();
            GameLifeCycle.IsGameRunningProperty.Value = true;
        }
        catch (Win32Exception ex)
        {
            if (ex.HResult is HRESULT.E_FAIL)
            {
                return;
            }

            throw;
        }

        context.Progress.Report(new(SH.ServiceGameLaunchPhaseProcessStarted));
    }
}
