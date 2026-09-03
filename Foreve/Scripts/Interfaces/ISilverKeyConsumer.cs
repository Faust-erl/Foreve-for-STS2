namespace Foreve.Scripts.Interfaces;

/// <summary>
/// 消耗银钥的能力接口（灵知觉醒卡牌、特定遗物等）
/// </summary>
public interface ISilverKeyConsumer
{
    int SilverKeyCost { get; }
    bool CanAfford(int currentSilverKeys) => currentSilverKeys >= SilverKeyCost;
}
