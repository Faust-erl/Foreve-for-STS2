using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using Foreve.Scripts.DualCharacter;

namespace Foreve.Scripts.Patches;

/// <summary>
/// 双角色模式：奖励/商店卡池合并（2026-08-14 实测修复）。全部 IL 结论来自 C:\tmp\sts2_full.il。
///
/// 问题：副玩家（后选角色）的卡牌在战斗奖励、商店中均不出现。
/// 根因：两处卡池都只取主玩家角色卡池——
///   - 奖励：RewardsSet.GenerateRewardsFor（IL 210310）→ CardCreationOptions.ForRoom(player, roomType)
///     （IL 155211，单元素列表 [player.Character.CardPool]，IL 155262）→ GetPossibleCards
///     （IL 155311，SelectMany 逐池 GetUnlockedCards，lambda b__0 IL 156031）→
///     CardPoolModel.GetUnlockedCards（IL 1111050）；
///   - 商店：MerchantInventory.PopulateCharacterCardEntries（IL 1763143）直接
///     player.Character.CardPool.GetUnlockedCards(player.UnlockState, player.RunState.
///     CardMultiplayerConstraint).ToList()（IL 1763167-1763174）。
///
/// 方案（方法级单点，版本漂移免疫）：Postfix CardPoolModel.GetUnlockedCards(UnlockState,
/// CardMultiplayerConstraint) —— 双人模式且 __instance 严格等于主玩家角色卡池时，才把副玩家
/// 角色卡池的 GetUnlockedCards 结果并入（按 card.Id 去重）。一次 patch 覆盖：
///   - 奖励（战斗奖励/宝箱/事件卡奖励等一切走 CardCreationOptions → GetPossibleCards 的流程）；
///   - 商店角色卡槽（都经 GetUnlockedCards 返回）。
/// 奖励份数不变（GetRewardPlayers 玩家收敛 patch 不动，仍是 1 份）。
///
/// 2026-08-15 增补 1：双人局过滤联机专属内容 ——
///   - 卡牌：CardMultiplayerConstraint.MultiplayerOnly（如 BelieveInYou/相信的你）一律排除。
///     双玩家 run 会让 IRunState.CardMultiplayerConstraint 返回 MultiplayerOnly，原版反而
///     把这些卡放入奖励/商店；本地双人模式下它们不适用。
///   - 遗物：MassiveScroll（巨大卷轴）的 IsAllowed 以 run.Players.Count &gt; 1 为条件，
///     本地双人局会被误判为联机而出现在奖励/商店；双人模式下强制 IsAllowed=false、
///     IsAllowedInShops=false，并在 RelicPoolModel.GetUnlockedRelics 结果中剔除。
///
/// 2026-08-15 增补 2（linker 分支）：mod 角色卡牌不属于无色牌。旧逻辑只排除副池防递归，
/// 导致无色卡池（ColorlessCardPool）的 GetUnlockedCards 也被并入副角色卡池 → 奖励/商店/
/// 遗物或药水效果生成无色牌时混入萝坦/奥吉尔卡。现改为 ReferenceEquals 主玩家角色卡池才合并，
/// 无色池/事件池/其他任何池零污染。
///
/// 2026-08-15 增补 3（linker 分支）：「来自其他角色的牌」不包括副角色。万花筒（Kaleidoscope）、
/// 棱镜（PrismaticGem）等效果使用 UnlockState.CharacterCardPools 选取其他角色卡池时，会把
/// 副角色池也当「其他角色」选入。现按效果点过滤副角色池：
///   - Kaleidoscope.&lt;AfterObtained&gt;b__7_0（其他角色池过滤）：副角色池返回 false；
///   - PrismaticGem.ModifyCardRewardCreationOptions：结果 CardPools 剔除副角色池；
///   - Splash.&lt;OnPlay&gt;b__2_0（其他角色牌选择器）：副角色池返回空集合；
///   - ColorfulPhilosophers.get_CardPoolColorOrder：颜色池顺序剔除副角色池。
/// 主角色奖励/商店合并不受影响（副角色卡牌仍属于本次冒险可用卡池）。
///
/// 防递归：__instance 已是副玩家卡池（不等于主池）时直接返回。
/// 单玩家局 / 非双人 / 副玩家未接线：不处理（零变化）。
/// 找不到方法时 Warn 并跳过（不崩）。
///
/// ⚠️ 接线（主流程在 Entry.cs 统一处理，不要直接改 Entry.cs）：
///   Foreve.Scripts.Patches.DualCharacterRewardPoolsPatch.Install(Logger);
/// </summary>
public static class DualCharacterRewardPoolsPatch
{
    private static MegaCrit.Sts2.Core.Logging.Logger? _logger;

