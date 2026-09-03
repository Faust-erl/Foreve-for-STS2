using Foreve.Scripts.Enums;

namespace Foreve.Scripts.Interfaces;

public interface IForeveCharacter
{
    ForeveCharacterArchetype Archetype { get; }
    int UnlockPriority { get; }          // 0 = 初始角色, 1+ = 可解锁
    string UnlockConditionDescription { get; }
}
