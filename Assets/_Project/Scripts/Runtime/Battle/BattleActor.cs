// 职责：战斗舞台上的一个角色（玩家 / BOSS）——站位、外观挂载、出招位移、受击闪白与后仰、饮酒 / 醉晃、倒地。
//   全部走不受缩放的时间（PRP D5 / §7 坑 3：战斗全程 timeScale = 0）。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：ChibiPuppet / ChibiPuppetMotion 只会「看位移演待机 / 走 / 跑」，不产生位移、没有受击与倒地；
//      EncounterSceneView 是巡逻怪遭遇的逻辑投影视图，且 Runtime/Monster 禁改。
//   2. 扩展不行：往 CharacterPuppet 里加出招位移会让「只是一层皮」的小人认识战斗节奏（characterpuppet guide「职责边界」）。
// 与小人的协作：挂上外观后关掉其 ChibiPuppetMotion（它在 timeScale = 0 时每帧把小人强制写回待机），由本组件直接调
//   ChibiPuppet.SetMoving / SetFacing——「同一小人只允许一个驱动者写参数」（characterpuppet guide 已知约束）。
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.CharacterPuppet;
using Game.Core.Simulation;
using LitMotion;
using LitMotion.Extensions;
using UnityEngine;

namespace Game.Battle
{
    /// <summary>舞台角色。根节点放在脚底（小人原点在脚底），旋转倒地绕脚底转。</summary>
    [DisallowMultipleComponent]
    public sealed class BattleActor : MonoBehaviour
    {
        /// <summary>奔跑演出时写给小人的播放速率（同 ChibiPuppetMotionRules 的夹取上限）。</summary>
        private const float DashPlaybackRate = 1.6f;

        /// <summary>圆周率（GameMath 没有常量，表现层自备一份）。</summary>
        private const float Pi = 3.14159265f;

        /// <summary>醉晃一个来回的周期数。</summary>
        private const float SwayCycles = 2f;

        /// <summary>闪白至少保持的渲染帧数。</summary>
        private const int FlashMinFrames = 2;

        /// <summary>倒地转到的角度（度，不转满 90° 免得纸片侧过去变成一条线）。</summary>
        private const float FallAngle = 82f;

        [Tooltip("外观挂点：纸片小人放在它下面。玩家在场景里摆好；BOSS 开场时按 BossDefinition.StagePrefab 实例化到这里。")]
        [SerializeField] private Transform visualRoot;

        [Tooltip("头顶锚点：界面上的血条 / 状态字 / 晕眩标记 / 飘字跟着它。")]
        [SerializeField] private Transform head;

        [Tooltip("朝左（右侧的 BOSS 朝左）。")]
        [SerializeField] private bool faceLeft;

        [Tooltip("受击闪白材质（Art/Materials/Battle/M_SpriteFlash）。空 = 不闪白。")]
        [SerializeField] private Material flashMaterial;

        [Tooltip("倒地后的整体染色（乘在原色上）。")]
        [SerializeField] private Color downTint = new Color(0.55f, 0.55f, 0.6f, 1f);

        private SpriteRenderer[] renderers = Array.Empty<SpriteRenderer>();
        private Material[] baseMaterials = Array.Empty<Material>();
        private Color[] baseColors = Array.Empty<Color>();
        private ChibiPuppet puppet;
        private GameObject mounted;
        private Vector3 home;
        private Quaternion homeRotation = Quaternion.identity;
        private bool homeCaptured;
        private MotionHandle motion;

        /// <summary>头顶锚点（跟着本物体动：冲刺、后仰、跳、醉晃、倒地时都会移走）。</summary>
        public Transform Head => head;

        /// <summary>
        /// 站立时的头顶位置（世界坐标）：本物体在原位、摆正时头顶锚点所在处，与此刻的姿势无关。
        /// 界面的头顶条与飘字锚在这里——倒地绕脚底转 82° 时头顶锚点会横着甩出去，条跟着走就会滑出屏幕。
        /// </summary>
        public Vector3 StandingHeadPosition
        {
            get
            {
                CaptureHome();
                if (head == null) return home;
                // 锚点在本物体局部空间里的位置不随姿势变；按原位 + 原朝向摆回世界。
                Vector3 local = transform.InverseTransformPoint(head.position);
                return home + homeRotation * Vector3.Scale(local, transform.lossyScale);
            }
        }

