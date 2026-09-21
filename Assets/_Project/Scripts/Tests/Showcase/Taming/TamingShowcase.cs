// 职责：独立回放驯服后移动与镜头接管；复用场景入口，不再创建第二套规则。
using System.Collections;
using Game.IsometricExploration;
using Game.Monster;
using Game.Taming;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace Game.Tests.Showcase.Taming
{
    [Category("Showcase")]
    public sealed class TamingShowcase : ShowcaseScenario
    {
        protected override string Module => "Taming";
        protected override string ScenePath => "Assets/_Project/Scenes/Verify/Taming.unity";
        protected override bool LoadBootScene => false;

        [UnityTest]
        public IEnumerator ToggleControl_MovesEnemyAndCamera_ThenReturns()
        {
            var controller = FindRequired<TamingSceneController>("Encounter");
            var view = FindRequired<EncounterSceneView>("Encounter");
            var follow = FindRequired<SmoothCameraFollow>("Main Camera");
            var keyboard = InputSystem.AddDevice<Keyboard>();
            try
            {
            Vector2 start = controller.Player.Position;
            Vector2 enemyStart = controller.Enemy.Position;
            Vector3 cameraStart = follow.transform.position;
            yield return Step("短按 T 驯服并接管敌人", () =>
            {
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.T));
                InputSystem.Update();
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                InputSystem.Update();
            });
            yield return Check("镜头跟随敌人", () => follow.Target == view.MonsterBody, 2f);
            controller.ManualSimulation = true;
            yield return Step("控制敌人向右移动", () => controller.Simulate(new TamingIntent(Vector2.right, false), 1f));
            yield return Check("敌人移动而玩家留在原地", () => controller.Enemy.Position.x > enemyStart.x && controller.Player.Position == start);
            yield return Check("摄像机实际移动", () => follow.transform.position.x > cameraStart.x + 1f, 3f);
            yield return Snapshot("敌人视角");
            yield return Step("再次按 T 返回玩家", () => controller.Simulate(new TamingIntent(Vector2.zero, true), 0f));
            yield return Check("镜头回到玩家且保留驯服", () => follow.Target == view.PlayerBody && controller.Rules.IsTamed);
            yield return Snapshot("返回玩家");
            }
            finally { InputSystem.RemoveDevice(keyboard); }
        }
    }
}
