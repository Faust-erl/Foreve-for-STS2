namespace Foreve.Scripts.Data;

public static class ForeveDataKeys
{
    // ===== ModDataStore 键 (Profile 级别, 跨局持久化) =====
    public const string UnlockedCards = "foreve_unlocked_cards";
    public const string UnlockedCharacters = "foreve_unlocked_characters";
    public const string StartingRelicLevel = "foreve_starting_relic_level";
    public const string TotalRunsCompleted = "foreve_total_runs";
    public const string BossKills = "foreve_boss_kills";
    public const string StoryFlags = "foreve_story_flags";

    // ===== RunSavedData 键 (单局内, 跨战斗持久化) =====
    public const string SilverKeyAmount = "foreve_silver_key";
    public const string SecondaryCharacterId = "foreve_secondary_character";
    public const string DiscoveredSpecialCards = "foreve_discovered_special_cards";
    public const string MaxHonorReached = "foreve_max_honor_reached";

    /// <summary>遗物「装备角色」归属存档（2026-08-18 遗物系统重做）。</summary>
    public const string RelicEquips = "foreve_relic_equips";
}
