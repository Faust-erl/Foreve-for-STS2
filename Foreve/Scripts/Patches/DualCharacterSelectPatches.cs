using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using Foreve.Scripts.Characters.Dore;

using Foreve.Scripts.Characters.Ogier;
using Foreve.Scripts.Characters.Rotan;
using Foreve.Scripts.DualCharacter;

namespace Foreve.Scripts.Patches;

/// <summary>
/// 双角色模式：选人界面两步选人（批次 1a）。
///
/// 玩法：4 角色池（萝坦/奥吉尔/铁甲战士/静默猎手）先自选 1，再「发现 3」从剩余 3 名中选第 2；
/// 随机按钮只随机第一名角色，第二名仍走发现 3；两名未选完前 Embark 点击无效（Prefix 拦截 + GD.Print 提示）。
///
/// 实现要点：
///   - SelectCharacter 前缀：第一步放行原逻辑（_lobby.SetLocalCharacter 写本地槽）；
///     第二步只写 DualCharacterState.SecondCharacter，跳过原方法体（绝不调 SetLocalCharacter，
///     否则会覆盖第一步的主角色）。
///   - OnLocalCharacterChangedForRandom 后缀：随机开局时捕获解析出的实际角色作为 MainCharacter。
///   - OnSubmenuOpened 后缀：重置选择状态 + 应用 4 池过滤 + 纠正原版"自动选中第一个按钮"
///     （容器第一个子节点可能是被过滤隐藏的池外角色）。
///   - InitCharacterButtons 后缀：4 池过滤（_Ready 阶段 lobby 未建时跳过，OnSubmenuOpened 补做）。
///
/// 已知简化（1a）：第二名角色选中后信息面板仍显示第一名角色的资料（原版面板只反映 lobby 本地槽位）。
///
/// 交互反馈（1a.1，用户实测「能选择多个角色且无响应」修复）：
///   - 发现阶段常驻提示 Label（CanvasLayer 挂到选人界面下，纯代码 UI，无遮罩弹窗）：
///     显示「发现：选择第二名角色」+ 提示 + 「已选择：X」（X = CharacterModel.Title 本地化名），
///     文本来自扁平 zhs.json/eng.json（LocTableI18NInjection 注入，键 FOREVE_DUAL_SELECT_SECOND_*）；
///   - 进入发现阶段：已选角色按钮 Disable()+置灰，随机按钮禁用（发现阶段只选第二名），剩余 3 名保持可点；
///   - 选完第二名：全部按钮禁用，两名已选角色保留原版选中高亮（按钮自身 Select()→RefreshState 提供），
///     其余置灰，提示切换为 FOREVE_DUAL_SELECT_DONE「双角色就绪，可以开始征程」；
///   - Embark 未完成时提示切换为 FOREVE_DUAL_EMBARK_BLOCKED；
///   - 幂等：EnterDiscoverPhase 以 _discoverEnteredFor 去重（自动选角 + 用户点击可能重复触发），
///     提示 Label 创建一次（IsInstanceValid 校验），返回界面后 OnSubmenuOpened 恢复全部按钮状态。
///
/// ⚠️ 接线（主流程在 Entry.cs 统一处理，不要直接改 Entry.cs）：
///   Foreve.Scripts.Patches.DualCharacterSelectPatches.Install(Logger);
/// </summary>
public static class DualCharacterSelectPatches
{
    private static readonly FieldInfo LobbyField = AccessTools.Field(typeof(NCharacterSelectScreen), "_lobby");
    private static readonly FieldInfo CharButtonContainerField = AccessTools.Field(typeof(NCharacterSelectScreen), "_charButtonContainer");

    /// <summary>发现阶段常驻提示层/文本（挂到选人界面下的 CanvasLayer，界面销毁时随树释放）。</summary>
    private static CanvasLayer? _phaseLayer;
    private static Label? _phaseLabel;

    /// <summary>幂等标记：已为当前 MainCharacter 进入过发现阶段（自动选角 + 用户点击可能重复触发）。</summary>
    private static CharacterModel? _discoverEnteredFor;

    /// <summary>取消防抖：取消分支后 500ms 内对同一角色的 SelectCharacter 调用直接忽略（返回 false），
    /// 防止同一次点击的 OnPress/FocusEntered 双路径在取消后又把角色重新选中（实测轮 8 复测：取消后
    /// 立即又出现「第一步完成」）。</summary>
    private static CharacterModel? _cancelGuardFor;
    private static ulong _cancelGuardMs;

    /// <summary>最近一次成功选中（第一步）的角色与时间（2026-08-15 IL 实证）：
    /// 鼠标点击未聚焦按钮时，Godot 先抓焦点 → FocusEntered → Select() 完成选中，
    /// 同一按下的 HandleMousePress → OnPress 紧随其后（同一帧，<50ms）。
    /// OnPressPostfix 用它识别「同一笔点击的尾巴」，防止把刚完成的选中误判为取消。</summary>
    private static CharacterModel? _lastSelectChar;
    private static ulong _lastSelectMs;

    /// <summary>最近一次成功选中（第二步）的角色与时间：用途同上，防止第二名选中后
    /// 被同一笔点击的 OnPress 尾巴立即取消。</summary>
    private static CharacterModel? _lastSecondSelectChar;
    private static ulong _lastSecondSelectMs;

    /// <summary>已锁定按钮的置灰色调（Modulate；NClickableControl.Disable 本身无视觉变化，需手动置灰）。</summary>
    private static readonly Color LockedTint = new(0.6f, 0.6f, 0.6f, 1f);

    /// <summary>双角色可选池：萝坦 / 奥吉尔 / 朵尔。拉蒙娜实装后加入此数组即可。</summary>
    private static readonly Type[] DualPoolTypes =
    {
        typeof(RotanCharacter),
        typeof(OgierCharacter),
        typeof(DoreCharacter),
    };

