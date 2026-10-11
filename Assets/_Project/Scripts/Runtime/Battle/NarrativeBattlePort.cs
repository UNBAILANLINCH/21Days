// 职责：IBattleNarrative 的真实实现——把三件事转给 NarrativeService（TryBeginBattle / ReleaseBattle / CompleteBattleAsync）。
// 为什么新建：BattleFlow 只依赖接口才能在 EditMode 里单测（见 IBattleNarrative 文件头）；这层是那道缝的真实一侧，
//   放在战斗模块而不是 Narrative：剧情不该认识战斗侧的接口（依赖方向 Battle → Narrative）。
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Narrative;

namespace Game.Battle
{
    /// <summary>经 <see cref="NarrativeService"/> 的剧情口（根作用域单例）。</summary>
    public sealed class NarrativeBattlePort : IBattleNarrative
    {
        private readonly NarrativeService narrative;

        public NarrativeBattlePort(NarrativeService narrative)
        {
            this.narrative = narrative ?? throw new ArgumentNullException(nameof(narrative));
        }

        public bool TryBeginBattle(BattleStageEnteredEvent stage) =>
            narrative.TryBeginBattle(stage.Generation, stage.ActivationId, stage.TargetId);

        public void ReleaseBattle(BattleStageEnteredEvent stage) =>
            narrative.ReleaseBattle(stage.Generation, stage.ActivationId);

        public async UniTask<bool> CompleteBattleAsync(BattleStageEnteredEvent stage, string exitKey, CancellationToken ct)
        {
            // 剧情正在推进（DriveAsync 在跑）时 CompleteBattleAsync 会被 CanAccept 直接拒掉；等它空下来再交，
            // 免得一场打完的仗因为时机不巧被当成「旧结果」丢掉。
            if (narrative.IsBusy) await UniTask.WaitUntil(() => !narrative.IsBusy, PlayerLoopTiming.Update, ct);
            return await narrative.CompleteBattleAsync(stage.Generation, stage.ActivationId, stage.TargetId, exitKey, ct);
        }
    }
}
