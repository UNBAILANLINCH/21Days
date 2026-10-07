// 职责：BattleArena 场景的舞台根——战斗相机、玩家 / BOSS 两个站位、默认 BOSS 外观，以及「出招冲上去 → 命中 → 退回」
//   「原地挨打」「震屏」这几段编排与节奏参数。全部走不受缩放的时间（PRP D5 / §7 坑 3）。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：PerformanceStage 是时间轴驱动的演出舞台（PlayableDirector + 演员名单），战斗是逐事件、节奏由规则结果决定的即时编排；
//      世界场景里没有任何「两个角色对打」的组件。
//   2. 扩展不行：塞进 PerformanceStage 会让演出认识战斗事件；BattleActor 只管一个角色，两人之间的相对位置与震屏放这一层。
// 舞台放在远离世界原点的偏移处（场景里根节点 y = −1000，PRP D4），不切 ActiveScene：雾 / 环境光吃世界场景的 RenderSettings，
//   靠相机离角色近（雾起点之内）和场景里自带的补光看清（PRP §7 坑 2）。
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Simulation;
using LitMotion;
using UnityEngine;

namespace Game.Battle
{
    /// <summary>战斗舞台（BattleArena 场景里唯一一个，由 BattleScenePresenter 在开场时找到）。</summary>
    [DisallowMultipleComponent]
    public sealed class BattleStage : MonoBehaviour
    {
        /// <summary>震屏的两路正弦频率（弧度 / 归一化时间）；取不成整数倍，免得横纵同步成一条斜线。</summary>
        private const float ShakeFrequencyX = 47f;
        private const float ShakeFrequencyY = 31f;

        [Header("引用")]
        [Tooltip("战斗相机（场景里默认关着，开场由相机交接打开；不打 MainCamera 标签、不挂 AudioListener）。")]
        [SerializeField] private Camera stageCamera;

        [Tooltip("相机的父节点，震屏只动它的局部位置。")]
        [SerializeField] private Transform cameraRig;

        [Tooltip("左侧玩家站位（外观在场景里摆好：Chibi_amiya）。")]
        [SerializeField] private BattleActor player;

        [Tooltip("右侧 BOSS 站位（外观开场时按 BossDefinition.StagePrefab 挂上）。")]
        [SerializeField] private BattleActor boss;

        [Tooltip("BossDefinition 没填舞台外观时用的占位外观（Prefabs/Battle/BattleBoss_Placeholder）。")]
        [SerializeField] private GameObject defaultBossPrefab;

        [Header("出招节奏（秒，不受时间缩放）")]
        [SerializeField, Min(0.01f)] private float dashSeconds = 0.16f;
        [SerializeField, Min(0.01f)] private float returnSeconds = 0.26f;
        [Tooltip("加重版（招式 3）出招前后撤蓄力的时长。")]
        [SerializeField, Min(0f)] private float windUpSeconds = 0.3f;
        [SerializeField, Min(0f)] private float windUpDistance = 0.45f;
        [Tooltip("命中后的顿帧。")]
        [SerializeField, Min(0f)] private float hitStopSeconds = 0.14f;
        [SerializeField, Min(0.01f)] private float flashSeconds = 0.1f;
        [Tooltip("冲到对手身前停多远（世界单位）。")]
        [SerializeField, Min(0f)] private float strikeGap = 1.1f;
        [Tooltip("加重版刺得更深：停得更近。")]
        [SerializeField, Min(0f)] private float heavyStrikeGap = 0.45f;

        [Header("受击 / 震屏")]
        [SerializeField, Min(0f)] private float recoilDistance = 0.22f;
        [SerializeField, Min(0f)] private float heavyRecoilDistance = 0.5f;
        [SerializeField, Min(0.01f)] private float recoilSeconds = 0.22f;
        [SerializeField, Min(0.01f)] private float shakeSeconds = 0.22f;
        [SerializeField, Min(0f)] private float shakeAmplitude = 0.05f;
        [SerializeField, Min(0f)] private float heavyShakeAmplitude = 0.16f;

        [Header("饮酒 / 醉晃 / 倒地")]
        [SerializeField, Min(0f)] private float hopHeight = 0.3f;
        [SerializeField, Min(0.01f)] private float hopSeconds = 0.45f;
        [SerializeField, Min(0f)] private float swayAngle = 9f;
        [SerializeField, Min(0.01f)] private float swaySeconds = 1.1f;
        [SerializeField, Min(0.01f)] private float fallSeconds = 0.5f;

        private Vector3 rigHome;
        private bool rigCaptured;
        private MotionHandle shake;

        /// <summary>战斗相机。</summary>
        public Camera Camera => stageCamera;

        /// <summary>玩家站位。</summary>
        public BattleActor Player => player;

        /// <summary>BOSS 站位。</summary>
        public BattleActor Boss => boss;

        /// <summary>醉晃时长（界面中央提示与它同步）。</summary>
        public float SwaySeconds => swaySeconds;

        private void OnDisable()
        {
            if (shake.IsActive()) shake.Cancel();
            ResetRig();
        }

