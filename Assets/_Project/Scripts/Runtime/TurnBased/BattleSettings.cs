// 职责：把五份拆开的数值配置打包成一份「战斗设置」，让规则类只需要一个参数。
//
// 为什么要有这一层：`TurnBasedConfig` 是 ScriptableObject（要靠 Unity 创建），
// 而规则与会话的测试不该依赖资产；打包成纯值结构后，测试直接 new 一份占位设置就能跑，
// 接线侧也能在运行时按阶段换一份设置（例如 C90 拍板后不同 BOSS 用不同数值）。
//
// 出处：`docs/design/spotlight/07_回合制作战文档.md` 全篇（各字段在各自结构里注明行号）。

namespace Game.TurnBased
{
    /// <summary>回合制战斗的全部数值设置。</summary>
    public readonly struct BattleSettings
    {
        /// <summary>构造。</summary>
        public BattleSettings(
            in BattleEntrySettings entry,
            in PlayerSkillSettings playerSkills,
            in BossSkillSettings bossSkills,
            in DrunkSettings drunk,
            in BattleFlowSettings flow,
            bool itemOncePerBattle,
            bool inheritsDrunkValue)
        {
            Entry = entry;
            PlayerSkills = playerSkills;
            BossSkills = bossSkills;
            Drunk = drunk;
            Flow = flow;
            ItemOncePerBattle = itemOncePerBattle;
            InheritsDrunkValue = inheritsDrunkValue;
        }

        /// <summary>进入战斗的判定参数。</summary>
        public BattleEntrySettings Entry { get; }

        /// <summary>玩家三招式。</summary>
        public PlayerSkillSettings PlayerSkills { get; }

        /// <summary>BOSS 三招式与权重。</summary>
        public BossSkillSettings BossSkills { get; }

        /// <summary>醉酒四档。</summary>
        public DrunkSettings Drunk { get; }

        /// <summary>回合流程与胜负。</summary>
        public BattleFlowSettings Flow { get; }

        /// <summary>一件道具同一场是否只能用一次（07:42）。</summary>
        public bool ItemOncePerBattle { get; }

        /// <summary>进战斗是否继承战斗外的醉酒值（07:66）。</summary>
        public bool InheritsDrunkValue { get; }

        /// <summary>本工程当前的占位默认值（与 `TurnBasedConfig` 的字段默认完全一致）。</summary>
        public static BattleSettings PlaceholderDefault =>
            new BattleSettings(
                BattleEntrySettings.PlaceholderDefault,
                PlayerSkillSettings.PlaceholderDefault,
                BossSkillSettings.PlaceholderDefault,
                DrunkSettings.PlaceholderDefault,
                BattleFlowSettings.PlaceholderDefault,
                true,
                true);
    }
}
