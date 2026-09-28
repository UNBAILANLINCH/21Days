// 职责：场景演出触发器的纯判定——「只播一次」已播过、服务正忙时不触发，并给出原因；
//   以及演出期间隐藏 / 恢复触发者与指定场景物体（NPC、巡逻怪、任务标记）的 Renderer 与 Canvas 的成对操作
//   （与场景解耦，可直接喂物体 / 组件数组单测）。
// 为什么新建（复用 → 扩展 → 新建）：判定逻辑要脱离场景与服务单测，PerformanceRules 是单段演出的阶段机，
//   触发判定不属于它的职责；工程里没有可复用的「一次性触发」判定。
using System.Collections.Generic;
using Game.CharacterPuppet;
using UnityEngine;

namespace Game.Performance
{
    /// <summary>触发判定与演出期间的显隐。全部静态；判定无分配。</summary>
    public static class PerformanceTriggerRules
    {
        /// <summary>原因：只播一次且已播过。</summary>
        public const string ReasonPlayed = "played";

        /// <summary>原因：已有演出在播放。</summary>
        public const string ReasonBusy = "busy";

        /// <summary>
        /// 是否应触发。已播过优先于忙碌（已播过是永久原因，更有诊断价值）。
        /// </summary>
        /// <param name="once">是否只播一次。</param>
        /// <param name="hasPlayed">存档里是否已播过（once 为 false 时忽略）。</param>
        /// <param name="serviceRunning">服务是否正在播放。</param>
        /// <param name="reason">不触发时为 <see cref="ReasonPlayed"/> / <see cref="ReasonBusy"/>；触发时为 null。</param>
        public static bool ShouldFire(bool once, bool hasPlayed, bool serviceRunning, out string reason)
        {
            if (once && hasPlayed)
            {
                reason = ReasonPlayed;
                return false;
            }
            if (serviceRunning)
            {
                reason = ReasonBusy;
                return false;
            }
            reason = null;
            return true;
        }

        /// <summary>在实例化舞台之前收集场景角色；包含未激活角色，避免演出期间启用时漏出。只扫描一次。</summary>
        public static HiddenVisuals HideSceneCharacters()
        {
            var roots = new List<GameObject>();
            foreach (ChibiPuppet puppet in Object.FindObjectsByType<ChibiPuppet>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (puppet.GetComponentInParent<PerformanceStage>(true) != null) continue;
                ChibiPuppetMotion motion = puppet.GetComponent<ChibiPuppetMotion>();
                // 使用表现组件公开的角色根，不依赖玩家或对白模块。
                // ponytail: 无驱动的小人退回顶层根；放入公共容器时应配置 ChibiPuppetMotion.TrackedRoot。
                Transform root = motion != null && motion.TrackedRoot != null ? motion.TrackedRoot : puppet.transform.root;
                roots.Add(root.gameObject);
            }
            return HideVisuals(roots);
        }

        /// <summary>
        /// 隐藏一批物体根下的全部 Renderer 与 Canvas（世界空间名牌 / 气泡 / 任务标记不是 Renderer），记下原 enabled 值。
        /// 只切组件 enabled、不 SetActive，物体上的脚本与协程照跑。根为 null、重复、互相嵌套都安全：组件去重后各隐藏一次，
        /// 否则第二次会把「已被关掉」当成原值记下，恢复时就回不去了。入参为 null 返回空快照。
        /// </summary>
        public static HiddenVisuals HideVisuals(IReadOnlyList<GameObject> roots)
        {
            var renderers = new List<Renderer>();
            var canvases = new List<Behaviour>();
            if (roots != null)
            {
                var seen = new HashSet<Component>();
                for (int i = 0; i < roots.Count; i++)
                {
                    GameObject root = roots[i];
                    if (root == null) continue;
                    foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                    {
                        if (seen.Add(renderer)) renderers.Add(renderer);
                    }
                    foreach (Canvas canvas in root.GetComponentsInChildren<Canvas>(true))
                    {
                        if (seen.Add(canvas)) canvases.Add(canvas);
                    }
                }
            }
            return new HiddenVisuals(renderers, HideRenderers(renderers), canvases, HideBehaviours(canvases));
        }

