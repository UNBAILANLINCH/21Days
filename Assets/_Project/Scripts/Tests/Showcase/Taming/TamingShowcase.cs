// 职责：从 Boot 标题进入真实遭遇，验证多巡逻者控制、输入归属、镜头和存档恢复。
using System.Collections;
using Game.Core.Flow;
using Game.Core.Input;
using Game.IsometricExploration;
using Game.Monster;
using Game.Player;
using Game.Taming;
using TMPro;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests.Showcase.Taming
{
    [Category("Showcase")]
    public sealed class TamingShowcase : ShowcaseScenario
    {
        protected override string Module => "Taming";
        protected override string ScenePath => null;
        protected override bool LoadBootScene => true;

        // 复用 0bcd63e 的探索回放规则：SampleScene 根节点主相机，正在渲染且等于 Camera.main。
        private static Camera FindWorldCamera(EncounterSceneView view)
        {
            var scene = view.gameObject.scene;
            if (!scene.isLoaded || scene.path != ShowcaseOptions.DemoScenePath) return null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name != "Main Camera") continue;
                Camera camera = root.GetComponent<Camera>();
                return camera != null && camera.isActiveAndEnabled && Camera.main == camera ? camera : null;
            }
            return null;
        }

        [UnityTest]
        public IEnumerator TwoPatrols_TameSwitchSaveUnloadRestore()
        {
            yield return EnterWorldFromTitle();
            var step = ResolveService<EncounterStep>();
            var player = ResolveService<PlayerModel>();
            var view = FindRequired<EncounterSceneView>("Encounter");
            SmoothCameraFollow follow = null;
            yield return Check("SampleScene 渲染主相机有跟随组件且是实际控制镜头", () =>
            {
                Camera camera = FindWorldCamera(view);
                follow = camera == null ? null : camera.GetComponent<SmoothCameraFollow>();
                return follow != null && follow == view.ControlCamera;
            }, 3f);
            if (follow == null || follow != view.ControlCamera) yield break;
            Assert.That(view.PatrolActors.Length, Is.EqualTo(2));
            string a = view.PatrolActors[0].StableId;
            string b = view.PatrolActors[1].StableId;
            string lastEvent = null;
            System.Action<string> changed = id => lastEvent = id;
            step.Taming.OnControlChanged += changed;
            try
            {
                yield return Step("未驯服不可接管", () => Assert.That(view.RequestControl(b), Is.False));
                yield return Input.Press(ResolveService<IInputService>().Actions.Gameplay.Tame);
                yield return Check("原单敌 T 流程接管 A", () => step.CurrentControlId == a && follow.Target == view.PatrolActors[0].transform, 3f);
                yield return Input.Press(ResolveService<IInputService>().Actions.Gameplay.Tame);
                yield return Check("再次 T 返回玩家", () => step.CurrentControlId == step.Taming.PlayerId && follow.Target == view.PlayerBody, 3f);
                yield return Step("定向驯服 B", () => Assert.That(view.RequestControl(b, true), Is.True));
                yield return Check("两名独立驯服且控制 B", () => step.CurrentControlId == b && step.Taming.IsTargetTamed(a) && step.Taming.IsTargetTamed(b), 3f);
                for (int i = 0; i < 4; i++)
                {
                    string owner = i % 2 == 0 ? a : b;
                    string other = owner == a ? b : a;
                    yield return Step("定向切换 " + owner, () => Assert.That(view.RequestControl(owner), Is.True));
                    yield return Check("对象、ID、镜头与事件一致", () => view.CurrentControlId == owner && lastEvent == owner
                        && view.CurrentControlObject == follow.Target && view.CurrentControlObject.GetComponent<TamingActor>().StableId == owner, 3f);
                    Vector2 before = step.Taming.GetTarget(owner).Model.Position;
                    Vector2 idle = step.Taming.GetTarget(other).Model.Position;
                    Vector2 playerBefore = player.Position;
                    yield return Walk(Vector2.right, 0.4f);
                    yield return Check("仅当前对象响应移动", () => step.Taming.GetTarget(owner).Model.Position.x > before.x
                        && step.Taming.GetTarget(other).Model.Position == idle && player.Position == playerBefore);
                }
                yield return Check("显示标识与代码一致", () => view.PatrolActors[1].GetComponentInChildren<TMP_Text>(true).text
                    == step.Taming.GetDisplayName(b) + " [" + b + "]");
                yield return Snapshot("两名巡逻者已驯服·控制B");
                yield return Step("无效请求保持控制", () => Assert.That(view.RequestControl("unknown"), Is.False));
                var flow = ResolveService<IGameFlow>();
                bool left = false;
                yield return Step("回标题，走离场保存与卸载", () => BeginLeaveToTitle(flow, () => left = true), 0f);
                yield return Check("已回标题", () => left && flow.Current is TitleState, 20f);
                yield return Step("继续读取隔离存档", () => RequireTitleButton("ContinueButton").onClick.Invoke(), 0f);
                yield return Check("重新进入且控制 B 恢复", () => step.IsActive && flow.Current is MonsterEncounterState && step.CurrentControlId == b, 20f);
                view = FindRequired<EncounterSceneView>("Encounter");
                yield return Check("恢复后同一规则定位世界渲染主相机", () =>
                {
                    Camera camera = FindWorldCamera(view);
                    follow = camera == null ? null : camera.GetComponent<SmoothCameraFollow>();
                    return follow != null && follow == view.ControlCamera;
                }, 3f);
                if (follow == null || follow != view.ControlCamera) yield break;
                yield return Check("恢复后 A/B 可选且镜头跟随 B", () => step.Taming.CanControl(a) && step.Taming.CanControl(b) && view.CurrentControlObject == follow.Target, 3f);
                yield return Step("禁用当前目标", () => view.PatrolActors[1].gameObject.SetActive(false));
                yield return Check("自动返回玩家且禁用目标不可选", () => step.CurrentControlId == step.Taming.PlayerId && !view.RequestControl(b), 3f);
                yield return Step("重新启用目标", () => view.PatrolActors[1].gameObject.SetActive(true));
                yield return Step("按稳定 ID 再次选择", () => Assert.That(view.RequestControl(b), Is.True));
                yield return Check("再次接管 B", () => step.CurrentControlId == b, 3f);
                yield return Step("销毁当前目标", () => Object.Destroy(view.PatrolActors[1].gameObject));
                yield return Check("销毁后返回玩家且旧 ID 不可选", () => step.CurrentControlId == step.Taming.PlayerId && !view.RequestControl(b), 3f);
                yield return Snapshot("目标销毁后·返回玩家");
            }
            finally { step.Taming.OnControlChanged -= changed; }
        }
    }
}
