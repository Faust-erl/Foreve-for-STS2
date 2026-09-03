using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using Foreve.Scripts.DualCharacter;

namespace Foreve.Scripts.Patches;

/// <summary>
/// 双角色模式：卡牌对己效果归属（批次 2b-2，2026-08-14 实测修复：失效 transpiler → 方法级重构）。
/// 全部 IL 结论来自 C:\tmp\sts2_full.il（2026-08-14 实证）。
///
/// 问题：双人模式开战时 PopulateCombatState 按「牌所在牌库的玩家」为每张牌生成战斗实例（_owner=主玩家，
/// 副玩家牌库已在 RunStarted 合并进主玩家），因此所有卡牌实例的 CardModel.Owner 都是主玩家 ——
/// 卡牌 OnPlay 里普遍的 Owner.Creature（格挡/力量/敏捷等对己增益）全部落到主玩家 creature。
///
/// 原实现（已失效）：transpiler 指令级替换 &lt;OnPlayWrapper&gt;d__339.MoveNext 中的 OnPlay 调用 ——
/// 游戏运行时 DLL 与 IL dump 版本漂移后匹配失败（日志 WARN「OnPlayWrapper OnPlay 调用未找到」），
/// 卡牌归属功能整体失效（所有牌的效果都由主角色执行）。
///
/// 现实现（方法级，版本漂移免疫）：patch CardModel.OnPlayWrapper（public 方法，
/// Task OnPlayWrapper(PlayerChoiceContext, Creature, bool, ResourceInfo, bool)，IL 1107358）：
///   - Prefix：双人模式下把私有字段 _owner 临时换成「卡牌所属角色」的玩家（ResolveCardOwnerPlayer，
///     判定逻辑保留复用：双人模式查两名玩家角色 CardPool.AllCardIds 是否含卡 Id —— 萝坦卡→萝坦、
///     奥吉尔卡→奥吉尔、原版铁甲/静默卡→对应角色；无色/共享/生成卡未命中 → 不替换）；
///     原 owner 记入静态字典 SwappedOwners（按 card 实例，支持顺序/重入播放）。
///   - Postfix（ref Task __result）：async 方法的 Postfix 在原始方法返回 Task 时即执行（早于 async 体
///     完成），直接恢复会过早；因此把返回的 Task 替换为 RestoreOwnerAfterAsync 包装任务 —— await 原
///     任务后（finally 语义，异常/取消也恢复）恢复 _owner 并清理字典。恢复时机 = OnPlay 执行完
///     （OnPlay 在 OnPlayWrapper 状态机内被 await）。
///   - Prefix 自愈：若同卡上次同步异常泄漏了交换（字典残留），先恢复再重新交换。
///   - 效果归属：卡牌 OnPlay 执行期间 Owner.Creature 返回所属角色 creature → 格挡/力量等对己效果
///     给对角色；攻击/敌人目标不受影响；牌仍进主玩家弃牌堆（恢复后）；BeginCardOrPotionEffect/
///     EndCardOrPotionEffect 在 OnPlayWrapper 包裹区之外，仍用主玩家，不受影响。
///   - 2026-08-15 死亡容错：自然归属玩家死亡时，效果归属重定向到存活玩家（死亡角色的牌由存活角色打出）；
///     自然归属存活/两名都活时行为不变。
///   - 2026-08-16 无归属卡指定目标：无色牌/其他角色牌（ResolveCardOwnerPlayer 未命中）的技能牌/
///     能力牌，由 DualCharacterCardTargetPatch 在出牌入口弹窗指定目标角色，本 Prefix 按指定
///     角色交换 owner（选副角色时），对己效果按指定角色结算。
///   - 单玩家局 / 非双人 / 目标玩家 creature 已死且无存活者：不替换（零变化）。
///   - ⚠️ 用反射直写 _owner 字段而非 set_Owner：set_Owner 在卡已有 owner 时抛 InvalidOperationException
///     （IL 实证 "Card X already has an owner."）。
///   - RitsuLib 也 patch 了 OnPlayWrapper（CardModelOnPlayWrapperSecondaryResourcesPatch，Prefix/
///     Postfix）——Harmony 多 patch 共存，无冲突。
///   - 找不到方法时 Warn 并跳过（不崩）。
///
/// ⚠️ 2026-08-24 增补（指定副角色获得小刀 / 副玩家生成卡可玩）：
///   - 副玩家自有生成卡（如 CloakAndDagger 指定副角色时创建的 Shiv）不再统一归主：
///     CardModel.Pile 对副玩家 owner 的卡永久补查主玩家牌堆；再次打出时进入
///     SecondaryOwnedCardPlayScopes，效果归属副玩家、牌堆走主玩家。
///   - 混合 owner 批量入堆补丁：主手牌里同时有主/副 owner 卡时，CardPileCmd.Add
///     (IEnumerable, PileType, ...) 按主玩家牌堆分组执行，避免原版 different-owners 异常。
///
/// ⚠️ 接线（主流程在 Entry.cs 统一处理，不要直接改 Entry.cs）：
///   由 Foreve.Scripts.Patches.DualCharacterCombatPatches.Install 调用（Entry.cs 已接线 CombatPatches，
///   无需改 Entry.cs）；亦可独立调用 DualCharacterCardOwnerPatch.Install(Logger)。
/// </summary>
public static class DualCharacterCardOwnerPatch
{
    private static MegaCrit.Sts2.Core.Logging.Logger? _logger;