        /// <summary>按 <see cref="HideVisuals"/> 的快照恢复；null 快照、演出期间被销毁的组件都跳过。</summary>
        public static void RestoreVisuals(HiddenVisuals hidden)
        {
            if (hidden == null) return;
            RestoreRenderers(hidden.Renderers, hidden.RendererStates);
            RestoreBehaviours(hidden.Canvases, hidden.CanvasStates);
        }

        /// <summary>
        /// 记下每个渲染器当前的 enabled 并全部关掉；返回与入参等长的原值数组（交给 <see cref="RestoreRenderers"/>）。
        /// 入参为 null 返回空数组；数组里已销毁 / 为 null 的项记 false 并跳过。
        /// </summary>
        public static bool[] HideRenderers(IReadOnlyList<Renderer> renderers)
        {
            if (renderers == null) return new bool[0];
            var states = new bool[renderers.Count];
            for (int i = 0; i < renderers.Count; i++)
            {
                Renderer renderer = renderers[i];
                // Renderer 是 UnityEngine.Object，判空只用 == null。
                if (renderer == null) continue;
                states[i] = renderer.enabled;
                renderer.enabled = false;
            }
            return states;
        }

        /// <summary>
        /// 同 <see cref="HideRenderers"/>，给不是 Renderer 的可见组件用（触发者头顶名牌这类世界空间 Canvas）；
        /// 返回原 enabled 值，交给 <see cref="RestoreBehaviours"/>。
        /// </summary>
        public static bool[] HideBehaviours(IReadOnlyList<Behaviour> behaviours)
        {
            if (behaviours == null) return new bool[0];
            var states = new bool[behaviours.Count];
            for (int i = 0; i < behaviours.Count; i++)
            {
                Behaviour behaviour = behaviours[i];
                if (behaviour == null) continue;
                states[i] = behaviour.enabled;
                behaviour.enabled = false;
            }
            return states;
        }

        /// <summary>按 <see cref="HideBehaviours"/> 返回的原值恢复 enabled；已销毁的跳过。</summary>
        public static void RestoreBehaviours(IReadOnlyList<Behaviour> behaviours, bool[] states)
        {
            if (behaviours == null || states == null) return;
            int count = behaviours.Count < states.Length ? behaviours.Count : states.Length;
            for (int i = 0; i < count; i++)
            {
                Behaviour behaviour = behaviours[i];
                if (behaviour == null) continue;
                behaviour.enabled = states[i];
            }
        }

        /// <summary>按 <see cref="HideRenderers"/> 返回的原值恢复 enabled；演出期间被销毁的渲染器跳过。</summary>
        public static void RestoreRenderers(IReadOnlyList<Renderer> renderers, bool[] states)
        {
            if (renderers == null || states == null) return;
            int count = renderers.Count < states.Length ? renderers.Count : states.Length;
            for (int i = 0; i < count; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null) continue;
                renderer.enabled = states[i];
            }
        }

        /// <summary><see cref="HideVisuals"/> 的快照：被隐藏的组件与它们的原 enabled 值，交给 <see cref="RestoreVisuals"/>。</summary>
        public sealed class HiddenVisuals
        {
            internal HiddenVisuals(IReadOnlyList<Renderer> renderers, bool[] rendererStates,
                IReadOnlyList<Behaviour> canvases, bool[] canvasStates)
            {
                Renderers = renderers;
                RendererStates = rendererStates;
                Canvases = canvases;
                CanvasStates = canvasStates;
            }

            public IReadOnlyList<Renderer> Renderers { get; }
            public bool[] RendererStates { get; }
            public IReadOnlyList<Behaviour> Canvases { get; }
            public bool[] CanvasStates { get; }
        }
    }
}
