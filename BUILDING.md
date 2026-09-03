# 构建指南

## 环境要求

| 组件 | 说明 |
|---|---|
| Steam 版《Slay the Spire 2》 | 构建时引用 `sts2.dll` / `0Harmony.dll`，需本地安装 |
| .NET SDK 9.0+ | 编译 C# |
| Godot 4.5.1（.NET / Mono 版） | 导入素材、导出 `foreve.pck` |
| STS2-RitsuLib | NuGet 自动恢复；游戏内需要订阅/安装 |

## 1. 配置游戏路径

优先使用环境变量 `STS2_DIR`：

```powershell
$env:STS2_DIR = "D:\SteamLibrary\steamapps\common\Slay the Spire 2"
```

也可以直接传给 MSBuild：

```powershell
dotnet build Foreve\Foreve.csproj -p:Sts2Dir="D:\SteamLibrary\steamapps\common\Slay the Spire 2"
```

如果没有设置，Windows 默认回退到：

```text
C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2
```

## 2. 只改代码 / 本地化（不需要重新打包 PCK）

```powershell
cd Foreve
dotnet build
```

说明：

- `dotnet build` 会把 `Foreve.dll`、`Foreve.pdb`、`foreve.json` 和仓库内素材复制到游戏 `mods\Foreve\`。
- `foreve.pck` 不在仓库中；如果本目录不存在 `foreve.pck`，构建会跳过 PCK 复制，不会报错。
- 本地化 JSON 已作为 C# 内嵌资源编译进 DLL，因此只改本地化文本时只需重新 build。

## 3. 修改美术 / 场景后完整构建（需要重新导出 PCK）

### 方式一：一键脚本（Windows PowerShell）

```powershell
$env:STS2_DIR = "D:\SteamLibrary\steamapps\common\Slay the Spire 2"
$env:GODOT4_BIN = "C:\path\to\Godot_v4.5.1-stable_win64_console.exe"

powershell -File tools\build_full.ps1
```

脚本会依次执行：

1. Godot 导入素材（`--import`）
2. Godot 导出 `Foreve\foreve.pck`（`--export-pack "PCK"`）
3. `dotnet build` 复制到游戏 mods 目录

### 方式二：手动命令

```powershell
cd Foreve

# 1. 导入素材（新增/修改 png/tscn 后需要）
& "C:\path\to\Godot_v4.5.1-stable_win64_console.exe" --headless --path . --import

# 2. 导出 foreve.pck
& "C:\path\to\Godot_v4.5.1-stable_win64_console.exe" --headless --path . --export-pack "PCK" .\foreve.pck

# 3. 编译并复制到游戏 mods 目录
$env:STS2_DIR = "D:\SteamLibrary\steamapps\common\Slay the Spire 2"
dotnet build
```

## 4. 常见问题

- **找不到 `sts2.dll` / `0Harmony.dll`**：确认 `STS2_DIR` 指向游戏根目录，且该目录下有 `data_sts2_windows_x86_64\sts2.dll`。
- **构建时提示找不到 `foreve.pck`**：仓库默认不提交 `foreve.pck`。如果只想改代码，可忽略；如果要完整运行，请先执行第 3 节导出 PCK。
- **NuGet 无法恢复 STS2.RitsuLib**：确认 `nuget.config` 使用 nuget.org 源。
- **游戏内没有加载新素材**：修改美术/场景后必须重新导出并部署 `foreve.pck`，不能只 `dotnet build`。
