# ============================================================
# install_env.ps1 —— /onboard 环境一键安装（Windows / PowerShell 5.1+）
#
# 用途：按 docs/developer-guide.md 第 1 章的安装顺序，自动装 Git、Python、
#       uv、.NET SDK 8、Unity Hub 这五项；不装 Unity 编辑器与 Claude Code——
#       前者体积大且要手动勾模块，后者走官方渠道——这两项只打印安装指引，
#       需要开发者自己完成。
#
# 怎么跑（需要管理员权限的 PowerShell，装系统级工具要提权）：
#     powershell -ExecutionPolicy Bypass -File .claude/skills/onboard/install_env.ps1
#
# 参数：
#     -SkipHub   跳过 Unity Hub 这一项
#     -DryRun    只打印将执行的 winget 命令，不真正安装（Claude 可以用这个预览）
#
# 幂等：每项先检测是否已装且版本达标，达标就打印「已安装，跳过」，不重复装。
# 单项安装失败不中断整体流程，记到末尾汇总。
#
# 装完请重开一个新终端窗口再跑 check_env.py —— winget 新装的工具不会立刻
# 出现在当前终端的 PATH 里。
# ============================================================

param(
    [switch]$SkipHub,
    [switch]$DryRun
)

$ErrorActionPreference = 'Continue'

