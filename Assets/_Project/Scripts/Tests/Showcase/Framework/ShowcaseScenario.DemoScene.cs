// 职责：ShowcaseScenario 的「SampleScene demo 内容」部分（partial）——只与这张回放舞台的内容相关的共用知识与操作：
//   出生点 / 村口演出触发区 / 北侧绕行路线 / 巡逻线观察点 / 楼梯口 / 长者站位这些坐标常量，
//   以及进世界并等遭遇逻辑就绪（EnterDemoWorld）、按路线逐点走（WalkRoute）、走到巡逻线观察点（GoToPatrolLookout）、
//   和巡逻怪交手（StrikeMonster / FaceMonster）。
//   与通用回放引擎分开：ShowcaseScenario.cs 管报告节奏，BootFlow 管进退场，PlayerDrive 管输入设备与走路；
//   SampleScene 的 demo 内容一改（挪了巡逻怪、换了楼梯、移了触发区），只动这一个文件。
//
// 用法（ScenePath 返回 null，走 Boot 真实流程）：
//   yield return EnterDemoWorld("遭遇逻辑在跑、输入服务与玩家 / 怪物模型可用", () =>
//   {
//       inputService = ResolveService<IInputService>(); player = ResolveService<PlayerModel>();
//       return inputService != null && inputService.Actions != null && player != null;
//   }, "进世界后容器里取不到 PlayerModel / IInputService");
//   yield return GoToPatrolLookout();     // 切跑 → 北侧路线 → 观察点 → 切回步行
//   yield return StrikeMonster();         // 追上巡逻怪打中一下；StrikeSwings 记累计出手次数
//
// 坐标一律是玩家逻辑坐标（= 场景 XZ，y 是场景 z），见 ShowcaseScenario.PlayerDrive.cs 的坐标约定。
//
// 为什么新建（project-root.md「加能力的顺序」）：
//   复用 —— Player / Monster / Disguise / IsometricExploration / CharacterPuppet 五份回放各复制了一份路线常量、
//           进世界包装、切跑走到观察点、追怪出手，没有可复用的公共件。
//   扩展 —— 要调 Step / WaitUntil / WalkTo / ResolveService 等 protected 成员，只能落在基类；但这些是「这张场景的内容知识」，
//           塞进 PlayerDrive（通用走路）或 BootFlow（通用进退场）职责说不通，按职责单独一个 partial。
using System;
using System.Collections;
using System.Collections.Generic;
using Game.Core.Input;
using Game.Monster;
using Game.Player;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.Showcase
{
    public abstract partial class ShowcaseScenario
    {
        /// <summary><see cref="EnterDemoWorld"/> 等标题就绪的上限（真实秒）。</summary>
        protected const float DemoBootTimeoutSeconds = 20f;

        /// <summary><see cref="EnterDemoWorld"/> 点「开始」后等进世界、再等遭遇逻辑就绪的上限（真实秒）。</summary>
        protected const float DemoEnterTimeoutSeconds = 20f;

        /// <summary>
        /// 玩家出生点 (-4, 3.4)：离长者 3 米（交互半径 2 之外，要先走过去）、离巡逻怪约 18 米、离左墙（x -6）2 米。
        /// 在出生点附近左右走的回放按它算终点碰不碰墙、出不出画。
        /// </summary>
        protected static readonly Vector2 DemoSpawnPoint = new Vector2(-4f, 3.4f);

        /// <summary>
        /// 村口演出触发区 Trigger_VillageEntrance 的范围（x 8..11、z 1..4）：每次进入都会拉起演出、冻住世界，
        /// 而它正挡在出生点与巡逻怪之间——去巡逻怪那边的回放一律走 <see cref="RouteToPatrol"/> 从北侧绕开。
        /// </summary>
        protected static readonly Rect VillageEntranceTriggerArea = Rect.MinMaxRect(8f, 1f, 11f, 4f);

        /// <summary>
        /// 出生点去巡逻怪 / 楼梯那边的北侧绕行路线：(3.5,5) → (8.5,7.2) → (15.5,7.2)。
        /// 从北侧绕开 <see cref="VillageEntranceTriggerArea"/>；终点 (15.5,7.2) 是塔与楼梯之间的空地，
        /// 在巡逻线北侧近 4 米，巡逻怪（视野锥 ±37.5°、警戒半径 6）任何时候都看不到这里。
        /// </summary>
        protected static readonly IReadOnlyList<Vector2> RouteToPatrol = new[]
        {
            new Vector2(3.5f, 5f),
            new Vector2(8.5f, 7.2f),
            new Vector2(15.5f, 7.2f),
        };

        /// <summary>
        /// 巡逻线北侧的观察点 (20.86, 7.6)：取巡逻段（2026-10-07 由 4 米拉到 14 米，x 13.86..27.86、z 3.4）
        /// 的中点，并在**北侧留足 4.2 米**。
        /// <para>
        /// 为什么不是原来的「北侧 2 米」：那个位置的安全性是**按 4 米短巡逻线算的**——线短时 |dx| ≤ 2，
        /// 视野锥 ±37.5° 要求的 |dx| ≥ 2.6 恒成立。线拉到 14 米后 |dx| 最大到 7，站太近就会被从西边
        /// 走过来的怪正面看到（会转警戒，后面的绕背/停顿回放全崩）。
        /// </para>
        /// <para>
        /// 安全条件是两条判据取并集：**被视野锥排除**（|dx| &lt; 1.304·D，1.304 = 1/tan37.5°）或
        /// **距离超过橙区**（|dx|² + D² &gt; 36）。要让它们覆盖全部 |dx|，需要 1.304·D ≥ √(36 − D²)，
        /// 解得 D ≥ 3.65 米；这里取 D = 4.2 留余量（z = 7.6）。
        /// </para>
        /// <para>
        /// **x 为什么是 15.5 而不是巡逻线中点 20.86**：走中点那条直线要跨过楼梯区（x 16.5..19.5），
        /// `WalkTo` 在 5 秒内走不到（实测「走到 (20.86, 7.6) 超时」）。15.5 与 <see cref="RouteToPatrol"/>
        /// 的终点同 x，只需再往北 0.4 米，全程无遮挡；到怪背后最远约 12.5 米，潜行 1.5 m/s 约 8.3 秒
        /// （绕背窗口已相应放到 14 秒）。
        /// </para>
        /// </summary>
        protected static readonly Vector2 PatrolLookout = new Vector2(15.5f, 7.6f);

        /// <summary>走楼梯所在的场景 x：在楼梯宽度 16.5..19.5 内、离东沿留出胶囊半径。</summary>
        protected const float StairsLaneX = 18.5f;

        /// <summary>楼梯口地面离第 1 级台阶中心的距离（第 1 级 z 6..6.8，胶囊半径 0.3，站在 z≈5.5 仍是平地）。</summary>
        protected const float StairsFootOffset = 0.9f;

        /// <summary>从 <see cref="RouteToPatrol"/> 终点的空地下到楼梯口前的拐点：x 16 离第 1 级台阶西沿（16.5）留出胶囊半径，z 5.5 在台阶南沿之外。</summary>
        protected static readonly Vector2 StairsCorner = new Vector2(16f, 5.5f);

        /// <summary>
        /// 巡逻怪朝东走过这个 x 之后，楼梯口在它视野外的时间窗 ≥ 2.7 秒：楼梯口 z≈5.5 离巡逻线只有 2.1 米，怪物朝东走时会落进视野锥；
        /// 过了 x 16.3 它要么朝东看不到楼梯口（夹角 &gt; 37.5°）、要么折返朝西背对楼梯口，直到再走回 x 13.86 掉头，
        /// 这段时间足够玩家走过楼梯第 3 级（z ≥ 7.6，此后怎么都进不了警戒半径）。
        /// </summary>
        protected const float StairsSafeMonsterX = 16.3f;

        /// <summary>长者 NPC 的物体名（对白 1001 的发起者，站在出生点东边 3 米）。</summary>
        protected const string ElderName = "Npc_Elder";

        /// <summary>站在长者西侧 1.2 米（交互半径 2 内，且最近的 NPC 就是长者）。</summary>
        protected static readonly Vector2 ElderStandOffset = new Vector2(-1.2f, 0f);

        /// <summary>
        /// <see cref="StrikeMonster"/> 里按攻击键被逻辑帧采到的累计次数（按下后进了攻击冷却）。
        /// 写进检查点文字，区分「没按上」与「按上了没打中」。按实例累计（同一回放类的多条用例共用一个实例）。
        /// </summary>
        protected int StrikeSwings { get; private set; }

        /// <summary>
        /// 从标题「开始」进 SampleScene，再等遭遇逻辑激活（<c>EncounterStep.IsActive</c>）且 <paramref name="resolveServices"/> 为真；
        /// 之后仍取不到服务就 <c>Assert.Fail(<paramref name="missingMessage"/>)</c>（前置条件不成立，后续步骤全是噪音）。
        /// 最后把虚拟手柄与键盘建齐、摇杆回中，等两帧。
        /// </summary>
        /// <param name="readyWhat">就绪等待在报告里的描述，如「遭遇逻辑在跑、输入服务与玩家 / 怪物模型可用」。</param>
        /// <param name="resolveServices">取服务并赋给调用方字段、返回是否全部取到；等待期间逐帧调用，结束后再调一次判定。</param>
        /// <param name="missingMessage">取不到服务时的失败文字。</param>
        protected IEnumerator EnterDemoWorld(string readyWhat, Func<bool> resolveServices, string missingMessage)
        {
            yield return EnterWorldFromTitle(DemoBootTimeoutSeconds, DemoEnterTimeoutSeconds);
            yield return WaitUntil(readyWhat, () =>
            {
                bool resolved = resolveServices();
                EncounterStep step = ResolveService<EncounterStep>();
                return step != null && step.IsActive && resolved;
            }, DemoEnterTimeoutSeconds);

            if (!resolveServices())
            {
                Assert.Fail(missingMessage);
            }

            Input.Prime();
            Input.ReleaseStick();
            yield return null;
            yield return null;
        }

        /// <summary>按顺序逐点 <see cref="WalkTo"/>；某段超时照 WalkTo 口径记失败，继续走下一段。</summary>
        /// <param name="waypoints">途经点（玩家逻辑坐标），如 <see cref="RouteToPatrol"/>。</param>
        /// <param name="stopDistance">每个途经点离多近算到（米）。</param>
        /// <param name="timeoutPerLeg">每段最多走多久（真实秒）。</param>
        protected IEnumerator WalkRoute(IReadOnlyList<Vector2> waypoints, float stopDistance = 1f, float timeoutPerLeg = 10f)
        {
            for (int i = 0; i < waypoints.Count; i++)
            {
                yield return WalkTo(waypoints[i], stopDistance, timeoutPerLeg);
            }
        }

        /// <summary>
        /// 记一步「切到奔跑，沿北侧绕开村口演出触发区，走到巡逻线北侧观察点」：按走跑键切跑 → <see cref="RouteToPatrol"/>（每点 0.4 米 / 8 秒）
        /// → <see cref="PatrolLookout"/>（0.3 米 / 5 秒）→ 再按走跑键切回步行。需要先 <see cref="EnterDemoWorld"/>。
        /// </summary>
        protected IEnumerator GoToPatrolLookout()
        {
            IInputService inputService = ResolveService<IInputService>();
            PlayerModel player = ResolveService<PlayerModel>();
            yield return Step("切到奔跑，沿北侧绕开村口演出触发区，走到巡逻线北侧观察点", null, 0f);
            if (inputService == null || inputService.Actions == null || player == null)
            {
                Debug.LogWarning($"{ShowcaseOptions.Prefix}[{Module}] GoToPatrolLookout 取不到输入服务 / PlayerModel，没法走");
                yield break;
            }

            yield return Input.PressUntil(inputService.Actions.Gameplay.Run, () => player.IsRunning);
            yield return WalkRoute(RouteToPatrol, 0.4f, 8f);
            yield return WalkTo(PatrolLookout, 0.3f, 5f);
            yield return Input.PressUntil(inputService.Actions.Gameplay.Run, () => !player.IsRunning);
        }

        /// <summary>
        /// 追上巡逻怪并打中一下：摇杆逐帧朝怪物推到 0.7 米内，等攻击冷却转好，<see cref="FaceMonster"/> 对准朝向
        /// （普通攻击要求目标在朝向前方半圆、1 米内），再按攻击键（<see cref="ShowcaseInputDriver.PressUntil"/>，最多 0.3 秒）；
        /// 按下被采到就 <see cref="StrikeSwings"/> +1。没打中（怪物走开、朝向没对准）就重来，
        /// 直到怪物掉血、任一方死亡或满 <paramref name="timeout"/> 真实秒。不记报告条目，调用方随后自己 Check 结果。
        /// </summary>
        protected IEnumerator StrikeMonster(float timeout = 5f)
        {
            IInputService inputService = ResolveService<IInputService>();
            PlayerModel player = ResolveService<PlayerModel>();
            MonsterModel monster = ResolveService<MonsterModel>();
            if (inputService == null || inputService.Actions == null || player == null || monster == null)
            {
                Debug.LogWarning($"{ShowcaseOptions.Prefix}[{Module}] StrikeMonster 取不到输入服务 / PlayerModel / MonsterModel，没法出手");
                yield break;
            }

            int before = monster.Health;
            float deadline = Time.realtimeSinceStartup + timeout;
            while (monster.Health == before && monster.Health > 0 && player.Health > 0
                   && Time.realtimeSinceStartup < deadline)
            {
                Vector2 diff = monster.Position - player.Position;
                if (diff.magnitude > 0.7f)
                {
                    Input.SetStick(diff.normalized);
                    yield return null;
                    continue;
                }

                Input.ReleaseStick();
                while (player.AttackCooldownLeft > 0f && Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }

                yield return FaceMonster();
                yield return Input.PressUntil(inputService.Actions.Gameplay.Attack, () => player.AttackCooldownLeft > 0f, 0.3f);
                if (player.AttackCooldownLeft > 0f)
                {
                    StrikeSwings++;
                }

                float settle = Time.realtimeSinceStartup + 0.2f;
                while (monster.Health == before && Time.realtimeSinceStartup < settle)
                {
                    yield return null;
                }
            }

            Input.ReleaseStick();
        }

        /// <summary>
        /// 攻击前对准巡逻怪：敌对怪物会一直走到玩家脚下，两者几乎重合时「目标在朝向前方半圆」判不准（实测连打落空、被反杀），
        /// 所以贴得太近（&lt; 0.3 米）先背向它退 0.12 秒拉开半步，再朝它轻推 0.06 秒（摇杆 0.3）把朝向对准，最后松杆、等一帧。
        /// </summary>
        protected IEnumerator FaceMonster()
        {
            PlayerModel player = ResolveService<PlayerModel>();
            MonsterModel monster = ResolveService<MonsterModel>();
            if (player == null || monster == null)
            {
                yield return null;
                yield break;
            }

            Vector2 gap = monster.Position - player.Position;
            if (gap.magnitude < 0.3f)
            {
                Vector2 away = gap.magnitude > 0.01f ? -gap.normalized : -player.Facing;
                float backUntil = Time.realtimeSinceStartup + 0.12f;
                while (Time.realtimeSinceStartup < backUntil)
                {
                    Input.SetStick(away);
                    yield return null;
                }
            }

            float faceUntil = Time.realtimeSinceStartup + 0.06f;
            while (Time.realtimeSinceStartup < faceUntil)
            {
                Vector2 toMonster = monster.Position - player.Position;
                if (toMonster.magnitude > 0.01f)
                {
                    Input.SetStick(toMonster.normalized * 0.3f);
                }

                yield return null;
            }

            Input.ReleaseStick();
            yield return null;
        }
    }
}
