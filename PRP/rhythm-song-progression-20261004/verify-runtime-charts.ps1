# 手动纯逻辑回归：反射载入已编译的真实 Rules/InputQueue，不调用 Unity 编辑器 API。
# 状态锚点为 PASS/实际数量；源码晚于程序集则拒绝。只读 PRP JSON 与 DLL，不写玩家档案。
param([string]$UnityEditorPath)
$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new()
$rhythmProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
if (!$UnityEditorPath) {
    $UnityEditorPath = @(Get-Process Unity -ErrorAction SilentlyContinue | Where-Object { $_.Path -match '[\\/]Editor[\\/]Unity.exe$' } | Select-Object -ExpandProperty Path)[0]
}
if (!$UnityEditorPath) { throw '请提供本机现有 Unity.exe 路径；本脚本不会启动 Unity。' }
$rhythmRuntimePath = Join-Path $rhythmProjectRoot 'Library/ScriptAssemblies/Game.Runtime.dll'
$rhythmRuntimeStamp = (Get-Item -LiteralPath $rhythmRuntimePath).LastWriteTimeUtc
foreach ($rhythmTypeName in @('RhythmRules','RhythmNoteData','RhythmHitIntent','RhythmHitResult','RhythmInputQueue')) {
    if ((Get-Item -LiteralPath (Join-Path $rhythmProjectRoot ('Assets/_Project/Scripts/Runtime/Rhythm/' + $rhythmTypeName + '.cs'))).LastWriteTimeUtc -gt $rhythmRuntimeStamp) { throw ('被测源码未编译：' + $rhythmTypeName) }
}
[System.Reflection.Assembly]::LoadFrom((Join-Path (Split-Path $UnityEditorPath) 'Data/Managed/UnityEngine/UnityEngine.CoreModule.dll')) | Out-Null
[System.Reflection.Assembly]::LoadFrom((Join-Path $rhythmProjectRoot 'Library/ScriptAssemblies/Game.Core.dll')) | Out-Null
[System.Reflection.Assembly]::LoadFrom($rhythmRuntimePath) | Out-Null
function Assert-Rhythm($condition, $message) { if (!$condition) { throw $message } }
$rhythmCases = 0
foreach ($rhythmBundleName in @('hybeboy-guitar.chart.json','attention.chart.json')) {
    $rhythmBundle = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $PSScriptRoot $rhythmBundleName) | ConvertFrom-Json
    $rhythmChart = $rhythmBundle.chart
    $rhythmNotes = [Game.Rhythm.RhythmNoteData[]]@($rhythmChart.notes | ForEach-Object { [Game.Rhythm.RhythmNoteData]::new($_.id, $_.lane, $_.timeMs, [Game.Rhythm.RhythmNoteType]$_.type, $_.durationMs) })
    $rhythmHolds = @($rhythmNotes | Where-Object { $_.Type -eq [Game.Rhythm.RhythmNoteType]::Hold })
    foreach ($rhythmOffset in @(-300, 0, 300)) {
        $rhythmRules = [Game.Rhythm.RhythmRules]::new($rhythmNotes, $rhythmChart.perfectMs, $rhythmChart.goodMs, $rhythmOffset, $rhythmChart.chartOffsetMs, $null)
        $rhythmQueue = [Game.Rhythm.RhythmInputQueue]::new(); $rhythmQueue.Begin(7)
        $rhythmEvents = @(); $rhythmSequence = 0
        foreach ($rhythmNote in $rhythmNotes) {
            $rhythmHead = ($rhythmNote.TimeMs + $rhythmOffset) / 1000
            $rhythmEvents += [pscustomobject]@{ Time=$rhythmHead; Lane=$rhythmNote.Lane; Edge=[Game.Rhythm.RhythmInputEdge]::Press; Sequence=$rhythmSequence++ }
            $rhythmTail = if ($rhythmNote.Type -eq [Game.Rhythm.RhythmNoteType]::Hold) { $rhythmHead + $rhythmNote.DurationMs / 1000 + 0.001 } else { $rhythmHead + 0.05 }
            $rhythmEvents += [pscustomobject]@{ Time=$rhythmTail; Lane=$rhythmNote.Lane; Edge=[Game.Rhythm.RhythmInputEdge]::Release; Sequence=$rhythmSequence++ }
        }
        foreach ($rhythmEvent in ($rhythmEvents | Sort-Object Time,Sequence -Descending)) {
            $rhythmIntent = [Game.Rhythm.RhythmHitIntent]::new($rhythmEvent.Lane, $rhythmEvent.Time, $rhythmEvent.Edge, 7)
            $rhythmQueue.Enqueue([ref]$rhythmIntent)
        }
        $rhythmQueue.Drain($rhythmRules, $rhythmChart.durationSeconds + 0.6, $null)
        Assert-Rhythm ($rhythmRules.Perfect -eq $rhythmNotes.Length -and $rhythmRules.Good -eq 0 -and $rhythmRules.Miss -eq 0 -and $rhythmRules.CompletedHolds -eq $rhythmHolds.Length -and $rhythmRules.Score -eq $rhythmNotes.Length * 1000 -and $rhythmQueue.LateInputs -eq 0) ('完整判定失败：' + $rhythmBundle.song.id + '/' + $rhythmOffset)
        Write-Output ('PASS actual Rules/Queue ' + $rhythmBundle.song.id + ': ' + $rhythmRules.Perfect + ' Perfect, ' + $rhythmRules.CompletedHolds + ' Hold; offset ' + $rhythmOffset + '; reverse backlog')
        $rhythmCases++
    }
    foreach ($rhythmHold in $rhythmHolds) {
        $rhythmRules = [Game.Rhythm.RhythmRules]::new([Game.Rhythm.RhythmNoteData[]]@($rhythmHold), 65, 140, 0, 0, $null)
        $rhythmPress = [Game.Rhythm.RhythmHitIntent]::new($rhythmHold.Lane, $rhythmHold.TimeMs / 1000, [Game.Rhythm.RhythmInputEdge]::Press, 1)
        Assert-Rhythm ($rhythmRules.Hit([ref]$rhythmPress).HoldStarted) '长按未激活'
        $rhythmRelease = [Game.Rhythm.RhythmHitIntent]::new($rhythmHold.Lane, ($rhythmHold.TimeMs + $rhythmHold.DurationMs - 1) / 1000, [Game.Rhythm.RhythmInputEdge]::Release, 1)
        $rhythmResult = $rhythmRules.Hit([ref]$rhythmRelease)
        $rhythmRules.Advance($rhythmChart.durationSeconds + 0.6) | Out-Null
        Assert-Rhythm ($rhythmResult.Grade -eq [Game.Rhythm.RhythmGrade]::Miss -and $rhythmRules.Miss -eq 1 -and $rhythmRules.Score -eq 0 -and $rhythmRules.CompletedHolds -eq 0) ('早放必须只失败一次：' + $rhythmHold.Id)
        $rhythmCases++
    }
    $rhythmRules = [Game.Rhythm.RhythmRules]::new($rhythmNotes, 65, 140, 0, 0, $null)
    $rhythmRules.Advance($rhythmChart.durationSeconds + 0.6) | Out-Null
    Assert-Rhythm ($rhythmRules.Miss -eq $rhythmNotes.Length -and $rhythmRules.Score -eq 0 -and $rhythmRules.Finished) ('全漏结算失败：' + $rhythmBundle.song.id)
    $rhythmCases++
    Write-Output ('PASS all ' + $rhythmHolds.Length + ' Holds early-release -1ms and all-miss settlement: ' + $rhythmBundle.song.id)
}
Write-Output ('PASS ' + $rhythmCases + ' actual runtime chart cases; no editor API or user save writes')