    public static void Install(MegaCrit.Sts2.Core.Logging.Logger logger)
    {
        var harmony = new Harmony("foreve.dual_character_select");

        var select = AccessTools.Method(typeof(NCharacterSelectScreen), "SelectCharacter");
        harmony.Patch(select,
            prefix: new HarmonyMethod(GetMethod(nameof(SelectCharacterPrefix))),
            postfix: new HarmonyMethod(GetMethod(nameof(SelectCharacterPostfix))));

        var randomResolved = AccessTools.Method(typeof(NCharacterSelectScreen), "OnLocalCharacterChangedForRandom");
        harmony.Patch(randomResolved, postfix: new HarmonyMethod(GetMethod(nameof(OnRandomResolvedPostfix))));

        var embark = AccessTools.Method(typeof(NCharacterSelectScreen), "OnEmbarkPressed");
        harmony.Patch(embark, prefix: new HarmonyMethod(GetMethod(nameof(OnEmbarkPressedPrefix))));

        var initButtons = AccessTools.Method(typeof(NCharacterSelectScreen), "InitCharacterButtons");
        harmony.Patch(initButtons, postfix: new HarmonyMethod(GetMethod(nameof(InitCharacterButtonsPostfix))));

        var opened = AccessTools.Method(typeof(NCharacterSelectScreen), "OnSubmenuOpened");
        harmony.Patch(opened, postfix: new HarmonyMethod(GetMethod(nameof(OnSubmenuOpenedPostfix))));

        // 选人按钮 Select 守卫修复（2026-08-14 实测修复轮 7）：
        // 原版 Select() 有 if (!_isSelected) 守卫——已选角色按钮 _isSelected=true，再点 Select()
        // 直接短路 return，_delegate.SelectCharacter 不触发，取消逻辑永远收不到调用。
        // SelectButtonPrefix（Priority.First）识别「发现阶段点击已选角色」→ 反射把 _isSelected
        // 置 false，放行原方法 → if(!_isSelected) 成立 → SelectCharacter → SelectCharacterPrefix
        // 取消分支执行。
        var selectButton = AccessTools.Method(typeof(NCharacterSelectButton), "Select");
        harmony.Patch(selectButton,
            prefix: new HarmonyMethod(GetMethod(nameof(SelectButtonPrefix))));

        // 选人按钮点击钩子（2026-08-14 实测修复轮 8 复测）：
        // 原版 Select() 由 FocusEntered 信号触发（_Ready: Connect(FocusEntered, Select)，
        // OnPress 是空方法）——已选按钮已持有焦点，再次点击焦点不变 → FocusEntered
        // 不触发 → Select() 不调用 → 取消逻辑收不到点击（日志实证：第二次进入后
        // 连续点击已选角色零日志）。OnPress 每次鼠标点击必调（与焦点无关）→
        // OnPressPostfix 补这条路径：识别「发现阶段点击已选角色」→ 解除守卫 +
        // 调 Select() → SelectCharacterPrefix 取消分支。
        var onPress = AccessTools.Method(typeof(NCharacterSelectButton), "OnPress");
        if (onPress != null)
        {
            harmony.Patch(onPress,
                postfix: new HarmonyMethod(GetMethod(nameof(OnPressPostfix))));
        }

        logger.Info("[Foreve][Dual] 双角色选人 patch 已安装 (两步选人/4池过滤/Embark 门禁)");
        logger.Info("[Foreve][Dual] 选人按钮 Select 守卫修复已安装 (再点已选角色=取消，FocusEntered+OnPress 双路径)");
    }

    // ── 第一步/第二步选角 ──────────────────────────────────────────────

