using System;
using System.Linq;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace Foreve.Scripts.Content.Cards.Rotan;

[RegisterCard(typeof(Characters.Rotan.RotanCardPool))]
public class RotanHardScale : ModCardTemplate
{
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Rotan/{GetType().Name}.png"
    );

    public RotanHardScale() : base(baseCost: 1, type: CardType.Power, rarity: CardRarity.Uncommon, target: TargetType.Self, showInCardLibrary: true) { }

    // 升级（坚硬鳞甲+）：获得固有关键字（Innate）——游戏 Keywords 缓存不随 IsUpgraded 刷新，在 OnUpgrade 动态添加
    public override HashSet<CardKeyword> CanonicalKeywords => [];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await PowerCmd.Apply<ThornsPower>(choiceContext, Owner.Creature, IsUpgraded ? 4 : 3, Owner.Creature, null, false);
        await PowerCmd.Apply<RotanScaleArmorPower>(choiceContext, Owner.Creature, 1, Owner.Creature, null, false);
    }

    protected override void OnUpgrade()
    {
        AddKeyword(CardKeyword.Innate);
    }
}

[RegisterPower]
public class RotanScaleArmorPower : ModPowerTemplate
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;

    public override PowerAssetProfile AssetProfile => new(
        IconPath: "res://Foreve/Assets/Powers/rotan_scale_armor.png",
        BigIconPath: "res://Foreve/Assets/Powers/rotan_scale_armor_big.png"
    );

    // 坚硬鳞甲触发机制（重写版）：
    // 覆写游戏原生「伤害结算前」虚方法 AbstractModel.BeforeDamageReceived。
    // 调用链实证（sts2_full.il）：CreatureCmd.Damage 状态机内先调 Hook.BeforeDamageReceived
    // （遍历 runState.IterateHookListeners 分发到全部模型，原版 ThornsPower 同款覆写），
    // 之后才调 Creature.DamageBlockInternal 扣格挡 → ModifyHpLost → Hook.AfterDamageReceived。
    // 因此本方法执行时 target.Block 仍是本次伤害前的格挡值，amount 是修正后的原始伤害。
    public override async Task BeforeDamageReceived(PlayerChoiceContext ctx, Creature target, Decimal amount, ValueProp props, Creature dealer, CardModel cardSource)
    {
        // 只处理自己受到的伤害
        if (!ReferenceEquals(target, Owner)) return;

        // 无视格挡的伤害不转化：ValueProp.Unpowered 即穿格挡标志
        // （DamageBlockInternal IL 实证：props.HasFlag(Unpowered) 时 blocked=0，格挡不起作用）
        if (props.HasFlag(ValueProp.Unpowered)) return;

        // 荆棘是原版 power（MegaCrit.Sts2.Core.Models.Powers.ThornsPower），由本卡打出时附加
        var thorns = Owner.Powers.OfType<ThornsPower>().FirstOrDefault();
        if (thorns == null || thorns.Amount <= 0) return;

        int incoming = (int)Math.Ceiling(amount); // 即将结算的伤害（格挡扣除前）
        int currentBlock = target.Block;          // 本次伤害前的当前格挡
        if (incoming <= currentBlock) return;     // 格挡足够，不触发

        // 需要额外格挡 = 伤害 - 当前格挡；1 点荆棘 = 2 点格挡，尽量少转化 → ceil(need / 2)；荆棘不足则全部转化
        int need = incoming - currentBlock;
        int convert = Math.Min((need + 1) / 2, thorns.Amount);
        if (convert <= 0) return;

        // 转化 = 先消耗荆棘、再获得格挡。
        // 消耗走 PowerCmd.ModifyAmount 负数改层（官方标准路径，PowerCmd.Decrement 即 ModifyAmount(..., -1)；
        // 内部走 BeforePowerAmountChanged/AfterPowerAmountChanged hook 并刷新 UI，PowerModel.Amount 无 setter 不可直写）。
        // 转化后荆棘减少 → 原版荆棘反弹量同步减少，符合「消耗前者获得后者」语义。
        await PowerCmd.ModifyAmount(ctx, thorns, -convert, Owner, null, false);
        await CreatureCmd.GainBlock(Owner, convert * 2, ValueProp.Move, null, false);
    }
}
