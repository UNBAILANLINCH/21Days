// 职责：占位演员——一个 SpriteRenderer + 「表情名 → Sprite」列表；没有 Live2D 资源时先用立绘把演出排出来。
// 为什么新建（复用 → 扩展 → 新建）：PerformanceActor 是抽象类，必须有一个不依赖 Live2D SDK 的具体实现；
//   对白立绘（DialogueCharacter）是 UI Image 数据且 Performance 不得依赖 Dialogue，复用不了。
using System;
using System.Collections.Generic;
using Game.Core.Logging;
using UnityEngine;
using UnityEngine.Timeline;

namespace Game.Performance
{
    /// <summary>Sprite 占位演员。表情名大小写敏感；找不到的名字每个只记一次 Warn。</summary>
    public sealed class SpritePerformanceActor : PerformanceActor
    {
        /// <summary>一条表情：名字 → Sprite。</summary>
        [Serializable]
        public struct ExpressionEntry
        {
            [Tooltip("表情名，时间轴表情片段里填的就是它。")]
            [SerializeField] private string name;

            [Tooltip("该表情显示的 Sprite。")]
            [SerializeField] private Sprite sprite;

            public ExpressionEntry(string name, Sprite sprite)
            {
                this.name = name;
                this.sprite = sprite;
            }

            public string Name => name;
            public Sprite Sprite => sprite;
        }

        [Tooltip("显示表情的 SpriteRenderer。")]
        [SerializeField] private SpriteRenderer target;

        [Tooltip("表情列表：名字 → Sprite。")]
        [SerializeField] private List<ExpressionEntry> expressions = new List<ExpressionEntry>();

        private readonly List<string> names = new List<string>();
        private readonly HashSet<string> warnedNames = new HashSet<string>(StringComparer.Ordinal);
        private bool warnedNoTarget;

        public SpriteRenderer Target => target;

        public override IReadOnlyList<string> ExpressionNames
        {
            get
            {
                // 编辑器校验与运行时都可能调；每次按序列化列表重建，不缓存（Inspector 里改了列表要立即反映）。
                names.Clear();
                if (expressions == null) return names;
                for (int i = 0; i < expressions.Count; i++)
                {
                    if (!string.IsNullOrEmpty(expressions[i].Name)) names.Add(expressions[i].Name);
                }
                return names;
            }
        }

        public override void SetExpression(string expressionName)
        {
            if (target == null)
            {
                if (!warnedNoTarget)
                {
                    warnedNoTarget = true;
                    Log.Warn($"SpritePerformanceActor {name}：target 没接 SpriteRenderer，表情切换无效。", this);
                }
                return;
            }
            if (expressions != null)
            {
                for (int i = 0; i < expressions.Count; i++)
                {
                    if (string.Equals(expressions[i].Name, expressionName, StringComparison.Ordinal))
                    {
                        target.sprite = expressions[i].Sprite;
                        return;
                    }
                }
            }
            // 每个缺失的名字只报一次，时间轴反复经过同一片段也不刷屏。
            if (expressionName != null && warnedNames.Add(expressionName))
                Log.Warn($"SpritePerformanceActor {name}：没有名为「{expressionName}」的表情。", this);
        }

        public override void SetVisible(bool visible)
        {
            if (target != null) target.enabled = visible;
        }

        public override void GatherPreviewProperties(IPropertyCollector driver)
        {
            if (driver == null || target == null) return;
            driver.AddFromName<SpriteRenderer>(target.gameObject, "m_Sprite");
        }
    }
}
