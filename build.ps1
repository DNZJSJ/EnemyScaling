# 构建并打包 EnemyScaling（敌人数值倍率）
#
# 用法：
#   .\build.ps1                          构建 + 打包 zip
#   .\build.ps1 -Deploy                  构建 + 打包 + 部署到游戏 mods 目录
#   .\build.ps1 -GameDir "D:\Steam\..."  显式指定游戏目录
#   .\build.ps1 -Clean                   先清掉 bin/obj 再构建
#
# 本脚本自包含，不依赖工作区公共脚本，方便放进独立仓库。

[CmdletBinding()]
param(
    [string]$GameDir = 'E:\STEAM\steamapps\common\Slay the Spire 2',
    [string]$RitsuLibDir,
    [string]$Configuration = 'Release',
    [switch]$Deploy,
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$modId = 'EnemyScaling'

function Step([string]$t) { Write-Host ""; Write-Host "=== $t ===" -ForegroundColor Cyan }
function Ok([string]$t)   { Write-Host "  OK  $t" -ForegroundColor Green }
function Warn2([string]$t){ Write-Host "  !!  $t" -ForegroundColor Yellow }

# 0. 前置检查
Step '前置检查'

if (-not (Test-Path -LiteralPath $GameDir)) {
    throw "找不到游戏目录：$GameDir（用 -GameDir 指定）"
}
$dataDir = Join-Path $GameDir 'data_sts2_windows_x86_64'
if (-not (Test-Path -LiteralPath (Join-Path $dataDir 'sts2.dll'))) {
    throw "游戏数据目录里没有 sts2.dll：$dataDir"
}
Ok "游戏目录 $GameDir"

if (-not $RitsuLibDir) {
    $up1 = Split-Path $GameDir -Parent
    $up2 = Split-Path $up1 -Parent
    $up3 = Split-Path $up2 -Parent
    $compatRoot = Join-Path $up3 'workshop\content\2868840\3747602295\compat'
    if (Test-Path -LiteralPath $compatRoot) {
        $dirs = Get-ChildItem -LiteralPath $compatRoot -Directory
        $RitsuLibDir = ($dirs | Sort-Object Name -Descending | Select-Object -First 1).FullName
    }
}
$ritsulibDll = ''
if ($RitsuLibDir) { $ritsulibDll = Join-Path $RitsuLibDir 'STS2-RitsuLib.Runtime.dll' }
if (-not $RitsuLibDir -or -not (Test-Path -LiteralPath $ritsulibDll)) {
    Warn2 "找不到 RitsuLib 运行时（可用 -RitsuLibDir 指定），编译可能失败"
} else {
    Ok "RitsuLib $RitsuLibDir"
}

$dotnetCmd = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnetCmd) { throw '找不到 dotnet（需要 .NET 9 SDK）' }
$dotnet = $dotnetCmd.Source
Ok "dotnet $dotnet"

# 1. 清理
if ($Clean) {
    Step '清理 bin/obj'
    foreach ($d in @('bin', 'obj')) {
        $p = Join-Path $root $d
        if (Test-Path -LiteralPath $p) {
            Remove-Item -LiteralPath $p -Recurse -Force
            Ok "已删 $d"
        }
    }
}

# 2. 构建
Step "构建 $Configuration"
Push-Location $root
try {
    $out = & $dotnet build "$modId.csproj" -c $Configuration --nologo -p:Sts2Dir="$GameDir" -p:RitsuLibDir="$RitsuLibDir" 2>&1
    $code = $LASTEXITCODE
} finally {
    Pop-Location
}

$errors = $out | Select-String -Pattern ': error'
if ($errors) {
    Write-Host '编译错误：' -ForegroundColor Red
    $errors | Select-Object -Unique | ForEach-Object { Write-Host ('  ' + $_.Line) -ForegroundColor Red }
    throw '构建失败'
}
Ok "构建成功（退出码 $code）"

$dll = Join-Path $root "bin\$Configuration\net9.0\$modId.dll"
if (-not (Test-Path -LiteralPath $dll)) { throw "找不到构建产物：$dll" }
Ok ("$modId.dll  " + (Get-Item $dll).Length + " B")

# 3. 打包 zip
Step '打包发行包'
$distDir = Join-Path $root 'dist'
if (-not (Test-Path -LiteralPath $distDir)) { New-Item -ItemType Directory -Path $distDir | Out-Null }

$manifest = Get-Content -LiteralPath (Join-Path $root "$modId.json") -Encoding UTF8 | ConvertFrom-Json
$version = $manifest.version
$zipPath = Join-Path $distDir "$modId-$version.zip"
if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }

# zip 内部结构：<modId>/... ，玩家解压后直接放进 mods 目录
$stage = Join-Path $env:TEMP ("$modId-pack-" + [guid]::NewGuid().ToString('N').Substring(0, 8))
$inner = Join-Path $stage $modId
New-Item -ItemType Directory -Path $inner -Force | Out-Null
Copy-Item $dll $inner -Force
Copy-Item (Join-Path $root "$modId.json") $inner -Force
Copy-Item (Join-Path $root 'README.md') $inner -Force
Compress-Archive -Path $inner -DestinationPath $zipPath -Force
Remove-Item -LiteralPath $stage -Recurse -Force

Ok ((Split-Path $zipPath -Leaf) + "  " + [math]::Round((Get-Item $zipPath).Length / 1KB, 1) + " KB")

# 4. 部署
if ($Deploy) {
    Step '部署到游戏'
    $dest = Join-Path $GameDir "mods\$modId"
    if (-not (Test-Path -LiteralPath $dest)) { New-Item -ItemType Directory -Path $dest -Force | Out-Null }

    $running = Get-Process -Name 'SlayTheSpire2' -ErrorAction SilentlyContinue
    if ($running) {
        Warn2 ("游戏正在运行（pid=" + $running.Id + "），DLL 会被占用，请先关闭游戏")
        throw '游戏在运行，无法部署'
    }

    Copy-Item $dll (Join-Path $dest "$modId.dll") -Force
    Copy-Item (Join-Path $root "$modId.json") $dest -Force
    Get-ChildItem -LiteralPath $dest -File | ForEach-Object {
        $h = (Get-FileHash -LiteralPath $_.FullName).Hash.Substring(0, 12)
        Ok ($_.Name + "  " + $_.Length + " B  " + $h)
    }
}

Write-Host ""
Write-Host "完成。发行包：$zipPath" -ForegroundColor Green
if (-not $Deploy) { Write-Host '（加 -Deploy 可直接部署到游戏 mods 目录）' -ForegroundColor DarkGray }
