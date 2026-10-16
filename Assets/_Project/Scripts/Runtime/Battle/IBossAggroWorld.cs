// 职责：BOSS 发难组件（BossAggro）看得见的外部世界——守卫、玩家在哪、在不在跟它打、世界是否被战斗挡住、发起交互
//   （PRP/combat-iteration D6）。
// 为什么新建（抽成接口）：发难状态机要能被 EditMode 测试逐帧直接驱动（每个守卫各一条负对照、接触只触发一次交互），
//   而真实的守卫来源（DialogueService / 演出 / BattleFlow / 暂停 / NarrativeService）与交互入口（NarrativeTrigger →
//   NarrativeService）都要整套服务才建得起来。真实实现是 BossAggroSceneBinder（场景加载时把自己交给组件），
//   测试换假实现——同 IBattleWorldGate / IBattleNarrative 的拆法。
// 每帧读的成员（CheckGuards / IsFighting / IsWorldHiddenByBattle / TryGetPlayerPosition）实现方必须零分配、不查找。
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
// 两个同名 BattleOutcome（Game.TurnBased / Game.Narrative）：剧情侧类型用别名（PRP/turnbased-battle §7 坑 4）。
using NarrativeTrigger = Game.Narrative.NarrativeTrigger;

namespace Game.Battle
{
    /// <summary>BOSS 发难的外部世界（真实实现 <see cref="BossAggroSceneBinder"/>）。</summary>
    public interface IBossAggroWorld
    {
        /// <summary>这一份世界还能用（根作用域没销毁）；为假时组件什么都不做。</summary>
        bool IsAvailable { get; }

        /// <summary>按 <see cref="BossAggroGuard"/> 的顺序查，返回第一个挡着的；全过返回 <see cref="BossAggroGuard.None"/>。</summary>
        BossAggroGuard CheckGuards();

        /// <summary>流程里（含等门闸）的那一仗是不是跟 <paramref name="bossId"/> 打的。</summary>
        bool IsFighting(string bossId);

        /// <summary>
        /// 世界此刻被战斗挡着、玩家看不见（战斗场景在场：开场黑幕已落满、或战斗舞台在屏上、或收场黑幕还盖着）。
        /// BOSS 在这段时间里瞬移回原位，玩家看不到它「闪回去」。
        /// </summary>
        bool IsWorldHiddenByBattle { get; }

        /// <summary>玩家根物体此刻的世界位置（渲染位置，已做 tick 插值）；场景里没有玩家、或玩家已倒地时返回 false。</summary>
        bool TryGetPlayerPosition(out Vector3 position);

        /// <summary>
        /// 以 <paramref name="trigger"/> 的名义发起一次交互——与按 E 挑战走同一条入口（嘲讽对白 → Battle 阶段 → 开仗）。
        /// 返回 false = 剧情没受理（忙 / 距离 / 条件），异常与取消原样抛出。每次接触只调一次。
        /// </summary>
        UniTask<bool> InteractAsync(NarrativeTrigger trigger, CancellationToken ct);
    }
}
