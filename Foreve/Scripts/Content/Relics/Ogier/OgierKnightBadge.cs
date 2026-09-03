using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib.Combat.AttackHits;
using STS2RitsuLib.Combat.SecondaryResources;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;
using Foreve.Scripts.Characters.Ogier;
using Foreve.Scripts.Content.Powers.Ogier;
using Foreve.Scripts.DualCharacter;

namespace Foreve.Scripts.Content.Relics.Ogier;

[RegisterRelic(typeof(OgierRelicPool))]
[RegisterCharacterStarterRelic(typeof(OgierCharacter))]
public class OgierKnightBadge : ModRelicTemplate, IAttackHitHookListener
{
    public override RelicRarity Rarity => RelicRarity.Starter;

    public override RelicAssetProfile AssetProfile => new(
        IconPath: "res://Foreve/Assets/Relics/Ogier/OgierKnightBadge.png",
        IconOutlinePath: "res://Foreve/Assets/Relics/Ogier/OgierKnightBadge_outline.png",
        BigIconPath: "res://Foreve/Assets/Relics/Ogier/OgierKnightBadge_big.png"
    );

    private bool _honorPowersApplied;
    private bool _perfectBlockHonorUsedThisTurn;

    public override async Task BeforeCombatStart()
    {
        _honorPowersApplied = false;
    }

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
    {
        _perfectBlockHonorUsedThisTurn = false;

        if (!_honorPowersApplied)
        {
            // 荣誉属于奥吉尔角色本人：双人局无论奥吉尔是主/副玩家，标记 power 都必须挂到
            // 奥吉尔 creature 上（否则 Owner.Player 读到另一名玩家的荣誉，+2 与关键词全错）。
            var honorCreature = DualCharacterRelicScoping.ResolveOgierCreature(player.Creature);
            if (honorCreature == null) return;

            _honorPowersApplied = true;
            await PowerCmd.Apply<OgierHonorPower>(choiceContext, honorCreature, 1, honorCreature, null, false);
        }
    }

    // 单玩家模式：原版 hook（owner 受击时触发）；双人下由 IAttackHitHookListener 接管。
    public override async Task AfterDamageReceived(
        PlayerChoiceContext choiceContext,
        Creature target,
        DamageResult result,
        ValueProp props,
        Creature dealer,
        CardModel? cardSource)
    {
        if (DualCharacterState.Enabled) return; // 双人模式走攻击命中全量 hook（下），避免双重触发
        if (_perfectBlockHonorUsedThisTurn) return;
        if (target != Owner.Creature) return;
        if (result.TotalDamage <= 0) return;
        if (result.UnblockedDamage > 0) return;

        _perfectBlockHonorUsedThisTurn = true;
        await SecondaryResourceCmd.Gain(Owner, OgierCharacter.HonorResourceId, 1, this);
    }

    /// <summary>
    /// 双人模式：RitsuLib 攻击命中战斗级全量 hook（谁挨打都收到，与遗物 owner 无关）。
    /// 2026-08-15 新规格：遗物（含初始遗物）对两名角色适用 —— 萝坦或奥吉尔完全格挡
    /// 一次均触发本遗物；但荣誉是奥吉尔专属系统，+1 只入账并显示在奥吉尔所属玩家身上。
    /// 完全格挡判定与单玩家路径一致：TotalDamage &gt; 0 且 UnblockedDamage == 0。
    /// 单目标与 AOE 统一处理：context.Targets 与 context.Results 按目标顺序一一对应
    /// （SetResults 承接 CreatureCmd.Damage 的逐目标结算），一次命中同时打两名角色时，
    /// 只要任一名角色的对应结算满足完全格挡即触发。
    /// </summary>
    public async Task AfterAttackHit(AttackHitContext context)
    {
        if (!DualCharacterState.Enabled) return;
        if (_perfectBlockHonorUsedThisTurn) return;
        if (context.Results == null || context.Results.Count == 0) return;

        // 荣誉只能记在奥吉尔身上；没有奥吉尔在场时本遗物不产生荣誉。
        var honorPlayer = DualCharacterRelicScoping.GetOgierPlayer();
        if (honorPlayer == null) return;

        for (var i = 0; i < context.Targets.Count && i < context.Results.Count; i++)
        {
            // 只看玩家 creature（主/副皆可）；敌人目标与本次遗物无关。
            if (!DualCharacterEffects.IsPlayerCreature(context.CombatState, context.Targets[i])) continue;

            var result = context.Results[i];
            if (result.TotalDamage <= 0) continue;    // 该命中未对这名角色造成伤害
            if (result.UnblockedDamage > 0) continue; // 这名角色未完全格挡

            _perfectBlockHonorUsedThisTurn = true;
            // 荣誉入账到奥吉尔所属玩家（主/副皆可），而不是遗物 owner（遗物已合并到主玩家）。
            await SecondaryResourceCmd.Gain(
                honorPlayer,
                OgierCharacter.HonorResourceId,
                1,
                this);
            return;
        }
    }
}
