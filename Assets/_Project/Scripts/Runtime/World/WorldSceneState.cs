// 职责：一个状态服务所有世界场景——进入时按「待处理转场」拿 Addressables 地址加载场景、选出生点把玩家摆好、
//   把场景作用域绑到该场景键；离开时解绑（不动存档里的进度）。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：MonsterEncounterState 的 SceneKey 是常量 "IsometricEncounter"（遗留原型路径），
//      它认的是遭遇场景，没有「按表切换场景」这一维；本 PRP 明确两条路并存，不改它。
//   2. 扩展不行：SceneGameState 是流程层基类（Core 不许认识玩法表）；把「读转场、查表、选出生点」写进基类
//      等于让框架层认识 world 表。
//   3. 也不做「一场景一状态类」（PRP §2.1 的核心决策）：十二阶段 × 两界会膨胀到两位数状态类，
//      而且每加一张图都要改代码 + 维护一张 scene_key → 类型 的映射表（第二份真相）。
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Assets;
using Game.Core.Flow;
using Game.Core.Save;
using Game.Core.Telemetry;
using Game.IsometricExploration;
using UnityEngine;

namespace Game.World
{
    /// <summary>
    /// 世界场景状态（根作用域单例）。数据驱动的场景键：地址来自 <see cref="IWorldTransition"/> 的待处理转场，
    /// 出生点来自 <see cref="WorldRules"/>，摆人交给 <see cref="ISpawnPlacement"/>。
    /// <para>
    /// <b>接线状态（本波）</b>：本状态已经注册进容器，但**还没有调用方**——<c>GoToAsync&lt;WorldSceneState&gt;()</c>
    /// 要等场景波把传送点 / 新开局那条路的调用补上（PRP §2.1 的流程图里那一步）。
    /// </para>
    /// </summary>
    public sealed class WorldSceneState : SceneGameState
    {
        private readonly IWorldTransition transition;
        private readonly WorldCatalog catalog;
        private readonly ISpawnPlacement placement;
        private readonly ISaveService saves;
        private readonly CameraConstraintPolicy cameraConstraints;
        private readonly ITelemetryScope telemetry;

        public WorldSceneState(IAssetService assets, IWorldTransition transition, WorldCatalog catalog,
            ISpawnPlacement placement, ISaveService saves, CameraConstraintPolicy cameraConstraints = null,
            ITelemetryScope telemetry = null) : base(assets)
        {
            // 基类不查 assets（SceneGameState 只是把它收下），但本状态的整条加载路径都要用它，
            // 所以在这里就炸：依赖为空要在构造处暴露，而不是等进场景时抛一个看不懂的空引用。
            if (assets == null)
            {
                throw new ArgumentNullException(nameof(assets));
            }

            this.transition = transition ?? throw new ArgumentNullException(nameof(transition));
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            this.placement = placement ?? throw new ArgumentNullException(nameof(placement));
            this.saves = saves ?? throw new ArgumentNullException(nameof(saves));
            this.cameraConstraints = cameraConstraints;
            this.telemetry = telemetry ?? NullTelemetryScope.Instance;
        }

        /// <summary>
        /// 本状态要加载的 Addressables 地址（就是基类的 <see cref="SceneGameState.SceneKey"/>）。
        /// <b>公开是为了能在 EditMode 里钉住它</b>：基类的成员是 protected，测试够不到，
        /// 而「按待处理转场取地址 / 没有转场就报错」正是本波最要钉死的一条（没有它就会悄悄进错图）。
        /// </summary>
        public string SceneAddress => SceneKey;

        /// <summary>本次进入绑定的场景作用域（箱子 / 怪物状态按场景隔离）；未进入或已卸载时为 null。</summary>
        public SceneStateScope Scope { get; private set; }

        /// <summary>本次进入实际用的落点；未进入时为 null。</summary>
        public WorldSpawnTarget Spawn { get; private set; }