    /// <summary>CardModel._owner 私有字段（get_Owner 的 backing field；set_Owner 有「已有 owner 抛异常」守卫，必须直写字段）。</summary>
    private static readonly FieldInfo OwnerField = AccessTools.Field(typeof(CardModel), "_owner");

    /// <summary>被 Prefix 交换了 owner 的卡 → 原 owner（Postfix 的 Task 包装完成后恢复并移除；同步异常泄漏由下次 Prefix 自愈）。</summary>
    private static readonly Dictionary<CardModel, Player> SwappedOwners = new();

    /// <summary>
    /// 副玩家自有卡牌的出牌作用域（2026-08-24「斗篷与匕首指定副角色不能获得小刀」修复）：
    /// 生成卡（如指定副角色时生成的小刀）owner 就是副玩家，OnPlayWrapper 无需交换 owner，
    /// 但牌堆解析仍必须走主玩家（副玩家 PCS 是空壳）。本表让牌堆重定向/选择重定向在该卡
    /// 结算期间保持生效，owner 不动——效果归属副玩家、牌堆归属主玩家。
    /// </summary>
    private static readonly Dictionary<CardModel, Player> SecondaryOwnedCardPlayScopes = new();

    /// <summary>当前是否处于 owner 交换窗口（OnPlayWrapper 执行期间）。供其他 patch 区分
    /// 「副玩家卡牌效果正在结算（牌堆/抽牌应走主玩家）」与「副玩家空壳常规流程（应跳过）」。</summary>
    public static bool IsSwapActive => SwappedOwners.Count > 0;

    /// <summary>
    /// 当前是否有任何「副玩家侧卡牌效果」在结算（owner 交换 或 副玩家自有卡直接打出）。
    /// 与 IsSwapActive 的区别：副玩家自有生成卡（如小刀）出牌时不交换 owner，但仍需要
    /// 牌堆/抽牌/选择重定向到主玩家。
    /// </summary>
    public static bool IsSecondaryCardEffectActive => SwappedOwners.Count > 0 || SecondaryOwnedCardPlayScopes.Count > 0;

    public static void Install(MegaCrit.Sts2.Core.Logging.Logger logger)
    {
        _logger = logger;
        var harmony = new Harmony("foreve.dual_character_card_owner");

        var onPlayWrapper = AccessTools.Method(typeof(CardModel), "OnPlayWrapper", new[]
        {
            typeof(PlayerChoiceContext), typeof(Creature), typeof(bool), typeof(ResourceInfo), typeof(bool),
        });
        if (onPlayWrapper == null)
        {
            logger.Warn("[Foreve][Dual] CardModel.OnPlayWrapper 未找到 - 卡牌归属 patch 跳过（游戏结构变更?）");
            return;
        }

        harmony.Patch(onPlayWrapper,
            prefix: new HarmonyMethod(GetMethod(nameof(OnPlayWrapperPrefix))),
            postfix: new HarmonyMethod(GetMethod(nameof(OnPlayWrapperPostfix))));

        InstallPileLookupRedirects(harmony);
        InstallCombatPilePropertyRedirects(harmony);
        InstallCardSelectRedirects(harmony);
        InstallMixedOwnerAddPatch(harmony);


        _logger?.Info($"[Foreve][Dual] 卡牌归属 patch 已装 (OnPlayWrapper={onPlayWrapper}, OwnerField={OwnerField != null}, 牌堆重定向已装)");
    }

