// 职责：巡逻怪自己的行为回放——沿巡逻点位移并折返、感知（背后潜行不警戒 / 正面红区转敌对）、追击、攻击玩家、被打死。
// 舞台是 SampleScene，走 Boot 真实流程（标题「开始」进场），虚拟手柄推摇杆、虚拟键盘按动作驱动场景里的真实玩家，
//   怪物由容器里的 MonsterRules 在逻辑 tick 里自己跑；读状态只读 MonsterModel / PlayerModel，不调规则、不瞬移。
// 本次重写理由：原版不加载 Boot，自己 new MonsterRules + 默认值 ScriptableObject、手动喂 MonsterIntent、瞬移玩家、
//   直接调 ApplyDamage，画面是代码生成的占位方块，与 demo 场景里的巡逻怪 enerme、巡逻点、镜头全都脱节，
//   验的也是 MonsterConfig 默认值而不是 MonsterConfig.asset 里调好的数值。
// 站位怎么算：怪物一直在动，「背后」「正面」都按它此刻的位置与朝向实时算——
//   背后 = 等它巡逻停顿（PatrolPause，停 2 秒、朝向不变）时，逐帧朝「位置 − 朝向 × 1 米」推摇杆，离它 2 米内改按住潜行；
//   正面 = 保持潜行站在巡逻线上不动，等它走到端点折返、迎面走来进入 2 米红区。
// 路线：出生点与巡逻怪之间隔着村口演出触发区，一律走北侧路线到巡逻线北侧观察点（GoToPatrolLookout）；
//   坐标与理由见 Framework/ShowcaseScenario.DemoScene.cs（RouteToPatrol / PatrolLookout），追怪出手用同文件的 StrikeMonster。
using System;
using System.Collections;
using Game.Core.Input;
using Game.Monster;
using Game.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests.Showcase.Monster
{
    [Category("Showcase")]
    public sealed class MonsterShowcase : ShowcaseScenario
    {
        /// <summary>背后贴近的目标：怪物身后多远（米），在近身感知半径 1.5 以内。</summary>
        private const float BehindDistance = 1f;

        /// <summary>离怪物多近时改按住潜行（米）：留出近身感知半径 1.5 之外的余量。</summary>
        private const float SneakFromDistance = 2f;

        private IInputService inputService;
        private PlayerModel player;
        private MonsterModel monster;
        private MonsterConfig monsterConfig;

        protected override string Module => "Monster";

        /// <summary>世界由流程加载（标题「开始」→ MonsterEncounterState → SampleScene），基类不再叠加载一份。</summary>
        protected override string ScenePath => null;

        [UnityTest]
        public IEnumerator Patrol_WalksAlongPointsAndTurnsBack()
        {
            yield return EnterWorld();
            yield return GoToPatrolLookout();

            EncounterSceneView view = UnityEngine.Object.FindObjectOfType<EncounterSceneView>();
            Vector2[] points = view == null ? new Vector2[0] : view.PatrolPositions();
            if (points.Length < 2)
            {
                Assert.Fail("SampleScene 的 EncounterSceneView 至少要两个巡逻点，才能演巡逻折返");
            }

            float minX = Mathf.Min(points[0].x, points[1].x) - 0.05f;
            float maxX = Mathf.Max(points[0].x, points[1].x) + 0.05f;
            bool stayedOnSegment = true;
            Vector2 origin = Vector2.zero;
            yield return Step("站在观察点，看怪物沿巡逻点走动", () => origin = monster.Position, 0f);
            yield return Check("怪物在巡逻（走或停顿张望），警戒条为 0（状态色灰）",
                () => IsPatrolling() && monster.Alert <= 0f, 2f);
            yield return Check("怪物沿巡逻线走出 1 米以上", () => Vector2.Distance(origin, monster.Position) >= 1f, 8f);
            yield return Snapshot("巡逻位移");

            float heading = 0f;
            yield return Step("继续看它走到巡逻端点", () => heading = Mathf.Sign(monster.Facing.x), 0f);
            yield return Check("怪物到端点后掉头往回走，全程不离开两巡逻点之间",
                () =>
                {
                    if (monster.Position.x < minX || monster.Position.x > maxX)
                    {
                        stayedOnSegment = false;
                    }

                    return stayedOnSegment && IsPatrolling() && Mathf.Sign(monster.Facing.x) != heading;
                },
                12f);
            yield return Snapshot("巡逻折返");
        }

        [UnityTest]
        public IEnumerator Sense_SneakBehindStaysCalm_FrontRedZoneTurnsHostile()
        {
            yield return EnterWorld();
            yield return GoToPatrolLookout();

            yield return Step("在观察点等怪物停步张望（巡逻停顿，朝向不变）", null, 0f);
            yield return WaitUntil("怪物进入巡逻停顿",
                () => monster.Mode == MonsterMode.PatrolPause && monster.PatrolPauseLeft > 1.2f, 15f);

            bool closeBehind = false;
            bool stayedCalm = true;
            yield return Step("绕到怪物背后，离它 2 米内按住潜行，贴到身后 1 米", null, 0f);
            yield return SneakUpBehind(seen => closeBehind = seen, calm => stayedCalm = calm);
            yield return Check("背后 1.5 米内潜行：怪物仍在巡逻、警戒条始终为 0（状态色保持灰）",
                () => closeBehind && stayedCalm);
            yield return Snapshot("背后潜行不警戒");

            yield return Step("保持潜行站着不动，等怪物折返迎面走来", () => Input.ReleaseStick(), 0f);
            yield return Check("怪物迎面走进 2 米正面红区即转敌对（潜行挡不住正面，状态色变红）",
                () => monster.Mode == MonsterMode.Hostile && player.IsSneaking, 15f);
            yield return Snapshot("正面红区敌对");
            yield return Input.Release(inputService.Actions.Gameplay.Sneak);
        }

        [UnityTest]
        public IEnumerator Hostile_ChasesFleeingPlayerAndHitsIt()
        {
            yield return EnterWorld();
            yield return GoToPatrolLookout();

            yield return Step("迎着怪物走到它正前方", null, 0f);
            yield return WaitUntil("怪物转敌对（状态色变红）", () =>
            {
                if (monster.Mode == MonsterMode.Hostile)
                {
                    Input.ReleaseStick();
                    return true;
                }

                Vector2 ahead = monster.Position + monster.Facing * 1.2f - player.Position;
                Input.SetStick(ahead.magnitude > 0.1f ? ahead.normalized : Vector2.zero);
                return false;
            }, 8f);
            Input.ReleaseStick();

            Vector2 chaseFrom = Vector2.zero;
            yield return Step("转身往北逃 1.2 秒", () => chaseFrom = monster.Position, 0f);
            yield return Walk(Vector2.up, 1.2f);
            yield return Check("怪物追着玩家移动（位移 ≥ 1.5 米），仍然敌对",
                () => Vector2.Distance(chaseFrom, monster.Position) >= 1.5f && monster.Mode == MonsterMode.Hostile, 2f);
            yield return Snapshot("追击");

            int healthBefore = 0;
            yield return Step("停下不动，等怪物追上", () => healthBefore = player.Health, 0f);
            yield return Check("怪物追上后出手，玩家生命 -1", () => player.Health == healthBefore - 1, 5f);
            yield return Snapshot("怪物出手");
        }

        [UnityTest]
        public IEnumerator Attacked_TakesHitsUntilDead()
        {
            yield return EnterWorld();
            yield return GoToPatrolLookout();

            int monsterMax = monsterConfig.MaxHealth;
            yield return Step("走向怪物，按攻击键打它一下", null, 0f);
            yield return StrikeMonster();
            yield return Check($"怪物生命 {monsterMax} → {monsterMax - 1}，挨打后转敌对（状态色变红；出手 {StrikeSwings} 次）",
                () => monster.Health == monsterMax - 1 && monster.Mode == MonsterMode.Hostile, 2f);
            yield return Snapshot("怪物挨打");

            yield return Step("攻击冷却一好就接着打，直到怪物倒下", null, 0f);
            for (int i = 1; i < monsterMax && monster.Health > 0 && player.Health > 0; i++)
            {
                yield return StrikeMonster();
            }

            yield return Check($"怪物生命归零、停止行动（状态 Dead，状态色变黑；出手 {StrikeSwings} 次，玩家剩 {player.Health} 血）",
                () => monster.Health == 0 && monster.Mode == MonsterMode.Dead, 2f);
            Vector2 deadAt = monster.Position;
            yield return Step("原地看一会儿倒下的怪物", null, 0f);
            yield return Check("倒下后不再移动", () => Vector2.Distance(deadAt, monster.Position) < 0.01f, 0f);
            yield return Snapshot("怪物被打死");
        }

        // ───────────────────────── 进场与驱动 ─────────────────────────

        private bool IsPatrolling()
        {
            return monster.Mode == MonsterMode.PatrolWalk || monster.Mode == MonsterMode.PatrolPause;
        }

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

        /// <summary>
        /// 逐帧朝「怪物位置 − 朝向 × 1 米」推摇杆；离怪物 2 米内改按住潜行（按住到用例结束）。
        /// 记录两件事：是否出现过「潜行中、在怪物背后半圆、1.5 米内」的时刻；全程怪物有没有进入警戒 / 敌对。
        /// 出现过背后近身后再原地停 0.8 秒给人看，最多 4 秒。
        /// </summary>
        private IEnumerator SneakUpBehind(Action<bool> reportClose, Action<bool> reportCalm)
        {
            bool sneaking = false;
            bool close = false;
            bool calm = true;
            float closeSince = -1f;
            float deadline = Time.realtimeSinceStartup + 4f;
            while (Time.realtimeSinceStartup < deadline)
            {
                Vector2 toPlayer = player.Position - monster.Position;
                if (!sneaking && toPlayer.magnitude <= SneakFromDistance)
                {
                    yield return Input.Hold(inputService.Actions.Gameplay.Sneak);
                    sneaking = true;
                    continue;
                }

                Vector2 behind = monster.Position - monster.Facing * BehindDistance;
                Vector2 toBehind = behind - player.Position;
                if (toBehind.magnitude <= 0.15f)
                {
                    Input.ReleaseStick();
                }
                else
                {
                    Input.SetStick(toBehind.normalized);
                }

                if (monster.Mode == MonsterMode.Alert || monster.Mode == MonsterMode.Hostile)
                {
                    calm = false;
                }

                if (player.IsSneaking && toPlayer.magnitude <= 1.5f && Vector2.Dot(monster.Facing, toPlayer) < 0f && !close)
                {
                    close = true;
                    closeSince = Time.realtimeSinceStartup;
                }

                if (close && Time.realtimeSinceStartup - closeSince >= 0.8f)
                {
                    break;
                }

                yield return null;
            }

            Input.ReleaseStick();
            reportClose(close);
            reportCalm(calm);
        }
    }
}