        /// <summary>
        /// 本状态的场景地址。<b>不是常量</b>：读待处理转场里目标场景的 <c>scene_address</c>。
        /// <para>
        /// 为什么必须在这里读：基类的 <c>EnterAsync</c> 在 <c>OnSceneReadyAsync</c> **之前**就要用这个地址加载场景
        /// （<c>SceneGameState.cs:41/56</c>），所以地址必须在进入之前就确定。
        /// </para>
        /// <para>
        /// <b>没有待处理转场时直接抛</b>（PRP §2.1 二选一里选「报错」）：悄悄进一张默认图，会让「本来要去妖界
        /// 却站在人间」表现成一张正常的画面——查起来比崩贵得多。而「没有目标却要进世界场景」本身就是接线错误，
        /// 抛出的消息里说清了该往哪儿写目标。
        /// </para>
        /// </summary>
        protected override string SceneKey
        {
            get
            {
                WorldTransitionResolution pending = transition.Peek();
                if (!pending.Success)
                {
                    throw new WorldResolveException(
                        $"WorldSceneState 拿不到场景地址：{pending.Error}（本状态不以常量地址兜底，见本属性注释）");
                }

                return catalog.SceneAddressOf(pending.Request.SceneKey);
            }
        }

        /// <summary>
        /// 进入这张图的四步：① 取并消费待处理转场；② 用 <see cref="WorldRules"/> 选出生点（失败是正常分支）；
        /// ③ 把落点交给 <see cref="ISpawnPlacement"/> 摆人；④ 把场景作用域绑到该场景键。
        /// <para>
        /// <b>为什么是 public</b>：EditMode 里加载不了场景，<c>OnSceneReadyAsync</c> 跑不到，
        /// 但「消费了没有、选了哪个落点、失败在哪一步」必须能测。所以把这段与 Unity 无关的判定与落地单独开出来，
        /// <c>OnSceneReadyAsync</c> 只剩「调它 + 把相机约束策略推进场景相机」。
        /// </para>
        /// </summary>
        public WorldSceneEntry EnterScene()
        {
            // 先清掉上一次的绑定：失败重进时不能留着上一张图的作用域（那会让箱子状态串图）。
            Scope = null;
            Spawn = null;

            WorldSceneEntry entry = ResolveEntry();
            if (!entry.Success)
            {
                return entry;
            }

            // 摆人这一步由场景侧实现，没摆成是正常分支（本波默认实现一定返回 false，见 UnwiredSpawnPlacement）。
            // 失败也把**已经解析出来的落点**带回去：调用方要能报出「本该摆到哪、结果没摆成」。
            if (!placement.TryPlace(entry.Spawn, entry.Request, out string reason))
            {
                telemetry.TrackWarn("resolve_failed", TelemetryProps.Of(
                    ("reason", WorldSceneEntryFailure.PlacementFailed.ToString()),
                    ("scene", entry.Spawn.SceneKey)));
                return WorldSceneEntry.Failed(WorldSceneEntryFailure.PlacementFailed, entry.Request, reason, entry.Spawn);
            }

            // 场景作用域绑定：箱子 / 已死怪按「场景键::实体标识」各记一份。
            // **只绑视图，不读也不清 WorldSaveData 里的进度**——进度归存档，进出场景不改它。
            Spawn = entry.Spawn;
            Scope = new SceneStateScope(saves, entry.Spawn.SceneKey);

            telemetry.Track("scene_entered",
                ("scene", entry.Spawn.SceneKey),
                ("arrival", entry.Request.ArrivalMethod),
                ("spawn", entry.Spawn.SpawnId),
                ("fallback", entry.Spawn.UsedFallback));
            return entry;
        }