    /// <summary>
    /// 牌堆解析重定向：owner 临时换成副玩家期间，牌堆移动仍必须按主玩家解析。
    /// 覆盖 CardModel.Pile（旧牌堆查找）与 CardPile.Get / PileTypeExtensions.GetPile（目标牌堆查找）。
    /// 这样 AddDuringManualCardPlay 和最终 result pile Add 都会把副角色的牌视觉/数据归到主玩家牌堆，
    /// 不会进副玩家空 PCS（副玩家无 UI 容器，导致卡牌悬停画面中央）。
    /// </summary>
    private static void InstallPileLookupRedirects(Harmony harmony)
    {
        var pileProperty = AccessTools.PropertyGetter(typeof(CardModel), "Pile");
        if (pileProperty != null)
        {
            harmony.Patch(pileProperty, prefix: new HarmonyMethod(GetMethod(nameof(CardPilePropertyPrefix))));
        }

        var cardPileGet = AccessTools.Method(typeof(CardPile), "Get",
            new[] { typeof(PileType), typeof(Player) });
        if (cardPileGet != null)
        {
            harmony.Patch(cardPileGet, prefix: new HarmonyMethod(GetMethod(nameof(CardPileGetPrefix))));
        }

        var pileTypeGetPile = AccessTools.Method(typeof(PileTypeExtensions), "GetPile",
            new[] { typeof(PileType), typeof(Player) });
        if (pileTypeGetPile != null)
        {
            harmony.Patch(pileTypeGetPile, prefix: new HarmonyMethod(GetMethod(nameof(PileTypeExtensionsGetPilePrefix))));
        }

        _logger?.Info($"[Foreve][Dual] 牌堆重定向已装 (Pile={pileProperty != null}, CardPile.Get={cardPileGet != null}, PileTypeExtensions.GetPile={pileTypeGetPile != null})");
    }

    /// <summary>
    /// PlayerCombatState 牌堆属性重定向（2026-08-17 修复「重振旗鼓/不羁之剑不能结算」）：
    /// owner 交换期间，副玩家 PCS 的 Hand/DrawPile/DiscardPile/ExhaustPile/PlayPile 全部解析到
    /// 主玩家同类型牌堆。原因：卡牌 OnPlay 里常见的 Owner.PlayerCombatState.DiscardPile 等
    /// 直接走 PCS 属性（不走 CardPile.Get/GetPile），交换后读到的副玩家牌堆是空壳（牌库已合并
    /// 到主玩家）→ 重振旗鼓/不羁之剑/铁纪/骑士的遗书/斩断等「从牌堆检索」效果全部失效。
    /// </summary>
    private static void InstallCombatPilePropertyRedirects(Harmony harmony)
    {
        var secondary = typeof(PlayerCombatState);
        var props = new (string Name, PileType Type)[]
        {
            ("Hand", PileType.Hand),
            ("DrawPile", PileType.Draw),
            ("DiscardPile", PileType.Discard),
            ("ExhaustPile", PileType.Exhaust),
            ("PlayPile", PileType.Play),
        };
        var installed = 0;
        foreach (var (name, type) in props)
        {
            var getter = AccessTools.PropertyGetter(secondary, name);
            if (getter == null) continue;
            harmony.Patch(getter, prefix: new HarmonyMethod(GetMethod(nameof(PcsPilePropertyPrefix))));
            PcsPilePropertyTypes[getter] = type;
            installed++;
        }

        _logger?.Info($"[Foreve][Dual] PCS 牌堆属性重定向已装 ({installed}/5)");
    }

    /// <summary>PCS 牌堆属性 → PileType 映射（前缀按被调 getter 查类型）。</summary>
    private static readonly Dictionary<MethodInfo, PileType> PcsPilePropertyTypes = new();

    private static bool PcsPilePropertyPrefix(PlayerCombatState __instance, MethodBase __originalMethod, ref CardPile __result)
    {
        try
        {
            if (__instance == null || !IsSecondaryCardEffectActive) return true;
            if (!PcsPilePropertyTypes.TryGetValue((MethodInfo)__originalMethod, out var type)) return true;
            if (!DualCharacterState.Enabled) return true;

            var secondary = DualCharacterState.SecondaryPlayer;
            if (secondary == null || !ReferenceEquals(__instance, secondary.PlayerCombatState)) return true;

            var main = DualCharacterState.MainPlayer;
            if (main == null) return true;
            var pile = CardPile.Get(type, main);
            if (pile == null) return true;
            __result = pile;
            return false;
        }
        catch (Exception e)
        {
            GD.Print($"[Foreve][Dual] PCS 牌堆属性重定向异常: {e}");
            return true;
        }
    }

