// 职责：伪装回放——伪装期间敌对怪物不出手、取消伪装后立刻挨打、普通攻击照样能击杀；以及键盘短按也能到达伪装与攻击。
// 舞台是 SampleScene，走 Boot 真实流程（标题「开始」进场），虚拟手柄推摇杆、虚拟键盘按动作驱动场景里的真实玩家与巡逻怪；
//   读状态只读容器里的 PlayerModel / MonsterModel，不 new 规则、不手动推模拟、不瞬移。
// 本次重写理由：原版不加载 Boot，把 SampleScene 里的 StandaloneEncounterController 切成手动模拟、自己拼 InputCommand 逐小步推
//   （短按用例还往 PlayerInput 塞了一只自建键盘），与标题「开始」进场的正常游玩链路脱节，
//   验的也不是容器里 PlayerConfig.asset / MonsterConfig.asset 那套场景调好的数值。
// 「短按」用例保留：它验的是虚拟键盘 → Gameplay 动作图 → LiveInputSource 逐 tick 采样 → 伪装 / 攻击按下沿这条真实路径。
//   按键时长取「一个逻辑帧」：按下后等够 1.5 个逻辑步长（60 Hz 下约 25 毫秒）就松开（本文件的 ShortPress），
//   不用别处的「按住直到生效」（Input.PressUntil）——那样验不到按下沿本身。
//   LiveInputSource 只在逻辑 tick 上读 IsPressed、没有按下沿锁存，短于一个逻辑帧的按键会被吞——2026-09-28 全量跑实测伪装、攻击
//   各红过一次（当时 Input.Press 只按两帧，已改为至少跨一个 tick）。这是运行时的现状（真人按键远长于 17 毫秒，不影响游玩），
//   在报告里单列，不在回放里掩盖。
// 路线：出生点与巡逻怪之间隔着村口演出触发区，一律走北侧路线到巡逻线北侧观察点（GoToPatrolLookout，坐标与理由见
//   Framework/ShowcaseScenario.DemoScene.cs）；再从观察点逐帧朝怪物当前位置推摇杆贴近，出手用同文件的 StrikeMonster / FaceMonster。
using System.Collections;
using Game.Core.Input;
using Game.Core.Simulation;
using Game.Monster;
using Game.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace Game.Tests.Showcase.Disguise
{
    [Category("Showcase")]
    public sealed class DisguiseShowcase : ShowcaseScenario
    {
        /// <summary>伪装期间观察多久不挨打（真实秒）：怪物攻击冷却 1 秒，2.5 秒内不伪装至少会挨两下。</summary>
        private const float DisguisedWatchSeconds = 2.5f;

        private IInputService inputService;
        private PlayerModel player;
        private MonsterModel monster;
        private MonsterConfig monsterConfig;

        protected override string Module => "Disguise";

        /// <summary>世界由流程加载（标题「开始」→ MonsterEncounterState → SampleScene），基类不再叠加载一份。</summary>
        protected override string ScenePath => null;

        [UnityTest]
        public IEnumerator Disguise_BlocksHostileAttack_ThenCanKillNormally()
        {
            yield return EnterWorld();
            yield return GoToPatrolLookout();

            yield return Step("按伪装键开启伪装，走到怪物身边", null, 0f);
            yield return Input.PressUntil(inputService.Actions.Gameplay.Disguise, () => player.IsDisguised);
            yield return ApproachMonster(0.8f, 8f);
            yield return Check("贴身后怪物察觉并转敌对（状态色变红）", () => player.IsDisguised && monster.Mode == MonsterMode.Hostile, 5f);

            int health = player.Health;
            float watchUntil = 0f;
            yield return Step($"伪装着站在敌对怪物身边 {DisguisedWatchSeconds} 秒", () => watchUntil = Time.realtimeSinceStartup + DisguisedWatchSeconds, 0f);
            yield return WaitUntil($"过去 {DisguisedWatchSeconds} 秒", () => Time.realtimeSinceStartup >= watchUntil, DisguisedWatchSeconds + 1f);
            yield return Check("伪装期间生命不下降，怪物仍敌对",
                () => player.IsDisguised && player.Health == health && monster.Mode == MonsterMode.Hostile);
            yield return Snapshot("伪装禁攻");

            yield return Step("按伪装键取消伪装", null, 0f);
            yield return Input.PressUntil(inputService.Actions.Gameplay.Disguise, () => !player.IsDisguised);
            yield return Check("取消伪装后怪物立刻出手，玩家掉血", () => !player.IsDisguised && player.Health < health, 3f);
            yield return Snapshot("取消伪装挨打");

            yield return Step("重新伪装，连续普通攻击直到怪物倒下", null, 0f);
            yield return Input.PressUntil(inputService.Actions.Gameplay.Disguise, () => player.IsDisguised);
            for (int i = 0; i < monsterConfig.MaxHealth && monster.Health > 0 && player.Health > 0; i++)
            {
                yield return StrikeMonster();
            }

            yield return Check("伪装不影响出手：普通攻击令怪物血量归零（状态 Dead）",
                () => player.IsDisguised && monster.Health == 0 && monster.Mode == MonsterMode.Dead, 2f);
            yield return Snapshot("普通攻击击杀");
        }

        [UnityTest]
        public IEnumerator ShortKeyboardPress_ReachesDisguiseAndAttack()
        {
            yield return EnterWorld();

            yield return Step("键盘短按一下伪装键（按下约一个逻辑帧即松开）", null, 0f);
            yield return ShortPress(inputService.Actions.Gameplay.Disguise);
            yield return Check("短按开启伪装", () => player.IsDisguised, 2f);

            yield return GoToPatrolLookout();
            yield return Step("伪装着贴到怪物身边、面朝它", null, 0f);
            yield return ApproachMonster(0.6f, 8f);
            yield return WaitUntil("攻击冷却就绪", () => player.AttackCooldownLeft <= 0f, 2f);

            int before = monster.Health;
            yield return Step("面朝怪物，键盘短按一下攻击键（按下约一个逻辑帧即松开）", null, 0f);
            // 对准朝向紧贴在按键前做：敌对怪物一直往玩家脚下走，隔久了两者重合、朝向判定又不准。
            yield return FaceMonster();
            yield return ShortPress(inputService.Actions.Gameplay.Attack);
            bool swung = false;
            yield return WaitUntil("短按被逻辑帧采到（出手进入冷却）", () => swung = player.AttackCooldownLeft > 0f, 1f);
            yield return Check($"短按攻击确实扣血（怪物生命 -1；出手{(swung ? "已" : "未")}被采到）", () => monster.Health == before - 1, 2f);
            yield return Snapshot("真实输入短按命中");
        }

        // ───────────────────────── 进场与驱动 ─────────────────────────

        /// <summary>标题「开始」进世界（EnterDemoWorld），等容器里的输入服务、玩家 / 怪物模型与怪物配置可用。</summary>
        private IEnumerator EnterWorld()
        {
            yield return EnterDemoWorld("遭遇逻辑在跑、输入服务与玩家 / 怪物模型可用", () =>
            {
                inputService = ResolveService<IInputService>();
                player = ResolveService<PlayerModel>();
                monster = ResolveService<MonsterModel>();
                monsterConfig = ResolveService<MonsterConfig>();
                return inputService != null && inputService.Actions != null
                       && player != null && monster != null && monsterConfig != null;
            }, "进世界后容器里取不到 PlayerModel / MonsterModel / IInputService / MonsterConfig，后续步骤无法驱动");
        }

        /// <summary>逐帧朝怪物当前位置推摇杆（它在巡逻、位置一直在变），进入 <paramref name="within"/> 米内松杆；超时记失败继续。</summary>
        private IEnumerator ApproachMonster(float within, float timeout)
        {
            yield return WaitUntil($"贴到怪物 {within} 米内", () =>
            {
                Vector2 diff = monster.Position - player.Position;
                if (diff.magnitude <= within)
                {
                    Input.ReleaseStick();
                    return true;
                }

                Input.SetStick(diff.normalized);
                return false;
            }, timeout);
            Input.ReleaseStick();
        }

        /// <summary>
        /// 短按：按下 → 等够 1.5 个逻辑步长（真实时间）→ 松开。保证按住期间至少跨过一次逻辑 tick 采样，
        /// 又远短于「按住直到生效」，验的是按下沿本身（理由见文件头）。
        /// </summary>
        private IEnumerator ShortPress(InputAction action)
        {
            SimulationRunner runner = ResolveService<SimulationRunner>();
            float step = runner == null ? 1f / 60f : runner.Clock.FixedDeltaTime;
            yield return Input.Hold(action);
            float until = Time.realtimeSinceStartup + step * 1.5f;
            while (Time.realtimeSinceStartup < until)
            {
                yield return null;
            }

            yield return Input.Release(action);
        }
    }
}
