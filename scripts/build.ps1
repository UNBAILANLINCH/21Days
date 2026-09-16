<#
.SYNOPSIS
    21Days 本机打包入口：拉起 Unity 批处理模式，调 Game.Editor.BuildScript 出包。

.DESCRIPTION
    Windows 与 Android 共用同一套内容，差异全在编辑器侧的 BuildScript 里，本脚本只负责：
    找到 Unity → 确认工程没被编辑器占着 → 拼命令行 → 跑 → 把结果翻译成人话。

    退出码：
        0   打包成功
        2   Unity 编辑器正开着本工程（工程被锁，批处理跑不了）
        3   找不到 Unity 编辑器
        4   参数或环境有问题（工程根不对、日志目录建不出来等）
        其他 Unity 自己的退出码原样透出

    先查 Unity 再查工程锁：Unity 路径属于「工具链齐不齐」，和工程当前状态无关，
    两个都不满足时，先报根本性的那条更有用；顺带也让「编辑器开着」时仍能验证路径探测这条分支。

.PARAMETER Target
    目标平台，Windows 或 Android。

.PARAMETER Version
    版本号，写进 PlayerSettings.bundleVersion。不给就保持工程里的值。

.PARAMETER BuildNumber
    Android 的 bundleVersionCode。不给而给了 -Version 时，BuildScript 会自增。

.PARAMETER OutputPath
    产物路径，相对工程根。不给就用 Builds/Windows/21Days.exe 或 Builds/Android/21Days.apk。

.PARAMETER Development
    开发版构建：带 Development 标记，可连 Profiler，体积更大，不要用来发版。

.PARAMETER Release
    强制 Android 走 IL2CPP + 仅 ARM64：临时改 ProjectSettings，出完包由 BuildScript
    立刻改回原样，工作区不留 diff。

    **2026-09-16 起工程默认就是 IL2CPP + ARM64**，所以这个开关现在不切换任何东西，
    只剩一层保险：万一谁把 ProjectSettings 改回了 Mono / ARMv7，加上它仍能出 64 位包。
    改默认的起因不是上架，是装机 —— 骁龙 8 Gen 3 那一代之后的手机大核去掉了 AArch32，
    32 位包根本装不上，而真机验证是当前的主要手段。

    随之作废的旧说法：「平时不加，因为 IL2CPP 慢 3.6 倍」—— 现在不加也一样走 IL2CPP。
    实测数字（2026-09-16 本工程）：IL2CPP + ARM64 约 268 秒 / 41.6 MB；
    旧的 Mono + ARMv7 路径 75 秒 / 33.4 MB，只有手动把 ProjectSettings 改回去才走得到。
    这个差距随代码量增长，工程大了 IL2CPP 到十几分钟很常见。

    只对 Android 有意义，Windows 上加了会被忽略。
    注意：它只解决 64 位这一条，导出的仍是 APK，而 Google Play 对新应用要的是 AAB，
    所以加了它也**不等于**可以直接上架，见 docs/developer-guide.md 第 14 章。

.PARAMETER UnityPath
    直接指定 Unity.exe，覆盖自动探测。

.EXAMPLE
    powershell -NoProfile -File scripts/build.ps1 -Target Windows

.EXAMPLE
    powershell -NoProfile -File scripts/build.ps1 -Target Android -Version 0.3.0 -BuildNumber 12

.EXAMPLE
    powershell -NoProfile -File scripts/build.ps1 -Target Windows -Development -OutputPath Builds/Dev/21Days.exe

.EXAMPLE
    powershell -NoProfile -File scripts/build.ps1 -Target Android -Release

.NOTES
    打包前必须关掉 Unity 编辑器：批处理模式要独占工程锁。
    完整日志在 Logs/build-<平台>.log（Logs/ 已被 .gitignore 忽略）。
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Windows', 'Android')]
    [string]$Target,

    [string]$Version,

    [int]$BuildNumber,

    [string]$OutputPath,

    [switch]$Development,

    # 只影响 Android：强制 IL2CPP + 仅 ARM64，出完包设置立刻还原。
    # 工程默认已是 IL2CPP + ARM64，所以平时加不加都一样，它只防「默认被人改回 32 位」。
    [switch]$Release,

    [string]$UnityPath
)

$ErrorActionPreference = 'Stop'

# 参数值里有空格就补引号：Start-Process 把数组用空格拼成一整行命令行，不补引号路径会被拆开。
function Format-UnityArgument {
    param([string]$Value)

    if ($Value -match '\s') {
        return '"' + $Value + '"'
    }

    return $Value
}