    /// <summary>
    /// CardSelectCmd 选择玩家重定向（2026-08-17 修复「重振旗鼓/不羁之剑升级后不能结算」）：
    /// owner 交换期间把 FromCombatPile/FromHand 的 player 参数从副玩家换成主玩家。
    /// 原因：FromCombatPile 内部按 ShouldSelectLocalCard(player) 决定本地弹选择还是
    /// WaitForRemoteChoice(player)——副玩家不是本地玩家，等待远程选择永远不会到来 → 卡死。
    /// 换为主玩家后选择 UI 正常弹出；所选牌仍从主玩家牌堆取（PCS 牌堆属性重定向已覆盖）。
    /// </summary>
    private static void InstallCardSelectRedirects(Harmony harmony)
    {
        var fromCombatPile4 = AccessTools.Method(typeof(CardSelectCmd), "FromCombatPile",
            new[] { typeof(PlayerChoiceContext), typeof(CardPile), typeof(Player), typeof(CardSelectorPrefs) });
        var fromCombatPile5 = AccessTools.Method(typeof(CardSelectCmd), "FromCombatPile",
            new[] { typeof(PlayerChoiceContext), typeof(CardPile), typeof(Player), typeof(CardSelectorPrefs), typeof(Func<CardModel, bool>) });
        var fromHand = AccessTools.Method(typeof(CardSelectCmd), "FromHand",
            new[] { typeof(PlayerChoiceContext), typeof(Player), typeof(CardSelectorPrefs), typeof(Func<CardModel, bool>), typeof(AbstractModel) });

        foreach (var m in new[] { fromCombatPile4, fromCombatPile5, fromHand })
        {
            if (m == null) continue;
            harmony.Patch(m, prefix: new HarmonyMethod(GetMethod(nameof(CardSelectPlayerPrefix))));
        }

        _logger?.Info($"[Foreve][Dual] CardSelectCmd 选择玩家重定向已装 (FromCombatPile4={fromCombatPile4 != null}, FromCombatPile5={fromCombatPile5 != null}, FromHand={fromHand != null})");
    }

    /// <summary>
    /// 混合 owner 批量入堆补丁（2026-08-24「副玩家生成卡导致回合结束 Add 抛异常」防护）：
    /// 主玩家手牌里可能同时有主玩家 owner 和副玩家 owner 的卡（例如指定副角色时生成的小刀）。
    /// 原版 CardPileCmd.Add(IEnumerable, PileType, ...) 先按第一张卡 owner 解析目标牌堆，
    /// 核心方法又要求同批次所有卡 owner 相同，否则抛「different owners」异常。
    /// 双人模式下只要批次里出现副玩家 owner 的卡，就把整个批次按主玩家牌堆分组执行，
    /// 既保持各卡 owner 不变，又全部落到共享主牌堆。
    /// </summary>
    private static void InstallMixedOwnerAddPatch(Harmony harmony)
    {
        var addMany = AccessTools.Method(typeof(CardPileCmd), "Add",
            new[]
            {
                typeof(IEnumerable<CardModel>), typeof(PileType), typeof(CardPilePosition),
                typeof(AbstractModel), typeof(bool),
            });
        if (addMany != null)
        {
            harmony.Patch(addMany, prefix: new HarmonyMethod(GetMethod(nameof(CardPileCmdAddManyPrefix))));
        }
        _logger?.Info($"[Foreve][Dual] 混合 owner 批量入堆补丁已装 (AddMany={addMany != null})");
    }

    private static bool CardPileCmdAddManyPrefix(IEnumerable<CardModel> cards, PileType newPileType,
        CardPilePosition position, AbstractModel? clonedBy, bool skipVisuals,
        ref Task<IReadOnlyList<CardPileAddResult>> __result)
    {
        try
        {
            if (!DualCharacterState.Enabled || cards == null) return true;
            var secondary = DualCharacterState.SecondaryPlayer;
            var main = DualCharacterState.MainPlayer;
            if (secondary == null || main == null) return true;

            var list = cards as IReadOnlyList<CardModel> ?? cards.ToList();
            if (list.Count == 0 || !list.Any(c => c != null && ReferenceEquals(c.Owner, secondary))) return true;

            var mainPile = CardPile.Get(newPileType, main);
            if (mainPile == null) return true;

            __result = AddCardsToMainPileGroupedAsync(list, mainPile, position, clonedBy, skipVisuals);
            return false;
        }
        catch (Exception e)
        {
            GD.Print($"[Foreve][Dual] 混合 owner 批量入堆前缀异常: {e}");
            return true;
        }
    }

