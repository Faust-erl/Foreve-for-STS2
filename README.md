# Foreve（忘却前夜）

《Slay the Spire 2》双角色 Mod：选择两名角色并肩作战，掌控银钥之力。

> 本项目是非官方粉丝 Mod，与 Mega Crit 无关。
> Mod 依赖 [STS2-RitsuLib](https://github.com/BAKAOLC/STS2-RitsuLib)（MIT License）。

## 功能简介

- 双角色模式：本地创建两名玩家，牌库/金币/药水合并到主玩家
- 奥吉尔荣誉系统
- 银钥/钥令系统
- 多位原创/粉丝角色与配套卡牌、遗物、药水、动画
- 本地化：简体中文 / English

## 目录结构

```text
Foreve/
├── Foreve.csproj      # C# 项目；需要本地安装《Slay the Spire 2》和 Godot 4.5.1
├── Foreve.json         # Mod 清单
├── project.godot       # Godot 项目
├── export_presets.cfg  # PCK 导出预设
├── Scripts/            # C# 源码
├── scenes/             # Godot 场景源码
├── images/             # 打包用图片资源
└── Foreve/
    ├── Assets/         # Mod 美术资源（原始可编辑素材）
    └── localization/   # 本地化 JSON
```

> `foreve.pck` 是构建产物，不在仓库中。改美术/场景后需要按 `BUILDING.md` 重新导出 PCK。

## 快速开始

构建说明请见 [BUILDING.md](BUILDING.md)。参与协作请见 [CONTRIBUTING.md](CONTRIBUTING.md)。

## 依赖

- Steam 版《Slay the Spire 2》（需要本地 `sts2.dll` / `0Harmony.dll`）
- [.NET SDK 9.0](https://dotnet.microsoft.com/)
- [Godot 4.5.1](https://godotengine.org/)（Mono/.NET 版本，用于导出 PCK）
- [STS2-RitsuLib](https://github.com/BAKAOLC/STS2-RitsuLib)（NuGet 自动恢复，游戏内需订阅/安装）

## 许可证

- 代码：MIT，见 [LICENSE](LICENSE)
- 美术资源：请作者在发布前补充明确授权范围；如需保护素材，可在本文件单独声明“Assets All Rights Reserved”。
