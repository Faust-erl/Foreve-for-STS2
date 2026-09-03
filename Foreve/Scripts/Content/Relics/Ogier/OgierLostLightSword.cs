using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib.Combat.SecondaryResources;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;
using Foreve.Scripts.DualCharacter;

namespace Foreve.Scripts.Content.Relics.Ogier;

[RegisterRelic(typeof(Characters.Ogier.OgierRelicPool))]
public class OgierLostLightSword : ModRelicTemplate, ISecondaryResourceHookListener
{
    public override RelicRarity Rarity => RelicRarity.Common;

    public override RelicAssetProfile AssetProfile => new(
        IconPath: "res://Foreve/Assets/Relics/Ogier/OgierLostLightSword.png",
        IconOutlinePath: "res://Foreve/Assets/Relics/Ogier/OgierLostLightSword_outline.png",
        BigIconPath: "res://Foreve/Assets/Relics/Ogier/OgierLostLightSword_big.png"
    );

    public async Task AfterSecondaryResourceSpent(SecondaryResourceSpendContext ctx)
    {
        // 2026-08-15 新规格：遗物对所有卡牌适用 —— 任何来源消耗荣誉都触发（荣誉本身仍是
        // 奥吉尔专属系统，只有奥吉尔玩家持有/消耗荣誉）。不再限定消耗者必须是奥吉尔玩家。
        if (ctx.Definition.Id != Characters.Ogier.OgierCharacter.HonorResourceId) return;

        var combat = ctx.CombatState;
        var enemies = combat.Enemies.Where(e => !e.IsDead).ToList();
        if (enemies.Count == 0) return;

        // dealer 跟随实际消耗荣誉的玩家 creature：单玩家=Owner.Creature；
        // 双人=打出消耗荣誉卡牌的角色 creature（卡牌归属 patch 会把 ctx.Player 指向对应角色）。
        var dealer = ctx.Player.Creature
            ?? DualCharacterRelicScoping.ResolveOgierCreature(Owner.Creature);
        if (dealer == null) return;

        var random = new Random();
        var target = enemies[random.Next(enemies.Count)];

        var choiceCtx = new ThrowingPlayerChoiceContext();
        for (int i = 0; i < ctx.Amount; i++)
        {
            // dealer 跟随消耗荣誉的角色 creature，+2 等角色自身效果按实际来源结算。
            await CreatureCmd.Damage(choiceCtx, target, 3, ValueProp.Move, dealer, null);
        }
    }
}