# 控制台默认代码页在中文 Windows 上常是 936（GBK），中文会乱码；改成 UTF-8。
# 输出被重定向/不是真终端时这行可能抛异常，忽略即可，不影响脚本本身逻辑。
try {
    [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
} catch { }

# 工程根：本脚本在 <root>/.claude/skills/onboard/ 下，往上三层推导，不写死盘符。
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
$GuideDoc = "docs/developer-guide.md"

Write-Host "[install_env] 环境一键安装（幂等，已装且达标的会跳过）"
Write-Host ""

$installed = New-Object System.Collections.ArrayList
$skipped = New-Object System.Collections.ArrayList
$failed = New-Object System.Collections.ArrayList
$manual = New-Object System.Collections.ArrayList

function Invoke-WingetInstall {
    param(
        [string]$Id
    )
    $cmdText = "winget install --id $Id -e --accept-source-agreements --accept-package-agreements"
    if ($DryRun) {
        Write-Host "  [DryRun] 将执行：$cmdText"
        return $true
    }
    Write-Host "  安装中：$cmdText"
    try {
        winget install --id $Id -e --accept-source-agreements --accept-package-agreements
        if ($LASTEXITCODE -eq 0) {
            return $true
        }
        Write-Host "  winget 返回码 $LASTEXITCODE，视为失败"
        return $false
    } catch {
        Write-Host "  安装出错：$($_.Exception.Message)"
        return $false
    }
}

# ---- winget 可用性：没有就退化为打印地址，直接退出 ----
$hasWinget = $null -ne (Get-Command winget -ErrorAction SilentlyContinue)
if (-not $hasWinget) {
    Write-Host "[install_env] 本机没有 winget，无法自动安装，改为打印各工具官网地址："
    Write-Host "  Git        https://git-scm.com/download/win     （$GuideDoc §1.3）"
    Write-Host "  Python     https://www.python.org/downloads/     （$GuideDoc §1.4）"
    Write-Host "  uv         https://astral.sh/uv                  （$GuideDoc §1.5）"
    Write-Host "  .NET SDK   https://dotnet.microsoft.com/download （$GuideDoc §1.6）"
    Write-Host "  Unity Hub  https://unity.com/download             （$GuideDoc §1.1）"
    Write-Host ""
    Write-Host "手动装完后跑：python .claude/skills/onboard/check_env.py"
    exit 2
}

# ---- git ----
Write-Host "[git]"
$gitCmd = Get-Command git -ErrorAction SilentlyContinue
if ($gitCmd) {
    Write-Host "  已安装，跳过：$($gitCmd.Source)"
    [void]$skipped.Add("git")
} else {
    if (Invoke-WingetInstall -Id "Git.Git") {
        [void]$installed.Add("git")
    } else {
        [void]$failed.Add("git")
    }
}
Write-Host ""

# ---- Python 3.10+ ----
Write-Host "[Python 3.10+]"
$pyOk = $false
$pyCmd = Get-Command python -ErrorAction SilentlyContinue
if ($pyCmd) {
    try {
        $verText = (& python --version) 2>&1 | Out-String
        if ($verText -match "(\d+)\.(\d+)\.(\d+)") {
            $maj = [int]$Matches[1]
            $min = [int]$Matches[2]
            if (($maj -gt 3) -or (($maj -eq 3) -and ($min -ge 10))) {
                $pyOk = $true
                Write-Host "  已安装，跳过：$($verText.Trim())"
                [void]$skipped.Add("python")
            }
        }
    } catch { }
}
if (-not $pyOk) {
    if ($pyCmd) { Write-Host "  已安装但版本低于 3.10，尝试安装较新版本" }
    if (Invoke-WingetInstall -Id "Python.Python.3.12") {
        [void]$installed.Add("python")
    } else {
        [void]$failed.Add("python")
    }
}
Write-Host ""

# ---- uv / uvx ----
Write-Host "[uv / uvx]"
$uvCmd = Get-Command uv -ErrorAction SilentlyContinue
if ($uvCmd) {
    Write-Host "  已安装，跳过：$($uvCmd.Source)"
    [void]$skipped.Add("uv")
} else {
    if (Invoke-WingetInstall -Id "astral-sh.uv") {
        [void]$installed.Add("uv")
    } else {
        [void]$failed.Add("uv")
    }
}
Write-Host ""

# ---- .NET SDK 8.0+ ----
Write-Host "[.NET SDK 8.0+]"
$dotnetOk = $false
$dotnetCmd = Get-Command dotnet -ErrorAction SilentlyContinue
if ($dotnetCmd) {
    try {
        $sdks = & dotnet --list-sdks 2>&1
        foreach ($line in $sdks) {
            if ($line -match "^(\d+)\.") {
                if ([int]$Matches[1] -ge 8) {
                    $dotnetOk = $true
                    break
                }
            }
        }
    } catch { }
}
if ($dotnetOk) {
    Write-Host "  已安装，跳过（已有 8.0+ SDK）"
    [void]$skipped.Add("dotnet")
} else {
    if ($dotnetCmd) { Write-Host "  dotnet 在 PATH 但没有 8.0+ SDK，尝试安装" }
    if (Invoke-WingetInstall -Id "Microsoft.DotNet.SDK.8") {
        [void]$installed.Add("dotnet")
    } else {
        [void]$failed.Add("dotnet")
    }
}
Write-Host ""

# ---- Unity Hub ----
if ($SkipHub) {
    Write-Host "[Unity Hub] -SkipHub 已跳过"
} else {
    Write-Host "[Unity Hub]"
    $hubCandidates = @()
    if ($env:ProgramFiles) { $hubCandidates += (Join-Path $env:ProgramFiles "Unity Hub\Unity Hub.exe") }
    $pf86 = ${env:ProgramFiles(x86)}
    if ($pf86) { $hubCandidates += (Join-Path $pf86 "Unity Hub\Unity Hub.exe") }

    $hubFound = $false
    foreach ($p in $hubCandidates) {
        if (Test-Path $p) { $hubFound = $true; break }
    }
    if ($hubFound) {
        Write-Host "  已安装，跳过"
        [void]$skipped.Add("Unity Hub")
    } else {
        if (Invoke-WingetInstall -Id "Unity.UnityHub") {
            [void]$installed.Add("Unity Hub")
        } else {
            [void]$failed.Add("Unity Hub")
        }
    }
}
Write-Host ""

# ---- Unity 编辑器 2022.3.62f2（不自动装，只打印指引） ----
Write-Host "[Unity 编辑器 2022.3.62f2] 不自动安装，需手动完成"
$versionFile = Join-Path $RepoRoot "ProjectSettings\ProjectVersion.txt"
if (Test-Path $versionFile) {
    $content = Get-Content $versionFile -Raw
    $m = [regex]::Match($content, "m_EditorVersionWithRevision:\s*(\S+)\s*\(([0-9a-f]+)\)")
    if ($m.Success) {
        $ver = $m.Groups[1].Value
        $changeset = $m.Groups[2].Value
        Write-Host "  深链（浏览器打开会唤起 Hub 安装该版本）：unityhub://$ver/$changeset"
    } else {
        Write-Host "  未能从 ProjectVersion.txt 解析出 changeset，去 Unity Hub 的 Archive 里手动找版本"
    }
} else {
    Write-Host "  找不到 ProjectSettings/ProjectVersion.txt，去 Unity Hub 的 Archive 里手动找版本"
}
Write-Host "  必须勾选模块：Windows Build Support (IL2CPP)、Android Build Support（含 SDK & NDK Tools、OpenJDK）"
Write-Host "  详见 $GuideDoc §1.2"
[void]$manual.Add("Unity 编辑器 2022.3.62f2")
Write-Host ""

# ---- Claude Code（不自动装，只打印指引） ----
Write-Host "[Claude Code] 不自动安装，需手动完成"
Write-Host "  以官方文档为准安装；装完用 claude --version 验证"
Write-Host "  详见 $GuideDoc §1.7"
[void]$manual.Add("Claude Code")
Write-Host ""

# ---- 汇总 ----
Write-Host "[install_env] 汇总"
if ($skipped.Count -gt 0) { Write-Host "  已安装（本次跳过）：$($skipped -join '、')" } else { Write-Host "  已安装（本次跳过）：无" }
if ($installed.Count -gt 0) { Write-Host "  本次安装：$($installed -join '、')" } else { Write-Host "  本次安装：无" }
if ($failed.Count -gt 0) {
    Write-Host "  失败：$($failed -join '、') —— 手动按 $GuideDoc 第 1 章对应小节安装"
} else {
    Write-Host "  失败：无"
}
Write-Host "  需手动完成：$($manual -join '、')"
Write-Host ""
Write-Host "[install_env] 重开一个新终端窗口，再跑 python .claude/skills/onboard/check_env.py 做自检。"

if ($failed.Count -gt 0) {
    exit 1
} else {
    exit 0
}
