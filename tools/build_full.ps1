#requires -Version 5.1
<#
.SYNOPSIS
    Foreve 完整构建：Godot 导入素材 -> 导出 foreve.pck -> dotnet build 并复制到游戏 mods。

.EXAMPLE
    $env:STS2_DIR = "D:\SteamLibrary\steamapps\common\Slay the Spire 2"
    $env:GODOT4_BIN = "C:\Godot\Godot_v4.5.1-stable_win64_console.exe"
    powershell -File tools\build_full.ps1

.PARAMETER GodotExe
    Godot 4.5.1 .NET/console 可执行文件路径。默认读取环境变量 GODOT4_BIN。

.PARAMETER Sts2Dir
    Slay the Spire 2 游戏根目录。默认读取环境变量 STS2_DIR。
#>
param(
    [string]$GodotExe = $env:GODOT4_BIN,
    [string]$Sts2Dir = $env:STS2_DIR
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectDir = Join-Path $repoRoot "Foreve"

if (-not $GodotExe) {
    throw "未指定 Godot 可执行文件。请传 -GodotExe 或先设置环境变量 GODOT4_BIN，例如：`$env:GODOT4_BIN = 'C:\Godot\Godot_v4.5.1-stable_win64_console.exe'"
}
if (-not $Sts2Dir) {
    throw "未指定 Slay the Spire 2 目录。请传 -Sts2Dir 或先设置环境变量 STS2_DIR。"
}
if (-not (Test-Path $GodotExe)) {
    throw "找不到 Godot: $GodotExe"
}
if (-not (Test-Path $Sts2Dir)) {
    throw "找不到 Slay the Spire 2 目录: $Sts2Dir"
}

Write-Host "[1/3] Godot import: $projectDir"
Push-Location $projectDir
try {
    & $GodotExe --headless --path $projectDir --import
    if ($LASTEXITCODE -ne 0) { throw "Godot --import 失败，退出码 $LASTEXITCODE" }
} finally {
    Pop-Location
}

$pckPath = Join-Path $projectDir "foreve.pck"
Write-Host "[2/3] Godot export-pack -> $pckPath"
Push-Location $projectDir
try {
    & $GodotExe --headless --path $projectDir --export-pack "PCK" $pckPath
    if ($LASTEXITCODE -ne 0) { throw "Godot --export-pack 失败，退出码 $LASTEXITCODE" }
} finally {
    Pop-Location
}

Write-Host "[3/3] dotnet build (STS2_DIR=$Sts2Dir)"
$env:STS2_DIR = $Sts2Dir
$csproj = Join-Path $projectDir "Foreve.csproj"
dotnet build $csproj
if ($LASTEXITCODE -ne 0) { throw "dotnet build 失败，退出码 $LASTEXITCODE" }

Write-Host ""
Write-Host "构建完成。mod 已复制到: $(Join-Path $Sts2Dir 'mods\Foreve')"
