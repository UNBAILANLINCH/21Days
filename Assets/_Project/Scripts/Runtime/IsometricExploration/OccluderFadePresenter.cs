// 职责：每帧从场景相机向玩家胸口扫一根粗射线（球形扫掠），命中的 SceneOccluder（桥、甲板、塔、楼梯、墙等）变半透明，离开视线后恢复。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：现有入口点（HUD / 控件 / 万向标）都是 UI 呈现，没有场景几何的遮挡处理；
//   2. 扩展不行：塞进 ExplorationCompassPresenter 会让万向标同时管场景材质切换，职责说不通。
// 纯表现：扫掠结果只决定材质引用与颜色，不回写任何玩法状态。
using System;
using System.Collections.Generic;
using Game.Quest;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer.Unity;

namespace Game.IsometricExploration
{
    /// <summary>
    /// 遮挡半透明入口点（PRP/exploration-whitebox 波 9 建、波 10 改粗射线 + 过渡）。相机与玩家锚点取 <see cref="QuestSceneBinder"/>（同万向标）。
    /// 探测：物理球形扫掠（SphereCastNonAlloc，纯表现），半径取 <see cref="IsometricExplorationConfig.OccluderProbeRadius"/>，
    /// 终点停在胸口前一个半径处（见 <see cref="TryBuildProbe"/>），不把玩家脚下地面扫进来；地面不挂 SceneOccluder，扫到也只缓存成「没挂」。
    /// 每帧无分配：扫掠结果进预分配数组，Collider → SceneOccluder 的查找结果（含「没挂」）按 Collider 缓存；
    /// 只有「正在过渡」的遮挡物进 <c>animating</c> 列表逐帧插值，静止时零开销；场景卸载时清缓存，被销毁的遮挡物按 Unity 伪空跳过。
    /// </summary>
    public sealed class OccluderFadePresenter : ITickable, IDisposable
    {
        private const int MaxHits = 16;
        private const float TargetHeight = 0.8f;
        private const string OccluderLayer = "Ground";

        private readonly QuestSceneBinder binder;
        private readonly IsometricExplorationConfig config;
        private readonly RaycastHit[] hits = new RaycastHit[MaxHits];
        private readonly Dictionary<Collider, SceneOccluder> lookup = new Dictionary<Collider, SceneOccluder>();
        private readonly List<SceneOccluder> animating = new List<SceneOccluder>(MaxHits);
        private List<SceneOccluder> fadedLast = new List<SceneOccluder>(MaxHits);
        private List<SceneOccluder> fadedNow = new List<SceneOccluder>(MaxHits);
        private readonly int mask;
        private bool disposed;

        public OccluderFadePresenter(QuestSceneBinder binder, IsometricExplorationConfig config)
        {
            this.binder = binder ?? throw new ArgumentNullException(nameof(binder));
            // ScriptableObject 是 UnityEngine.Object，判空只用 == null。
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            this.config = config;
            mask = LayerMask.GetMask(OccluderLayer);
            SceneManager.sceneUnloaded += OnSceneUnloaded;
        }

        /// <summary>当前处于半透明（目标状态）的遮挡物个数（回放 / 调试读取）。</summary>
        public int FadedCount => fadedLast.Count;

        /// <summary>
        /// 纯规则：由相机位置、目标点与探测半径算出球形扫掠的方向与距离。距离 = 相机到目标的长度 − 半径，
        /// 让球停在目标前一个半径处，不扫到目标脚下的地面。距离 ≤ 0（相机贴着目标）时返回 false。
        /// </summary>
        public static bool TryBuildProbe(Vector3 origin, Vector3 target, float radius, out Vector3 direction, out float distance)
        {
            Vector3 toTarget = target - origin;
            float length = toTarget.magnitude;
            distance = length - (radius > 0f ? radius : 0f);
            if (length <= 0f || distance <= 0f)
            {
                direction = Vector3.zero;
                distance = 0f;
                return false;
            }

            direction = toTarget / length;
            return true;
        }

        public void Tick()
        {
            if (disposed)
            {
                return;
            }

            Camera camera = binder.SceneCamera;
            Transform anchor = binder.PlayerAnchor;
            fadedNow.Clear();
            if (camera != null && anchor != null && mask != 0)
            {
                Vector3 origin = camera.transform.position;
                float radius = config.OccluderProbeRadius;
                if (TryBuildProbe(origin, anchor.position + Vector3.up * TargetHeight, radius, out Vector3 direction, out float distance))
                {
                    int count = Physics.SphereCastNonAlloc(origin, radius, direction, hits, distance, mask, // lint-ok: 纯表现，只决定遮挡物材质，不参与判定
                        QueryTriggerInteraction.Ignore);
                    for (int i = 0; i < count; i++)
                    {
                        SceneOccluder occluder = Resolve(hits[i].collider);
                        if (occluder != null && !fadedNow.Contains(occluder))
                        {
                            Fade(occluder, true);
                            fadedNow.Add(occluder);
                        }
                    }
                }
            }

            for (int i = 0; i < fadedLast.Count; i++)
            {
                SceneOccluder previous = fadedLast[i];
                if (previous != null && !fadedNow.Contains(previous))
                {
                    Fade(previous, false);
                }
            }

            List<SceneOccluder> swap = fadedLast;
            fadedLast = fadedNow;
            fadedNow = swap;

            // 只推进正在过渡的遮挡物；到终态的移出列表。
            float deltaTime = Time.unscaledDeltaTime;
            for (int i = animating.Count - 1; i >= 0; i--)
            {
                SceneOccluder occluder = animating[i];
                if (occluder == null || !occluder.Advance(deltaTime))
                {
                    animating.RemoveAt(i);
                }
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            for (int i = 0; i < fadedLast.Count; i++)
            {
                if (fadedLast[i] != null)
                {
                    fadedLast[i].SetFaded(false);
                    fadedLast[i].Advance(float.MaxValue);
                }
            }

            fadedLast.Clear();
            animating.Clear();
            lookup.Clear();
        }

        private void Fade(SceneOccluder occluder, bool faded)
        {
            occluder.SetFaded(faded);
            if (occluder.IsTransitioning && !animating.Contains(occluder))
            {
                animating.Add(occluder);
            }
        }

        // 缓存未命中时才取组件；没挂 SceneOccluder 的碰撞体（地面、围栏等）也缓存为 null，之后不再查。
        private SceneOccluder Resolve(Collider collider)
        {
            if (collider == null)
            {
                return null;
            }

            if (!lookup.TryGetValue(collider, out SceneOccluder occluder))
            {
                occluder = collider.GetComponent<SceneOccluder>();
                lookup.Add(collider, occluder);
            }

            return occluder;
        }

        private void OnSceneUnloaded(Scene scene)
        {
            lookup.Clear();
            fadedLast.Clear();
            fadedNow.Clear();
            animating.Clear();
        }
    }
}
