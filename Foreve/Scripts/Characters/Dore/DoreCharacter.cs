using Godot;
using MegaCrit.Sts2.Core.Entities.Characters;
using MegaCrit.Sts2.Core.Nodes.Combat;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Characters;
using STS2RitsuLib.Scaffolding.Godot;
using Foreve.Scripts.Enums;
using Foreve.Scripts.Interfaces;

namespace Foreve.Scripts.Characters.Dore;

[RegisterCharacter]
public class DoreCharacter : ModCharacterTemplate<DoreCardPool, DoreRelicPool, DorePotionPool>,
    IForeveCharacter
{
    public ForeveCharacterArchetype Archetype => ForeveCharacterArchetype.Defense;
    public int UnlockPriority => 0;
    public string UnlockConditionDescription => "初始角色";

    public override Color NameColor => new(0.25f, 0.75f, 0.85f);
    public override Color EnergyLabelOutlineColor => new(0.1f, 0.5f, 0.6f);
    public override Color MapDrawingColor => new(0.25f, 0.75f, 0.85f);

    public override CharacterGender Gender => CharacterGender.Feminine;

    public override int StartingHp => 29;
    public override int StartingGold => 53;

    public override CharacterAssetProfile AssetProfile => CharacterAssetProfiles.Merge(
        CharacterAssetProfiles.Ironclad(),
        new(
            Scenes: new(
                VisualsPath: "res://Foreve/Assets/Characters/Dore/dore_character.tscn",
                EnergyCounterPath: "res://Foreve/Assets/Characters/Dore/dore_energy_counter.tscn",
                MerchantAnimPath: "res://Foreve/Assets/Characters/Dore/dore_merchant.tscn"
            ),
            Ui: new(
                IconTexturePath: "res://Foreve/Assets/Characters/Dore/images/dore_portrait.png",
                IconPath: "res://Foreve/Assets/Characters/Dore/dore_icon.tscn",
                CharacterSelectBgPath: "res://Foreve/Assets/Characters/Dore/dore_bg.tscn",
                CharacterSelectIconPath: "res://Foreve/Assets/Characters/Dore/images/char_select_dore.png",
                CharacterSelectLockedIconPath: "res://Foreve/Assets/Characters/Dore/images/char_select_dore_locked.png",
                MapMarkerPath: "res://Foreve/Assets/Characters/Dore/images/dore_portrait.png"
            )
        ));

    public override float AttackAnimDelay => 0f;
    public override float CastAnimDelay => 0f;

    public override bool RequiresEpochAndTimeline => false;

    public override List<string> GetArchitectAttackVfx() => new();

    protected override NCreatureVisuals? TryCreateCreatureVisuals()
    {
        var path = AssetProfile.Scenes?.VisualsPath;
        if (string.IsNullOrWhiteSpace(path) || !ResourceLoader.Exists(path))
            return null;
        return RitsuGodotNodeFactories.CreateFromScenePath<NCreatureVisuals>(path);
    }
}