    /// <summary>按 owner 分组后全部加入主玩家目标牌堆（核心 Add 只处理同 owner 批次，不再抛异常）。</summary>
    private static async Task<IReadOnlyList<CardPileAddResult>> AddCardsToMainPileGroupedAsync(
        IEnumerable<CardModel> cards, CardPile mainPile, CardPilePosition position,
        AbstractModel? clonedBy, bool skipVisuals)
    {
        var results = new List<CardPileAddResult>();
        foreach (var group in cards.Where(c => c != null).GroupBy(c => c.Owner))
        {
            results.AddRange(await CardPileCmd.Add(group.ToList(), mainPile, position, clonedBy, skipVisuals));
        }
        return results;
    }

    private static void CardSelectPlayerPrefix(ref Player player)
    {
        try
        {
            if (player == null || !IsSecondaryCardEffectActive || !DualCharacterState.Enabled) return;
            var secondary = DualCharacterState.SecondaryPlayer;
            if (secondary == null || !ReferenceEquals(player, secondary)) return;
            var main = DualCharacterState.MainPlayer;
            if (main != null) player = main;
        }
        catch (Exception e)
        {
            GD.Print($"[Foreve][Dual] CardSelectCmd 选择玩家重定向异常: {e}");
        }
    }

    /// <summary>副玩家牌堆查找在 owner 交换期间统一解析为主玩家（副玩家无战斗牌堆 UI，且牌实际已合并到主玩家）。</summary>
    private static bool TryGetMainPlayerForPileRedirect(Player player, out Player? main)
    {
        main = null;
        if (!DualCharacterState.Enabled || !IsSecondaryCardEffectActive || player == null) return false;
        var secondary = DualCharacterState.SecondaryPlayer;
        if (secondary == null || !ReferenceEquals(player, secondary)) return false;
        main = DualCharacterState.MainPlayer;
        return main != null;
    }

    private static bool CardPilePropertyPrefix(CardModel __instance, ref CardPile? __result)
    {
        try
        {
            if (__instance == null) return true;
            if (SwappedOwners.TryGetValue(__instance, out var main))
            {
                __result = main.Piles.FirstOrDefault(p => p.Cards.Contains(__instance));
                if (__result == null)
                {
                    // 诊断：交换期间在主玩家牌堆找不到该牌（可能在副玩家牌堆/已移除/异常路径）
                    GD.Print($"[Foreve][Dual] Pile 重定向未命中: {__instance.Id.Entry}");
                }
                return false;
            }

            // 副玩家自有牌（含指定副角色时生成的小刀/TURBO 等）：物理上在主玩家牌堆，
            // 但 _owner=副玩家，原 getter 按副玩家牌堆查不到 → 永远补查主玩家牌堆。
            // 这样生成卡在出牌入口（ExecuteAction 读 card.Pile）与结算前后都能正确解析。
            // canonical 模型没有 Owner，直接放行原 getter，避免误抛 "used in incorrect place"。
            if (!__instance.IsMutable || !DualCharacterState.Enabled) return true;
            var secondary = DualCharacterState.SecondaryPlayer;
            if (secondary == null || !ReferenceEquals(__instance.Owner, secondary)) return true;
            main = DualCharacterState.MainPlayer;
            if (main == null) return true;
            var pile = main.Piles.FirstOrDefault(p => p.Cards.Contains(__instance));
            if (pile == null) return true;
            __result = pile;
            return false;
        }
        catch (Exception e)
        {
            GD.Print($"[Foreve][Dual] CardModel.Pile 重定向异常: {e.Message}");
            return true; // 降级走原 getter（按 _owner 解析）
        }
    }

    private static bool CardPileGetPrefix(PileType type, Player player, ref CardPile? __result)
    {
        if (!TryGetMainPlayerForPileRedirect(player, out var main) || main == null) return true;
        __result = CardPile.Get(type, main);
        return false;
    }

    private static bool PileTypeExtensionsGetPilePrefix(PileType pileType, Player player, ref CardPile __result)
    {
        if (!TryGetMainPlayerForPileRedirect(player, out var main) || main == null) return true;
        var mainPile = CardPile.Get(pileType, main);
        if (mainPile == null) return true;
        __result = mainPile;
        return false;
    }


