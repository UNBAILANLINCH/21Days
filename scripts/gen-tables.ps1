<#
.SYNOPSIS
    21Days 配置表生成入口：跑 Luban，把 Tables/ 下的 Excel 变成 C# 代码与二进制数据。

.DESCRIPTION
    Excel 是唯一数据源。改完 Tables/Data/*.xlsx 跑一次本脚本，产出两份生成物：

        代码  Assets/_Project/Scripts/Core/Config/Generated/    （cfg.Tables 及各表、各 bean、各枚举）
        数据  Assets/_Project/Data/Config/                      （每张表一个 .bytes）

    两份都进 git —— 同事和 CI 不必装 Luban 工具链就能编译和跑测试。
    两份都是生成物，钩子会拒绝手改；要改内容去改 Excel 再跑本脚本。

    Luban 工具本体（Tools/Luban/）不进 git，本脚本发现缺失时自动下载解压。

    退出码：
        0   生成成功
        2   下载或解压 Luban 失败
        3   找不到 dotnet
        4   工程结构不对（缺 Tables/luban.conf 等）
        其他 Luban 自己的退出码原样透出

.PARAMETER Force
    强制重新下载 Luban 工具（本地已有也删掉重下）。工具包损坏或要换版本时用。

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts/gen-tables.ps1

.NOTES
    首次运行会从 GitHub 下载约 30 MB 的 Luban.7z，需要能访问 github.com。
    Luban 工具是 net8.0 程序，本机只装了 .NET 9 时靠 DOTNET_ROLL_FORWARD=Major 前滚运行；
    若前滚失败（报 "framework 'Microsoft.NETCore.App' version '8.0.0' was not found"），
    装一个 .NET 8 运行时即可：winget install Microsoft.DotNet.Runtime.8
#>

[CmdletBinding()]
param(
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

# ---------------------------------------------------------------- 可调变量
# 换 Luban 版本只改这里；资产名在 Release 页面里是固定的 Luban.7z。
$LubanVersion = '5.1.0'
$LubanAsset = 'Luban.7z'
$LubanReleaseUrl = "https://github.com/focus-creative-games/luban/releases/download/v$LubanVersion/$LubanAsset"

# ---------------------------------------------------------------- 路径
# 工程根由脚本自身位置推出（本文件在 <root>/scripts/ 下），不写死任何本机路径。
$RepoRoot = Split-Path -Parent $PSScriptRoot
$ToolDir = Join-Path $RepoRoot 'Tools/Luban'
$LubanDll = Join-Path $ToolDir 'Luban.dll'
$ConfFile = Join-Path $RepoRoot 'Tables/luban.conf'
$CodeOutDir = Join-Path $RepoRoot 'Assets/_Project/Scripts/Core/Config/Generated'
$DataOutDir = Join-Path $RepoRoot 'Assets/_Project/Data/Config'

function Write-Step([string]$message) {
    Write-Host "[配置表] $message"
}

function Fail([string]$message, [int]$code) {
    Write-Host "[配置表] 失败：$message" -ForegroundColor Red
    exit $code
}

# ---------------------------------------------------------------- 前置检查
if (-not (Test-Path -LiteralPath $ConfFile)) {
    Fail "找不到 $ConfFile。确认仓库完整、并且是从工程根跑的本脚本。" 4
}

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if ($null -eq $dotnet) {
    Fail "PATH 里没有 dotnet。装 .NET SDK 8.0 或更高版本后重试（winget install Microsoft.DotNet.SDK.8），见 docs/developer-guide.md 1.6。" 3
}

# ---------------------------------------------------------------- 下载工具
function Install-Luban {
    Write-Step "下载 Luban v$LubanVersion（约 30 MB，只在首次运行时发生）"

    if (Test-Path -LiteralPath $ToolDir) {
        Remove-Item -LiteralPath $ToolDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $ToolDir -Force | Out-Null

    $archive = Join-Path $ToolDir $LubanAsset
    try {
        # GitHub 只接受 TLS 1.2 及以上；PowerShell 5.1 默认可能还在用 TLS 1.0。
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        # 进度条会让 Invoke-WebRequest 在大文件上慢一个数量级，关掉。
        $oldProgress = $ProgressPreference
        $ProgressPreference = 'SilentlyContinue'
        Invoke-WebRequest -Uri $LubanReleaseUrl -OutFile $archive -UseBasicParsing
        $ProgressPreference = $oldProgress
    }
    catch {
        Fail "下载 $LubanReleaseUrl 失败：$($_.Exception.Message)。检查网络/代理，或手动下载后解压到 Tools/Luban/。" 2
    }

    Write-Step '解压'
    $extracted = $false

    # Windows 10 1803 起自带 tar.exe（libarchive），多数情况下能直接解 7z。
    $tar = Get-Command tar -ErrorAction SilentlyContinue
    if ($null -ne $tar) {
        Push-Location $ToolDir
        try {
            & tar -xf $LubanAsset 2>$null
            $extracted = ($LASTEXITCODE -eq 0)
        }
        catch {
            $extracted = $false
        }
        finally {
            Pop-Location
        }
    }

    if (-not $extracted) {
        $sevenZip = Join-Path $env:ProgramFiles '7-Zip/7z.exe'
        if (Test-Path -LiteralPath $sevenZip) {
            & $sevenZip x $archive "-o$ToolDir" -y | Out-Null
            $extracted = ($LASTEXITCODE -eq 0)
        }
    }

    if (-not $extracted) {
        Fail "解压 $LubanAsset 失败：自带的 tar 解不了，也没找到 7-Zip。装一个再重试：winget install 7zip.7zip" 2
    }

    Remove-Item -LiteralPath $archive -Force -ErrorAction SilentlyContinue

    # 有的版本会多套一层目录，把 Luban.dll 捞到 Tools/Luban/ 根下。
    if (-not (Test-Path -LiteralPath $LubanDll)) {
        $found = Get-ChildItem -LiteralPath $ToolDir -Filter 'Luban.dll' -Recurse -File |
            Select-Object -First 1
        if ($null -eq $found) {
            Fail "解压完没找到 Luban.dll。删掉 Tools/Luban/ 后重试，或手动下载 $LubanReleaseUrl。" 2
        }
        Get-ChildItem -LiteralPath $found.DirectoryName -Force |
            Move-Item -Destination $ToolDir -Force
    }

    Write-Step "Luban v$LubanVersion 就位：Tools/Luban/"
}

if ($Force -or -not (Test-Path -LiteralPath $LubanDll)) {
    Install-Luban
}

# ---------------------------------------------------------------- 生成
# Luban 工具本体是 net8.0；本机可能只装了更高版本的 .NET，让它前滚到已装的主版本。
$env:DOTNET_ROLL_FORWARD = 'Major'

New-Item -ItemType Directory -Path $CodeOutDir -Force | Out-Null
New-Item -ItemType Directory -Path $DataOutDir -Force | Out-Null

Write-Step "生成中：代码 -> Assets/_Project/Scripts/Core/Config/Generated/，数据 -> Assets/_Project/Data/Config/"

# -t client        用 luban.conf 里名为 client 的 target（只含 c 分组）
# -c cs-bin        C# 代码 + 二进制数据（配 Luban.Runtime 的 ByteBuf 读取）
# -d bin           数据输出格式同上
& dotnet $LubanDll `
    -t client `
    -c cs-bin `
    -d bin `
    --conf $ConfFile `
    -x "outputCodeDir=$CodeOutDir" `
    -x "outputDataDir=$DataOutDir"

$exitCode = $LASTEXITCODE
if ($exitCode -ne 0) {
    Write-Host "[配置表] Luban 退出码 $exitCode，生成失败。上面是 Luban 的原始输出，按它定位是哪张表/哪一行的问题。" -ForegroundColor Red
    exit $exitCode
}

$codeCount = (Get-ChildItem -LiteralPath $CodeOutDir -Filter '*.cs' -Recurse -File).Count
$dataFiles = Get-ChildItem -LiteralPath $DataOutDir -Filter '*.bytes' -File
Write-Step "完成：$codeCount 个 .cs，$($dataFiles.Count) 个 .bytes（$($dataFiles.Name -join ', ')）"
Write-Step '回到 Unity 等它刷新完，再跑 /unity-test EditMode 确认表能读。'
exit 0
