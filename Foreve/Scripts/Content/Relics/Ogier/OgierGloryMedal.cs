using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using STS2RitsuLib.Combat.SecondaryResources;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;
using Foreve.Scripts.DualCharacter;

namespace Foreve.Scripts.Content.Relics.Ogier;

[RegisterRelic(typeof(Characters.Ogier.OgierRelicPool))]
public class OgierGloryMedal : ModRelicTemplate, ISecondaryResourceHookListener
{
    public override RelicRarity Rarity => RelicRarity.Rare;

    public override RelicAssetProfile AssetProfile => new(
        IconPath: "res://Foreve/Assets/Relics/Ogier/OgierGloryMedal.png",
        IconOutlinePath: "res://Foreve/Assets/Relics/Ogier/OgierGloryMedal_outline.png",
        BigIconPath: "res://Foreve/Assets/Relics/Ogier/OgierGloryMedal_big.png"
    );

    public Decimal ModifyMaxSecondaryResource(SecondaryResourceMaxContext ctx, Decimal currentMax)
    {
        if (ctx.Player == DualCharacterRelicScoping.ResolveOgierPlayer(Owner) &&
            ctx.Definition.Id == Characters.Ogier.OgierCharacter.HonorResourceId)
            return currentMax + 2;
        return currentMax;
    }

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
    {
        // 荣誉是奥吉尔专属系统：仍只在奥吉尔所属玩家的回合开始配置阶段触发一次，
        // 并从奥吉尔玩家读取荣誉（主/副无关）。
        var honorPlayer = DualCharacterRelicScoping.ResolveOgierPlayer(Owner);
        if (player != honorPlayer) return;

        var honor = SecondaryResourceCmd.Get(honorPlayer, Characters.Ogier.OgierCharacter.HonorResourceId);
        if (honor > 5)
        {
            // 2026-08-18 遗物系统重做：力量只给「装备角色」（初始遗物=奥吉尔），不再全队化。
            // 单玩家模式退化为只给 Owner.Creature（零变化）。
            var equipper = DualCharacterRelicEquip.ResolveEquippedPlayer(this, Owner);
            var target = equipper.Creature;
            if (target == null || target.IsDead) return;
            await PowerCmd.Apply<StrengthPower>(choiceContext, target, 1, target, null, false);
        }
    }
}
