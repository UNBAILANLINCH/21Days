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

        /// <summary>
        /// 离怪物多近时改按住潜行（米）：要留出近身感知半径 1.5 之外的余量。
        /// 2026-10-07 由 2 米放到 3 米——巡逻点修正到世界坐标 (13.86..21.86, z 3.4) 之后，怪真的贴在观察点
        /// 南边 4.2 米，玩家从北侧绕到它背后时 2 米只留 0.5 米余量，会在按下潜行前先踩进 1.5 米的近身圈
        /// （这条用例因此从"怪在 10 米外、怎么走都不会被发现"的假绿变成真红）。
        /// </summary>
        private const float SneakFromDistance = 3f;

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
            // 诊断：列出**运行时**所有 PatrolPoint* 的世界坐标——磁盘上的 localPosition 与 PatrolPositions()
            // 的读数对不上时，靠它分清是「读到了别的物体」还是坐标系在某个环节被换了。
            string patrolTransformsDiag = "";
            foreach (Transform t in UnityEngine.Object.FindObjectsOfType<Transform>())
            {
                if (t.name.StartsWith("PatrolPoint"))
                {
                    patrolTransformsDiag += $"{t.name}@{t.position} ";
                }
            }
            if (points.Length < 2)
            {
                Assert.Fail("SampleScene 的 EncounterSceneView 至少要两个巡逻点，才能演巡逻折返");
            }

            float minX = Mathf.Min(points[0].x, points[1].x) - 0.05f;
            float maxX = Mathf.Max(points[0].x, points[1].x) + 0.05f;
            bool stayedOnSegment = true;
            string patrolDiag = "还没取到";
            Vector2 origin = Vector2.zero;
            yield return Step("站在观察点，看怪物沿巡逻点走动", () => origin = monster.Position, 0f);
            yield return Check("怪物在巡逻（走或停顿张望），警戒条为 0（状态色灰）",
                () => IsPatrolling() && monster.Alert <= 0f, 2f);
            yield return Check("怪物沿巡逻线走出 1 米以上", () => Vector2.Distance(origin, monster.Position) >= 1f, 8f);
            yield return Snapshot("巡逻位移");

            // 补前提：玩家走到观察点的路上可能短暂惊到怪，之后它会有一段「走回巡逻轨道」的行程
            // （同样是 PatrolWalk，但朝向斜着指向 waypoint，不在两巡逻点之间），那种状态下测「端点掉头」不成立。
            // 这里先等它真的回到巡逻段上再开始测——补前提，不是放宽判定。
            yield return WaitUntil("怪物回到两巡逻点之间",
                () => monster.Position.x >= minX && monster.Position.x <= maxX, 10f);

            float heading = 0f;
            yield return Step("继续看它走到巡逻端点", () => heading = Mathf.Sign(monster.Facing.x), 0f);
            yield return Check("怪物到端点后掉头往回走，全程不离开两巡逻点之间",
                () =>
                {
                    if (monster.Position.x < minX || monster.Position.x > maxX)
                    {
                        stayedOnSegment = false;
                    }

                    // 诊断快照：把三个判据的实时取值记下来，失败时随报告输出（不再靠截图猜朝向）。
                    patrolDiag = $"点数={points.Length} 点0=({points[0].x:F2},{points[0].y:F2})"
                                 + $" 点1=({points[1].x:F2},{points[1].y:F2})"
                                 + $" x={monster.Position.x:F2} 段=[{minX:F2},{maxX:F2}] 在段内={stayedOnSegment}"
                                 + $" 巡逻中={IsPatrolling()} 模式={monster.Mode} facingX={monster.Facing.x:F2}"
                                 + $" sign={Mathf.Sign(monster.Facing.x)} heading={heading}";

                    return stayedOnSegment && IsPatrolling() && Mathf.Sign(monster.Facing.x) != heading;
                },
                20f); // 8 米巡逻线（2026-10-07 由 4 米拉长）：单程 4 秒 + 端点停顿 2 秒
            yield return Step($"折返判据快照：{patrolDiag} ‖ 运行时 PatrolPoint：{patrolTransformsDiag}", null, 0f);
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

        /// <summary>
        /// 2026-10-07 内容改动连锁：demo 场景那只怪换成了**市令**（`monster_species` 1003 → `yao` 3），
        /// 设计原文给它的 `defeat_method` 是「暗杀」（`06_怪物分层.md:144`；同文 `:121` R9 的口径是
        /// 「只能暗杀」填 `killable = false`），所以 <c>MonsterRules.ApplyDamage</c> 对它**一律拒伤**——
        /// 常规攻击打不死它。这是 S3「本体打不过任何怪」的设计意图，不是缺陷。
        /// <para>
        /// 本用例因此演这条边界本身：**打上去没有伤害，但它照样挨打转敌对、照样追打你**。
        /// 旧版「生命归零 → 状态 Dead → 倒下后不再移动」的断言随内容退场，规则本身仍有覆盖：
        /// 常规击杀致死走 EditMode 的 <c>MonsterRulesTests</c>（用可击杀的种类），
        /// 而这只怪的死亡途径是绕背处决，由 <c>StealthShowcase</c> 的回放演。
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator Attacked_ImmuneToNormalAttack_StillTurnsHostile()
        {
            yield return EnterWorld();
            yield return GoToPatrolLookout();

            int healthBefore = monster.Health;
            yield return Step("走向怪物，按攻击键打它一下", null, 0f);
            yield return StrikeMonster();
            yield return Check($"怪物生命仍是 {healthBefore}（只能暗杀的怪对常规攻击拒伤），但挨打后转敌对（状态色变红；出手 {StrikeSwings} 次）",
                () => monster.Health == healthBefore && monster.Mode == MonsterMode.Hostile, 2f);
            yield return Snapshot("打不动：拒伤但转敌对");

            yield return Step("攻击冷却一好就接着打，连打几下", null, 0f);
            for (int i = 1; i < 3 && player.Health > 0; i++)
            {
                yield return StrikeMonster();
            }

            yield return Check($"连打之后生命依然是 {healthBefore}、也没有进入 Dead（出手 {StrikeSwings} 次；玩家剩 {player.Health} 血）——它只能靠绕背处决解决",
                () => monster.Health == healthBefore && monster.Mode != MonsterMode.Dead, 2f);
            yield return Snapshot("常规攻击解决不了它");
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
