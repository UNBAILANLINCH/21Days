# 显式纯逻辑回归，加载已有程序集，不调用编辑器或启动 Unity；PASS 为状态锚点。
# 源码晚于程序集时拒绝；本次谱面退役后可移除此定向回归。
param([string]$UnityEditorPath)
$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new()
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
if (!$UnityEditorPath) {
    $UnityEditorPath = @(Get-Process Unity -ErrorAction SilentlyContinue | Where-Object { $_.Path -match '[\\/]Editor[\\/]Unity.exe$' } | Select-Object -ExpandProperty Path)[0]
}
if (!$UnityEditorPath) { throw '请传入本机现有 Unity.exe 路径；此脚本不会启动 Unity。' }
$runtimePath = Join-Path $projectRoot 'Library/ScriptAssemblies/Game.Runtime.dll'
$runtimeStamp = (Get-Item -LiteralPath $runtimePath).LastWriteTimeUtc
foreach ($name in @('RhythmRules','RhythmNoteData','RhythmHitIntent','RhythmHitResult','RhythmInputQueue')) {
    if ((Get-Item -LiteralPath (Join-Path $projectRoot ('Assets/_Project/Scripts/Runtime/Rhythm/' + $name + '.cs'))).LastWriteTimeUtc -gt $runtimeStamp) { throw '被测代码尚未编译：' + $name }
}
[System.Reflection.Assembly]::LoadFrom((Join-Path (Split-Path $UnityEditorPath) 'Data/Managed/UnityEngine/UnityEngine.CoreModule.dll')) | Out-Null
[System.Reflection.Assembly]::LoadFrom((Join-Path $projectRoot 'Library/ScriptAssemblies/Game.Core.dll')) | Out-Null
[System.Reflection.Assembly]::LoadFrom($runtimePath) | Out-Null
$bundle = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $PSScriptRoot 'chongerfei-unreviewed-draft-v3.library-read.json') | ConvertFrom-Json
$notes = [Game.Rhythm.RhythmNoteData[]]@($bundle.chart.notes | ForEach-Object { [Game.Rhythm.RhythmNoteData]::new($_.id, $_.lane, $_.timeMs, [Game.Rhythm.RhythmNoteType]$_.type, $_.durationMs) })
function Assert-True($condition, $message) { if (!$condition) { throw $message } }
foreach ($offset in @(-300, 0, 300)) {
    $rules = [Game.Rhythm.RhythmRules]::new($notes, 65, 140, $offset, 0, $null)
    $queue = [Game.Rhythm.RhythmInputQueue]::new(); $queue.Begin(7)
    $events = @(); $sequence = 0
    foreach ($n in $notes) {
        $head = ($n.TimeMs + $offset) / 1000
        $events += [pscustomobject]@{ Time = $head; Lane = $n.Lane; Edge = [Game.Rhythm.RhythmInputEdge]::Press; Sequence = $sequence++ }
        $tail = if ($n.Type -eq [Game.Rhythm.RhythmNoteType]::Hold) { $head + $n.DurationMs / 1000 + 0.001 } else { $head + 0.05 }
        $events += [pscustomobject]@{ Time = $tail; Lane = $n.Lane; Edge = [Game.Rhythm.RhythmInputEdge]::Release; Sequence = $sequence++ }
    }
    # 故意逆序送达完整积压批次，真实 Queue 必须还原歌曲时间顺序。
    foreach ($event in ($events | Sort-Object Time, Sequence -Descending)) {
        $intent = [Game.Rhythm.RhythmHitIntent]::new($event.Lane, $event.Time, $event.Edge, 7)
        $queue.Enqueue([ref]$intent)
    }
    $queue.Drain($rules, 40.1, $null)
    Assert-True ($rules.Count -eq 58 -and $rules.Perfect -eq 58 -and $rules.Good -eq 0 -and $rules.Miss -eq 0 -and $rules.CompletedHolds -eq 2 -and $rules.Score -eq 58000 -and $rules.MaxCombo -eq 58 -and $queue.LateInputs -eq 0) ('完整谱面判定失败，补偿 ' + $offset)
    Write-Output ('PASS actual Rules/Queue: 58 Perfect, 2 Hold, score 58000; reverse backlog; offset ' + $offset)
}
foreach ($hold in @($notes | Where-Object { $_.Type -eq [Game.Rhythm.RhythmNoteType]::Hold })) {
    $rules = [Game.Rhythm.RhythmRules]::new([Game.Rhythm.RhythmNoteData[]]@($hold), 65, 140, 0, 0, $null)
    $press = [Game.Rhythm.RhythmHitIntent]::new($hold.Lane, $hold.TimeMs / 1000, [Game.Rhythm.RhythmInputEdge]::Press, 1)
    $head = $rules.Hit([ref]$press)
    Assert-True $head.HoldStarted 'Hold 头部未进入保持状态'
    $release = [Game.Rhythm.RhythmHitIntent]::new($hold.Lane, ($hold.TimeMs + $hold.DurationMs - 1) / 1000, [Game.Rhythm.RhythmInputEdge]::Release, 1)
    $result = $rules.Hit([ref]$release)
    $rules.Advance(40.1) | Out-Null
    Assert-True ($result.Grade -eq [Game.Rhythm.RhythmGrade]::Miss -and $rules.Miss -eq 1 -and $rules.Score -eq 0 -and $rules.CompletedHolds -eq 0) ('提前 1ms 松开必须只失败一次：' + $hold.Id)
    Write-Output ('PASS actual Hold: early release -1ms, single Miss; ' + $hold.Id)
}
Write-Output 'PASS 5 uploaded-chart logic scenarios; no Unity editor API invoked'