        /// <summary>
        /// 开场：校验引用、关着战斗相机（等相机交接打开）、玩家沿用场景里的外观、BOSS 挂上 <paramref name="bossPrefab"/>
        /// （空则用默认占位外观）、两人回原位。引用没拖齐时抛 <see cref="InvalidOperationException"/>（带修法）。
        /// </summary>
        public void Prepare(GameObject bossPrefab)
        {
            if (stageCamera == null || player == null || boss == null || cameraRig == null)
            {
                throw new InvalidOperationException(
                    "BattleStage 引用没拖齐（Stage Camera / Camera Rig / Player / Boss）。修法：打开 Assets/_Project/Scenes/BattleArena.unity，" +
                    "在根节点 BattleStage 上补齐，或用菜单 21Days/战斗/重建战斗白盒 重建。");
            }

            if (!rigCaptured)
            {
                rigHome = cameraRig.localPosition;
                rigCaptured = true;
            }

            ResetRig();
            stageCamera.enabled = false;
            player.Mount(null);
            boss.Mount(bossPrefab != null ? bossPrefab : defaultBossPrefab);
        }

        /// <summary>
        /// 一次出招（07:50-54 / :79-82）：加重版先后撤蓄力 → 冲到对手身前 → <paramref name="onImpact"/>（界面掉血 / 飘字）
        /// 同时对手闪白、后仰、震屏 → 顿帧 → 退回原位。
        /// </summary>
        public async UniTask StrikeAsync(BattleActor attacker, BattleActor target, bool heavy, Action onImpact, CancellationToken ct)
        {
            if (heavy && windUpSeconds > 0f) await attacker.WindUpAsync(windUpDistance, windUpSeconds, ct);
            await attacker.MoveToAsync(StrikePoint(attacker, target, heavy), heavy ? dashSeconds * 1.25f : dashSeconds, true, ct);
            await ImpactAsync(target, heavy, onImpact, ct);
            if (hitStopSeconds > 0f) await UniTask.Delay(TimeSpan.FromSeconds(hitStopSeconds), true, PlayerLoopTiming.Update, ct);
            await attacker.ReturnHomeAsync(returnSeconds, ct);
        }

        /// <summary>原地挨一下（偷袭开战扣血）：闪白、后仰、震屏。</summary>
        public UniTask HitInPlaceAsync(BattleActor target, bool heavy, Action onImpact, CancellationToken ct) =>
            ImpactAsync(target, heavy, onImpact, ct);

        /// <summary>原地一跳（饮酒 / 用道具）。</summary>
        public UniTask HopAsync(BattleActor actor, CancellationToken ct) => actor.HopAsync(hopHeight, hopSeconds, ct);

        /// <summary>醉晃（BOSS 跳过回合 / 玩家晕眩跳过回合）。</summary>
        public UniTask SwayAsync(BattleActor actor, CancellationToken ct) => actor.SwayAsync(swayAngle, swaySeconds, ct);

        /// <summary>倒地。</summary>
        public UniTask FallAsync(BattleActor actor, CancellationToken ct) => actor.FallAsync(fallSeconds, ct);

        /// <summary>震屏：相机父节点按衰减的正弦抖动，结束归位。</summary>
        public async UniTask ShakeAsync(bool heavy, CancellationToken ct)
        {
            float amplitude = heavy ? heavyShakeAmplitude : shakeAmplitude;
            if (amplitude <= 0f || cameraRig == null) return;
            if (shake.IsActive()) shake.Cancel();
            try
            {
                shake = LMotion.Create(0f, 1f, shakeSeconds)
                    .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                    .Bind(t =>
                    {
                        float decay = amplitude * (1f - t);
                        cameraRig.localPosition = rigHome + new Vector3(
                            GameMath.Sin(t * ShakeFrequencyX) * decay, GameMath.Sin(t * ShakeFrequencyY) * decay * 0.6f, 0f);
                    })
                    .AddTo(gameObject);
                await shake.ToUniTask(CancelBehavior.Cancel, false, ct);
            }
            finally
            {
                ResetRig();
            }
        }

        /// <summary>
        /// 出招落点：停在对手朝向攻击方那一侧 <c>gap</c> 远处，与对手同高。
        /// 公开给测试与 W2b 回放核对「冲上去」的位置。
        /// </summary>
        public Vector3 StrikePoint(BattleActor attacker, BattleActor target, bool heavy)
        {
            Vector3 targetHome = target.HomePosition;
            float side = attacker.HomePosition.x <= targetHome.x ? -1f : 1f;
            return targetHome + Vector3.right * (side * (heavy ? heavyStrikeGap : strikeGap));
        }

        private async UniTask ImpactAsync(BattleActor target, bool heavy, Action onImpact, CancellationToken ct)
        {
            onImpact?.Invoke();
            await UniTask.WhenAll(
                target.FlashAsync(flashSeconds, ct),
                target.RecoilAsync(heavy ? heavyRecoilDistance : recoilDistance, recoilSeconds, ct),
                ShakeAsync(heavy, ct));
        }

        private void ResetRig()
        {
            if (rigCaptured && cameraRig != null) cameraRig.localPosition = rigHome;
        }
    }
}
