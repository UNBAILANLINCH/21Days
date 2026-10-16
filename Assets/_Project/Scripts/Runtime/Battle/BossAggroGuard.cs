// 职责：BOSS 主动发难的守卫——哪一件事此刻挡着它不许冲（PRP/combat-iteration D6）。
// 为什么新建：一个类型一个文件；IBossAggroWorld 按它回答「第一个挡住的是谁」，埋点 boss_aggro_blocked 的 guard 按它记。
//   「已被击败」不在这里：击败后 NarrativeFlagVisibility 把整个 BOSS 物体关掉，组件随之不再步进（BossAggro.Tick 首行）。
//   「这只 BOSS 没开发难」也不在这里：那是绑定时定的（BossDefinition.AggroEnabled），关着的组件根本不步进。
namespace Game.Battle
{
    /// <summary>发难守卫（按检查顺序排列；<see cref="None"/> = 全过）。</summary>
    public enum BossAggroGuard
    {
        /// <summary>没有守卫挡着。</summary>
        None = 0,

        /// <summary>有对白在播（DialogueService.IsRunning）。</summary>
        Dialogue = 1,

        /// <summary>有演出在播（IPerformanceService.IsRunning）。</summary>
        Performance = 2,

        /// <summary>有一场战斗在流程里（BattleFlow.IsBattleRunning，含实时遭遇升级出来的那一仗）。</summary>
        Battle = 3,

        /// <summary>世界被暂停（IWorldPauseService.IsPaused：暂停菜单、背包等）。</summary>
        WorldPaused = 4,

        /// <summary>剧情正在推进（NarrativeService.IsBusy）——这时发起交互也会被剧情拒掉。</summary>
        NarrativeBusy = 5,

        /// <summary>剧情服务不在或没就绪（没装 / 已释放）：碰到了也开不了仗，不冲。</summary>
        NarrativeUnavailable = 6,
    }
}
