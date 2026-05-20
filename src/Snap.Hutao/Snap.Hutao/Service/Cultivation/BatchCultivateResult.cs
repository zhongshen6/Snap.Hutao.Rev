// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.

namespace Snap.Hutao.Service.Cultivation;

internal struct BatchCultivateResult
{
    public int SucceedCount;
    public int SkippedCount;
    public BatchCultivateStopReason StopReason;
}

internal enum BatchCultivateStopReason
{
    None = 0,
    NoProject = 1,
}