        /// <summary>原位（世界坐标）。</summary>
        public Vector3 HomePosition
        {
            get
            {
                CaptureHome();
                return home;
            }
        }

        /// <summary>朝左。</summary>
        public bool FaceLeft => faceLeft;

        /// <summary>当前挂着的外观里有几个纸片渲染器（诊断 / 测试用）。</summary>
        public int RendererCount => renderers.Length;

        private void Awake() => CaptureHome();

        private void OnDisable()
        {
            CancelMotion();
            SetFlash(false);
        }

        /// <summary>
        /// 挂外观：<paramref name="prefab"/> 非空 → 换掉上次挂的、实例化到挂点；为空 → 沿用挂点下现有的子物体（玩家在场景里摆好）。
        /// 之后回到原位、恢复原色。
        /// </summary>
        public void Mount(GameObject prefab)
        {
            CaptureHome();
            if (visualRoot == null) visualRoot = transform;
            if (prefab != null)
            {
                if (mounted != null) Destroy(mounted);
                mounted = Instantiate(prefab, visualRoot, false);
            }

            BindVisual();
            ResetPose();
        }

        /// <summary>回原位、摆正、恢复原材质与原色，小人回待机。</summary>
        public void ResetPose()
        {
            CancelMotion();
            SetFlash(false);
            transform.SetPositionAndRotation(HomePosition, homeRotation);
            Tint(Color.white);
            if (puppet != null)
            {
                puppet.SetFacing(faceLeft);
                puppet.SetMoving(false, false, 1f);
            }
        }

        /// <summary>移到 <paramref name="target"/>（世界坐标）。<paramref name="running"/> 为真时小人演奔跑。</summary>
        public async UniTask MoveToAsync(Vector3 target, float seconds, bool running, CancellationToken ct)
        {
            CancelMotion();
            SetMoving(running);
            try
            {
                motion = LMotion.Create(transform.position, target, GameMath.Max(0.01f, seconds))
                    .WithEase(Ease.OutCubic)
                    .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                    .BindToPosition(transform)
                    .AddTo(gameObject);
                await motion.ToUniTask(CancelBehavior.Cancel, false, ct);
            }
            finally
            {
                SetMoving(false);
            }
        }

        /// <summary>退回原位（07:50「攻击结束退后到原本位置」）。</summary>
        public UniTask ReturnHomeAsync(float seconds, CancellationToken ct) => MoveToAsync(HomePosition, seconds, true, ct);

        /// <summary>加重版出招前的蓄力：往自己身后撤一小步。</summary>
        public UniTask WindUpAsync(float distance, float seconds, CancellationToken ct) =>
            MoveToAsync(HomePosition + Vector3.right * (faceLeft ? distance : -distance), seconds, false, ct);

        /// <summary>
        /// 受击闪白：整片换成闪白材质至少 <paramref name="seconds"/> 秒、且至少渲染 <see cref="FlashMinFrames"/> 帧，再换回原材质。
        /// 只按秒等的话，帧率低（编辑器失焦、卡顿）时一帧就超过闪白时长，换上去的材质还没画出来就被换回，闪白整个看不见。
        /// </summary>
        public async UniTask FlashAsync(float seconds, CancellationToken ct)
        {
            if (flashMaterial == null) return;
            SetFlash(true);
            try
            {
                await UniTask.WhenAll(
                    UniTask.Delay(TimeSpan.FromSeconds(seconds), true, PlayerLoopTiming.Update, ct),
                    UniTask.DelayFrame(FlashMinFrames, PlayerLoopTiming.Update, ct));
            }
            finally
            {
                SetFlash(false);
            }
        }