    public static void Install(MegaCrit.Sts2.Core.Logging.Logger logger)
    {
        _logger = logger;
        var harmony = new Harmony("foreve.dual_character_reward_pools");

        var getUnlockedCards = AccessTools.Method(typeof(CardPoolModel), "GetUnlockedCards",
            new[] { typeof(UnlockState), typeof(CardMultiplayerConstraint) });
        if (getUnlockedCards == null)
        {
            logger.Warn("[Foreve][Dual] CardPoolModel.GetUnlockedCards 未找到 - 卡池合并 patch 跳过（游戏结构变更?）");
            return;
        }

        harmony.Patch(getUnlockedCards, postfix: new HarmonyMethod(GetMethod(nameof(GetUnlockedCardsPostfix))));
        // 2026-08-17：重逢仅能通过「约定」获得 —— 从一切卡池解锁结果中剔除（奖励/商店/无色池/
        // 事件/万花筒/棱镜等所有经 GetUnlockedCards 的获取路径；约定变重逢走 CreateCard 直建，
        // 不走卡池，不受影响）。全局生效（单玩家局同样过滤）。
        harmony.Patch(getUnlockedCards, postfix: new HarmonyMethod(GetMethod(nameof(GetUnlockedCardsExclusivePostfix))));

        // 联机专属遗物过滤：MassiveScroll.IsAllowed 只按 Players.Count>1 判定，本地双人局会误放行。
        var massiveScrollIsAllowed = AccessTools.Method(typeof(MassiveScroll), "IsAllowed",
            new[] { typeof(IRunState) });
        if (massiveScrollIsAllowed != null)
        {
            harmony.Patch(massiveScrollIsAllowed,
                prefix: new HarmonyMethod(GetMethod(nameof(MassiveScrollIsAllowedPrefix))));
        }

        // 商店入口：RelicModel.IsAllowedInShops 是 virtual getter，MassiveScroll 未覆写 →
        // patch 基类 getter，按实例类型拦截。
        var isAllowedInShops = AccessTools.PropertyGetter(typeof(RelicModel), nameof(RelicModel.IsAllowedInShops));
        if (isAllowedInShops != null)
        {
            harmony.Patch(isAllowedInShops,
                prefix: new HarmonyMethod(GetMethod(nameof(IsAllowedInShopsPrefix))));
        }

        // 双保险：从所有 RelicPool.GetUnlockedRelics 结果里直接剔除 MassiveScroll，
        // 即使未来某些生成路径不调 IsAllowed/IsAllowedInShops 也不会进 grab bag。
        var getUnlockedRelics = AccessTools.Method(typeof(RelicPoolModel), "GetUnlockedRelics",
            new[] { typeof(UnlockState) });
        if (getUnlockedRelics != null)
        {
            harmony.Patch(getUnlockedRelics,
                postfix: new HarmonyMethod(GetMethod(nameof(GetUnlockedRelicsPostfix))));
        }

        // 「来自其他角色的牌」排除副角色（2026-08-15 用户反馈：万花筒中出现副角色牌）。
        var kaleidoscopeFilter = AccessTools.Method(typeof(Kaleidoscope), "<AfterObtained>b__7_0",
            new[] { typeof(CardPoolModel) });
        if (kaleidoscopeFilter != null)
        {
            harmony.Patch(kaleidoscopeFilter,
                postfix: new HarmonyMethod(GetMethod(nameof(KaleidoscopePoolFilterPostfix))));
        }

        var prismaticModify = AccessTools.Method(typeof(PrismaticGem), "ModifyCardRewardCreationOptions",
            new[] { typeof(Player), typeof(CardCreationOptions) });
        if (prismaticModify != null)
        {
            harmony.Patch(prismaticModify,
                postfix: new HarmonyMethod(GetMethod(nameof(PrismaticGemOptionsPostfix))));
        }

        var splashPoolCards = AccessTools.Method(typeof(Splash), "<OnPlay>b__2_0",
            new[] { typeof(CardPoolModel) });
        if (splashPoolCards != null)
        {
            harmony.Patch(splashPoolCards,
                postfix: new HarmonyMethod(GetMethod(nameof(SplashPoolCardsPostfix))));
        }

        var colorfulPoolOrder = AccessTools.Method(typeof(ColorfulPhilosophers), "get_CardPoolColorOrder");
        if (colorfulPoolOrder != null)
        {
            harmony.Patch(colorfulPoolOrder,
                postfix: new HarmonyMethod(GetMethod(nameof(ColorfulPhilosophersPoolOrderPostfix))));
        }

        _logger?.Info($"[Foreve][Dual] 奖励/商店卡池合并 + 联机专属内容过滤 patch 已装 " +
                      $"(GetUnlockedCards={getUnlockedCards}, MassiveScroll.IsAllowed={massiveScrollIsAllowed != null}, " +
                      $"IsAllowedInShops={isAllowedInShops != null}, GetUnlockedRelics={getUnlockedRelics != null}, " +
                      $"重逢仅约定可得={true})");
        _logger?.Info($"[Foreve][Dual] 其他角色卡池排除副角色 patch 已装 " +
                      $"(Kaleidoscope={kaleidoscopeFilter != null}, PrismaticGem={prismaticModify != null}, " +
                      $"Splash={splashPoolCards != null}, ColorfulPhilosophers={colorfulPoolOrder != null})");
    }

