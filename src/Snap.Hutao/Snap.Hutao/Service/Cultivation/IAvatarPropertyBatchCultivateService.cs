// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.

using Snap.Hutao.Service.AvatarInfo.Factory;
using Snap.Hutao.ViewModel.AvatarProperty;
using System.Collections.Immutable;

namespace Snap.Hutao.Service.Cultivation;

internal interface IAvatarPropertyBatchCultivateService
{
    /// <summary>
    /// <see langword="null"/> when the baseline dialog is dismissed without confirmation.
    /// </summary>
    ValueTask<BatchCultivateResult?> ExecuteAsync(SummaryFactoryMetadataContext metadataContext, ImmutableArray<AvatarView> targetAvatars, CancellationToken cancellationToken);
}
