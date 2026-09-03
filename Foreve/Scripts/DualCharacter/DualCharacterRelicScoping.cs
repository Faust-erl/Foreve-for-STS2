using System;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using Foreve.Scripts.Characters.Ogier;
using Foreve.Scripts.Characters.Rotan;

namespace Foreve.Scripts.DualCharacter;

/// <summary>
/// 双角色模式：角色/卡牌归属判定辅助（2026-08-15 新规格，linker 分支）。
///
/// 规格：主、副角色的专属遗物（含初始遗物）对两名角色以及所有卡牌都适用；
/// 但角色专属系统（例如奥吉尔的荣誉标记）仍只属于奥吉尔本人。
///   例：萝坦+奥吉尔出战 → 任何角色的打击都触发「黑衣剑士的剑」抽 1；
///       萝坦或奥吉尔完全格挡都触发「骑士纹章」，但荣誉 +1 只记在奥吉尔身上。
///
/// 职责划分：
///   - mod 遗物触发与「对己增益」全队化：见 <see cref="DualCharacterEffects"/>。
///   - 本类只保留角色专属系统的归属判定（荣誉玩家/creature、奥吉尔/萝坦卡牌识别）。
///   - 卡牌 _owner 在资源合并时统一改为主玩家（DualCharacterRunMerge），卡牌角色归属
///     改用 Id.Entry 前缀判定（FOREVE_CARD_OGIER_* / FOREVE_CARD_ROTAN_*，稳定识别符）。
///   - 单玩家模式零变化：查找失败时调用方回退 Owner/自身 creature。
/// </summary>
public static class DualCharacterRelicScoping
{
    /// <summary>奥吉尔卡牌 Id.Entry 前缀（卡牌模型注册名 FOREVE_CARD_OGIER_*）。</summary>
    public const string OgierCardPrefix = "FOREVE_CARD_OGIER_";

    /// <summary>萝坦卡牌 Id.Entry 前缀（卡牌模型注册名 FOREVE_CARD_ROTAN_*）。</summary>
    public const string RotanCardPrefix = "FOREVE_CARD_ROTAN_";

    /// <summary>当前持有奥吉尔角色的玩家（主/副皆可），未找到返回 null。
    /// 仅双人局有效；单玩家局返回 null（调用方回退 Owner），避免旧局残留引用污染。</summary>
    public static Player? GetOgierPlayer()
    {
        if (!DualCharacterState.Enabled) return null;
        if (DualCharacterState.MainPlayer is { Character: OgierCharacter }) return DualCharacterState.MainPlayer;
        if (DualCharacterState.SecondaryPlayer is { Character: OgierCharacter }) return DualCharacterState.SecondaryPlayer;
        return null;
    }

    /// <summary>当前持有萝坦角色的玩家（主/副皆可），未找到返回 null。
    /// 仅双人局有效；单玩家局返回 null（调用方回退 Owner）。</summary>
    public static Player? GetRotanPlayer()
    {
        if (!DualCharacterState.Enabled) return null;
        if (DualCharacterState.MainPlayer is { Character: RotanCharacter }) return DualCharacterState.MainPlayer;
        if (DualCharacterState.SecondaryPlayer is { Character: RotanCharacter }) return DualCharacterState.SecondaryPlayer;
        return null;
    }

    /// <summary>奥吉尔角色的 creature（死亡也返回，调用方自行判定），未找到返回 null。</summary>
    public static Creature? GetOgierCreature() => GetOgierPlayer()?.Creature;

    /// <summary>萝坦角色的 creature（死亡也返回，调用方自行判定），未找到返回 null。</summary>
    public static Creature? GetRotanCreature() => GetRotanPlayer()?.Creature;

    /// <summary>双人局返回奥吉尔所属玩家；单玩家/未找到返回 fallback（通常为模型的 Owner）。</summary>
    public static Player ResolveOgierPlayer(Player fallback) => GetOgierPlayer() ?? fallback;

    /// <summary>双人局返回奥吉尔 creature；单玩家/未找到返回 fallback（通常为模型的 Owner.Creature）。</summary>
    public static Creature? ResolveOgierCreature(Creature? fallback) => GetOgierCreature() ?? fallback;

    /// <summary>该卡是否为奥吉尔的牌（按模型注册名前缀判定）。</summary>
    public static bool IsOgierCard(CardModel card)
        => card?.Id?.Entry?.StartsWith(OgierCardPrefix, StringComparison.Ordinal) == true;

    /// <summary>该卡是否为萝坦的牌（按模型注册名前缀判定）。</summary>
    public static bool IsRotanCard(CardModel card)
        => card?.Id?.Entry?.StartsWith(RotanCardPrefix, StringComparison.Ordinal) == true;
}