        private WorldSceneEntry ResolveEntry()
        {
            WorldTransitionResolution consumed = transition.TryConsume();
            if (!consumed.Success)
            {
                telemetry.TrackWarn("resolve_failed", TelemetryProps.Of(
                    ("reason", consumed.Failure.ToString()),
                    ("scene", consumed.Request == null ? string.Empty : consumed.Request.SceneKey)));
                return WorldSceneEntry.Failed(MapFailure(consumed.Failure), consumed.Request, consumed.Error);
            }

            // 出生点解析失败是**正常分支**（表里没写默认出生点、指名的出生点不存在），用不抛异常的入口。
            // 这里不用 WorldRules.Resolve 的抛异常版本：抛出去就没人给「失败原因」分类了，
            // 而调用方要对失败原因做分支（报错文案、埋点、以及「这张图先不进」）。
            SpawnResolution spawn = WorldRules.TryResolveSpawn(catalog, consumed.Request.SceneKey,
                consumed.Request.SpawnId, consumed.Request.ArrivalMethod);
            if (!spawn.Success)
            {
                telemetry.TrackWarn("resolve_failed", TelemetryProps.Of(
                    ("reason", WorldSceneEntryFailure.SpawnUnresolved.ToString()),
                    ("scene", consumed.Request.SceneKey)));
                return WorldSceneEntry.Failed(WorldSceneEntryFailure.SpawnUnresolved, consumed.Request, spawn.Error);
            }

            return WorldSceneEntry.Resolved(consumed.Request, spawn.Target);
        }

        protected override UniTask OnSceneReadyAsync(CancellationToken ct)
        {
            WorldSceneEntry entry = EnterScene();
            if (!entry.Success)
            {
                // 抛出去，不静默继续：场景已经加载完，但没有可用的落点，继续跑只会得到「画面正常、人不在场上」。
                // 基类的 EnterAsync 会接住异常并 ExitAsync 把刚加载的场景卸掉（SceneGameState.EnterAsync 的 catch），
                // 所以这条路上不会漏一张挂在内存里的场景。
                string message = $"进入世界场景失败：{entry.Error}";
                throw entry.Failure == WorldSceneEntryFailure.PlacementFailed
                    ? new InvalidOperationException(message)
                    : new WorldResolveException(message);
            }

            // 「相机对准」的框架那一半（边界由相机自己在 Inspector 上摆）。
            BindCameraConstraints();
            return UniTask.CompletedTask;
        }

        protected override UniTask OnSceneUnloadingAsync(CancellationToken ct)
        {
            // 只解绑视图：WorldSaveData 里的进度不动（那是存档，不是场景的运行期状态）。
            // 解绑之后谁再拿 Scope 用会拿到 null —— 那正是「这张图已经不在了」的显式表达。
            Scope = null;
            Spawn = null;
            return UniTask.CompletedTask;
        }

        /// <summary>
        /// 转场失败 → 进入失败的档位映射。两份档位分开是为了各自表达自己的阶段；
        /// 这一层只区分「压根没有目标」与「目标不可加载」，因为调用方对这两者的处置不同
        /// （前者是接线漏了，后者是内容没做好）。
        /// </summary>
        private static WorldSceneEntryFailure MapFailure(WorldTransitionFailure failure) =>
            failure == WorldTransitionFailure.NoPending
                ? WorldSceneEntryFailure.NoPendingTransition
                : WorldSceneEntryFailure.SceneNotLoadable;

        /// <summary>
        /// 「相机对准」的框架那一半：把容器里那份**共享**的边界策略推进本场景的跟随相机
        /// （边界值本身由相机在 Inspector 上摆，见 <see cref="CameraConstraintPolicy"/>）。
        /// 场景里没有相机、或相机没挂 <see cref="SmoothCameraFollow"/> 时静默跳过——
        /// 本波还没有世界场景，这不是错误。
        /// </summary>
        private void BindCameraConstraints()
        {
            if (cameraConstraints == null)
            {
                return;
            }

            SmoothCameraFollow follow = FindCameraFollow();
            if (follow == null)
            {
                return;
            }

            follow.BindConstraintPolicy(cameraConstraints);
        }

        // 场景加载完成后扫一次，不在每帧路径上（同 MonsterEncounterState 找 EncounterSceneView 的写法）。
        private SmoothCameraFollow FindCameraFollow()
        {
            GameObject[] roots = Scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                SmoothCameraFollow follow = roots[i].GetComponentInChildren<SmoothCameraFollow>(true);
                if (follow != null)
                {
                    return follow;
                }
            }

            return null;
        }
    }
}
