using HarmonyLib;
using MegaCrit.Sts2.Core.Localization;
using STS2RitsuLib.Utils;

namespace Foreve.Scripts;

/// <summary>
/// 直接拦截 LocTable.GetRawText()，将 Foreve 的 I18N 翻译注入原版 LocTable 查询。
/// 这是 RitsuLib I18N 桥接无法自动关联原版 LocTable 的临时解决方案。
/// </summary>
[HarmonyPatch(typeof(LocTable), nameof(LocTable.GetRawText))]
public static class LocTableI18NInjection
{
    public static I18N? ForeveI18N;

    static void Postfix(string key, ref string __result)
    {
        if (ForeveI18N == null || string.IsNullOrEmpty(key)) return;
        if (!key.StartsWith("FOREVE_")) return;

        if (ForeveI18N.TryGet(key, out var translated) && !string.IsNullOrEmpty(translated))
        {
            __result = translated;
        }
    }
}

/// <summary>
/// 补丁：LocString.Exists 只查原版 LocTable.HasEntry，看不到 RitsuLib I18N 里的
/// FOREVE_* 键 → EventModel.GetOptionTitle/GetOptionDescription 走 GetIfExists 会返回 null，
/// EventOption 构造时 CharacterModel.AddDetailsTo(null) 抛 NullReferenceException
/// （奥吉尔角色事件进入即报错，背景正常、文字为原版预设报错信息）。
/// 此 Prefix 让 FOREVE_* 且 I18N 中存在的键对 Exists 返回 true，
/// GetIfExists 即可正常构造 LocString，文本再由 LocTableI18NInjection 注入翻译。
/// </summary>
[HarmonyPatch(typeof(LocString), nameof(LocString.Exists), new[] { typeof(string), typeof(string) })]
public static class LocStringExistsI18NInjection
{
    private static bool Prefix(string table, string key, ref bool __result)
    {
        var i18n = LocTableI18NInjection.ForeveI18N;
        if (i18n == null || string.IsNullOrEmpty(key) || !key.StartsWith("FOREVE_")) return true;
        if (!i18n.TryGet(key, out var translated) || string.IsNullOrEmpty(translated)) return true;

        __result = true;
        return false;
    }
}