    /// <summary>
    /// 双人模式：仅当 __instance 是主玩家角色卡池时，把副玩家角色卡池的解锁卡并入结果（按 Id 去重）。
    /// 无色卡池 / 事件卡池 / 遗物药水效果使用的其他卡池一律不并 —— mod 角色卡不属于无色牌，
    /// 否则奖励/商店/遗物或药水效果生成无色牌时会混入萝坦/奥吉尔卡（2026-08-15 用户反馈）。
    /// 副玩家自己的池（防递归）/单玩家局/未接线：不处理。
    /// </summary>
    private static void GetUnlockedCardsPostfix(CardPoolModel __instance, UnlockState unlockState,
        CardMultiplayerConstraint multiplayerConstraint, ref IEnumerable<CardModel> __result)
    {
        try
        {
            if (!DualCharacterState.Enabled) return;
            var main = DualCharacterState.MainPlayer;
            var secondary = DualCharacterState.SecondaryPlayer;
            if (main?.Character?.CardPool == null || secondary?.Character?.CardPool == null) return;
            if (!ReferenceEquals(__instance, main.Character.CardPool)) return; // 只合并主角色池，绝不污染无色/事件池
            if (__result == null) return;

            var extra = secondary.Character.CardPool.GetUnlockedCards(secondary.UnlockState, multiplayerConstraint);
            if (extra == null) return;

            var merged = __result.Concat(extra)
                .GroupBy(c => c.Id)
                .Select(g => g.First())
                // 双人局排除联机专属卡（MultiplayerConstraint == MultiplayerOnly，如 BelieveInYou）。
                // 双玩家 run 的 RunState.CardMultiplayerConstraint 是 MultiplayerOnly，原版会把
                // 这些卡放进奖励/商店；本地双人模式没有真实队友，效果不适用（2026-08-15 用户反馈）。
                .Where(c => c.MultiplayerConstraint != CardMultiplayerConstraint.MultiplayerOnly)
                // 重逢仅能通过「约定」获得（2026-08-17 用户需求）：合并路径也剔除，
                // 与 GetUnlockedCardsExclusivePostfix 双保险（postfix 执行顺序不保证）。
                .Where(c => c.Id.Entry != ReunionCardId)
                .ToList();
            __result = merged;
        }
        catch (Exception e)
        {
            GD.Print($"[Foreve][Dual] GetUnlockedCards 卡池合并异常: {e}");
        }
    }

    /// <summary>重逢卡牌 Id（仅能通过「约定」获得，禁止从商店/奖励等一切卡池来源出现）。</summary>
    private const string ReunionCardId = "FOREVE_CARD_ROTAN_REUNION";

    /// <summary>
    /// 全局过滤（2026-08-17 重逢；2026-08-25 联机专属卡）：
    ///   1. 重逢（FOREVE_CARD_ROTAN_REUNION）不允许从商店、奖励等任何经卡池的途径获得，
    ///      仅能通过「约定」第 4 场战斗开始变重逢获得（RotanOath.BeforeCombatStart
    ///      走 CreateCard 直建 + RemoveFromDeck + AddGeneratedCardToCombat，不经过本方法）。
    ///   2. 联机专属卡（MultiplayerConstraint == MultiplayerOnly，如 BelieveInYou/Coordinate/
    ///      TagTeam 等）在本 mod 中一律不进入任何卡池结果。之前只在主角色池合并路径过滤，
    ///      商店无色槽/其他角色槽等不走合并路径时仍会漏出；这里挂在 GetUnlockedCards 全局，
    ///      所有池（角色池/无色池/事件池）统一生效。
    /// 单玩家/双人局一致生效；双人合并路径的副池结果同样经过本方法（同一 patch 点）。
    /// </summary>
    private static void GetUnlockedCardsExclusivePostfix(ref IEnumerable<CardModel> __result)
    {
        try
        {
            if (__result == null) return;
            __result = __result
                .Where(c => c != null
                    && c.Id.Entry != ReunionCardId
                    && c.MultiplayerConstraint != CardMultiplayerConstraint.MultiplayerOnly)
                .ToList();
        }
        catch (Exception e)
        {
            GD.Print($"[Foreve][Dual] GetUnlockedCards 全局卡牌过滤异常: {e}");
        }
    }

