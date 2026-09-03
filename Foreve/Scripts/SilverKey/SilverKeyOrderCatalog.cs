using System.Collections.Generic;

namespace Foreve.Scripts.SilverKey;

/// <summary>
/// 钥令条目静态定义（K01-K15）。后续扩充钥令时只需往 <see cref="All"/> 追加条目，
/// 并把对应 PNG 放到素材目录即可。
/// </summary>
public sealed record SilverKeyOrderDefinition(
    string Code,
    string Name,
    string Description,
    string IconFileName);

public static class SilverKeyOrderCatalog
{
    /// <summary>
    /// 钥令素材源目录。公开版不再依赖本机绝对路径；
    /// 运行时优先加载 PCK 内 res://Foreve/Assets/UI/KeyOrders，其次 mod 部署目录 Assets/KeyOrders。
    /// </summary>
    public const string SourceDirectory = "";

    /// <summary>1：2 钥令背景板文件名（1024×2048）。</summary>
    public const string BackgroundFileName = "钥令背景板-1：2.png";

    /// <summary>K01-K15 全量钥令定义（每场战斗内抽取不重复）。</summary>
    public static IReadOnlyList<SilverKeyOrderDefinition> All { get; } = new List<SilverKeyOrderDefinition>
    {
        new("K01", "纯白初遇", "弃掉所有手牌，抽取弃掉数量+1的牌。", "90px-纯白初遇钥令.png"),
        new("K02", "脑中之音", "给予所有敌人1层虚弱和易伤。", "90px-脑中之音钥令.png"),
        new("K03", "注射守护", "给予一名己方角色10点格挡，当前生命不大于上限1/4的己方角色获得2层再生。", "90px-注射守护钥令.png"),
        new("K04", "鼠鼠的智慧", "获得2点能量。", "90px-鼠鼠的智慧钥令.png"),
        new("K05", "小小心愿", "给予一名己方角色6层活力。", "90px-小小心愿钥令.png"),
        new("K06", "永世执念", "给予一名己方角色5点力量。在你的这个回合结束时，其失去4点力量。", "90px-永世执念钥令.png"),
        new("K07", "不平等交换", "抽4张牌。", "90px-不平等交换钥令.png"),
        new("K08", "不朽的葬仪", "所有己方角色获得4层活力和2层灾厄。", "90px-不朽的葬仪钥令.png"),
        new("K09", "最后的誓言", "驱散一名角色的虚弱状态，若为精英战或boss战则额外给予其9点格挡并给予最后一排的敌人2层易伤。", "90px-最后的誓言钥令.png"),
        new("K10", "奥瑞塔的宝藏", "从抽牌堆中抽3张能量消耗最低的卡牌。", "90px-奥瑞塔的宝藏钥令.png"),
        new("K11", "咆哮的血与沙", "随机给所有己方角色分配共11层活力，所有己方角色失去1点生命。", "90px-咆哮的血与沙钥令.png"),
        new("K12", "短暂的永恒", "获得1点能量。选择1名己方角色，将其1张「打击」与1张「防御」的带有 虚无、消耗 的复制加入手牌。", "90px-短暂的永恒钥令.png"),
        new("K13", "腐烂盛筵", "获得4层荆棘。给予所有攻击意图的敌人1层虚弱。", "90px-腐烂盛筵钥令.png"),
        new("K14", "蚀骨的拥抱", "获得1点能量，你的下回合开始时给予一名己方角色9点格挡。", "90px-蚀骨的拥抱钥令.png"),
        new("K15", "湖畔回眸", "给予生命最高的敌人4层中毒，所有己方角色获得6点格挡。", "90px-湖畔回眸钥令.png"),
    };
}
