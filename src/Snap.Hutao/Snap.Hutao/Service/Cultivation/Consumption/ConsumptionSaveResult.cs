// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.

namespace Snap.Hutao.Service.Cultivation.Consumption;

internal readonly record struct ConsumptionSaveResult(
    ConsumptionSaveResultKind Kind,
    Guid? CreatedEntryInnerId = null);
