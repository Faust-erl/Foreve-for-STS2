using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using Foreve.Scripts.Enums;
using Foreve.Scripts.Interfaces;

namespace Foreve.Scripts.DualCharacter;

/// <summary>
/// 双角色模式静态状态类（批次 1a 核心状态）。
///
/// 玩法：同一台电脑单机开局创建两个玩家（Players.Count == 2，类似联机但纯本地），
/// 资源合并到第一个玩家（主玩家），第二个玩家（副玩家）只保留场上角色实体。
///
/// 状态流转：
///   1. 选人界面两步选人（DualCharacterSelectPatches）：
///      第一步选 MainCharacter（走原版 _lobby.SetLocalCharacter），
///      第二步「发现 3」从剩余 3 名角色中选 SecondCharacter（只写本状态，不改 lobby）。
///   2. RunState.CreateForNewRun Prefix（DualCharacterRunStartPatches）：
///      在开局创建 RunState 前把 SecondCharacter 注入为第二个 Player（NetId = 主 NetId + 1）。
///   3. RunStartedEvent（DualCharacterRunMerge）：
///      副玩家牌库/金币/药水并入主玩家、副玩家 MaxEnergy=0，随后消费并清空 MainCharacter/SecondCharacter。
///
/// 后续批次（1b：副玩家自动 ready、嘲讽权重应用、双死判负等）会引用本类的 API。
/// </summary>
public static class DualCharacterState
{
    /// <summary>当前局是否双角色模式（选人完成时置 true；RunStartedEvent 合并时读取/维持）。</summary>
    public static bool Enabled { get; set; }

    /// <summary>第一名角色（主玩家），选人界面第一步写入；开局注入后由合并逻辑清空。</summary>
    public static CharacterModel? MainCharacter { get; set; }

    /// <summary>第二名角色（副玩家），选人界面「发现 3」写入；开局注入后由合并逻辑清空。</summary>
    public static CharacterModel? SecondCharacter { get; set; }

    /// <summary>主玩家（RunStartedEvent 合并时写入，供 IsMainPlayer/IsSecondaryPlayer 引用比较）。</summary>
    public static Player? MainPlayer { get; set; }

    /// <summary>副玩家（RunStartedEvent 合并时写入）。</summary>
    public static Player? SecondaryPlayer { get; set; }

    /// <summary>两名角色是否都已选好（Embark 门禁用）。</summary>
    public static bool IsSelectionComplete => MainCharacter != null && SecondCharacter != null;

    /// <summary>是否为双角色局（Players.Count &gt; 1 且 Enabled）。旧档单玩家局恒为 false。</summary>
    public static bool IsDualMode(RunState? runState)
        => Enabled && runState != null && runState.Players.Count > 1;

    /// <summary>战斗态版本：ICombatState.RunState 可能为 NullRunState 等实现，可空处理。</summary>
    public static bool IsDualMode(ICombatState? combatState)
        => Enabled && combatState != null && combatState.RunState != null && combatState.RunState.Players.Count > 1;

    public static bool IsMainPlayer(Player? player)
        => player != null && ReferenceEquals(player, MainPlayer);

    public static bool IsSecondaryPlayer(Player? player)
        => player != null && ReferenceEquals(player, SecondaryPlayer);

    /// <summary>
    /// 嘲讽权重：mod 角色（IForeveCharacter）按 Archetype 折算 —— Attack=1.0 / Defense=2.0（奥吉尔）/ Support=0.8；
    /// 非 mod 角色（原版铁甲/静默等）= 1.0 占位符。
    /// </summary>
    public static double GetTauntWeight(CharacterModel? character)
    {
        if (character is IForeveCharacter foreve)
        {
            return foreve.Archetype switch
            {
                ForeveCharacterArchetype.Defense => 2.0,
                ForeveCharacterArchetype.Support => 0.8,
                _ => 1.0, // Attack
            };
        }
        return 1.0;
    }

    /// <summary>进入新选人界面时重置选择状态（DualCharacterSelectPatches 的 OnSubmenuOpened Postfix 调用）。</summary>
    public static void ResetForNewSelection()
    {
        Enabled = false;
        MainCharacter = null;
        SecondCharacter = null;
        // MainPlayer/SecondaryPlayer 故意不清：旧引用不会与新局玩家实例相等（ReferenceEquals），残留无害
    }
}
