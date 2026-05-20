// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.

namespace Snap.Hutao.Service.Cultivation;

internal readonly record struct CultivationStatisticsMergeOptions(
    bool MergeUpgradeMaterials,
    bool TalentSynthCritTenPercent,
    bool WeeklyBossMaterialInterchange);
