using Godot;
using MegaCrit.Sts2.Core.Entities.Characters;
using MegaCrit.Sts2.Core.Nodes.Combat;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Characters;
using STS2RitsuLib.Scaffolding.Godot;
using Foreve.Scripts.Enums;
using Foreve.Scripts.Interfaces;

namespace Foreve.Scripts.Characters.Rotan;

[RegisterCharacter]
public class RotanCharacter : ModCharacterTemplate<RotanCardPool, RotanRelicPool, RotanPotionPool>,
    IForeveCharacter
{
    public ForeveCharacterArchetype Archetype => ForeveCharacterArchetype.Attack;
    public int UnlockPriority => 0;
    public string UnlockConditionDescription => "初始角色";

    public override Color NameColor => new(0.9f, 0.3f, 0.2f);
    public override Color EnergyLabelOutlineColor => new(0.7f, 0.1f, 0.0f);
    public override Color MapDrawingColor => new(0.9f, 0.3f, 0.2f);

    public override CharacterGender Gender => CharacterGender.Feminine;

    public override int StartingHp => 39;
    public override int StartingGold => 49;

    public override CharacterAssetProfile AssetProfile => CharacterAssetProfiles.Merge(
        CharacterAssetProfiles.Ironclad(),
        new(
            Scenes: new(
                VisualsPath: "res://Foreve/Assets/Characters/Rotan/rotan_character.tscn",
                EnergyCounterPath: "res://Foreve/Assets/Characters/Rotan/rotan_energy_counter.tscn",
                MerchantAnimPath: "res://Foreve/Assets/Characters/Rotan/rotan_merchant.tscn",
                RestSiteAnimPath: "res://Foreve/Assets/Characters/Rotan/rotan_rest_site.tscn"
            ),
            Ui: new(
                IconTexturePath: "res://Foreve/Assets/Characters/Rotan/images/rotan_portrait.png",
                IconPath: "res://Foreve/Assets/Characters/Rotan/rotan_icon.tscn",
                CharacterSelectBgPath: "res://Foreve/Assets/Characters/Rotan/rotan_bg.tscn",
                CharacterSelectIconPath: "res://Foreve/Assets/Characters/Rotan/images/char_select_rotan.png",
                CharacterSelectLockedIconPath: "res://Foreve/Assets/Characters/Rotan/images/char_select_rotan_locked.png",
                MapMarkerPath: "res://Foreve/Assets/Characters/Rotan/images/rotan_portrait.png"
            )
        ));

    public override float AttackAnimDelay => 0f;
    public override float CastAnimDelay => 0f;

    public override bool RequiresEpochAndTimeline => false;

    public override List<string> GetArchitectAttackVfx() => new();

    protected override NCreatureVisuals? TryCreateCreatureVisuals()
    {
        // 场景资源缺失时返回 null，让游戏 fallback 基础角色立绘（避免战斗黑屏）
        var path = AssetProfile.Scenes?.VisualsPath;
        if (string.IsNullOrWhiteSpace(path) || !ResourceLoader.Exists(path))
            return null;
        return RitsuGodotNodeFactories.CreateFromScenePath<NCreatureVisuals>(path);
    }
}