# Unity 路径探测，按优先级三级兜底；都落空返回 $null。
function Resolve-UnityExecutable {
    param(
        [string]$Override,
        [string]$Root
    )

    if ($Override) {
        return $Override
    }

    if ($env:UNITY_EDITOR_PATH) {
        Write-Host "使用环境变量 UNITY_EDITOR_PATH 指定的 Unity"
        return $env:UNITY_EDITOR_PATH
    }

    # 从工程自己的版本文件反推 Unity Hub 的默认安装路径，避免把版本号写死在脚本里。
    $versionFile = Join-Path $Root 'ProjectSettings\ProjectVersion.txt'
    if (-not (Test-Path $versionFile)) {
        Write-Host "找不到 $versionFile，无法推断 Unity 版本"
        return $null
    }

    $matched = Select-String -Path $versionFile -Pattern '^m_EditorVersion:\s*(\S+)' | Select-Object -First 1
    if (-not $matched) {
        Write-Host "$versionFile 里没读到 m_EditorVersion"
        return $null
    }

    $editorVersion = $matched.Matches[0].Groups[1].Value
    if (-not $env:ProgramFiles) {
        Write-Host "环境变量 ProgramFiles 为空，无法拼出 Unity Hub 默认安装路径"
        return $null
    }

    $hubPath = Join-Path $env:ProgramFiles "Unity\Hub\Editor\$editorVersion\Editor\Unity.exe"
    Write-Host "工程要求 Unity $editorVersion，按 Hub 默认路径查找"
    return $hubPath
}

# 工程根 = 本脚本所在目录（scripts/）的上一级。
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path (Join-Path $projectRoot 'Assets'))) {
    Write-Host "错误：$projectRoot 下没有 Assets 目录，这里不像 Unity 工程根。"
    Write-Host "本脚本必须放在工程根的 scripts/ 目录下运行。"
    exit 4
}

Write-Host "工程根：$projectRoot"

# ---- 第一关：Unity 在不在 ----
$unityExe = Resolve-UnityExecutable -Override $UnityPath -Root $projectRoot
if (-not $unityExe) {
    Write-Host "错误：没能确定 Unity 编辑器路径。"
    Write-Host "三种指定方式任选其一："
    Write-Host "  1. 传参数      -UnityPath <Unity.exe 的完整路径>"
    Write-Host "  2. 设环境变量  `$env:UNITY_EDITOR_PATH = '<Unity.exe 的完整路径>'"
    Write-Host "  3. 用 Unity Hub 装上 ProjectSettings/ProjectVersion.txt 里写的那个版本（默认路径即可）"
    exit 3
}

if (-not (Test-Path $unityExe)) {
    Write-Host "错误：Unity 不在这个路径上：$unityExe"
    Write-Host "确认版本装没装、装在哪；或用 -UnityPath / 环境变量 UNITY_EDITOR_PATH 指到实际位置。"
    exit 3
}

Write-Host "Unity：$unityExe"

# ---- 第二关：工程有没有被编辑器占着 ----
# 批处理模式要独占工程锁，编辑器开着就必然失败。这里直接拦下，不重试：
# 重试解决不了问题，只会把人晾在那儿等。
$lockFile = Join-Path $projectRoot 'Temp\UnityLockfile'
if (Test-Path $lockFile) {
    Write-Host "错误：Unity 编辑器正开着本工程，关闭编辑器后再打包，或走 CI。"
    Write-Host "（判据：存在 $lockFile）"
    exit 2
}

# ---- 组装 Unity 命令行 ----
$logDirectory = Join-Path $projectRoot 'Logs'
if (-not (Test-Path $logDirectory)) {
    New-Item -ItemType Directory -Path $logDirectory | Out-Null
}

$targetLower = $Target.ToLower()
$logFile = Join-Path $logDirectory "build-$targetLower.log"

if ($Target -eq 'Android') {
    $unityBuildTarget = 'Android'
    $executeMethod = 'Game.Editor.BuildScript.BuildAndroid'
    $defaultOutput = 'Builds/Android/21Days.apk'
}
else {
    $unityBuildTarget = 'Win64'
    $executeMethod = 'Game.Editor.BuildScript.BuildWindows'
    $defaultOutput = 'Builds/Windows/21Days.exe'
}

$unityArgs = @(
    '-batchmode',
    '-nographics',
    '-quit',
    '-projectPath', (Format-UnityArgument $projectRoot),
    '-buildTarget', $unityBuildTarget,
    '-executeMethod', $executeMethod,
    '-logFile', (Format-UnityArgument $logFile)
)

# 下面这些是 BuildScript 自己解析的自定义参数，Unity 不认识，会原样留在命令行里。
if ($OutputPath) {
    $unityArgs += '-outputPath'
    $unityArgs += (Format-UnityArgument $OutputPath)
}

