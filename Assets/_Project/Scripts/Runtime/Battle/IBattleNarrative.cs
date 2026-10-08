// 职责：战斗流程看到的「剧情口」——开打前登记在途（压住存档）、没打完时放弃、打完按出口键回写。
// 为什么新建：BattleFlow 要在 EditMode 里不起 Dialogue / Quest / Save 就能测「胜负各用哪个出口键回写、异常时有没有放弃」，
//   NarrativeService 的构造依赖太重；包成一个三方法接口（真实实现 NarrativeBattlePort），同 Session 的 ISessionStateSource 做法。
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Narrative;

namespace Game.Battle
{
    /// <summary>战斗流程的剧情口。身份一律用事件里带的三项，不要换成遭遇步骤的 EncounterId（PRP §7 坑 5）。</summary>
    public interface IBattleNarrative
    {
        /// <summary>开打前登记「战斗在途」：此后剧情不可保存（自动保存被闸住）。false = 旧身份 / 已在途，不该开仗。</summary>
        bool TryBeginBattle(BattleStageEnteredEvent stage);

        /// <summary>没打完就收场（异常 / 取消 / 回写被拒）：解除在途登记，阶段原地不动、可重试。身份过期时是空操作。</summary>
        void ReleaseBattle(BattleStageEnteredEvent stage);

        /// <summary>按出口键（<c>BattleSession.ExitKey</c>：Victory / Downed）回写。false = 被剧情拒绝（身份过期或结果未声明）。</summary>
        UniTask<bool> CompleteBattleAsync(BattleStageEnteredEvent stage, string exitKey, CancellationToken ct);
    }
}
