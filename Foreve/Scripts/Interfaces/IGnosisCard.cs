namespace Foreve.Scripts.Interfaces;

/// <summary>
/// 灵知觉醒卡牌标记接口 — 需要消耗银钥/持续型效果
/// </summary>
public interface IGnosisCard
{
    int SilverKeyCost { get; }
}