    /// <summary>双人模式下巨大卷轴（MassiveScroll）不允许作为奖励遗物出现。</summary>
    private static bool MassiveScrollIsAllowedPrefix(IRunState runState, ref bool __result)
    {
        if (!DualCharacterState.Enabled) return true;
        __result = false;
        return false;
    }

    /// <summary>双人模式下巨大卷轴（MassiveScroll）不允许进入商店。</summary>
    private static bool IsAllowedInShopsPrefix(RelicModel __instance, ref bool __result)
    {
        if (!DualCharacterState.Enabled || __instance is not MassiveScroll) return true;
        __result = false;
        return false;
    }

    /// <summary>双人模式从所有遗物池结果中剔除巨大卷轴（奖励/商店双重兜底）。</summary>
    private static void GetUnlockedRelicsPostfix(ref IEnumerable<RelicModel> __result)
    {
        try
        {
            if (!DualCharacterState.Enabled || __result == null) return;
            __result = __result.Where(r => r is not MassiveScroll).ToList();
        }
        catch (Exception e)
        {
            GD.Print($"[Foreve][Dual] GetUnlockedRelics 联机遗物过滤异常: {e}");
        }
    }

    /// <summary>当前双人局的副角色卡池；未接线/单玩家局返回 null。</summary>
    private static CardPoolModel? GetSecondaryCardPool()
    {
        if (!DualCharacterState.Enabled) return null;
        return DualCharacterState.SecondaryPlayer?.Character?.CardPool;
    }

    /// <summary>判断 pool 是否为副角色卡池（「其他角色」效果要排除它）。</summary>
    private static bool IsSecondaryPool(CardPoolModel? pool)
        => pool != null && ReferenceEquals(pool, GetSecondaryCardPool());

    /// <summary>
    /// 万花筒（Kaleidoscope）获取时挑「其他角色池」的私有过滤器：
    /// 副角色是本次冒险已在队角色，不属于「其他角色」。
    /// ⚠️ Harmony 参数名必须与原方法一致（原参数名为 p）。
    /// </summary>
    private static void KaleidoscopePoolFilterPostfix(CardPoolModel p, ref bool __result)
    {
        if (__result && IsSecondaryPool(p)) __result = false;
    }

    /// <summary>
    /// 棱镜（PrismaticGem）把奖励选项扩展为全部已解锁角色池：
    /// 从扩展后的 CardPools 里剔除副角色池；主角色池仍保留（其内部已合并副角色卡牌）。
    /// </summary>
    private static void PrismaticGemOptionsPostfix(ref CardCreationOptions __result)
    {
        try
        {
            if (!DualCharacterState.Enabled || __result == null) return;
            var secondaryPool = GetSecondaryCardPool();
            if (secondaryPool == null) return;

            var pools = __result.CardPools;
            if (pools == null || !pools.Any(p => ReferenceEquals(p, secondaryPool))) return;

            __result = __result.WithCardPools(
                pools.Where(p => !ReferenceEquals(p, secondaryPool)).ToList(),
                __result.CardPoolFilter);
        }
        catch (Exception e)
        {
            GD.Print($"[Foreve][Dual] PrismaticGem 副角色池剔除异常: {e}");
        }
    }

    /// <summary>
    /// 溅射（Splash）打牌时生成「其他角色」的牌：
    /// 副角色池返回空集合，避免把已在队角色当其他角色。
    /// ⚠️ Harmony 参数名必须与原方法一致（原参数名为 c）。
    /// </summary>
    private static void SplashPoolCardsPostfix(CardPoolModel c, ref IEnumerable<CardModel> __result)
    {
        if (IsSecondaryPool(c)) __result = Enumerable.Empty<CardModel>();
    }

    /// <summary>
    /// 色彩哲学家事件按颜色池顺序提供其他角色卡选项：
    /// 颜色池顺序剔除副角色池（副角色为原版角色时同样生效）。
    /// </summary>
    private static void ColorfulPhilosophersPoolOrderPostfix(ref IEnumerable<CardPoolModel> __result)
    {
        try
        {
            if (!DualCharacterState.Enabled || __result == null) return;
            var secondaryPool = GetSecondaryCardPool();
            if (secondaryPool == null) return;
            __result = __result.Where(p => !ReferenceEquals(p, secondaryPool)).ToList();
        }
        catch (Exception e)
        {
            GD.Print($"[Foreve][Dual] ColorfulPhilosophers 副角色池剔除异常: {e}");
        }
    }

    private static MethodInfo GetMethod(string name)
        => typeof(DualCharacterRewardPoolsPatch).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!;
}