    /// <summary>
    /// OnPlayWrapper 入口：双人模式下把 _owner 换成「卡牌所属角色」玩家（原 owner 记入 SwappedOwners）。
    /// 副玩家自有生成卡（owner 已是副玩家）无需交换，但记入 SecondaryOwnedCardPlayScopes 以维持
    /// 牌堆/选择重定向。非双人 / 未命中 / 目标 creature 已死：不处理。
    /// </summary>
    private static void OnPlayWrapperPrefix(CardModel __instance)
    {
        try
        {
            if (__instance == null || OwnerField == null) return;

            // 自愈：上次同步异常可能泄漏了交换/副玩家卡作用域（Postfix 未跑），先清理再走正常流程
            if (SwappedOwners.TryGetValue(__instance, out var leakedOwner))
            {
                SwappedOwners.Remove(__instance);
                OwnerField.SetValue(__instance, leakedOwner);
            }
            SecondaryOwnedCardPlayScopes.Remove(__instance);

            var originalOwner = (Player?)OwnerField.GetValue(__instance);
            var naturalOwner = ResolveCardOwnerPlayer(__instance);
            // 2026-08-15 用户需求：任一角色死亡不影响游戏，死亡角色的牌由存活角色打出。
            // 自然归属角色死亡（或未命中归属）时，重定向到存活玩家；两者都活时保持自然归属。
            var effectiveOwner = ResolveEffectiveCardOwner(naturalOwner, originalOwner);
            // 2026-08-16 用户需求：非主副角色技能牌/能力牌（无色牌/其他角色牌）出牌时由玩家
            // 弹窗指定目标角色（DualCharacterCardTargetPatch 在出牌动作入口记录）——对己效果
            // 按指定角色结算；选主角色时本就是默认 owner，无需交换；选副角色走下方统一交换。
            if (naturalOwner == null && __instance.Type is CardType.Skill or CardType.Power)
            {
                var pendingTarget = DualCharacterCardTargetPatch.GetPendingTarget(__instance);
                if (pendingTarget != null && pendingTarget.Creature is { IsDead: false })
                    effectiveOwner = pendingTarget;
            }

            if (effectiveOwner == null) return;

            // 副玩家自有生成卡（如指定副角色时获得的小刀）：owner 保持副玩家，结算期间只做牌堆重定向。
            if (ReferenceEquals(effectiveOwner, originalOwner))
            {
                if (DualCharacterState.Enabled && DualCharacterState.IsSecondaryPlayer(originalOwner)
                    && originalOwner.Creature is { IsDead: false })
                {
                    SecondaryOwnedCardPlayScopes[__instance] = originalOwner;
                }
                return;
            }

            if (effectiveOwner.Creature == null || effectiveOwner.Creature.IsDead) return;

            SwappedOwners[__instance] = originalOwner!;
            OwnerField.SetValue(__instance, effectiveOwner);
        }
        catch (Exception e)
        {
            GD.Print($"[Foreve][Dual] OnPlayWrapper 前缀异常: {e}");
        }
    }

    /// <summary>
    /// async 方法的 Postfix 在原始方法返回 Task 时即执行（早于 async 体完成），直接恢复会过早；
    /// 因此把返回的 Task 替换为包装任务：await 原任务后在 finally 恢复（恢复时机 = OnPlay 执行完）。
    /// </summary>
    private static void OnPlayWrapperPostfix(CardModel __instance, ref Task __result)
    {
        try
        {
            if (__instance == null || OwnerField == null || __result == null) return;
            if (SwappedOwners.ContainsKey(__instance))
            {
                __result = RestoreOwnerAfterAsync(__result, __instance);
            }
            else if (SecondaryOwnedCardPlayScopes.ContainsKey(__instance))
            {
                __result = RestoreSecondaryOwnedCardScopeAfterAsync(__result, __instance);
            }
        }
        catch (Exception e)
        {
            GD.Print($"[Foreve][Dual] OnPlayWrapper 后缀异常: {e}");
        }
    }

    /// <summary>等待原任务完成后移除副玩家自有卡作用域（finally 语义：异常/取消也恢复）。</summary>
    private static async Task RestoreSecondaryOwnedCardScopeAfterAsync(Task original, CardModel card)
    {
        try
        {
            await original;
        }
        finally
        {
            try
            {
                SecondaryOwnedCardPlayScopes.Remove(card);
                // 正常路径下 OnPlayWrapper 已把牌移入主玩家弃牌/消耗堆；异常中断时兜底收尾。
                var main = DualCharacterState.MainPlayer;
                if (main != null) SweepStuckPlayPile(card, main);
            }
            catch (Exception e)
            {
                SecondaryOwnedCardPlayScopes.Remove(card);
                GD.Print($"[Foreve][Dual] 副玩家自有卡作用域恢复异常: {e}");
            }
        }
    }

