// 职责：独立回放伪装禁攻、取消后受击，以及普通攻击击杀。
using System.Collections;
using Game.Core.Simulation;
using Game.Monster;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace Game.Tests.Showcase.Disguise
{
    [Category("Showcase")]
    public sealed class DisguiseShowcase : ShowcaseScenario
    {
        protected override string Module => "Disguise";
        protected override string ScenePath => "Assets/_Project/Scenes/Verify/Disguise.unity";
        protected override bool LoadBootScene => false;

        [UnityTest]
        public IEnumerator Disguise_BlocksHostileAttack_ThenCanKillNormally()
        {
            var controller = FindRequired<StandaloneEncounterController>("Encounter");
            controller.ManualSimulation = true;
            int health = controller.Player.Health;
            yield return Step("开启伪装并贴近敌人", () =>
            {
                Simulate(controller, InputCommand.ButtonDisguise, 0f);
                Simulate(controller, 0u, 1f, Vector2.right);
            });
            yield return Step("敌人已敌对，等待多次攻击冷却", () => Simulate(controller, 0u, 4f));
            yield return Check("伪装期间生命不下降", () => controller.Player.IsDisguised && controller.Player.Health == health && controller.Enemy.Mode == MonsterMode.Hostile);
            yield return Snapshot("伪装禁攻");
            yield return Step("取消伪装", () => Simulate(controller, InputCommand.ButtonDisguise, 1f));
            yield return Check("取消后敌人恢复攻击", () => controller.Player.Health < health);
            yield return Step("重新伪装，连续普通攻击", () =>
            {
                Simulate(controller, 0u, 0f);
                Simulate(controller, InputCommand.ButtonDisguise, 0f);
                for (int i = 0; i < 3; i++)
                {
                    Simulate(controller, InputCommand.ButtonAttack, 0.7f);
                    Simulate(controller, 0u, 0f);
                }
            });
            yield return Check("普通攻击令敌人血量归零", () => controller.Enemy.Health == 0);
            yield return Snapshot("普通攻击击杀");
        }

        private static void Simulate(StandaloneEncounterController controller, uint buttons, float deltaTime, Vector2 movement = default)
        {
            var command = new InputCommand(movement, Vector2.zero, buttons, Vector2.zero, 0);
            controller.Simulate(in command, deltaTime);
        }

        [UnityTest]
        public IEnumerator ShortKeyboardPress_ReachesDisguiseAndAttack()
        {
            var controller = FindRequired<StandaloneEncounterController>("Encounter");
            var input = FindRequired<PlayerInput>("player");
            // PlayerInput 配对会修改底层设备数组，必须复制后再恢复。
            InputDevice[] originalDevices = input.actions.devices?.ToArray();
            var keyboard = InputSystem.AddDevice<Keyboard>();
            input.actions.devices = new InputDevice[] { keyboard };
            try
            {
                yield return Step("在两个物理帧之间短按 G", () => ShortPress(keyboard, Key.G));
                yield return Check("短按开启伪装", () => controller.Player.IsDisguised, 2f);
                controller.ManualSimulation = true;
                Simulate(controller, 0u, 1f, Vector2.right);
                int before = controller.Enemy.Health;
                controller.ManualSimulation = false;
                yield return Step("在两个物理帧之间短按 J", () => ShortPress(keyboard, Key.J));
                yield return Check("短按攻击确实扣血", () => controller.Enemy.Health == before - 1, 2f);
                yield return Snapshot("真实输入短按命中");
            }
            finally
            {
                input.actions.devices = originalDevices;
                InputSystem.RemoveDevice(keyboard);
            }
        }

        private static void ShortPress(Keyboard keyboard, Key key)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
            InputSystem.Update();
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            InputSystem.Update();
        }
    }
}