    private static bool SelectCharacterPrefix(NCharacterSelectScreen __instance, NCharacterSelectButton charSelectButton, CharacterModel characterModel)
    {
        try
        {
            if (!IsSingleplayerScreen(__instance)) return true;

            // 注意（2026-08-15）：原 500ms 取消防抖已从前缀移除——它会误挡「取消后重选同一角色」
            // （用户实测：取消后的角色点不回去）。同一笔点击尾巴的防护由 OnPressPostfix 的
            // 80ms 选中守卫 + 150ms 取消守卫负责，Postfix 层保留 150ms 防重选（Harmony 语义：
            // Prefix 返回 false 后 Postfix 仍执行，必须挡住取消调用自身把 MainCharacter 设回）。

            // ── 取消/推进分支（2026-08-15 重构，用户拍板） ──
            // 双选完成后：
            //   点副角色 → 只取消副角色，回发现阶段重选第二名；
            //   点主角色 → 原副角色升为主角色（原主角色取消），回发现阶段重选第二名。
            // 发现阶段点主角色 → 全部取消重选（原有行为）。

            // 点副角色（双选完成后）→ 取消副角色
            if (ReferenceEquals(characterModel, DualCharacterState.SecondCharacter)
                && DualCharacterState.SecondCharacter != null)
            {
                charSelectButton.Deselect();
                DualCharacterState.SecondCharacter = null;
                FillInfoPanel(__instance, DualCharacterState.MainCharacter!); // 面板回显主角色
                SwapCharacterBg(__instance, DualCharacterState.MainCharacter!); // 背景大图回显主角色
                GD.Print($"[Foreve][Dual] 已取消第二名角色: {characterModel.Id.Entry}，回到发现阶段重新选择");
                _cancelGuardFor = characterModel;
                _cancelGuardMs = Time.GetTicksMsec();
                _discoverEnteredFor = null;
                ReenterDiscoverUi(__instance);
                return false;
            }

            // 点主角色
            if (ReferenceEquals(characterModel, DualCharacterState.MainCharacter)
                && DualCharacterState.MainCharacter != null)
            {
                if (DualCharacterState.SecondCharacter != null)
                {
                    // 双选完成后点主角色：原副角色升为主角色，原主角色取消，重新选第二名
                    var promoted = DualCharacterState.SecondCharacter;
                    charSelectButton.Deselect();      // 原主角色去光圈（RefreshState 撤销高亮）
                    DualCharacterState.MainCharacter = promoted;
                    DualCharacterState.SecondCharacter = null;
                    _lastSelectChar = promoted;        // 防同一笔点击的 OnPress 尾巴误判
                    _lastSelectMs = Time.GetTicksMsec();
                    _cancelGuardFor = characterModel;  // 防同一笔点击的 OnPress 尾巴二次触发
                    _cancelGuardMs = Time.GetTicksMsec();
                    // 2026-08-18 修复「切换主角色后开局仍是旧主角色」：开局主玩家取自 lobby
                    //（RunState.CreateForNewRun players[0] = lobby.LocalPlayer），晋升分支跳过了
                    // 原方法体（原方法体 IL_032e 会调 _lobby.SetLocalCharacter），必须在此同步
                    // lobby 本地角色，否则战斗主角色仍是原主角色（日志实证：主角色切换后
                    // Embarking on a singleplayer IRONCLAD run / 注入第二玩家 主=IRONCLAD）。
                    // SetLocalCharacter 与原版第一步路径同款调用（非 null 角色，安全）。
                    try
                    {
                        var lobby = LobbyField?.GetValue(__instance) as StartRunLobby;
                        lobby?.SetLocalCharacter(promoted);
                        // 同步 _selectedButton 到新主角色按钮（原方法体 IL_0129 行为，跳过原方法体需补做）
                        var promotedButton = GetPoolButtons(__instance)
                            .FirstOrDefault(b => ReferenceEquals(b.Character, promoted));
                        if (promotedButton != null)
                            AccessTools.Field(typeof(NCharacterSelectScreen), "_selectedButton")
                                ?.SetValue(__instance, promotedButton);
                        GD.Print($"[Foreve][Dual] 主角色切换 lobby 同步: {promoted.Id.Entry}");
                    }
                    catch (Exception e)
                    {
                        GD.Print($"[Foreve][Dual] 主角色切换 lobby 同步异常: {e}");
                    }
                    FillInfoPanel(__instance, promoted!); // 面板显示新主角色
                    SwapCharacterBg(__instance, promoted!); // 背景大图切换为新主角色
                    GD.Print($"[Foreve][Dual] 主角色切换：原副角色 {promoted!.Id.Entry} 升为主角色，原主角色 {characterModel.Id.Entry} 取消，重新选择第二名");
                    _discoverEnteredFor = null;
                    ReenterDiscoverUi(__instance);
                }
                else
                {
                    // 发现阶段点主角色 = 取消选择（回到第一步重新选），2026-08-14 用户拍板
                    charSelectButton.Deselect(); // 清除按钮选中高亮（_isSelected=false + RefreshState，不回调 SelectCharacter）
                    // ⚠️ 不再清 lobby 角色（实测轮 8 复测 17:07：ChangeCharacter(NetId, null) 把
                    // LocalPlayer.character 置 null → Embark 时 BeginRunForAllPlayers →
                    // UpdatePreferredAscension 访问 LocalPlayer.character.Id → NRE，选人页报错）。
                    // lobby 角色保留原值：用户重新选第一名时 SetLocalCharacter 自然覆盖，
                    // 「已选择 X」视觉残留由下方信息面板手动复位解决（零副作用）。
                    // 信息面板复位（实测轮 8 复测：仍显示「已选择 X」根因）——
                    // 取消分支 return false 跳过了 SelectCharacter 原方法体（信息面板刷新在
                    // 原方法体 830-842 行），面板残留已取消角色的名字/血量/遗物；
                    // 原版 PlayerChanged 对本地玩家短路（RefreshButtonSelectionForPlayer
                    // 直接 return），lobby 通知不会刷新面板 → 手动复位为「无选择」占位符。
                    try
                    {
                        SetInfoPanelText(__instance, "_name", "");
                        SetInfoPanelText(__instance, "_description", "");
                        SetInfoPanelText(__instance, "_hp", "??/??");
                        SetInfoPanelText(__instance, "_gold", "???");
                        SetInfoPanelText(__instance, "_relicTitle", "");
                        SetInfoPanelText(__instance, "_relicDescription", "");
                        var relicIcon = AccessTools.Field(typeof(NCharacterSelectScreen), "_relicIcon")?.GetValue(__instance) as TextureRect;
                        if (relicIcon != null) relicIcon.SelfModulate = new Color(1f, 1f, 1f, 0f);
                        var relicOutline = AccessTools.Field(typeof(NCharacterSelectScreen), "_relicIconOutline")?.GetValue(__instance) as TextureRect;
                        if (relicOutline != null) relicOutline.SelfModulate = new Color(1f, 1f, 1f, 0f);
                        AccessTools.Field(typeof(NCharacterSelectScreen), "_selectedButton")?.SetValue(__instance, null);
                        GD.Print("[Foreve][Dual] 信息面板已复位（无选择占位符）");
                    }
                    catch (Exception e) { GD.Print($"[Foreve][Dual] 信息面板复位异常: {e}"); }
                    DualCharacterState.MainCharacter = null;
                    DualCharacterState.Enabled = false;
                    _discoverEnteredFor = null; // 轮8：清幂等标记——取消后重选同一角色必须重新进入发现阶段
                    // 取消防抖：同一角色 500ms 内的立即重选（同一次点击的双路径第二发）忽略
                    _cancelGuardFor = characterModel;
                    _cancelGuardMs = Time.GetTicksMsec();
                    GD.Print("[Foreve][Dual] 已取消选择第一名角色，请重新选择");
                    ResetSelectionUi(__instance);
                }
                return false;
            }

            // 第一步：放行原逻辑（SetLocalCharacter + 信息面板刷新）
            if (DualCharacterState.MainCharacter == null) return true;

            // 第二步「发现 3」：只存状态，绝不调 _lobby.SetLocalCharacter（会覆盖第一步的主角色）
            if (characterModel is RandomCharacter)
            {
                GD.Print("[Foreve][Dual] 发现阶段：随机按钮只作用于第一名角色，请直接选择第二名角色");
                return false;
            }
            if (charSelectButton.IsLocked) return true; // 未解锁角色：交给原逻辑显示锁定信息（不会改 lobby）

            DualCharacterState.SecondCharacter = characterModel;
            DualCharacterState.Enabled = true;
            _lastSecondSelectChar = characterModel; // 防同一笔点击的 OnPress 尾巴把刚选的第二名取消
            _lastSecondSelectMs = Time.GetTicksMsec();
            FillInfoPanel(__instance, characterModel); // 面板显示第二名角色信息（2026-08-15 用户拍板）
            SwapCharacterBg(__instance, characterModel); // 背景大图切换为第二名角色
            GD.Print($"[Foreve][Dual] 第二名角色已选择: {characterModel.Id.Entry} —— 双角色模式就绪，可以开始征程");
            EnterSelectionComplete(__instance);
            return false; // 跳过原方法体（避免 SetLocalCharacter 覆盖主角色）
        }
        catch (Exception e)
        {
            GD.Print($"[Foreve][Dual] SelectCharacter 前缀异常: {e}");
            return true;
        }
    }

