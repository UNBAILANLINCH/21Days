// 职责：剧情阶段及结果出口；现有 GameState 管场景，不能为每段剧情建立全局状态类型。
using System;
using System.Collections.Generic;

namespace Game.Narrative
{
    public sealed class NarrativeContent
    {
        public enum StageKind { Condition, Dialogue, WaitAction, Battle, End }
        public sealed class Stage
        {
            public string Id { get; set; } = string.Empty;
            public StageKind Kind { get; set; }
            public string PayloadId { get; set; } = string.Empty;
            public bool AllowEncounter { get; set; }
            public bool IssueRequest { get; set; }
            public string[] RequiredParts { get; set; } = Array.Empty<string>();
            /// <summary>战斗阶段声明的结果出口键（BattleOutcome 代码）；非战斗阶段必须为空。</summary>
            public string[] BattleResults { get; set; } = Array.Empty<string>();
            public NarrativeCondition[][] Conditions { get; set; } = Array.Empty<NarrativeCondition[]>();
            public Dictionary<string, string> Exits { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);
            public string Outcome { get; set; } = "Success";
            public string[] SetFlags { get; set; } = Array.Empty<string>();
        }
        private readonly Dictionary<string, Stage> stages = new Dictionary<string, Stage>(StringComparer.Ordinal);
        public NarrativeContent(string id, string entry, IEnumerable<Stage> source)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("剧情 ID 不可为空");
            Id = id;
            Entry = entry;
            foreach (Stage stage in source ?? throw new ArgumentNullException(nameof(source)))
            {
                if (stage == null || string.IsNullOrWhiteSpace(stage.Id) || stages.ContainsKey(stage.Id) ||
                    !Enum.IsDefined(typeof(StageKind), stage.Kind) || stage.Exits == null || stage.RequiredParts == null ||
                    stage.SetFlags == null || stage.BattleResults == null)
                    throw new ArgumentException("剧情节点非法或重复");
                stages.Add(stage.Id, stage);
            }
            Get(entry);
            foreach (Stage stage in stages.Values)
            {
                foreach (string flag in stage.SetFlags)
                    if (string.IsNullOrWhiteSpace(flag)) throw new ArgumentException("剧情标记不可为空：" + stage.Id);
                foreach (KeyValuePair<string, string> exit in stage.Exits)
                {
                    if (string.IsNullOrWhiteSpace(exit.Key)) throw new ArgumentException("结果代码不可为空");
                    Get(exit.Value);
                }
                if (stage.Kind == StageKind.Condition && (!stage.Exits.ContainsKey("True") || !stage.Exits.ContainsKey("False")))
                    throw new ArgumentException("条件阶段必须有 True／False 出口");
                if (stage.AllowEncounter && stage.Kind != StageKind.WaitAction)
                    throw new ArgumentException("只允许在操作等待时进入局部遭遇");
                // 声明类规则（该不该声明战斗结果 / 外部请求要不要出口 / 多部分行为要不要 Success 出口）
                // 统一在 NarrativeCatalog.Validate 里查：那里才有「阶段引用是否解析得出来」等跨表上下文，
                // 也才能把「声明类」排在「结构类」之前，避免一条前置检查遮蔽掉信息量更大的声明错误。
                // 构造函数只留对象自身的不变量（枚举合法、数组非空、标记非空、出口引用存在、条件可求值）。
                foreach (string part in stage.RequiredParts)
                    if (string.IsNullOrWhiteSpace(part)) throw new ArgumentException("行为部分 ID 不可为空：" + stage.Id);
                NarrativeCondition.Matches(stage.Conditions, new EncounterContext("validate", "", true, false, false, true, false, false));
                CheckAutomaticPath(stage.Id, new HashSet<string>(StringComparer.Ordinal));
            }
        }
        public string Id { get; }
        public string Entry { get; }
        public IEnumerable<Stage> Stages => stages.Values;
        public Stage Get(string id)
        {
            if (id == null || !stages.TryGetValue(id, out Stage stage)) throw new ArgumentException("剧情节点不存在：" + id);
            return stage;
        }
        private void CheckAutomaticPath(string id, HashSet<string> path)
        {
            Stage stage = Get(id);
            if (stage.Kind != StageKind.Condition) return;
            if (!path.Add(id)) throw new ArgumentException("存在无等待的条件环路：" + id);
            foreach (string next in stage.Exits.Values) CheckAutomaticPath(next, path);
            path.Remove(id);
        }
    }
}
