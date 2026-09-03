using System;
using System.Collections.Generic;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using Foreve.Scripts.DualCharacter;

namespace Foreve.Scripts.Patches;

/// <summary>
/// 双角色模式：卡牌伤害/格挡数值按「卡牌所属角色」结算（2026-08-26 实测修复）。
///
/// 问题：双人模式开局副玩家牌库已并入主玩家，所有卡牌 CardModel.Owner 都是主玩家
/// （仅 OnPlayWrapper 执行期间被卡牌归属 patch 临时换成本名角色）。而游戏卡牌预览
/// （DamageVar/BlockVar.UpdateCardPreview，IL 实证）和 RitsuLib 预览都把
/// 「伤害来源 dealer / 格挡目标」解析为 card.Owner.Creature —— 主玩家 creature。
/// 于是主玩家的力量/活力（ModifyDamageAdditive 按 dealer 判定）会错误加成到副角色
/// 卡牌的预览数值，副角色自己的力量/敏捷反而加不上（格挡同理）。测试实证：
/// 主角色持活力时，副角色卡牌伤害数值被主角色活力抬高。
///
/// 修复：在 Hook.ModifyDamage / Hook.ModifyBlock 前缀把来源重定向为卡牌所属角色
/// （DualCharacterCardOwnerPatch.ResolveCardOwnerPlayer，按卡池判定）的 creature。
/// 前缀只拦截「dealer/target == card.Owner.Creature」的默认路径：
/// - 手牌预览/悬停/目标预览（Owner 未交换）→ 重定向到所属角色，力量/活力/敏捷按本人结算；
/// - 实际打出（OnPlayWrapper 已把 Owner 换成本名角色）→ dealer 已是本人 creature，无操作；
/// - 敌方攻击 / 药水 / 遗物伤害（cardSource 非玩家卡或为 null）→ 不拦截，零变化。
/// 单玩家局 / 未命中归属（无色/共享/生成卡）/ 所属角色已死：不重定向。
/// </summary>
public static class DualCharacterCardValuePatch
{
    private static MegaCrit.Sts2.Core.Logging.Logger? _logger;

    public static void Install(MegaCrit.Sts2.Core.Logging.Logger logger)
    {
        _logger = logger;
        var harmony = new Harmony("foreve.dual_character_card_value");

        var modifyDamage = AccessTools.Method(typeof(Hook), nameof(Hook.ModifyDamage),
            new[]
            {
                typeof(IRunState), typeof(ICombatState), typeof(Creature), typeof(Creature),
                typeof(decimal), typeof(ValueProp), typeof(CardModel), typeof(ModifyDamageHookType),
                typeof(CardPreviewMode), typeof(IEnumerable<AbstractModel>).MakeByRefType(),
            });
        if (modifyDamage != null)
        {
            harmony.Patch(modifyDamage, prefix: new HarmonyMethod(GetMethod(nameof(ModifyDamagePrefix))));
        }

        var modifyBlock = AccessTools.Method(typeof(Hook), nameof(Hook.ModifyBlock),
            new[]
            {
                typeof(ICombatState), typeof(Creature), typeof(decimal), typeof(ValueProp),
                typeof(CardModel), typeof(CardPlay), typeof(IEnumerable<AbstractModel>).MakeByRefType(),
            });
        if (modifyBlock != null)
        {
            harmony.Patch(modifyBlock, prefix: new HarmonyMethod(GetMethod(nameof(ModifyBlockPrefix))));
        }

        _logger?.Info($"[Foreve][Dual] 卡牌数值归属 patch 已装 (ModifyDamage={modifyDamage != null}, ModifyBlock={modifyBlock != null})");
    }

    /// <summary>伤害来源重定向：双人模式下把「预览/默认来源（Owner.Creature）」换为卡牌所属角色 creature。</summary>
    private static void ModifyDamagePrefix(ref Creature? dealer, CardModel? cardSource)
    {
        try
        {
            dealer = ResolveCardOwnerCreature(dealer, cardSource);
        }
        catch (Exception e)
        {
            GD.Print($"[Foreve][Dual] ModifyDamage 来源重定向异常: {e.Message}");
        }
    }

    /// <summary>格挡目标重定向：双人模式下把「预览/默认目标（Owner.Creature）」换为卡牌所属角色 creature。</summary>
    private static void ModifyBlockPrefix(ref Creature target, CardModel? cardSource)
    {
        try
        {
            target = ResolveCardOwnerCreature(target, cardSource) ?? target;
        }
        catch (Exception e)
        {
            GD.Print($"[Foreve][Dual] ModifyBlock 目标重定向异常: {e.Message}");
        }
    }

    /// <summary>
    /// 核心判定：仅当 current == card.Owner.Creature（预览默认来源路径）且卡牌有可解析的
    /// 所属角色且该角色 creature 存活且与 current 不同时，返回所属角色 creature，否则原样返回。
    /// 实际打出时 Owner 已被卡牌归属 patch 换成本名角色 → current == 所属角色，无操作。
    /// </summary>
    private static Creature? ResolveCardOwnerCreature(Creature? current, CardModel? cardSource)
    {
        if (current == null || cardSource == null || !DualCharacterState.Enabled) return current;
        if (!cardSource.IsMutable) return current; // canonical 模板（图鉴/奖励预览）无 Owner，不处理
        if (cardSource.Owner == null) return current;

        var natural = DualCharacterCardOwnerPatch.ResolveCardOwnerPlayer(cardSource);
        if (natural == null || natural.Creature == null || natural.Creature.IsDead) return current;
        if (!ReferenceEquals(current, cardSource.Owner.Creature)) return current; // 只重定向默认来源路径
        if (ReferenceEquals(current, natural.Creature)) return current;
        return natural.Creature;
    }

    private static MethodInfo GetMethod(string name)
        => typeof(DualCharacterCardValuePatch).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!;
}