        /// <summary>受击后仰：朝背后被推开 <paramref name="distance"/> 再弹回来。</summary>
        public async UniTask RecoilAsync(float distance, float seconds, CancellationToken ct)
        {
            Vector3 from = transform.position;
            Vector3 push = Vector3.right * (faceLeft ? distance : -distance);
            CancelMotion();
            motion = LMotion.Create(0f, 1f, GameMath.Max(0.01f, seconds))
                .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                .Bind(t => transform.position = from + push * GameMath.Sin(t * Pi))
                .AddTo(gameObject);
            await motion.ToUniTask(CancelBehavior.Cancel, false, ct);
            transform.position = from;
        }

        /// <summary>醉晃（BOSS 跳过回合）：绕脚底左右摇 <paramref name="angle"/> 度，结束摆正。</summary>
        public async UniTask SwayAsync(float angle, float seconds, CancellationToken ct)
        {
            CancelMotion();
            try
            {
                motion = LMotion.Create(0f, 1f, GameMath.Max(0.01f, seconds))
                    .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                    .Bind(t => transform.rotation = Quaternion.Euler(0f, 0f, angle * GameMath.Sin(t * SwayCycles * 2f * Pi) * (1f - t)))
                    .AddTo(gameObject);
                await motion.ToUniTask(CancelBehavior.Cancel, false, ct);
            }
            finally
            {
                transform.rotation = homeRotation;
            }
        }

        /// <summary>原地一跳（饮酒 / 用道具）。</summary>
        public async UniTask HopAsync(float height, float seconds, CancellationToken ct)
        {
            Vector3 from = transform.position;
            CancelMotion();
            try
            {
                motion = LMotion.Create(0f, 1f, GameMath.Max(0.01f, seconds))
                    .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                    .Bind(t => transform.position = from + Vector3.up * (height * GameMath.Sin(t * Pi)))
                    .AddTo(gameObject);
                await motion.ToUniTask(CancelBehavior.Cancel, false, ct);
            }
            finally
            {
                transform.position = from;
            }
        }

        /// <summary>倒地（07「胜负：倒地」）：绕脚底朝背后倒下并压暗，停在倒地姿势（场景随后卸载）。</summary>
        public async UniTask FallAsync(float seconds, CancellationToken ct)
        {
            float target = faceLeft ? -FallAngle : FallAngle;
            CancelMotion();
            motion = LMotion.Create(0f, target, GameMath.Max(0.01f, seconds))
                .WithEase(Ease.InQuad)
                .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                .Bind(a => transform.rotation = Quaternion.Euler(0f, 0f, a))
                .AddTo(gameObject);
            await motion.ToUniTask(CancelBehavior.Cancel, false, ct);
            Tint(downTint);
        }

        private void CaptureHome()
        {
            if (homeCaptured) return;
            home = transform.position;
            homeRotation = transform.rotation;
            homeCaptured = true;
        }

        // 只在挂外观时跑一次，不在每帧路径上。
        private void BindVisual()
        {
            renderers = visualRoot.GetComponentsInChildren<SpriteRenderer>(true);
            baseMaterials = new Material[renderers.Length];
            baseColors = new Color[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                baseMaterials[i] = renderers[i].sharedMaterial;
                baseColors[i] = renderers[i].color;
            }

            foreach (ChibiPuppetMotion driver in visualRoot.GetComponentsInChildren<ChibiPuppetMotion>(true))
                driver.enabled = false;
            puppet = visualRoot.GetComponentInChildren<ChibiPuppet>(true);
        }

        private void SetMoving(bool running)
        {
            if (puppet != null) puppet.SetMoving(running, running, running ? DashPlaybackRate : 1f);
        }

        private void SetFlash(bool on)
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null) continue;
                renderers[i].sharedMaterial = on && flashMaterial != null ? flashMaterial : baseMaterials[i];
            }
        }

        private void Tint(Color tint)
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null) renderers[i].color = baseColors[i] * tint;
            }
        }

        private void CancelMotion()
        {
            if (motion.IsActive()) motion.Cancel();
        }
    }
}
