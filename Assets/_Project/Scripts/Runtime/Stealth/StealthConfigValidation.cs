// 职责：`StealthConfig` 的纯函数校验——把「资产里填的数对不对」的判断从 ScriptableObject 里拿出来，
// 这样它既能在 Inspector 侧调用，也能在**不创建资产**的情况下用纯逻辑测（离线 / EditMode 都行，
// 不需要 Unity 引擎实例）。
//
// 为什么新建（不写在 `StealthConfig` 里）：
// - 复用：工程里没有别的「配置自检」入口可以借；Narrative 的 `ValidateFacts` 只管它的键，形态也不同。
// - 扩展：`StealthConfig` 是 ScriptableObject，任何对它的调用都要求 Unity 运行时能创建资产；
//   校验逻辑本身跟资产无关，塞在里面就没法脱离引擎测。
// - 新建：以上两条都不成立，故新建一个只吃数值的静态校验器。
//
// 出处：`docs/design/features-spotlight/04_追逐.md:165`（追兵速度 2.5 低于玩家步行 3 的现状问题）、
// `04:175`（约束 1：惩罚要重到让人怕，又不能一抓就死）、`00_功能总览.md` §8.1 #4 / #5。
namespace Game.Stealth
{
    /// <summary>
    /// 配置校验的纯函数入口。返回 null 表示没问题，否则返回第一条问题的中文描述。
    /// <para>
    /// 之所以做成「返回描述」而不是抛异常：资产是给策划在 Inspector 里改的，
    /// 校验要能一条条列出来给界面 / 日志用（`StealthConfig.Validate` 就是转发到这里的）。
    /// </para>
    /// </summary>
    public static class StealthConfigValidation
    {
        /// <summary>校验全部数值。返回第一条问题，没问题返回 null。</summary>
        public static string Validate(
            KnockdownSettings knockdown,
            ChaseSettings chase,
            ChaseBaseline baseline,
            SummonSettings summon,
            float chaseSpeed,
            int knockdownCountLowMax,
            int knockdownCountMidMax,
            int failAfterCaughtCount)
        {
            if (knockdownCountLowMax < 0 || knockdownCountMidMax < knockdownCountLowMax)
            {
                return "击倒次数档位阈值必须满足 0 <= 低档上界 <= 中档上界";
            }

            if (chaseSpeed <= 0f)
            {
                return "追兵速度必须为正数";
            }

            if (baseline.PlayerWalkSpeed <= 0f || baseline.PlayerRunSpeed <= 0f)
            {
                return "玩家速度基线必须为正数";
            }

            if (chaseSpeed <= baseline.PlayerWalkSpeed)
            {
                return $"追兵速度 {chaseSpeed} 不高于玩家步行 {baseline.PlayerWalkSpeed}，追逐会失去威胁（04:165）";
            }

            if (chaseSpeed > baseline.PlayerRunSpeed)
            {
                return $"追兵速度 {chaseSpeed} 高于玩家奔跑 {baseline.PlayerRunSpeed}，追逐会变成必死";
            }

            if (summon.MinGroupSize <= 0 || summon.MaxGroupSize < summon.MinGroupSize)
            {
                return "巡逻队规模必须满足 0 < 下限 <= 上限";
            }

            if (failAfterCaughtCount <= 0)
            {
                return "失败所需被抓次数必须为正数";
            }

            if (chase.LoseSightGraceSeconds <= 0f)
            {
                return "跟丢宽限时长必须为正数（否则永远摆脱不了）";
            }

            if (knockdown.CrawlSpeedMultiplier <= 0f)
            {
                return "击倒挣扎期的移动速度倍率必须为正数";
            }

            return null;
        }
    }
}
