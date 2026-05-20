// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Snap.Hutao.Model.Entity.Configuration;

internal sealed class CultivateEntryConfiguration : IEntityTypeConfiguration<CultivateEntry>
{
    public void Configure(EntityTypeBuilder<CultivateEntry> builder)
    {
        builder.HasOne(e => e.RelatedEntry)
            .WithMany()
            .HasForeignKey(e => e.RelatedEntryId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
