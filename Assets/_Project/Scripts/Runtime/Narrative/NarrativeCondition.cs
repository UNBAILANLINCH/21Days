// 职责：无副作用的强类型条件组；没有现有条件实现，不能把玩法判断放到对白视图。
using System;

namespace Game.Narrative
{
    public sealed class NarrativeCondition
    {
        public EncounterContext.Fact Fact { get; set; }
        public string Key { get; set; } = string.Empty;
        public bool Expected { get; set; } = true;

        public void Validate()
        {
            if (!Enum.IsDefined(typeof(EncounterContext.Fact), Fact))
                throw new ArgumentException("未接入的条件类型");
            if (Fact == EncounterContext.Fact.StoryFlag && string.IsNullOrWhiteSpace(Key))
                throw new ArgumentException("剧情标记不可为空");
        }

        // 外层 OR，内层 AND；空外层表示无条件，不接受空的 OR 分支。
        public static bool Matches(NarrativeCondition[][] groups, EncounterContext context)
        {
            if (groups == null) throw new ArgumentNullException(nameof(groups));
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (groups.Length == 0) return true;
            bool matched = false;
            foreach (NarrativeCondition[] group in groups)
            {
                if (group == null || group.Length == 0) throw new ArgumentException("条件组不能为空");
                bool all = true;
                foreach (NarrativeCondition condition in group)
                {
                    if (condition == null) throw new ArgumentException("条件不能为空");
                    condition.Validate();
                    // 不短路校验，非法条件不能被前面的 true 掩盖。
                    all &= context.Read(condition.Fact, condition.Key) == condition.Expected;
                }
                matched |= all;
            }
            return matched;
        }
    }
}
