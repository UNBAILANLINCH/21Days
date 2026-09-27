// 职责：Live2D 演员适配——把演出表情轨道的「按名字切表情 / 显隐」翻译成 Cubism SDK 的表情控制器与渲染控制器调用。
//   动作不经这里：Cubism 导入器把 .motion3.json 变成 AnimationClip，时间轴 Animation 轨绑模型的 Animator 直接播。
// 为什么新建（复用 → 扩展 → 新建）：SpritePerformanceActor 只认 SpriteRenderer；Live2D 类型只能出现在可选程序集 Game.Live2D 里
//   （SDK 缺席时整个程序集不编译），不能扩展进 Game.Runtime，只能在这里新建一个 PerformanceActor 实现。
//
// ！！未经本地编译验证（SDK 缺席）。导入 SDK 后若有编译错误，按下面的 API 清单核对（以官方源码
//   github.com/Live2D/CubismUnityComponents 为准，本文件按 develop 分支核对过）：
//   · Live2D.Cubism.Framework.Expression.CubismExpressionController（MonoBehaviour）
//       - public CubismExpressionList ExpressionsList;      表情列表资产
//       - public int CurrentExpressionIndex;               当前表情索引（-1 = 无）
//   · Live2D.Cubism.Framework.Expression.CubismExpressionList（ScriptableObject）
//       - public CubismExpressionData[] CubismExpressionObjects;   元素是 ScriptableObject，name 形如「F01.exp3」
//   · Live2D.Cubism.Rendering.CubismRenderController（MonoBehaviour）
//       - public float Opacity;                            整个模型的不透明度
//   程序集：官方 SDK 只有一个运行时 asmdef「Live2D.Cubism」（Core / Framework / Rendering 都在里面），
//   Game.Live2D.asmdef 的 references 已写为 Live2D.Cubism（不是 Live2D.Cubism.Core / Live2D.Cubism.Framework）。
#if LIVE2D_CUBISM
using System;
using System.Collections.Generic;
using Game.Core.Logging;
using Game.Performance;
using Live2D.Cubism.Framework.Expression;
using Live2D.Cubism.Rendering;
using UnityEngine;

namespace Game.Live2D
{
    /// <summary>Live2D 演员。挂在 Cubism 模型根上（与 CubismExpressionController、CubismRenderController 同物体）。</summary>
    public sealed class Live2DPerformanceActor : PerformanceActor
    {
        private const string ExpressionSuffix = ".exp3";

        [Tooltip("模型的表情控制器；留空则 Awake 时从本物体取。")]
        [SerializeField] private CubismExpressionController expressionController;

        [Tooltip("模型的渲染控制器（显隐用 Opacity）；留空则 Awake 时从本物体取。")]
        [SerializeField] private CubismRenderController renderController;

        private readonly List<string> names = new List<string>();
        private readonly HashSet<string> warnedNames = new HashSet<string>(StringComparer.Ordinal);

        public override IReadOnlyList<string> ExpressionNames
        {
            get
            {
                // 编辑器校验时可能还没跑 Awake，这里现取一次。
                CubismExpressionController controller = ResolveExpressionController();
                names.Clear();
                if (controller == null || controller.ExpressionsList == null) return names;
                CubismExpressionData[] objects = controller.ExpressionsList.CubismExpressionObjects;
                if (objects == null) return names;
                for (int i = 0; i < objects.Length; i++)
                {
                    if (objects[i] == null) continue;
                    names.Add(StripSuffix(objects[i].name));
                }
                return names;
            }
        }

        public override void SetExpression(string expressionName)
        {
            CubismExpressionController controller = ResolveExpressionController();
            if (controller != null && controller.ExpressionsList != null && controller.ExpressionsList.CubismExpressionObjects != null)
            {
                CubismExpressionData[] objects = controller.ExpressionsList.CubismExpressionObjects;
                for (int i = 0; i < objects.Length; i++)
                {
                    if (objects[i] != null && string.Equals(StripSuffix(objects[i].name), expressionName, StringComparison.Ordinal))
                    {
                        controller.CurrentExpressionIndex = i;
                        return;
                    }
                }
            }

            // 每个缺失的名字只报一次，时间轴反复经过同一片段也不刷屏。
            if (expressionName != null && warnedNames.Add(expressionName))
                Log.Warn($"Live2DPerformanceActor {name}：没有名为「{expressionName}」的表情（或没接表情控制器）。", this);
        }

        public override void SetVisible(bool visible)
        {
            CubismRenderController controller = ResolveRenderController();
            if (controller != null) controller.Opacity = visible ? 1f : 0f;
        }

        private void Awake()
        {
            ResolveExpressionController();
            ResolveRenderController();
        }

        private CubismExpressionController ResolveExpressionController()
        {
            if (expressionController == null) expressionController = GetComponent<CubismExpressionController>();
            return expressionController;
        }

        private CubismRenderController ResolveRenderController()
        {
            if (renderController == null) renderController = GetComponent<CubismRenderController>();
            return renderController;
        }

        private static string StripSuffix(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.EndsWith(ExpressionSuffix, StringComparison.Ordinal)
                ? value.Substring(0, value.Length - ExpressionSuffix.Length)
                : value;
        }
    }
}
#endif