    private static void SelectCharacterPostfix(NCharacterSelectScreen __instance, NCharacterSelectButton charSelectButton, CharacterModel characterModel)
    {
        try
        {
            if (!IsSingleplayerScreen(__instance)) return;
            // 取消调用自身的 Postfix 防重选（Harmony 语义：Prefix 返回 false 后 Postfix 仍执行，
            // 取消分支刚把 MainCharacter 置空，本 Postfix 若继续执行会立刻把它设回 →
            // 取消失效。150ms 窗口足够覆盖同一次调用的 Postfix；150ms 后取消角色可正常重选）。
            if (ReferenceEquals(characterModel, _cancelGuardFor)
                && Time.GetTicksMsec() - _cancelGuardMs < 150)
            {
                GD.Print("[Foreve][Dual] 取消防抖(Postfix)：忽略取消调用自身的重选");
                return;
            }
            if (DualCharacterState.MainCharacter != null) return;
            if (characterModel is RandomCharacter) return; // 随机角色：等 OnLocalCharacterChangedForRandom 捕获实际角色
            if (charSelectButton.IsLocked) return;         // 锁定角色并未真正选中
            if (!IsInDualPool(characterModel)) return;     // 池外角色（被过滤隐藏的按钮被原版自动选中时）不进入双角色流程

            DualCharacterState.MainCharacter = characterModel;
            _lastSelectChar = characterModel;
            _lastSelectMs = Time.GetTicksMsec();
            EnterDiscoverPhase(__instance);
        }
        catch (Exception e)
        {
            GD.Print($"[Foreve][Dual] SelectCharacter 后缀异常: {e}");
        }
    }

    // ── 选人按钮 Select 守卫解除（再点已选角色=取消） ─────────────────────
    //
    // 根因（2026-08-14 反编译实证，IL 699706）：原版 Select() 为
    //   if (!_isSelected) { _isSelected = true; _delegate.SelectCharacter(this, _character); RefreshState(); }
    // 已选角色按钮 _isSelected=true，再点击 Select() 直接短路 return（IL_0008: ret），
    // _delegate.SelectCharacter 根本不触发 → SelectCharacterPrefix 的取消分支永远收不到调用。
    // 修复：本 Prefix（Priority.First）识别「发现阶段点击已选角色」→ 反射把 _isSelected 置 false，
    // 放行原方法 → if(!_isSelected) 成立 → SelectCharacter → 取消分支执行（清状态+重置 UI）。
    // 上一轮只做 b.Enable() 是错的：按钮本来就 Enable，卡点是 Select() 的 _isSelected 守卫。

    /// <summary>
    /// 信息面板字段复位（取消选择后）：反射取选人屏私有 Label 节点，
    /// SetTextAutoSize(string) 优先（MegaLabel/MegaRichTextLabel 均有，反编译 830 行实证），
    /// 失败回退 Godot 通用 Set("text", ...)。
    /// </summary>
    private static void SetInfoPanelText(NCharacterSelectScreen screen, string fieldName, string text)
    {
        try
        {
            var field = AccessTools.Field(typeof(NCharacterSelectScreen), fieldName);
            var node = field?.GetValue(screen) as Control;
            if (node == null) return;
            var setter = AccessTools.Method(node.GetType(), "SetTextAutoSize");
            if (setter != null)
            {
                setter.Invoke(node, new object[] { text });
                return;
            }
            node.Set("text", text);
        }
        catch
        {
            // UI 复位失败静默（不影响取消主流程）
        }
    }

    /// <summary>
    /// 信息面板填充（2026-08-15 用户拍板：选第二名后面板显示第二名信息；取消副角色后回显主角色）。
    /// 复刻原版 SelectCharacter 原方法体的面板刷新段（IL 实证 830-842 行）：
    /// _name/_description/_hp/_gold/_relicTitle/_relicDescription + 遗物图标。
    /// 字段经反射取值（规避跨版本 API 可见性差异），取值失败静默。
    /// </summary>
    private static void FillInfoPanel(NCharacterSelectScreen screen, CharacterModel character)
    {
        try
        {
            SetInfoPanelText(screen, "_name", GetCharacterLocText(character, "CharacterSelectTitle") ?? character.Title?.GetFormattedText() ?? character.Id.Entry);
            var descLoc = AccessTools.Property(typeof(CharacterModel), "Description")?.GetValue(character) as LocString;
            SetInfoPanelText(screen, "_description", GetCharacterLocText(character, "CharacterSelectDesc") ?? descLoc?.GetFormattedText() ?? "");
            var hp = GetCharacterIntProp(character, "StartingHp");
            var gold = GetCharacterIntProp(character, "StartingGold");
            SetInfoPanelText(screen, "_hp", hp.HasValue ? $"{hp}/{hp}" : "??/??");
            SetInfoPanelText(screen, "_gold", gold.HasValue ? $"{gold}" : "???");

            var relics = AccessTools.Property(typeof(CharacterModel), "StartingRelics")?.GetValue(character) as IReadOnlyList<RelicModel>;
            if (relics == null || relics.Count == 0) return;
            var relic = relics[0];
            SetInfoPanelText(screen, "_relicTitle", relic.Title?.GetFormattedText() ?? "");
            var dynDesc = AccessTools.Property(typeof(RelicModel), "DynamicDescription")?.GetValue(relic) as LocString;
            SetInfoPanelText(screen, "_relicDescription", dynDesc?.GetFormattedText() ?? "");
            var relicIconTexture = AccessTools.Property(typeof(RelicModel), "Icon")?.GetValue(relic) as Texture2D;
            var relicOutlineTexture = AccessTools.Property(typeof(RelicModel), "IconOutline")?.GetValue(relic) as Texture2D;

            var relicIcon = AccessTools.Field(typeof(NCharacterSelectScreen), "_relicIcon")?.GetValue(screen) as TextureRect;
            if (relicIcon != null)
            {
                relicIcon.Texture = relicIconTexture;
                relicIcon.SelfModulate = Colors.White; // 原版 IL：选中非锁定角色时 icon 置 White
            }
            var relicOutline = AccessTools.Field(typeof(NCharacterSelectScreen), "_relicIconOutline")?.GetValue(screen) as TextureRect;
            if (relicOutline != null)
            {
                relicOutline.Texture = relicOutlineTexture;
                relicOutline.SelfModulate = new Color(0f, 0f, 0f, 0.5f); // StsColors.halfTransparentBlack 的字面等价
            }
        }
        catch (Exception e)
        {
            GD.Print($"[Foreve][Dual] 信息面板填充异常: {e}");
        }
    }

