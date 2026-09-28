// 职责：通灵视呈现——遭遇活动且玩家逻辑位置落在「条件满足」的区域里时，给半径内的妖挂影子提示（对象池、世界空间 SpriteRenderer、朝向相机），
//   离开区域即全部收起；生效状态变化时埋 spirit_sight_changed。提示不带任何名字。沉浸模式下同样显示（这是世界里的「看见」，不是 HUD）。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：SupplyCrateMarker / DialogueInteractableMarker / QuestTargetMarker 各绑自己模块的对象与状态，且跨模块共用同一张标记图会撞脸（pitfalls）。
//   2. 扩展不行：塞进 MirrorCrackPresenter 会把「被击中」与「察觉有妖」两件事绑在一起；放进绑定器会让事件驱动的登记变成每帧入口点。
//   朝向相机在渲染前对齐，不挂 IsometricExploration 的 CameraBillboard：PRP 2.1 的 Mirror 依赖清单里没有探索模块，
//   且 Loot 的标记同样刻意不引用它（SupplyCrateMarker 文件头）。
using System;
using System.Collections.Generic;
using Game.Core.Telemetry;
using Game.Monster;
using Game.Player;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using VContainer.Unity;

namespace Game.Mirror
{
    /// <summary>
    /// 通灵视入口点。提示对象挂在一个池根下，池根放进标记所在的场景：场景卸载时随之销毁，重进场景时按需重建（不会留下指向旧相机的提示）。
    /// 每帧只做区域判定、距离比较与赋值；池只在需要更多提示时扩容，稳定后无分配。
    /// </summary>
    public sealed class SpiritSightPresenter : ITickable, IDisposable
    {
        private const string PoolRootName = "SpiritSightHints";

        private readonly MirrorSceneBinder binder;
        private readonly MirrorConfig config;
        private readonly EncounterStep step;
        private readonly PlayerModel player;
        private readonly ITelemetryScope telemetry;
        private readonly List<SpriteRenderer> pool = new List<SpriteRenderer>();

        private GameObject poolRoot;
        private int shownCount;
        private bool lastActive;
        private SpiritSightZone lastZone;
        private bool disposed;

        public SpiritSightPresenter(MirrorSceneBinder binder, MirrorConfig config, EncounterStep step, PlayerModel player,
            ITelemetryScope telemetry)
        {
            this.binder = binder ?? throw new ArgumentNullException(nameof(binder));
            // MirrorConfig 是 ScriptableObject，判空只用 == null。
            if (config == null) throw new ArgumentNullException(nameof(config));
            this.config = config;
            this.step = step ?? throw new ArgumentNullException(nameof(step));
            this.player = player ?? throw new ArgumentNullException(nameof(player));
            this.telemetry = telemetry ?? NullTelemetryScope.Instance;
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        }

        /// <summary>通灵视当前是否生效。</summary>
        public bool IsActive => lastActive;

        /// <summary>当前显示中的影子提示数。</summary>
        public int ShownCount => shownCount;

        /// <summary>当前生效的通灵视区域；未生效时为 null（用 == null 判）。</summary>
        public SpiritSightZone ActiveZone => lastActive ? lastZone : null;

        /// <summary>影子提示的池根（提示都挂在它下面）；还没建出来、或已随场景卸载时为 null（用 == null 判）。</summary>
        public Transform HintRoot => poolRoot == null ? null : poolRoot.transform;

        public void Tick()
        {
            if (disposed) return;
            // 池根随场景销毁（伪空）：池里的提示也一起没了，清掉引用，下次按需重建。
            if (poolRoot == null && pool.Count > 0)
            {
                pool.Clear();
                shownCount = 0;
            }

            Vector2 playerPosition = player.Position;
            SpiritSightZone zone = step.IsActive ? binder.FindActiveZone(playerPosition) : null;
            bool active = zone != null;
            if (active != lastActive || !ReferenceEquals(zone, lastZone))
            {
                lastActive = active;
                lastZone = zone;
                telemetry.Track("spirit_sight_changed", ("active", active), ("zone", active ? zone.name : string.Empty));
            }

            int used = 0;
            Sprite sprite = config.SpiritShadowSprite;
            // Sprite 是 UnityEngine.Object，判空只用 == null / != null。
            if (active && sprite != null)
            {
                float radius = config.SightRadius;
                IReadOnlyList<MirrorSubject> subjects = binder.Subjects;
                for (int i = 0; i < subjects.Count; i++)
                {
                    MirrorSubject subject = subjects[i];
                    if (subject == null || !subject.isActiveAndEnabled) continue;
                    if (!SpiritSightRules.Visible(subject.Kind, binder.LogicPositionOf(subject), playerPosition, radius)) continue;
                    SpriteRenderer hint = Acquire(used, subject, sprite);
                    if (hint == null) break;
                    hint.transform.position = subject.HintAnchor.position + Vector3.up * config.SpiritHintLift;
                    used++;
                }
            }

            for (int i = used; i < shownCount && i < pool.Count; i++)
            {
                if (pool[i] != null) pool[i].gameObject.SetActive(false);
            }
            shownCount = used;
        }

        // URP 在渲染前给出当前相机：跟随已完成，也不缓存会被禁用、换标签或卸载的旧主相机。
        private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (disposed || shownCount == 0 || camera == null || !camera.isActiveAndEnabled ||
                camera.cameraType != CameraType.Game || !camera.CompareTag("MainCamera")) return;
            Quaternion rotation = camera.transform.rotation;
            for (int i = 0; i < shownCount && i < pool.Count; i++)
            {
                if (pool[i] != null) pool[i].transform.rotation = rotation;
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            if (poolRoot != null) UnityEngine.Object.Destroy(poolRoot);
            poolRoot = null;
            pool.Clear();
            shownCount = 0;
        }

        // 取第 index 个提示；池不够时扩容（只在出现更多妖时发生一次）。池根建在标记所在的场景里。
        private SpriteRenderer Acquire(int index, MirrorSubject owner, Sprite sprite)
        {
            if (poolRoot == null)
            {
                Scene scene = owner.gameObject.scene;
                if (!scene.IsValid()) return null;
                poolRoot = new GameObject(PoolRootName);
                SceneManager.MoveGameObjectToScene(poolRoot, scene);
                pool.Clear();
            }

            while (pool.Count <= index)
            {
                var go = new GameObject("SpiritShadowHint");
                go.transform.SetParent(poolRoot.transform, false);
                SpriteRenderer created = go.AddComponent<SpriteRenderer>();
                go.SetActive(false);
                pool.Add(created);
            }

            SpriteRenderer hint = pool[index];
            if (hint == null) return null;
            if (hint.sprite != sprite) hint.sprite = sprite;
            hint.sortingOrder = config.SpiritHintSortingOrder;
            float scale = config.SpiritHintScale;
            hint.transform.localScale = new Vector3(scale, scale, scale);
            if (!hint.gameObject.activeSelf) hint.gameObject.SetActive(true);
            return hint;
        }
    }
}
