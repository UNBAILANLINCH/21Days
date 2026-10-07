// 职责：感知与潜行内核的统一入口——把遮挡、暗杀、击倒、追逐、召唤与判定门装配成一个对象，
// 供 Monster / Player / 遭遇流程一次性注入。
//
// 为什么新建：
// - 复用：本模块的五个规则类各自独立，没有它们共同的门面；接线侧如果自己拼，会把
//   「谁先构造、配置从哪来」散到多个文件里，将来换配置就得改多处。
// - 扩展：没有可扩展的既有类型（本模块所有类型都是本次新建）。
// - 新建：以上两条都不成立，故新建一个只做装配的入口。
//
// 接线建议（本次不改任何 Monster / Player 文件，只给建议）：
// 由 `MonsterInstaller`（`Assets/_Project/Scripts/Runtime/Monster/MonsterInstaller.cs`）
// 用 VContainer 注册一个单例 `StealthKernel`，配置资产走 `StealthConfig`；
// 关口判定在 `EncounterStep.Step`（`EncounterStep.cs:101-141`）里、双方规则推进之后调一次。
//
// 出处：`docs/design/features-spotlight/03_潜行与暗杀.md`、`04_追逐.md`、
// `00_功能总览.md` §8.2 步 2 与步 7（S3 潜行与暗杀、S4 追逐）。
using UnityEngine;

namespace Game.Stealth
{
    /// <summary>
    /// 感知与潜行内核。装配好之后是只读的（配置资产在运行时不该被改），
    /// 唯一的可变部分是各规则类自己的推进状态。
    /// </summary>
    public sealed class StealthKernel
    {
        /// <summary>
        /// 用一份配置装配内核。配置为 null 抛异常——静默用默认值会让策划改的资产不生效，
        /// 那种 bug 很难查。
        /// </summary>
        public StealthKernel(StealthConfig config)
        {
            if (config == null)
            {
                throw new System.ArgumentNullException(nameof(config), "StealthConfig 不能为空");
            }

            Config = config;
            Sight = new StealthSight();
            SightSettings = config.Sight;
            Assassination = new AssassinationRules(config.Assassination);
            Knockdown = new KnockdownRules(config.Knockdown);
            Chase = new ChaseRules(config.Chase, config.Baseline);
            Chase.ConfigureChaseSpeed(config.ChaseSpeed);
            SummonSettings = config.Summon;
            ChaseOutcomeSettings = config.ChaseOutcome;
        }

        /// <summary>装配用的配置资产。</summary>
        public StealthConfig Config { get; }

        /// <summary>视线遮挡查询器（关卡加载时喂遮挡体）。</summary>
        public StealthSight Sight { get; }

        /// <summary>遮挡配置（判定门要用）。</summary>
        public SightSettings SightSettings { get; }

        /// <summary>暗杀规则。</summary>
        public AssassinationRules Assassination { get; }

        /// <summary>击倒状态机。</summary>
        public KnockdownRules Knockdown { get; }

        /// <summary>追逐规则。</summary>
        public ChaseRules Chase { get; }

        /// <summary>召唤配置。</summary>
        public SummonSettings SummonSettings { get; }

        /// <summary>被抓后果配置。</summary>
        public ChaseOutcomeSettings ChaseOutcomeSettings { get; }

        /// <summary>
        /// 配置自检：返回 null 表示没问题，否则是第一条问题的中文描述。
        /// 建议在接线启动时调一次并打日志（配置错误不许静默跑，见 `StealthConfig.Validate`）。
        /// </summary>
        public string Validate()
        {
            string configIssue = Config.Validate();
            if (configIssue != null)
            {
                return configIssue;
            }

            ChaseReject speedIssue = Chase.VerifyChaseSpeed();
            return speedIssue == ChaseReject.None ? null : ChaseRules.Describe(speedIssue);
        }

        /// <summary>清空关卡相关的运行态（离开关卡时调）：遮挡体与追逐 / 击倒的计时。</summary>
        public void ResetForNewLevel()
        {
            Sight.Clear();
            Chase.Reset(true);
            Knockdown.Reset(true);
        }

        /// <summary>
        /// 判定某个敌人能不能看见玩家：角度 / 距离先由调用方判（沿用 `MonsterRules.Sense`），
        /// 这里再叠一层视线遮挡。
        /// </summary>
        public bool EnemyPerceives(Vector2 enemyEye, Vector2 playerPosition, bool perceivesByCone) =>
            StealthDecisionGate.PerceivesThroughCover(Sight, enemyEye, playerPosition, perceivesByCone);
    }
}
