using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using STS2RitsuLib.CardTags;
using STS2RitsuLib.Content;
using STS2RitsuLib.Interop.AutoRegistration;

namespace Foreve.Scripts.Content.Cards.Rotan;

/// <summary>萝坦「打击牌」判定：严格牌名为打击（rotanstrike 标签），或描述中记述"视为打击"（mod 卡带 Strike 标签）。</summary>
[RegisterOwnedCardTag(nameof(RotanStrikeTag))]
public static class RotanTags
{
    public static readonly CardTag RotanStrikeTag = ModContentRegistry.GetQualifiedCardTagId(Entry.ModId, nameof(RotanStrikeTag)).GetModCardTag();

    /// <summary>是否算「打击牌」（用户定义：严格牌名为"打击"，或描述中记述了"视为打击"）。
    /// 判定=rotanstrike 标签（打击 + 视为打击的牌都挂此标签）；原版卡无此标签时仅严格名为"打击"才算
    /// （带 Strike 标签的究极打击/荣誉之击/穿透打击等名字非严格"打击"、描述无"视为打击"→ 不算）。</summary>
    public static bool IsStrikeCard(CardModel card)
    {
        if (card.HasModCardTag(RotanStrikeTag)) return true; // mod 打击牌（打击 + 视为打击）
        // 原版卡（无 rotanstrike 标签）：仅严格名为"打击"才算
        return card.Title.TrimEnd('+').Trim() == "打击";
    }
}
