// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.

using Snap.Hutao.ViewModel.HardChallenge;
using Snap.Hutao.ViewModel.RoleCombat;
using Snap.Hutao.ViewModel.SpiralAbyss;
using Snap.Hutao.Web.Hoyolab.Takumi.GameRecord.HardChallenge;
using Snap.Hutao.Web.Hoyolab.Takumi.GameRecord.RoleCombat;
using Snap.Hutao.Web.Hoyolab.Takumi.GameRecord.SpiralAbyss;
using WebSpiralAbyss = Snap.Hutao.Web.Hoyolab.Takumi.GameRecord.SpiralAbyss.SpiralAbyss;

namespace Snap.Hutao.Service.Metadata;

internal sealed partial class ExternalMetadataValidator
{
    public bool RequireSpiralAbyss(SpiralAbyssMetadataContext context, WebSpiralAbyss data)
    {
        bool valid = RequireKnown(context.IdTowerScheduleMap.ContainsKey(data.ScheduleId), "TowerSchedule", data.ScheduleId);

        valid &= RequireSpiralAbyssRanks(context, data.RevealRank);
        valid &= RequireSpiralAbyssRanks(context, data.DefeatRank);
        valid &= RequireSpiralAbyssRanks(context, data.DamageRank);
        valid &= RequireSpiralAbyssRanks(context, data.TakeDamageRank);
        valid &= RequireSpiralAbyssRanks(context, data.NormalSkillRank);
        valid &= RequireSpiralAbyssRanks(context, data.EnergySkillRank);

        foreach (SpiralAbyssFloor floor in data.Floors)
        {
            foreach (SpiralAbyssLevel level in floor.Levels)
            {
                foreach (SpiralAbyssBattle battle in level.Battles)
                {
                    foreach (SpiralAbyssAvatar avatar in battle.Avatars)
                    {
                        valid &= RequireAvatar(context, avatar.Id);
                    }
                }
            }
        }

        return valid;
    }

    public bool RequireRoleCombat(RoleCombatMetadataContext context, RoleCombatData data)
    {
        bool valid = RequireKnown(context.IdRoleCombatScheduleMap.ContainsKey(data.Schedule.ScheduleId), "RoleCombatSchedule", data.Schedule.ScheduleId);

        foreach (RoleCombatAvatar avatar in data.Detail.BackupAvatars)
        {
            valid &= RequireAvatar(context, avatar.AvatarId);
        }

        foreach (RoleCombatRoundData round in data.Detail.RoundsData)
        {
            foreach (RoleCombatAvatar avatar in round.Avatars)
            {
                valid &= RequireAvatar(context, avatar.AvatarId);
            }
        }

        RoleCombatFightStatistics statistics = data.Detail.FightStatistics;
        valid &= RequireRoleCombatStatistic(context, statistics.MaxDefeatAvatar);
        valid &= RequireRoleCombatStatistic(context, statistics.MaxDamageAvatar);
        valid &= RequireRoleCombatStatistic(context, statistics.MaxTakeDamageAvatar);
        valid &= RequireRoleCombatStatistic(context, statistics.TotalCoinConsumed);
        foreach (RoleCombatAvatarStatistics statistic in statistics.ShortestAvatarList)
        {
            valid &= RequireAvatar(context, statistic.AvatarId);
        }

        return valid;
    }

    public bool RequireHardChallenge(HardChallengeMetadataContext context, HardChallengeData data)
    {
        bool valid = RequireKnown(context.IdHardChallengeScheduleMap.ContainsKey(data.Schedule.ScheduleId), "HardChallengeSchedule", data.Schedule.ScheduleId);

        foreach (HardChallengeBlingAvatar avatar in data.Blings)
        {
            valid &= RequireAvatar(context, avatar.AvatarId);
        }

        valid &= RequireHardChallengeEntry(context, data.SinglePlayer);
        valid &= RequireHardChallengeEntry(context, data.MultiPlayer);
        return valid;
    }

    private bool RequireSpiralAbyssRanks(SpiralAbyssMetadataContext context, IEnumerable<SpiralAbyssRank> ranks)
    {
        bool valid = true;
        foreach (SpiralAbyssRank rank in ranks)
        {
            if (rank.AvatarId != 0U)
            {
                valid &= RequireAvatar(context, rank.AvatarId);
            }
        }

        return valid;
    }

    private bool RequireRoleCombatStatistic(RoleCombatMetadataContext context, RoleCombatAvatarStatistics? statistic)
    {
        return statistic is null || RequireAvatar(context, statistic.AvatarId);
    }

    private bool RequireHardChallengeEntry(HardChallengeMetadataContext context, HardChallengeDataEntry entry)
    {
        bool valid = true;
        foreach (HardChallengeChallenge challenge in entry.Challenges)
        {
            foreach (HardChallengeAvatar avatar in challenge.Team)
            {
                valid &= RequireAvatar(context, avatar.AvatarId);
            }

            foreach (HardChallengeBestAvatar avatar in challenge.BestAvatars)
            {
                valid &= RequireAvatar(context, avatar.AvatarId);
            }
        }

        return valid;
    }
}