    /// <summary>
    /// 背景大图切换（2026-08-15 用户实测需求：选第二名后面板换了但背景大图没换）。
    /// 复刻原版 SelectCharacter 原方法体的背景段（IL 实证：清空 _bgContainer 子节点 →
    /// PreloadManager.Cache.GetScene(CharacterSelectBg) → Instantiate&lt;Control&gt; →
    /// 命名 {Entry}_bg → AddChild）。原方法体在第二选择路径被跳过，故手动补做。
    /// 对齐工作仍由 CharSelectBgPositionFix（SelectCharacter Postfix）负责。
    /// </summary>
    private static void SwapCharacterBg(NCharacterSelectScreen screen, CharacterModel character)
    {
        try
        {
            var bgContainer = AccessTools.Field(typeof(NCharacterSelectScreen), "_bgContainer")?.GetValue(screen) as Control;
            if (bgContainer == null) return;

            foreach (var child in bgContainer.GetChildren())
            {
                bgContainer.RemoveChild(child);
                child.QueueFree();
            }

            var bgName = AccessTools.Property(typeof(CharacterModel), "CharacterSelectBg")?.GetValue(character) as string;
            if (string.IsNullOrEmpty(bgName)) return;

            // PreloadManager.Cache.GetScene(string) → PackedScene（类型可能非 public，走反射取）
            var preloadType = AccessTools.TypeByName("MegaCrit.Sts2.Core.Assets.PreloadManager");
            var cache = AccessTools.Property(preloadType, "Cache")?.GetValue(null);
            var scene = AccessTools.Method(cache?.GetType(), "GetScene")?.Invoke(cache, new object[] { bgName }) as PackedScene;
            if (scene == null)
            {
                GD.Print($"[Foreve][Dual] 背景场景获取失败: {bgName}");
                return;
            }

            var bg = scene.Instantiate<Control>(PackedScene.GenEditState.Disabled);
            bg.Name = character.Id.Entry + "_bg";
            bgContainer.AddChild(bg);
            GD.Print($"[Foreve][Dual] 背景大图已切换: {character.Id.Entry}");
        }
        catch (Exception e)
        {
            GD.Print($"[Foreve][Dual] 背景大图切换异常: {e}");
        }
    }

