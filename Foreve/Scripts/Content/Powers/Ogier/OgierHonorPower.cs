using MegaCrit.Sts2.Core.Entities;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib.Combat.SecondaryResources;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;
using Foreve.Scripts.Characters.Ogier;
using Foreve.Scripts.Content.Cards.Ogier;
using Foreve.Scripts.DualCharacter;

namespace Foreve.Scripts.Content.Powers.Ogier;

[RegisterPower]
public class OgierHonorPower : ModPowerTemplate
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.None;
    protected override bool IsVisibleInternal => false;

    public override PowerAssetProfile AssetProfile => new(
        IconPath: "res://Foreve/Assets/Powers/ogier_honor.png",
        BigIconPath: "res://Foreve/Assets/Powers/ogier_honor_big.png"
    );

    public override Decimal ModifyDamageAdditive(
        Creature target,
        Decimal amount,
        ValueProp props,
        Creature dealer,
        CardModel? cardSource)
    {
        // 只加成「受力量加成的攻击」（IsPoweredAttack = 带 Move 且非 Unpowered，与力量判定一致）。
        // 不能用 props == ValueProp.Move 精确比较：穿刺伤害实际结算带 Move|Unblockable|SkipHurtAnim
        // 附加标记，精确比较会让牌面预览有荣誉、实际伤害没有（2026-08-26 实测 10 vs 8）。
        if (!props.IsPoweredAttack()) return 0;

        // 荣誉只强化奥吉尔自己的攻击（主/副角色无关）。
        // 萝坦牌必须排除：主玩家为奥吉尔且持荣誉时，萝坦卡预览/结算曾错误 +2（2026-08-15 实测）。
        if (cardSource != null && DualCharacterRelicScoping.IsRotanCard(cardSource))
            return 0;

        // 双人局奥吉尔为副角色时，手牌中卡牌 Owner 仍为主玩家 → 预览阶段 dealer 是主玩家。
        // 只要是奥吉尔的牌，就按奥吉尔 creature 判定；实际打出时卡牌归属 patch 已把 dealer 换成奥吉尔。
        var ogierCreature = DualCharacterRelicScoping.GetOgierCreature();
        var effectiveDealer = dealer;
        if (cardSource != null && DualCharacterRelicScoping.IsOgierCard(cardSource) && ogierCreature != null)
            effectiveDealer = ogierCreature;

        if (ogierCreature != null ? effectiveDealer != ogierCreature : dealer != Owner)
            return 0;

        // 荣誉资源始终读奥吉尔所属玩家（单玩家回退 power owner）。
        var ownerPlayer = Owner.Player;
        if (ownerPlayer == null) return 0;
        var player = DualCharacterRelicScoping.ResolveOgierPlayer(ownerPlayer);
        var honor = SecondaryResourceCmd.Get(player, OgierCharacter.HonorResourceId);
        return honor > 0 ? 2 : 0;
    }

    public override bool TryModifyKeywordsInCombat(CardModel card, ISet<CardKeyword> keywords)
    {
        var ownerPlayer = Owner.Player;
        if (ownerPlayer == null) return false;

        // 荣誉属于奥吉尔角色本人（主/副无关），不依赖 power 挂在哪个玩家身上。
        var player = DualCharacterRelicScoping.ResolveOgierPlayer(ownerPlayer);
        var honor = SecondaryResourceCmd.Get(player, OgierCharacter.HonorResourceId);

        if (card is OgierShieldBash)
        {
            if (honor > 0)
                keywords.Add(CardKeyword.Retain);
        }
        else if (card is OgierHolyShieldPrayer)
        {
            if (card.IsUpgraded || honor >= 3)
                keywords.Add(CardKeyword.Retain);
        }

        return false;
    }
}