    /// <summary>等待原任务完成后恢复 _owner（finally 语义：原任务异常/取消也会恢复）。</summary>
    private static async Task RestoreOwnerAfterAsync(Task original, CardModel card)
    {
        try
        {
            await original;
        }
        finally
        {
            try
            {
                if (OwnerField != null && SwappedOwners.TryGetValue(card, out var originalOwner))
                {
                    // 牌堆归位（轮 8）：恢复 owner 前把牌从副玩家牌堆移到主玩家同名牌堆。
                    // 原因：OnPlayWrapper 期间 owner 被换成副玩家 → 内部 CardPileCmd.Add
                    // （AddDuringManualCardPlay 进 PlayPile、结果牌进 Discard/Exhaust）按 Owner
                    // 解析牌堆 → 牌进副玩家 PCS 的牌堆（副玩家无对应 UI 容器）→ 视觉悬停
                    // 画面中央，回合结束清理才进弃牌堆。
                    RestoreCardToMainPiles(card, originalOwner);
                    SwappedOwners.Remove(card);
                    OwnerField.SetValue(card, originalOwner);
                }

                // 交换期间生成的卡（小刀/TURBO 等）owner 保持副玩家，不再统一归主：
                // CardModel.Pile 已对副玩家 owner 的卡永久补查主玩家牌堆，且这些卡再次打出时
                // 会进入 SecondaryOwnedCardPlayScopes，效果归属副玩家、牌堆走主玩家。
            }
            catch (Exception e)
            {
                SwappedOwners.Remove(card);
                GD.Print($"[Foreve][Dual] OnPlayWrapper 恢复 owner 异常: {e}");
            }
        }
    }

    /// <summary>
    /// 牌堆归位（轮 8 修复「副角色牌使用后悬停画面中央」+ 2026-08-16 弃牌堆兜底）：
    /// 遍历副玩家 PCS 全部牌堆（Hand/Draw/Discard/Exhaust/Play），含该牌则移到主玩家
    /// 同类型牌堆（CardPile.RemoveInternal/AddInternal 公开 API，IL 实证；silent 不触发
    /// 事件，避免牌堆 UI 动画冲突）。OnPlay 效果本身（荣誉/伤害按副玩家结算）不受影响。
    ///
    /// 2026-08-16 追加兜底（用户反馈：副角色牌使用后不进入弃牌堆）：
    /// 若结果堆 Add 因异常中断或 CardModel.Pile 解析失败被跳过，牌会残留在 Play 堆。
    /// 归位时统一把残留在 Play 堆的交换牌按结果堆类型收尾：
    ///   消耗关键字 → 主玩家消耗堆；普通牌 → 主玩家弃牌堆；
    ///   能力牌/复制牌的结果堆是 None（走 CardPileCmd.RemoveFromCombat），不在此归位。
    /// 正常路径下牌早已进入弃牌/消耗堆，此兜底为无操作。
    /// </summary>
    private static void RestoreCardToMainPiles(CardModel card, Player main)
    {
        try
        {
            var secondary = DualCharacterState.SecondaryPlayer;
            if (secondary == null || main == null || card == null) return;
            var secPcs = secondary.PlayerCombatState;
            if (secPcs == null) return;
            foreach (var pile in secPcs.AllPiles)
            {
                if (pile == null || !pile.Cards.Contains(card)) continue;
                var target = CardPile.Get(pile.Type, main);
                if (target == null) continue;
                pile.RemoveInternal(card, silent: true);
                target.AddInternal(card, -1, silent: true);
                // silent 移动不触发 CardAddFinished → 左下角弃牌堆/抽牌堆/消耗堆计数不刷新，
                // 对有计数按钮的牌堆补发一次事件同步（NDiscardPileButton 等监听 CardAddFinished；
                // 手牌/出牌堆无计数按钮，不补发，避免触发手牌视觉逻辑）。
                if (target.Type is PileType.Draw or PileType.Discard or PileType.Exhaust)
                    target.InvokeCardAddFinished();
                // 主玩家手牌残留清理（实测轮 8 结束回合卡死根因）：
                // 副卡打牌时 owner 已换副玩家，原版「从手牌移除」步骤按副玩家手牌执行（空）→
                // 主玩家手牌残留该牌 → FlushPlayerHand 重复 Add 抛 InvalidOperationException（日志实证）。
                // CardPile 无 Contains 实例方法（IL 实证），用 Cards.Contains（与上方同款）。
                var mainPcs = main.PlayerCombatState;
                if (mainPcs?.Hand != null && mainPcs.Hand.Cards.Contains(card))
                {
                    mainPcs.Hand.RemoveInternal(card, silent: true);
                    GD.Print($"[Foreve][Dual] 主手牌残留清理: {card.Id.Entry}");
                }
                // optimizer：移除每次出牌必打的「牌堆归位」成功日志
                break; // 一张牌只可能在一个牌堆里，移出后结束扫描
            }

            // 弃牌堆兜底：结果堆 Add 被跳过时牌残留在主玩家 Play 堆（或刚被上方从副玩家
            // Play 堆移回主玩家 Play 堆），按结果堆类型收尾到弃牌/消耗堆。
            SweepStuckPlayPile(card, main);
        }
        catch (Exception e)
        {
            GD.Print($"[Foreve][Dual] 牌堆归位异常: {e}");
        }
    }

