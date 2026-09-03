using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;
using Foreve.Scripts.Content.Powers.Ogier;
using Foreve.Scripts.DualCharacter;

namespace Foreve.Scripts.Content.Relics.Ogier;

[RegisterRelic(typeof(Characters.Ogier.OgierRelicPool))]
public class OgierGauntlet : ModRelicTemplate
{
    public override RelicRarity Rarity => RelicRarity.Uncommon;

    public override RelicAssetProfile AssetProfile => new(
        IconPath: "res://Foreve/Assets/Relics/Ogier/OgierGauntlet.png",
        IconOutlinePath: "res://Foreve/Assets/Relics/Ogier/OgierGauntlet_outline.png",
        BigIconPath: "res://Foreve/Assets/Relics/Ogier/OgierGauntlet_big.png"
    );

    private bool _applied;

    public override async Task BeforeCombatStart()
    {
        _applied = false;
    }

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
    {
        if (_applied) return;
        // 单玩家模式保持只给 Owner；双人模式在任一名玩家的回合开始配置阶段触发一次，
        // 覆甲只给「装备角色」（2026-08-18 遗物系统重做：角色向遗物按装备者结算；
        // 初始遗物开局已自动绑定所属角色，获得时不再全队化）。
        if (!DualCharacterState.Enabled && player != Owner) return;

        var equipper = DualCharacterRelicEquip.ResolveEquippedPlayer(this, Owner);
        var target = equipper.Creature;
        if (target == null || target.IsDead) return;

        _applied = true;
        await PowerCmd.Apply<OgierGauntletArmorPower>(choiceContext, target, 6, target, null, false);
    }
}