if ($Version) {
    $unityArgs += '-buildVersion'
    $unityArgs += (Format-UnityArgument $Version)
}

if ($PSBoundParameters.ContainsKey('BuildNumber')) {
    $unityArgs += '-buildNumber'
    $unityArgs += "$BuildNumber"
}

if ($Development) {
    $unityArgs += '-development'
}

if ($Release) {
    $unityArgs += '-releaseBuild'
}

Write-Host "目标平台：$Target"

# 明确打出本次用的是哪种配置：出完包别让人对着体积猜自己刚才出的到底是不是能上架的那种。
if ($Target -eq 'Android') {
    if ($Release) {
        Write-Host "配置：Release（强制 IL2CPP + ARM64）"
        Write-Host "  工程默认已是这套配置，本开关只防「默认被人改回 32 位」，平时不必加。"
        Write-Host "  慢：IL2CPP 要先把 C# 转译成 C++ 再用 NDK 编原生库（本工程实测约 268 秒），别以为卡死了。"
        Write-Host "  设置是临时改的，出完包 BuildScript 会改回原样，工作区不留 diff。"
    }
    else {
        Write-Host "配置：沿用 ProjectSettings（工程默认 IL2CPP + ARM64，能装 2024 年后的纯 64 位手机）"
        Write-Host "  慢：默认路径就走 IL2CPP，本工程实测约 268 秒，别以为卡死了。"
    }
}
elseif ($Release) {
    Write-Host "提示：-Release 只对 Android 有意义（它切的是 Android 的脚本后端与 CPU 架构），本次忽略。"
}

Write-Host "日志：$logFile"
Write-Host "命令行：$unityExe $($unityArgs -join ' ')"
Write-Host "开始打包，首次切平台会重新导入资产，可能要几分钟……"

$process = Start-Process -FilePath $unityExe -ArgumentList $unityArgs -Wait -PassThru -NoNewWindow
$exitCode = $process.ExitCode
if ($null -eq $exitCode) {
    Write-Host "错误：没拿到 Unity 的退出码，按失败处理。日志：$logFile"
    exit 1
}

if ($exitCode -ne 0) {
    Write-Host "打包失败，Unity 退出码 $exitCode"
    if (Test-Path $logFile) {
        Write-Host "---- 日志末尾 60 行（完整日志：$logFile）----"
        Get-Content -Path $logFile -Tail 60 | ForEach-Object { Write-Host $_ }
        Write-Host "---- 日志结束 ----"
    }
    else {
        Write-Host "日志文件不存在：$logFile"
    }

    exit $exitCode
}

# ---- 成功：报产物路径与体积 ----
$finalOutput = $OutputPath
if (-not $finalOutput) {
    $finalOutput = $defaultOutput
}

if ([System.IO.Path]::IsPathRooted($finalOutput)) {
    $fullOutput = $finalOutput
}
else {
    $fullOutput = Join-Path $projectRoot $finalOutput
}

if (Test-Path $fullOutput) {
    # Windows 的产物是一个目录：exe 只是个几百 KB 的启动器，数据全在同级的 *_Data 里。
    # 只报 exe 的大小会让人以为整包就那么点，所以这里报**整个输出目录**的合计。
    # Android 的产物就是单个 apk，报它自己即可。
    $outputDir = Split-Path -Parent $fullOutput
    $selfMb = [math]::Round(((Get-Item $fullOutput).Length / 1MB), 1)
    if ($Target -eq 'Windows') {
        $totalBytes = (Get-ChildItem -Path $outputDir -Recurse -File | Measure-Object -Property Length -Sum).Sum
        $totalMb = [math]::Round(($totalBytes / 1MB), 1)
        Write-Host "打包成功：$fullOutput"
        Write-Host "  整包 $totalMb MB（目录 $outputDir），其中启动器 $selfMb MB"
    }
    else {
        Write-Host "打包成功：$fullOutput（$selfMb MB）"
    }

    # 上面那几行「配置：……」说的是本脚本的**意图**；这一行是 BuildScript 从 PlayerSettings 实读后
    # 打进日志的**实际结果**。两者不一致时以这条为准。
    if ($Target -eq 'Android') {
        $configLine = Select-String -Path $logFile -Pattern '\[Build\] 配置：' -Encoding utf8 | Select-Object -Last 1
        if ($configLine) {
            Write-Host "  实际生效 → $($configLine.Line.Trim())"
        }
    }
}
else {
    Write-Host "Unity 退出码 0，但没在 $fullOutput 找到产物。"
    Write-Host "确认 -OutputPath 和 BuildScript 里的默认路径对不对，或直接看日志：$logFile"
}

exit 0