    /// <summary>
    /// 交换牌残留 Play 堆兜底（2026-08-16）：能力牌/复制牌的结果堆是 None（RemoveFromCombat
    /// 路径），其余按「消耗 → 消耗堆，普通 → 弃牌堆」收尾，保证副角色普通牌使用后进弃牌堆。
    /// </summary>
    private static void SweepStuckPlayPile(CardModel card, Player main)
    {
        var mainPcs = main.PlayerCombatState;
        if (mainPcs?.PlayPile == null || !mainPcs.PlayPile.Cards.Contains(card)) return;
        if (card.Type == CardType.Power || card.IsDupe) return; // None 结果堆，交给 RemoveFromCombat

        var target = card.Keywords.Contains(CardKeyword.Exhaust) ? mainPcs.ExhaustPile : mainPcs.DiscardPile;
        mainPcs.PlayPile.RemoveInternal(card, silent: true);
        target.AddInternal(card, -1, silent: true);
        // silent 移动不触发 CardAddFinished → 左下角弃牌堆/消耗堆计数不刷新，补发事件同步
        target.InvokeCardAddFinished();
        GD.Print($"[Foreve][Dual] 牌堆兜底归位: {card.Id.Entry} Play → {target.Type}");
    }

    /// <summary>
    /// 计算卡牌实际效果归属玩家：
    ///   1. 自然归属玩家存活 → 用它（原逻辑）。
    ///   2. 自然归属玩家已死 / 未命中归属 → 存活玩家优先：原 owner 若在队且存活就保留，
    ///      否则取主/副中存活者（任一角色死亡后牌由存活角色打出）。
    ///   3. 两名角色都死亡 → 返回自然归属（游戏原生判负流程处理）。
    /// </summary>
    private static Player? ResolveEffectiveCardOwner(Player? naturalOwner, Player? originalOwner)
    {
        if (naturalOwner?.Creature is { IsDead: false }) return naturalOwner;
        if (!DualCharacterState.Enabled) return naturalOwner;

        var main = DualCharacterState.MainPlayer;
        var secondary = DualCharacterState.SecondaryPlayer;

        // 原 owner 就是在队玩家且仍存活 → 不需要转移（例如副角色死亡时主玩家继续用副牌）。
        var fallback = naturalOwner ?? originalOwner;
        if (fallback != null
            && (ReferenceEquals(fallback, main) || ReferenceEquals(fallback, secondary))
            && fallback.Creature is { IsDead: false })
        {
            return fallback;
        }

        if (main?.Creature is { IsDead: false }) return main;
        if (secondary?.Creature is { IsDead: false }) return secondary;
        return naturalOwner;
    }

    /// <summary>
    /// 卡牌归属玩家：双人模式下查两名玩家角色的 CardPool.AllCardIds（懒缓存 HashSet）是否含该卡
    /// canonical id。命中 → 该玩家；未命中（无色/共享/生成卡）→ null（默认主玩家，不替换）。
    /// internal 供 DualCharacterCardTargetPatch 判定「非主副角色卡」复用。
    /// </summary>
    internal static Player? ResolveCardOwnerPlayer(CardModel card)
    {
        if (!DualCharacterState.Enabled) return null;
        var combatState = card.CombatState;
        if (combatState == null || !DualCharacterState.IsDualMode(combatState)) return null;
        var players = combatState.Players;
        if (players == null || players.Count < 2) return null;

        for (var i = 0; i < players.Count; i++)
        {
            var pool = players[i].Character?.CardPool;
            if (pool == null) continue;
            if (pool.AllCardIds.Contains(card.Id)) return players[i];
        }
        return null;
    }

    private static MethodInfo GetMethod(string name)
        => typeof(DualCharacterCardOwnerPatch).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!;
}
