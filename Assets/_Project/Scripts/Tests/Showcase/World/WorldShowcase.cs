// 职责：世界场景流转（A4）的实例回放——走 Boot 真实流程进世界，从人间·泾阳走到出口、按交互键换到妖界·坊市，
//   再按一次走回来；每一步都对着**场景里的真锚点**与**容器里的真服务**判，不瞬移、不 new 规则。
// 舞台：两张真场景 Assets/_Project/Scenes/HumanJingyang.unity 与 YaoFangshi.unity，
//   由流程经 Addressables 按 TbScene.scene_address 加载（地址 = 场景键），不是基类自己加载的。
// 为什么新建：world 模块此前**没有 Showcase**（机制波交付时还没接线，见 world-module-guide.md 的「验证入口」），
//   按 docs/module-dev-spec.md §2 DoD 第 3 条不算做完。本波把「传送点 → 待处理转场 → 世界场景状态 → 出生点放置」
//   整条链接起来了，回放是唯一能一次验完「换图 + 落点 + 相机」的地方。
//
// 进场方式为什么不是点标题的「开始」：那条路当前路由到遭遇原型状态（MonsterEncounterState → SampleScene），
//   **新开局 / 读档恢复还没有接到世界场景**（world-module-guide.md「建议补丁」，本波只补了传送点这一半调用方）。
//   所以回放走的是「Boot 起来、流程停在标题，然后写一条待处理转场 + GoToAsync<WorldSceneState>()」——
//   与传送点触发后走的是同一条路，只是少了那一次触发。等新开局接上之后再改这一处即可，后面的步骤一行不用动。
//
// ⚠️ **不要按名字找世界场景**（2026-10-07 首轮实测踩到，13 条检查点全红）：
//   Addressables 按地址 `human_jingyang` 加载回来后，`Scene.name` 是**资产文件名** `HumanJingyang`，
//   地址只是地址。`SceneManager.GetSceneByName("human_jingyang")` 永远返回未加载的场景，
//   于是「场景没换」的假象把后面每一步都带红。
//   本文件改成像 `WorldSceneBinder` 一样**按结构认场景**：已加载场景里**含 `SpawnAnchor` 的那个**就是世界场景；
//   「当前场景键」直接读 `WorldSceneState.Spawn.SceneKey`（那是进入时从表里解析出来的真值，不靠文件名）。
//
// 三个机器可判的检查点（DoD）：① 当前场景键（`WorldSceneState.Spawn.SceneKey`）；② 玩家落点（逻辑坐标 = 锚点坐标）；
//   ③ 相机对准（`SmoothCameraFollow.Target` 是玩家根物体，且镜头已经跟到它附近，不是还在上一张图的位置）。
using System.Collections;
using Cysharp.Threading.Tasks;
using Game.Core.Flow;
using Game.Core.Input;
using Game.IsometricExploration;
using Game.Player;
using Game.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests.Showcase.World
{
    /// <summary>两界场景流转的实例回放（人间·泾阳 ⇄ 妖界·坊市）。</summary>
    [Category("Showcase")]
    public sealed class WorldShowcase : ShowcaseScenario
    {
        /// <summary>人间场景键（<c>TbScene.scene_key</c>，同时是 Addressables 地址与表的 scene_address）。</summary>
        private const string HumanScene = "human_jingyang";

        /// <summary>妖界场景键（同上）。</summary>
        private const string YaoScene = "yao_fangshi";

        /// <summary>进人间时指定的出生点：过渡平台（<c>TbScene.spawn_points</c> 里的一项）。
        /// 不落在默认出生点（街面）上，是因为世界的出口就摆在街面那个锚点上——从平台走过去才有「走传送点」这一段。</summary>
        private const string HumanEntrySpawn = "jingyang_platform";

        /// <summary>人间的出口锚点（<c>TbPortal.anchor_id</c> = 人间的默认出生点；也是从妖界回来时的 target_spawn_id）。</summary>
        private const string HumanPortalSpawn = "jingyang_street";

        /// <summary>妖界的出口锚点（<c>TbPortal.anchor_id</c> = 妖界的默认出生点）。</summary>
        private const string YaoPortalSpawn = "fangshi_street";

        /// <summary>落点判定容差（米）：放置是直接写坐标，容差只为浮点与投影留余量。</summary>
        private const float LandingTolerance = 0.05f;

        /// <summary>相机「跟到了」的判定容差（米）。</summary>
        private const float CameraTolerance = 0.5f;

        private IGameFlow flow;
        private IWorldTransition transition;
        private IInputService inputService;
        private PlayerModel player;

        protected override string Module => "World";

        /// <summary>两张世界场景都由流程加载（Addressables），基类不再叠加载一份。</summary>
        protected override string ScenePath => null;

        [UnityTest]
        public IEnumerator WalkThroughPortal_SwapsSceneAndLandsOnTheTargetSpawn()
        {
            yield return EnterWorld();
            yield return EnterHumanScene();
            yield return CheckHumanLanding();

            // —— 走过去：从过渡平台走到街面的出口上 ——
            Vector2 portal = PortalLogic();
            yield return Step("推摇杆走到人间的出口（街面锚点，TbPortal.anchor_id=jingyang_street）", null, 0f);
            yield return WalkTo(portal, 1.2f, 25f);
            yield return Check("玩家进了出口的触发半径（WorldSceneDriver 逐帧按距离维护 PlayerInRange）",
                () => PortalOf() != null && PortalOf().PlayerInRange, 3f);
            yield return Snapshot("走到人间出口");

            // —— 按交互键换图：TbPortal.trigger_kind=Interact，所以必须按一下 ——
            yield return Step("按交互键（Gameplay/Interact）——出口把转场交给 WorldSceneDriver，黑幕由 GameFlow 统一落/揭", null, 0f);
            yield return Input.Press(inputService.Actions.Gameplay.Interact);
            yield return WaitUntil($"换成妖界·坊市（场景键 {YaoScene}）", () => CurrentSceneKey() == YaoScene && SpawnOf(YaoPortalSpawn) != null, 25f);
            yield return WaitCurtainRevealed();
            yield return CheckYaoLanding();
            yield return Snapshot("妖界·坊市 落点");

            // —— 走回来：妖界的出口就在到达点上（TbPortal.target_spawn_id = fangshi_street = 出口锚点），
            //    所以这一步不用走，直接再按一次交互键。 ——
            yield return Step("原地再按一次交互键走回人间（妖界出口与到达点是同一个锚点）", null, 0f);
            yield return WaitUntil("玩家又进到妖界出口的触发半径内", () => PortalOf() != null && PortalOf().PlayerInRange, 5f);
            yield return Input.Press(inputService.Actions.Gameplay.Interact);
            yield return WaitUntil($"换回人间（场景键 {HumanScene}）", () => CurrentSceneKey() == HumanScene && SpawnOf(HumanEntrySpawn) != null, 25f);
            yield return WaitCurtainRevealed();
            yield return Check($"回到人间：落点是妖界那条导线的 target_spawn_id「{HumanPortalSpawn}」（表里写的就是这个）",
                () => CurrentSpawnId() == HumanPortalSpawn && NearLanding(SpawnLogic(HumanPortalSpawn)), 3f);
            yield return Snapshot("走回人间");
        }

        // ───────────────────────── 进场与判定 ─────────────────────────

        /// <summary>等 Boot 起来、流程停在标题，然后把容器里的转场 / 流程 / 输入 / 玩家模型取到手。</summary>
        private IEnumerator EnterWorld()
        {
            yield return WaitUntil(
                "Boot 起来、流程停在标题状态、容器里的世界模块服务可用",
                () =>
                {
                    flow = ResolveService<IGameFlow>();
                    transition = ResolveService<IWorldTransition>();
                    inputService = ResolveService<IInputService>();
                    player = ResolveService<PlayerModel>();
                    return flow != null && flow.Current is TitleState
                           && transition != null && inputService != null && player != null
                           && ResolveService<WorldSceneDriver>() != null;
                },
                30f);

            yield return Check("WorldInstaller 真的挂上了：容器里解析得出 WorldSceneDriver（挂不上就没有任何驱动）",
                () => ResolveService<WorldSceneDriver>() != null);
            yield return Check("世界场景登记器在跑：还没进世界时它应当认为「没有世界场景」",
                () => ResolveService<WorldSceneBinder>() != null && !ResolveService<WorldSceneBinder>().HasWorldScene);
        }

        /// <summary>写一条待处理转场（指定人间 + 过渡平台），再请流程切到世界场景状态。</summary>
        private IEnumerator EnterHumanScene()
        {
            yield return Step($"写一条待处理转场：场景「{HumanScene}」的出生点「{HumanEntrySpawn}」"
                              + "（没有转场时 WorldSceneState 直接抛，不以常量地址兜底）",
                () => transition.Request(new WorldTransitionRequest(
                    HumanScene, HumanEntrySpawn, WorldRules.ArrivalDefault, string.Empty)), 0f);
            yield return Check("转场挂上了（HasPending）", () => transition.HasPending);

            yield return Step("请流程切到 WorldSceneState：落黑幕 → 按表地址加载 → 摆玩家 → 揭幕", () => flow.GoToAsync<WorldSceneState>().Forget(), 0f);
            yield return WaitUntil($"世界场景加载出来（场景键 {HumanScene}，场景里有出生点锚点）",
                () => CurrentSceneKey() == HumanScene && SpawnOf(HumanEntrySpawn) != null, 25f);
            yield return WaitCurtainRevealed();
        }

        /// <summary>人间的三个检查点：场景键、落点、相机。</summary>
        private IEnumerator CheckHumanLanding()
        {
            yield return Check($"当前场景键是人间「{HumanScene}」（读 WorldSceneState.Spawn.SceneKey，不是靠场景名）",
                () => CurrentSceneKey() == HumanScene, 3f);
            yield return Check($"落点就是指定的出生点「{HumanEntrySpawn}」（状态报的 SpawnId 与逻辑坐标都对得上，容差 {LandingTolerance} 米）",
                () => CurrentSpawnId() == HumanEntrySpawn && NearLanding(SpawnLogic(HumanEntrySpawn)), 3f);
            yield return Check("相机对准：镜头跟的就是玩家根物体、玩家在画面内，且镜头已收敛到含边界钳制的位置（不是留在上一张图的位置）",
                () => CameraAimedAtPlayer(), 3f);
            yield return Snapshot("人间·泾阳 落点");
        }

        /// <summary>妖界的三个检查点。落点必须是**表里写的那个** target_spawn_id（fangshi_street）。</summary>
        private IEnumerator CheckYaoLanding()
        {
            yield return Check($"场景键换成妖界「{YaoScene}」，人间那一份已经卸掉",
                () => CurrentSceneKey() == YaoScene && SpawnOf(HumanEntrySpawn) == null, 3f);
            yield return Check($"落点就是表里的 target_spawn_id「{YaoPortalSpawn}」（世界出口的锚点）",
                () => CurrentSpawnId() == YaoPortalSpawn && NearLanding(SpawnLogic(YaoPortalSpawn)), 3f);
            yield return Check("相机对准妖界的玩家根物体（换了图之后重新绑过）",
                () => CameraAimedAtPlayer(), 3f);
        }

        // ───────────────────────── 场景查询（按结构认场景，不按名字） ─────────────────────────

        /// <summary>
        /// 已加载场景里**含 `SpawnAnchor` 的那一个**就是世界场景（与 `WorldSceneBinder` 同一套判据）；
        /// 没有世界场景时返回 <c>default</c>（`IsValid()` 为 false）。
        /// </summary>
        private static Scene LoadedWorldScene()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                {
                    continue;
                }

                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    if (root.GetComponentInChildren<SpawnAnchor>(true) != null)
                    {
                        return scene;
                    }
                }
            }

            return default;
        }

        /// <summary>当前世界场景键：读状态里**进入时解析出来的**那个值（不猜文件名、不靠字符串拼接）。</summary>
        private string CurrentSceneKey()
        {
            WorldSceneState state = ResolveService<WorldSceneState>();
            return state == null || state.Spawn == null ? null : state.Spawn.SceneKey;
        }

        /// <summary>当前落点的出生点 id；还没进场景时为 null。</summary>
        private string CurrentSpawnId()
        {
            WorldSceneState state = ResolveService<WorldSceneState>();
            return state == null || state.Spawn == null ? null : state.Spawn.SpawnId;
        }

        /// <summary>世界场景里那台挂了 SmoothCameraFollow 的相机；没有世界场景时返回 null。</summary>
        private static SmoothCameraFollow CameraOf()
        {
            Scene scene = LoadedWorldScene();
            if (!scene.IsValid())
            {
                return null;
            }

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                SmoothCameraFollow found = root.GetComponentInChildren<SmoothCameraFollow>(true);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        /// <summary>世界场景里的出口（PortalAnchor）；没有世界场景时返回 null。</summary>
        private static PortalAnchor PortalOf()
        {
            Scene scene = LoadedWorldScene();
            if (!scene.IsValid())
            {
                return null;
            }

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                PortalAnchor found = root.GetComponentInChildren<PortalAnchor>(true);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        /// <summary>按出生点 id 找世界场景里的锚点；找不到返回 null（检查点会判失败，不静默）。</summary>
        private static SpawnAnchor SpawnOf(string spawnId)
        {
            Scene scene = LoadedWorldScene();
            if (!scene.IsValid())
            {
                return null;
            }

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (SpawnAnchor anchor in root.GetComponentsInChildren<SpawnAnchor>(true))
                {
                    if (anchor.SpawnId == spawnId)
                    {
                        return anchor;
                    }
                }
            }

            return null;
        }

        /// <summary>锚点的世界坐标换算成玩家逻辑坐标（XZ 平面）：(x, z)。</summary>
        private static Vector2 SpawnLogic(string spawnId)
        {
            SpawnAnchor anchor = SpawnOf(spawnId);
            return anchor == null ? new Vector2(float.NaN, float.NaN) : new Vector2(anchor.transform.position.x, anchor.transform.position.z);
        }

        private static Vector2 PortalLogic()
        {
            PortalAnchor portal = PortalOf();
            return portal == null
                ? new Vector2(float.NaN, float.NaN)
                : new Vector2(portal.transform.position.x, portal.transform.position.z);
        }

        /// <summary>玩家逻辑坐标离目标点多远；玩家模型取不到或目标没解析出来时返回 false。</summary>
        private bool NearLanding(Vector2 expected)
        {
            if (player == null || float.IsNaN(expected.x))
            {
                return false;
            }

            return Vector2.Distance(player.Position, expected) <= LandingTolerance;
        }

        /// <summary>
        /// 相机有没有对准玩家。三条判据缺一不可，**都不依赖物体名**：
        /// ① 镜头跟的就是玩家根物体（它的逻辑坐标与 <see cref="PlayerModel.Position"/> 对得上）；
        /// ② 玩家落在画面内（视口坐标在 [0,1] 内且在前方）；
        /// ③ 镜头已经收敛到它自己算出来的位置（含 <c>Use Bounds</c> 的边界钳制），而不是还在上一张图的半路上。
        /// <para>
        /// ③ 不能写成「离『玩家 + 构图偏移』多远」：过渡平台在图的南缘，镜头会被边界钳住——
        /// 那是 A6 **该有**的行为（不设边界才是不约束），拿未钳制的期望值去判会把正确行为判成失败。
        /// </para>
        /// </summary>
        private bool CameraAimedAtPlayer()
        {
            SmoothCameraFollow camera = CameraOf();
            if (camera == null || camera.Target == null || player == null)
            {
                return false;
            }

            var targetLogic = new Vector2(camera.Target.position.x, camera.Target.position.z);
            if (Vector2.Distance(targetLogic, player.Position) > LandingTolerance)
            {
                return false;
            }

            Camera view = camera.GetComponent<Camera>();
            if (view == null)
            {
                return false;
            }

            Vector3 viewport = view.WorldToViewportPoint(camera.Target.position);
            if (viewport.z <= 0f || viewport.x < 0f || viewport.x > 1f || viewport.y < 0f || viewport.y > 1f)
            {
                return false;
            }

            return Vector3.Distance(camera.transform.position, camera.ResolveDesiredPosition()) <= CameraTolerance;
        }
    }
}
