// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.

using Snap.Hutao.Core.ExceptionService;
using Snap.Hutao.Service.Game.Island;
using Snap.Hutao.Service.Game.Launching.Context;

namespace Snap.Hutao.Service.Game.Launching.Handler;

internal sealed class LaunchExecutionGameLifeCycleHandler : AbstractLaunchExecutionHandler
{
    private readonly bool resume;

    public LaunchExecutionGameLifeCycleHandler(bool resume)
    {
        this.resume = resume;
    }

    public override async ValueTask BeforeAsync(BeforeLaunchExecutionContext context)
    {
        if (context.ServiceProvider.GetRequiredService<GameIslandWatchService>().IsActive.Value)
        {
            HutaoException.Throw(SH.ServiceGameIslandWatchBusy);
        }

        if (resume)
        {
            if (await GameLifeCycle.TryGetRunningGameProcessAsync(context.TaskContext).ConfigureAwait(false) is (true, { } gameProcess))
            {
                context.SetOption(LaunchExecutionOptionsKey.RunningProcess, gameProcess);
            }
        }
        else
        {
            if (await GameLifeCycle.IsGameRunningAsync(context.TaskContext).ConfigureAwait(false))
            {
                HutaoException.Throw(SH.ServiceGameLaunchExecutionGameIsRunning);
            }
        }
    }
}
