// 职责：纸片场景适配回放——玩家真走上灰盒楼梯（Stairs_Step_1..4）时身体贴地逐级抬升、走下来落回地面；验证相机距离剔除的消失与恢复。
// 舞台是 SampleScene，走 Boot 真实流程（标题「开始」进场），虚拟手柄推摇杆驱动场景里的真实玩家，
//   高度读 EncounterSceneView.PlayerScenePosition（纸片身体的场景位置），不瞬移、不 new 规则。
// 本次重写理由：原版不加载 Boot，自己 new PlayerRules / MonsterRules / EncounterStep + 默认值 ScriptableObject 重新 Bind 视图，
//   再 PlayerRules.Reset 把玩家一级一级瞬移上台阶——与 demo 场景的正常游玩链路脱节（瞬移还会跳过视图的碰撞与贴地插值），
//   验的是默认值配置而不是场景里调好的数值。原版的潜行接近 / 红区敌对 / 普通攻击击杀用例已挪到
//   Monster / Player 两份回放（每个行为只演一次），本文件只留纸片场景适配。
// 站位与时机：楼梯紧挨巡逻怪的巡逻线（巡逻段 x 13.86..17.86、z 3.4；楼梯 x 16.5..19.5、z 6..9.2），楼梯口 z≈5.5 离巡逻线只有 2.1 米，
//   怪物朝东走时楼梯口会落进它的视野锥（±37.5°、警戒半径 6）。所以先在塔与楼梯之间的空地 (15.5,7.2) 等（任何时候都在视野外），
//   等它朝东走过 x 16.3 再出发：此后它要么朝东看不到楼梯口（夹角 > 37.5°）、要么折返朝西背对楼梯口，
//   直到再走回 x 13.86 掉头（≥ 2.7 秒），这段时间里玩家已经走过楼梯第 3 级（z ≥ 7.6，此后怎么都进不了警戒半径）。
//   出发前的路线同 Player / Monster 回放：北侧 RouteToPatrol 绕开村口演出触发区。楼梯口坐标与时机常量（StairsLaneX / StairsFootOffset /
//   StairsCorner / StairsSafeMonsterX）的推导见 Framework/ShowcaseScenario.DemoScene.cs。
using System;
using System.Collections;
using Game.Core.Input;
using Game.IsometricExploration;
using Game.Monster;
using Game.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests.Showcase.IsometricExploration
{
    [Category("Showcase")]
    public sealed class IsometricExplorationShowcase : ShowcaseScenario
    {
        private IInputService inputService;
        private PlayerModel player;
        private MonsterModel monster;

        protected override string Module => "IsometricExploration";

        /// <summary>世界由流程加载（标题「开始」→ MonsterEncounterState → SampleScene），基类不再叠加载一份。</summary>
        protected override string ScenePath => null;

        [UnityTest]
        public IEnumerator CameraDistanceCulling_HidesDistantVisualAndRestoresIt()
        {
            yield return EnterWorld();
            Camera camera = FindRequired<Camera>("Main Camera");
            CameraDistanceCulling culling = FindRequired<CameraDistanceCulling>("Main Camera");
            SmoothCameraFollow follow = FindRequired<SmoothCameraFollow>("Main Camera");
            bool followWasEnabled = follow.enabled;
            var marker = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            marker.name = "DistanceCullingDemo";
            marker.layer = 31;
            marker.transform.position = camera.ViewportToWorldPoint(new Vector3(0.65f, 0.55f, 8f));
            var material = Track(new Material(Shader.Find("Universal Render Pipeline/Unlit")));
            material.color = Color.cyan;
            marker.GetComponent<Renderer>().sharedMaterial = material;
            try
            {
                follow.enabled = false;
                yield return Step("显示相机前方的青色测试方块", () =>
                    culling.ApplySettings(new[] { new CameraLayerCullSettings(1 << 31, 0f) }));
                yield return new WaitForEndOfFrame();
                Texture2D before = Track(ScreenCapture.CaptureScreenshotAsTexture());
                int x = Mathf.RoundToInt(before.width * 0.65f);
                int y = Mathf.RoundToInt(before.height * 0.55f);
                Color visible = before.GetPixel(x, y);
                yield return Check("采样位置确实显示青色方块", () =>
                    visible.g > visible.r + 0.2f && visible.b > visible.r + 0.2f);
                yield return Snapshot("距离剔除前");
                yield return Step("把该层剔除距离设为 4 米，8 米处方块应消失", () =>
                    culling.ApplySettings(new[] { new CameraLayerCullSettings(1 << 31, 4f) }));
                yield return new WaitForEndOfFrame();
                Texture2D hidden = Track(ScreenCapture.CaptureScreenshotAsTexture());
                yield return Check("方块位置的实际画面发生变化", () =>
                    Vector4.Distance(visible, hidden.GetPixel(x, y)) > 0.1f);
                Physics.SyncTransforms();
                yield return Check("剔除后方块仍激活且碰撞体可命中", () =>
                    marker.activeSelf && marker.GetComponent<Collider>().Raycast(
                        new Ray(camera.transform.position, marker.transform.position - camera.transform.position), out _, 20f));
                yield return Snapshot("距离剔除后");
                yield return Step("恢复该层远裁剪面距离，方块应重新出现", () =>
                    culling.ApplySettings(new[] { new CameraLayerCullSettings(1 << 31, 0f) }));
                yield return new WaitForEndOfFrame();
                Texture2D restored = Track(ScreenCapture.CaptureScreenshotAsTexture());
                yield return Check("方块位置恢复原来的颜色", () =>
                    Vector4.Distance(visible, restored.GetPixel(x, y)) < 0.1f);
                yield return Snapshot("距离剔除恢复");
            }
            finally
            {
                culling.ApplyConfiguration();
                follow.enabled = followWasEnabled;
            }
        }

        [UnityTest]
        public IEnumerator WalkUpStairs_BodyRisesStepByStepThenLandsBack()
        {
            yield return EnterWorld();
            EncounterSceneView view = FindRequired<EncounterSceneView>("Encounter");
            Transform step1 = FindRequired<Transform>("Stairs_Step_1");
            Collider step4 = FindRequired<Collider>("Stairs_Step_4");
            Vector2 foot = new Vector2(StairsLaneX, step1.position.z - StairsFootOffset);
            Vector2 top = new Vector2(StairsLaneX, step4.transform.position.z);

            yield return Step("切到奔跑，沿北侧走到塔与楼梯之间的空地（巡逻怪看不到这里）", null, 0f);
            yield return Input.PressUntil(inputService.Actions.Gameplay.Run, () => player.IsRunning);
            yield return WalkRoute(RouteToPatrol, 0.4f, 8f);

            yield return Step("等巡逻怪朝东走过楼梯口前方（接下来几秒它看不到楼梯口）", null, 0f);
            yield return WaitUntil("怪物朝东巡逻、已过 x 16.3", MonsterLeavesStairsFoot, 15f);

            float baseline = 0f;
            yield return Step("跑到楼梯口平地，记下地面高度", null, 0f);
            yield return WalkTo(StairsCorner, 0.3f, 3f);
            yield return WalkTo(foot, 0.2f, 3f);
            baseline = view.PlayerScenePosition.y;
            float rise = step4.bounds.max.y - baseline;
            yield return Check($"站在楼梯口平地上（身体高度 {baseline:0.00}，第 4 级台阶顶比地面高 {rise:0.00}）",
                () => rise > 0.9f && Vector2.Distance(player.Position, foot) <= 0.4f);

            float maxDrop = 0f;
            float lastY = baseline;
            yield return Step("推摇杆沿楼梯向上走到第 4 级", null, 0f);
            yield return PushSampling(Vector2.up, 3f, () => player.Position.y >= top.y - 0.1f, () =>
            {
                float y = view.PlayerScenePosition.y;
                maxDrop = Mathf.Max(maxDrop, lastY - y);
                lastY = y;
            });
            float climbed = view.PlayerScenePosition.y - baseline;
            yield return Check($"上楼过程中身体高度只升不降（最大回落 {maxDrop:0.000} 米）", () => maxDrop <= 0.001f);
            yield return Check($"身体贴着台阶抬到第 4 级上（抬升 {climbed:0.00} 米，台阶顶 {rise:0.00} 米）",
                () => view.PlayerScenePosition.y - baseline >= rise - 0.15f && player.Position.y >= top.y - 0.4f, 1f);
            yield return Snapshot("站上楼梯第4级");

            yield return Step("在楼梯顶等巡逻怪再次朝东走过楼梯口前方", null, 0f);
            yield return WaitUntil("怪物朝东巡逻、已过 x 16.3", MonsterLeavesStairsFoot, 15f);

            yield return Step("推摇杆沿楼梯走回楼梯口平地", null, 0f);
            yield return WalkTo(foot, 0.2f, 3f);
            yield return Check($"身体落回地面高度（与 {baseline:0.00} 相差 ≤ 0.05 米）",
                () => Mathf.Abs(view.PlayerScenePosition.y - baseline) <= 0.05f, 2f);
            yield return Snapshot("走回平地");
        }

        // ───────────────────────── 进场与驱动 ─────────────────────────

        /// <summary>怪物在朝东巡逻、已走过楼梯口前方：推导见文件头「站位与时机」。</summary>
        private bool MonsterLeavesStairsFoot()
        {
            return monster.Mode == MonsterMode.PatrolWalk && monster.Facing.x > 0.5f
                   && monster.Position.x >= StairsSafeMonsterX;
        }

        /// <summary>标题「开始」进世界（EnterDemoWorld），等容器里的输入服务与玩家 / 怪物模型可用。</summary>
        private IEnumerator EnterWorld()
        {
            yield return EnterDemoWorld("遭遇逻辑在跑、输入服务与玩家 / 怪物模型可用", () =>
            {
                inputService = ResolveService<IInputService>();
                player = ResolveService<PlayerModel>();
                monster = ResolveService<MonsterModel>();
                return inputService != null && inputService.Actions != null
                       && player != null && monster != null;
            }, "进世界后容器里取不到 PlayerModel / MonsterModel / IInputService，后续步骤无法驱动");
        }

        /// <summary>
        /// 逐帧朝 <paramref name="direction"/> 推摇杆并回调 <paramref name="onFrame"/> 采样，
        /// <paramref name="stopWhen"/> 成立或满 <paramref name="maxSeconds"/> 秒松杆，再等玩家停稳。
        /// </summary>
        private IEnumerator PushSampling(Vector2 direction, float maxSeconds, Func<bool> stopWhen, Action onFrame)
        {
            float deadline = Time.realtimeSinceStartup + maxSeconds;
            while (Time.realtimeSinceStartup < deadline && !stopWhen())
            {
                Input.SetStick(direction);
                yield return null;
                onFrame();
            }

            Input.ReleaseStick();
            yield return WaitPlayerStable();
            onFrame();
        }
    }
}
