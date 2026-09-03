using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace Foreve.Scripts.Content.Powers.Ogier;

[RegisterPower]
public class OgierSacrificePower : ModPowerTemplate
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.None;

    public override PowerAssetProfile AssetProfile => new(
        IconPath: "res://Foreve/Assets/Powers/ogier_sacrifice.png",
        BigIconPath: "res://Foreve/Assets/Powers/ogier_sacrifice_big.png"
    );

    public override async Task AfterCurrentHpChanged(Creature creature, Decimal amount)
    {
        if (creature != Owner) return;
        // 仅在失去生命时触发（amount > 0 表示治疗，amount < 0 表示伤害）
        if (amount >= 0) return;

        var lostAmount = Decimal.Abs(amount);
        await CreatureCmd.GainBlock(Owner, lostAmount, ValueProp.Move, null, false);
    }
}