    /// <summary>取 CharacterModel 的本地化 key 属性（如 CharacterSelectTitle）并格式化「characters」表文本。</summary>
    private static string? GetCharacterLocText(CharacterModel character, string propName)
    {
        try
        {
            var value = AccessTools.Property(typeof(CharacterModel), propName)?.GetValue(character) as string;
            if (string.IsNullOrWhiteSpace(value)) return null;
            return new LocString("characters", value).GetFormattedText();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>取 CharacterModel 的 int 属性（如 StartingHp/StartingGold），失败返回 null。</summary>
    private static int? GetCharacterIntProp(CharacterModel character, string propName)
    {
        try
        {
            var value = AccessTools.Property(typeof(CharacterModel), propName)?.GetValue(character);
            if (value is int i) return i;
        }
        catch
        {
            // 静默
        }
        return null;
    }

    /// <summary>
    /// 选人按钮 Select 守卫解除（2026-08-14 实测修复轮 7，2026-08-15 扩展）：
    /// 识别「点击已选的主/副角色」时反射解除 _isSelected 守卫，放行原方法
    /// → if(!_isSelected) 成立 → _delegate.SelectCharacter → SelectCharacterPrefix 对应取消分支；
    /// 其余情况一律放行原逻辑。全程 try-catch，异常放行。
    /// </summary>
    [HarmonyPriority(Priority.First)]
    private static bool SelectButtonPrefix(NCharacterSelectButton __instance)
    {
        try
        {
            // 无任何已选角色 → 放行原逻辑（第一步选择）
            if (DualCharacterState.MainCharacter == null && DualCharacterState.SecondCharacter == null) return true;
            // 调试日志：任何选择状态下进入 Select() 即打印（避免刷屏仅打印有状态的）
            GD.Print($"[Foreve][Dual] SelectButton: char={__instance.Character.Id.Entry} isSelected={__instance.IsSelected} main={DualCharacterState.MainCharacter?.Id.Entry} second={DualCharacterState.SecondCharacter?.Id.Entry}");
            // 点击的是已选角色按钮（主或副）且处于选中态 → 解除守卫 → 对应取消分支
            var isSelectedChar = ReferenceEquals(__instance.Character, DualCharacterState.MainCharacter)
                              || ReferenceEquals(__instance.Character, DualCharacterState.SecondCharacter);
            if (!isSelectedChar || !__instance.IsSelected) return true;

            // 用户点击已选角色想取消：反射解除 _isSelected 守卫，放行原方法
            // → if(!_isSelected) 成立 → _delegate.SelectCharacter → SelectCharacterPrefix 取消分支
            var field = AccessTools.Field(typeof(NCharacterSelectButton), "_isSelected");
            field?.SetValue(__instance, false);
        }
        catch (Exception e)
        {
            GD.Print($"[Foreve][Dual] SelectButton 前缀异常: {e}");
        }
        return true;
    }

    /// <summary>
    /// 按钮点击钩子（2026-08-14 实测修复轮 8 复测，2026-08-15 重构）：
    /// 原版 Select() 由 FocusEntered 信号触发（_Ready: Connect(FocusEntered, Select)，
    /// IL 实证；OnPress 是空方法）——已选按钮已持有焦点，再次点击焦点不变 →
    /// FocusEntered 不触发 → Select() 不调用。OnPress 每次鼠标点击必调（与焦点无关）
    /// → 本 Postfix 补这条路径：识别「点击已选角色」→ 解除 _isSelected 守卫并调 Select()
    /// → SelectCharacterPrefix 取消分支执行。
    /// 2026-08-15 IL 实证的三道防自取消守卫：
    ///   1) 同一笔点击的尾巴（FocusEntered 路径刚选中该角色，<80ms）→ 跳过；
    ///   2) 同一笔点击的尾巴（刚选中第二名，<80ms）→ 跳过；
    ///   3) 刚被取消的角色（150ms 防抖，防止焦点路径取消后 OnPress 尾巴二次触发）。
    /// 另补：无任何选择且按钮未选中时（取消后焦点仍在该按钮上）→ 直接调 Select() 重新选中。
    /// </summary>
    private static void OnPressPostfix(NCharacterSelectButton __instance)
    {
        try
        {
            // 防自取消 1：同一笔点击的 FocusEntered 路径刚选中该角色（同帧，<80ms）
            if (ReferenceEquals(__instance.Character, _lastSelectChar)
                && Time.GetTicksMsec() - _lastSelectMs < 80)
            {
                GD.Print($"[Foreve][Dual] OnPress 忽略：同一笔点击的 FocusEntered 路径刚选中 {__instance.Character.Id.Entry}（防自取消）");
                return;
            }
            // 防自取消 2：同一笔点击刚选中第二名（<80ms）
            if (ReferenceEquals(__instance.Character, _lastSecondSelectChar)
                && Time.GetTicksMsec() - _lastSecondSelectMs < 80)
            {
                GD.Print($"[Foreve][Dual] OnPress 忽略：同一笔点击刚选中第二名 {__instance.Character.Id.Entry}（防自取消）");
                return;
            }
            // 防自取消 3：该角色刚被取消（焦点路径已执行取消，OnPress 尾巴不再二次触发/重选）。
            // 150ms 窗口：同一次点击的尾巴 <50ms 必被覆盖；之后取消角色可立即重选（2026-08-15 用户实测修复）。
            if (ReferenceEquals(__instance.Character, _cancelGuardFor)
                && Time.GetTicksMsec() - _cancelGuardMs < 150)
            {
                return;
            }

            // 无任何选择 + 按钮未选中：取消后焦点仍停在原按钮上，再点同一角色 → 重新选中（第一步）
            if (DualCharacterState.MainCharacter == null && DualCharacterState.SecondCharacter == null)
            {
                if (!__instance.IsSelected)
                {
                    GD.Print($"[Foreve][Dual] OnPress 重选路径：无选择状态下点击 {__instance.Character.Id.Entry}，调 Select() 重新选中");
                    _cancelGuardFor = null; // 清除取消防抖，允许立即重选
                    __instance.Select();
                }
                return;
            }

            // 点击的是已选角色（主或副）且处于选中态 → 解除守卫 → Select() → 对应取消分支
            var isSelectedChar = ReferenceEquals(__instance.Character, DualCharacterState.MainCharacter)
                              || ReferenceEquals(__instance.Character, DualCharacterState.SecondCharacter);
            if (!isSelectedChar || !__instance.IsSelected) return;

            GD.Print($"[Foreve][Dual] SelectButton(OnPress): char={__instance.Character.Id.Entry} isSelected=True main={DualCharacterState.MainCharacter?.Id.Entry} second={DualCharacterState.SecondCharacter?.Id.Entry}");
            // 解除 _isSelected 守卫 → Select() 进入 if(!_isSelected) → SelectCharacter → 取消分支
            var field = AccessTools.Field(typeof(NCharacterSelectButton), "_isSelected");
            field?.SetValue(__instance, false);
            __instance.Select();
        }
        catch (Exception e)
        {
            GD.Print($"[Foreve][Dual] OnPress 后缀异常: {e}");
        }
    }

    // ── 随机选角解析完成（第一步随机时捕获实际角色） ─────────────────────

    private static void OnRandomResolvedPostfix(NCharacterSelectScreen __instance, CharacterModel characterModel)
    {
        try
        {
            if (!IsSingleplayerScreen(__instance)) return;
            if (DualCharacterState.MainCharacter != null) return;

            // 随机按钮底层从全角色池抽取：角色栏已移除铁甲战士/静默猎手，
            // 若随机到池外角色，则从当前可见的朵尔/萝坦/奥吉尔中重选一次。
            if (!IsInDualPool(characterModel))
            {
                var poolButtons = GetPoolButtons(__instance)
                    .Where(b => !b.IsRandom && b.Visible && IsInDualPool(b.Character))
                    .ToList();
                if (poolButtons.Count == 0) return;
                var pick = poolButtons[new Random().Next(poolButtons.Count)];
                GD.Print($"[Foreve][Dual] 随机选角命中池外角色 {characterModel.Id.Entry}，重选为 {pick.Character.Id.Entry}");
                pick.Select();
                return;
            }


            DualCharacterState.MainCharacter = characterModel;
            _lastSelectChar = characterModel;
            _lastSelectMs = Time.GetTicksMsec();
            GD.Print($"[Foreve][Dual] 随机选角解析: {characterModel.Id.Entry} —— 进入发现阶段");
            EnterDiscoverPhase(__instance);
        }
        catch (Exception e)
        {
            GD.Print($"[Foreve][Dual] 随机选角后缀异常: {e}");
        }
    }

    // ── Embark 门禁 ──────────────────────────────────────────────────

    private static bool OnEmbarkPressedPrefix(NCharacterSelectScreen __instance)
    {
        try
        {
            if (!IsSingleplayerScreen(__instance)) return true;
            if (!DualCharacterState.IsSelectionComplete)
            {
                GD.Print("[Foreve][Dual] 双角色模式：请先完成「发现」——从剩余 3 名角色中选择第二名角色");
                ShowEmbarkBlocked(__instance);
                return false;
            }
            return true;
        }
        catch (Exception e)
        {
            GD.Print($"[Foreve][Dual] OnEmbarkPressed 前缀异常: {e}");
            return true;
        }
    }

    // ── 4 角色池过滤 ──────────────────────────────────────────────────

    private static void InitCharacterButtonsPostfix(NCharacterSelectScreen __instance)
    {
        try
        {
            if (!IsSingleplayerScreen(__instance)) return;
            ApplyPoolFilter(__instance);
        }
        catch (Exception e)
        {
            GD.Print($"[Foreve][Dual] InitCharacterButtons 后缀异常: {e}");
        }
    }

    private static void OnSubmenuOpenedPostfix(NCharacterSelectScreen __instance)
    {
        try
        {
            // 上一轮双选是否已完成（完成后按返回键再进入：lobby 仍持有旧主角色，应重新开始选人）
            var wasComplete = DualCharacterState.SecondCharacter != null;
            DualCharacterState.ResetForNewSelection();
            if (!IsSingleplayerScreen(__instance)) return;

            ApplyPoolFilter(__instance);

            if (wasComplete)
            {
                // 重新开始双角色选人：恢复全部按钮可点/去置灰、隐藏阶段提示；不做任何自动选择，
                // 用户首次点击会走第一步（覆盖 lobby 旧角色）
                ResetSelectionUi(__instance);
                GD.Print("[Foreve][Dual] 重新开始双角色选人（上一轮已完成双选后返回）");
                return;
            }

            // 原版 OnSubmenuOpened 会自动 Select 容器第一个按钮（可能是被过滤隐藏的池外角色）：
            // 若本地槽位角色为空或不在 4 池，纠正为第一个可见池内角色；否则直接进入发现阶段。
            // （原方法体自动选角命中池内角色时，SelectCharacterPostfix 已设 MainCharacter 并应用过
            //  发现阶段 UI；这里按 lobby 槽位重建 MainCharacter，EnterDiscoverPhase 幂等跳过重复应用。）
            var localCharacter = GetLocalCharacter(__instance);
            if (localCharacter == null || !IsInDualPool(localCharacter))
            {
                // 新存档/新解锁原版角色时，原版会自动选中刚解锁的角色（如静默猎手）。
                // 该角色不在双角色池内，必须纠正为第一个可用的池内角色。
                // 注意不能直接取 FirstOrDefault(Visible)：随机按钮可能在容器最前面，
                // 且锁定角色即使 Visible 也不能真正写入 lobby，会导致纠正失败。
                var firstSelectable = GetPoolButtons(__instance)
                    .FirstOrDefault(b => !b.IsRandom && b.Visible && IsInDualPool(b.Character) && !b.IsLocked);

                if (firstSelectable != null)
                {
                    firstSelectable.Select(); // 触发 SelectCharacter → 前缀放行 → 后缀设置 MainCharacter
                    GD.Print($"[Foreve][Dual] 自动选角纠正：重新选择第一个池内角色 {firstSelectable.Character.Id.Entry}");
                }
                else
                {
                    GD.Print("[Foreve][Dual] 自动选角纠正失败：没有可用的池内角色按钮");
                }
            }
            else
            {
                DualCharacterState.MainCharacter = localCharacter;
                EnterDiscoverPhase(__instance);
            }
        }
        catch (Exception e)
        {
            GD.Print($"[Foreve][Dual] OnSubmenuOpened 后缀异常: {e}");
        }
    }

    private static void ApplyPoolFilter(NCharacterSelectScreen __instance)
    {
        var container = CharButtonContainerField?.GetValue(__instance) as Control;
        if (container == null) return;
        foreach (var b in container.GetChildren().OfType<NCharacterSelectButton>())
        {
            if (b.IsRandom) continue;                 // 随机按钮保留（只随机第一名角色）
            if (IsInDualPool(b.Character)) continue;  // 4 池角色保留
            b.Visible = false;                        // 隐藏但保留在树中（焦点邻居链不悬空）
            b.Disable();                              // NClickableControl.Disable —— 不可点、不可聚焦
        }
    }

    // ── 工具 ──────────────────────────────────────────────────────────

    /// <summary>
    /// 进入「发现 3」阶段：幂等（_discoverEnteredFor 去重 + 提示 UI 只建一次）。
    /// 已选角色按钮置灰禁用、随机按钮禁用（发现阶段只选第二名）、剩余 3 名保持可点；
    /// 界面顶部常驻提示显示「发现：选择第二名角色」+ 已选角色名。
    /// </summary>
    private static void EnterDiscoverPhase(NCharacterSelectScreen screen)
    {
        var main = DualCharacterState.MainCharacter;
        if (main == null) return;
        // 幂等：自动选角 + 用户点击可能重复触发；仅当提示 UI 仍挂在本界面实例上才跳过
        //（界面重建后旧标记失效，需重新应用 UI）
        if (ReferenceEquals(_discoverEnteredFor, main) && GodotObject.IsInstanceValid(_phaseLabel)) return;

        _discoverEnteredFor = main;
        GD.Print($"[Foreve][Dual] 第一步完成（{main.Id.Entry}），进入「发现 3」：从剩余 3 名角色中选择第二名角色");

        EnsurePhaseUi(screen);
        if (!GodotObject.IsInstanceValid(_phaseLabel)) return;

        _phaseLabel.Text = $"{GetLoc("FOREVE_DUAL_SELECT_SECOND_TITLE")}\n" +
                           $"{GetLoc("FOREVE_DUAL_SELECT_SECOND_PROMPT")}\n" +
                           $"{GetLoc("FOREVE_DUAL_SELECT_FIRST_PICKED")}{GetCharacterDisplayName(main)}";
        _phaseLabel.Visible = true;

        foreach (var b in GetPoolButtons(screen))
        {
            if (b.IsRandom) SetButtonLocked(b, true);                              // 随机按钮只作用于第一名，发现阶段禁用
            else if (ReferenceEquals(b.Character, main))
            {
                // 已选角色：原版 SetLocalCharacter 后按钮可能被 Disable（点击不派发→取消逻辑不触发，
                // 2026-08-14 实测「点击已选角色不能取消」根因）——先 Enable 再置灰，保持可点（再点=取消选择）
                b.Enable();
                b.Modulate = LockedTint;
            }
            // 其余 3 名池内角色保持可点
        }

        // 主按钮诊断探针（2026-08-15：首进自动选角后点击主角色零日志之谜）+ 悬停自愈：
        // NClickableControl.HandleMousePress 要求「hover 聚焦」（_isHovered）才派发 OnPress——
        // 屏幕打开时若鼠标已停在按钮上，MouseEntered 从未触发 → 点击被吞。此处打印按钮状态，
        // 若鼠标正位于按钮上则强制补齐 hover 状态（_isHovered=true + RefreshFocus），恢复可点。
        var mainButton = GetPoolButtons(screen).FirstOrDefault(x => ReferenceEquals(x.Character, main));
        if (mainButton != null)
        {
            var enabledField = AccessTools.Field(typeof(NClickableControl), "_isEnabled");
            var hoveredField = AccessTools.Field(typeof(NClickableControl), "_isHovered");
            GD.Print($"[Foreve][Dual] 探针: 主按钮={main.Id.Entry} _isEnabled={enabledField?.GetValue(mainButton)} FocusMode={mainButton.FocusMode} HasFocus={mainButton.HasFocus()} _isHovered={hoveredField?.GetValue(mainButton)} IsSelected={mainButton.IsSelected}");
            if (mainButton.GetGlobalRect().HasPoint(mainButton.GetViewport().GetMousePosition()))
            {
                hoveredField?.SetValue(mainButton, true);
                AccessTools.Method(typeof(NClickableControl), "RefreshFocus")?.Invoke(mainButton, null);
                GD.Print($"[Foreve][Dual] 悬停自愈：鼠标位于主按钮 {main.Id.Entry} 上，强制 hover 聚焦");
            }
        }
    }

    /// <summary>回到发现阶段 UI：候选按钮解锁、当前主角色置灰、随机按钮锁定、提示文本恢复。</summary>
    private static void ReenterDiscoverUi(NCharacterSelectScreen screen)
    {
        var main = DualCharacterState.MainCharacter;
        foreach (var b in GetPoolButtons(screen))
        {
            if (b.IsRandom) { SetButtonLocked(b, true); continue; }
            if (ReferenceEquals(b.Character, main)) { b.Enable(); b.Modulate = LockedTint; }
            else SetButtonLocked(b, false);
        }
        EnterDiscoverPhase(screen);
    }

    /// <summary>双选完成：全部按钮禁用（两名已选保留原版高亮、其余置灰），提示切换为就绪文本。</summary>
    private static void EnterSelectionComplete(NCharacterSelectScreen screen)
    {
        _discoverEnteredFor = null; // 阶段已推进，幂等标记作废
        GD.Print("[Foreve][Dual] 双角色模式就绪：选择已锁定，可以开始征程");

        EnsurePhaseUi(screen);
        if (GodotObject.IsInstanceValid(_phaseLabel))
        {
            _phaseLabel.Text = GetLoc("FOREVE_DUAL_SELECT_DONE");
            _phaseLabel.Visible = true;
        }

        foreach (var b in GetPoolButtons(screen))
        {
            if (b.IsRandom) { SetButtonLocked(b, true); continue; }
            if (ReferenceEquals(b.Character, DualCharacterState.MainCharacter)
                || ReferenceEquals(b.Character, DualCharacterState.SecondCharacter))
            {
                // 已选按钮：保持原版选中高亮（光圈）——不 Disable（Disable 会停掉
                // _process 光圈动画，实测轮 8 复测「选第二名后第一名光圈消失」根因）；
                // 点击拦截由 SelectCharacterPrefix 的 SecondCharacter != null 分支负责
                continue;
            }
            SetButtonLocked(b, true);
        }
    }

    /// <summary>Embark 未完成时把提示切换为「请先完成选择」（随后选择第二名会覆盖该文本）。</summary>
    private static void ShowEmbarkBlocked(NCharacterSelectScreen screen)
    {
        EnsurePhaseUi(screen);
        if (GodotObject.IsInstanceValid(_phaseLabel))
        {
            _phaseLabel.Text = GetLoc("FOREVE_DUAL_EMBARK_BLOCKED");
            _phaseLabel.Visible = true;
        }
    }

    /// <summary>重新开始选人：恢复全部池内/随机按钮可点 + 去置灰，隐藏阶段提示。</summary>
    private static void ResetSelectionUi(NCharacterSelectScreen screen)
    {
        foreach (var b in GetPoolButtons(screen))
        {
            if (b.IsRandom || IsInDualPool(b.Character)) SetButtonLocked(b, false);
        }
        if (GodotObject.IsInstanceValid(_phaseLabel)) _phaseLabel.Visible = false;
    }

    /// <summary>创建阶段提示 UI：CanvasLayer + PanelContainer + Label，挂到选人界面下（随界面释放），只建一次。</summary>
    private static void EnsurePhaseUi(NCharacterSelectScreen screen)
    {
        if (GodotObject.IsInstanceValid(_phaseLayer) && GodotObject.IsInstanceValid(_phaseLabel)) return;

        _phaseLayer = new CanvasLayer { Layer = 150 };
        screen.AddChild(_phaseLayer);

        var panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        panel.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        panel.GrowHorizontal = Control.GrowDirection.Both;
        panel.GrowVertical = Control.GrowDirection.Both;
        panel.Position = new Vector2(0f, 28f);
        _phaseLayer.AddChild(panel);

        _phaseLabel = new Label
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _phaseLabel.AddThemeFontSizeOverride("font_size", 26);
        panel.AddChild(_phaseLabel);
    }

    /// <summary>锁定/解锁角色按钮：Disable/Enable（输入 + 焦点）并叠加置灰/还原色调（Disable 本身无视觉变化）。</summary>
    private static void SetButtonLocked(NCharacterSelectButton b, bool locked)
    {
        if (locked)
        {
            b.Disable();
            b.Modulate = LockedTint;
        }
        else
        {
            b.Enable();
            b.Modulate = Colors.White;
        }
    }

    /// <summary>从扁平 zhs.json/eng.json 取本地化文本（LocTableI18NInjection 注入，key 以 FOREVE_ 开头）。
    /// 表名用 RegisterI18NLocTableBridge 注册的 stem 表（foreve_I18N_cards 等，OgierRegroup 同款）；
    /// 裸 "foreve_I18N" 表不存在会抛 LocException（2026-08-14 实测）。</summary>
    private static string GetLoc(string key)
    {
        try
        {
            return new LocString("foreve_I18N_cards", key).GetFormattedText();
        }
        catch (Exception e)
        {
            GD.Print($"[Foreve][Dual] 本地化文本获取异常 {key}: {e}");
            return key;
        }
    }

    /// <summary>角色显示名（CharacterModel.Title 本地化文本，取不到时回退 Id.Entry）。</summary>
    private static string GetCharacterDisplayName(CharacterModel? c)
    {
        try
        {
            var text = c?.Title?.GetFormattedText();
            if (!string.IsNullOrWhiteSpace(text)) return text;
        }
        catch (Exception e)
        {
            GD.Print($"[Foreve][Dual] 角色名获取异常: {e}");
        }
        return c?.Id.Entry ?? "?";
    }

    private static bool IsSingleplayerScreen(NCharacterSelectScreen screen)
    {
        try
        {
            var lobby = LobbyField?.GetValue(screen) as StartRunLobby;
            return lobby?.NetService != null && lobby.NetService.Type == NetGameType.Singleplayer;
        }
        catch
        {
            return false;
        }
    }

    private static CharacterModel? GetLocalCharacter(NCharacterSelectScreen screen)
    {
        var lobby = LobbyField?.GetValue(screen) as StartRunLobby;
        return lobby?.LocalPlayer.character;
    }

    private static IEnumerable<NCharacterSelectButton> GetPoolButtons(NCharacterSelectScreen __instance)
    {
        var container = CharButtonContainerField?.GetValue(__instance) as Control;
        return container == null
            ? Enumerable.Empty<NCharacterSelectButton>()
            : container.GetChildren().OfType<NCharacterSelectButton>();
    }

    private static bool IsInDualPool(CharacterModel? c)
    {
        if (c == null) return false;
        var t = c.GetType();
        foreach (var pt in DualPoolTypes)
            if (pt.IsAssignableFrom(t)) return true;
        return false;
    }

    private static MethodInfo GetMethod(string name)
        => typeof(DualCharacterSelectPatches).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!;
}
