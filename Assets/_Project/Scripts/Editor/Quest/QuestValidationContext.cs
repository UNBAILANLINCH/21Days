// 职责：任务表交叉引用校验用的「已知集合」（对话编号、地点键 → 场景名、计数键），由调用方提供，校验器本身不碰文件与资产库。
// 为什么新建：把「从工程里收集」与「拿集合做判断」拆开，校验器才能是纯函数、测试能喂假集合；现有代码里没有这类登记表可复用。

using System;
using System.Collections.Generic;

namespace Game.Editor.Quest
{
    public sealed class QuestValidationContext
    {
        private readonly HashSet<int> dialogueIds;
        private readonly Dictionary<string, string> locationKeys;
        private readonly HashSet<string> counterKeys;

        /// <param name="dialogueIds">存在的对话编号（Tables/Data/dialogue/*.json 的文件名）。</param>
        /// <param name="locationKeys">地点键 → 所在场景名；重复键保留第一个。</param>
        /// <param name="counterKeys">有玩法会上报的计数键。</param>
        public QuestValidationContext(
            IEnumerable<int> dialogueIds,
            IEnumerable<KeyValuePair<string, string>> locationKeys,
            IEnumerable<string> counterKeys)
        {
            this.dialogueIds = dialogueIds == null ? new HashSet<int>() : new HashSet<int>(dialogueIds);
            this.locationKeys = new Dictionary<string, string>(StringComparer.Ordinal);
            if (locationKeys != null)
            {
                foreach (KeyValuePair<string, string> pair in locationKeys)
                {
                    if (string.IsNullOrEmpty(pair.Key) || this.locationKeys.ContainsKey(pair.Key)) continue;
                    this.locationKeys.Add(pair.Key, pair.Value ?? string.Empty);
                }
            }

            this.counterKeys = new HashSet<string>(StringComparer.Ordinal);
            if (counterKeys != null)
            {
                foreach (string key in counterKeys)
                {
                    if (!string.IsNullOrEmpty(key)) this.counterKeys.Add(key);
                }
            }
        }

        /// <summary>Tables/Data/dialogue/*.json 的文件名（能解析成整数的）。</summary>
        public IReadOnlyCollection<int> DialogueIds => dialogueIds;

        /// <summary>地点键 → 所在场景名（多场景合并；重复键取第一个）。</summary>
        public IReadOnlyDictionary<string, string> LocationKeys => locationKeys;

        /// <summary>已知计数键（LootConfig.CrateQuestKey 等）。</summary>
        public IReadOnlyCollection<string> CounterKeys => counterKeys;

        public bool HasDialogue(int id) => dialogueIds.Contains(id);

        public bool HasLocation(string key) => key != null && locationKeys.ContainsKey(key);

        public bool HasCounter(string key) => key != null && counterKeys.Contains(key);
    }
}
