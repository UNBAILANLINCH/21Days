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

.PARAMETER UnityPath
    直接指定 Unity.exe，覆盖自动探测。

.EXAMPLE
    powershell -NoProfile -File scripts/build.ps1 -Target Windows

.EXAMPLE
    powershell -NoProfile -File scripts/build.ps1 -Target Android -Version 0.3.0 -BuildNumber 12

.EXAMPLE
    powershell -NoProfile -File scripts/build.ps1 -Target Windows -Development -OutputPath Builds/Dev/21Days.exe

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

Write-Host "目标平台：$Target"
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
    $sizeMb = [math]::Round(((Get-Item $fullOutput).Length / 1MB), 1)
    Write-Host "打包成功：$fullOutput（$sizeMb MB）"
}
else {
    Write-Host "Unity 退出码 0，但没在 $fullOutput 找到产物。"
    Write-Host "确认 -OutputPath 和 BuildScript 里的默认路径对不对，或直接看日志：$logFile"
}

exit 0
