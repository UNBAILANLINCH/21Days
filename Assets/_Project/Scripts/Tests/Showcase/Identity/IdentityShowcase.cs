// 职责：身份（S1 换皮与附身）回放——借到一个身份之后，敌对怪物照样红着脸贴上来，但**不出手**；
//   身份一失效，它立刻恢复攻击。S1 的核心验收点就是「身份生效中 → 敌人不攻击」，这条回放把它演在 Game 视图里。
// 舞台是 SampleScene，走 Boot 真实流程（标题「开始」进场）：虚拟手柄推摇杆走北侧路线到巡逻线北侧观察点，
//   再走向场景里那只真实巡逻怪（enerme）。身份三件套（IdentityRules / IdentityState / IdentityLedger）
//   **全部从容器取**——MonsterInstaller 的构建回调已经把容器里那只 IdentityState 绑给 EncounterStep
//   （MonsterInstaller.cs 的 builder.RegisterBuildCallback），所以这里借身份就是正式流程里借身份，
//   不是回放自己 new 一套规则再手工 BindIdentity。取不到就直接 Assert.Fail，不静默退到手工线路。
// 走哪条失效路径：**主动退出**（IdentityRules.TryExit）。理由有两条：
//   1. 原文只写了「触发规则 → 被发现 → 变回原主」（`01_换皮与附身.md:164` R25），主动退出的操作原文没写
//      （`:165` R26 [待定]），IdentityRules.TryExit 按「可退出、进冷却」处理 [推断]——它在真实容器里可用；
//   2. **时限到期这条路本波走不通**：`IdentityRules.AdvanceIdentity` 在 Runtime 下一个调用方都没有
//      （2026-10-07 全工程检索：只有 EditMode 测试调它），身份时限永远不会自己走完。
//      这条缺口列在交付报告的「建议补丁」里，回放**不替它兜底**（在回放里手推 AdvanceIdentity 会把缺口盖住）。
// 身份内容：`IdentityConfig.asset` 的 definitions 本波补了两条**占位**定义（都统面具 / 执事皮），
//   本回放用 `dutong`（都统面具）——出处 `01_换皮与附身.md:85`（sp03「阶段九个 · B 特殊怪物 · 都统」原句
//   「击杀后获得都统面具，可隐匿在巡逻队中」）与 `:152` R19；面具按「持有即生效」型理解，
//   与资产里 defaultDurationSeconds = 0（= 无时限）自洽。数值与内容整批待策划拍板（`00_功能总览.md` §8.1 #2）。
// 受伤预算：玩家 3 点血（PlayerConfig.maxHealth），怪物一次 1 点、冷却 1 秒（MonsterConfig）。
//   本回放故意挨两次（本体一次 + 身份失效后一次），留 1 点血活着收尾。
using System.Collections;
using Game.Core.Input;
using Game.Identity;
using Game.Monster;
using Game.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests.Showcase.Identity
{
    [Category("Showcase")]
    public sealed class IdentityShowcase : ShowcaseScenario
    {
        /// <summary>演示用的身份 id（占位内容，见文件头）：`IdentityConfig.asset` 的 definitions 第一条。</summary>
        private const string DemoIdentityId = "dutong";

        /// <summary>身份生效期间盯着看多久（真实秒）：怪物攻击冷却 1 秒，2.5 秒内不设防至少会挨两下。</summary>
        private const float NoAttackWatchSeconds = 2.5f;

        /// <summary>走到怪物正前方多远（米）：进敌对半径 2 之内让它转敌对，同时留在攻击距离 0.8 之外。</summary>
        private const float HostileApproachDistance = 1.5f;

        private IInputService inputService;
        private PlayerModel player;
        private MonsterModel monster;
        private IdentityRules identityRules;
        private IdentityState identityState;
        private IdentityLedger identityLedger;
        private IdentityId demoIdentity;

        protected override string Module => "Identity";

        /// <summary>世界由流程加载（标题「开始」→ MonsterEncounterState → SampleScene），基类不再叠加载一份。</summary>
        protected override string ScenePath => null;

        [UnityTest]
        public IEnumerator EnterIdentity_StopsEnemyAttack_ExitRestoresIt()
        {
            yield return EnterWorld();
            yield return GoToPatrolLookout();

            // ── 第 4 步：走进它正前方，让它转敌对 ──────────────────────────────────
            // 站到「怪物位置 + 朝向 × 1.5 米」：进敌对半径 2（红区）且落在视野锥里，它才会转敌对；
            // 站它背后或侧面是进不了红区的（MonsterRules.Sense 的红区判定要求 inCone），会一直跟着它绕。
            yield return Step("绕到巡逻怪正前方，等它发现你、转敌对（状态色变红）", null, 0f);
            yield return WaitUntil("贴到怪物正前方且它转敌对", () =>
            {
                if (monster.Mode == MonsterMode.Hostile)
                {
                    Input.ReleaseStick();
                    return true;
                }

                Vector2 ahead = monster.Position + monster.Facing * HostileApproachDistance - player.Position;
                Input.SetStick(ahead.magnitude > 0.15f ? ahead.normalized : Vector2.zero);
                return false;
            }, 10f);
            Input.ReleaseStick();
            yield return Check("怪物转敌对（状态色变红）、身份还没借（本体）",
                () => monster.Mode == MonsterMode.Hostile && !identityState.IsInEffect);

            // 本体先挨一下：这是后面「恢复攻击」的对照——同一个怪物、同一个距离，差别只在身份生效不生效。
            int healthBeforeIdentity = player.Health;
            yield return Check($"本体贴着敌对怪物：它会出手（生命 {healthBeforeIdentity} 起往下掉）",
                () => player.Health < healthBeforeIdentity, 4f);

            // ── 第 5 步：借身份（hold 0：挨打之后立刻借，别让中间那 1.5 秒停顿再挨一下）──
            IdentityEnterResult enterResult = IdentityEnterResult.InvalidId;
            yield return Step("借入身份：都统面具（占位内容，出处 01_换皮与附身.md:85 / :152）",
                () => enterResult = identityRules.TryEnter(identityState, identityLedger, demoIdentity), 0f);
            yield return Check($"身份生效中（TryEnter 返回 {enterResult}，来源应为面具）",
                () => enterResult == IdentityEnterResult.Entered
                      && identityState.IsInEffect
                      && identityState.CurrentOrigin == IdentityOrigin.Mask);

            // ── 第 6 步：身份生效中，盯着看它出不出手 ────────────────────────────────
            int healthInEffect = player.Health;
            bool damagedWhileInEffect = false;
            yield return Step($"身份生效中，贴着这只敌对怪物站 {NoAttackWatchSeconds} 秒", null, 0f);
            float watchUntil = Time.realtimeSinceStartup + NoAttackWatchSeconds;
            yield return WaitUntil($"过去 {NoAttackWatchSeconds} 秒", () =>
            {
                if (player.Health < healthInEffect)
                {
                    damagedWhileInEffect = true;
                }

                return Time.realtimeSinceStartup >= watchUntil;
            }, NoAttackWatchSeconds + 1f);
            yield return Check($"身份生效期间一次都没挨打（生命保持 {healthInEffect}），怪物仍然敌对",
                () => !damagedWhileInEffect && player.Health == healthInEffect
                      && identityState.IsInEffect && monster.Mode == MonsterMode.Hostile);
            yield return Snapshot("身份生效中，怪物不出手");

            // ── 第 7 步：退出身份（主动退出），验证它恢复攻击 ────────────────────────
            // 基准血量必须在**退出之前**取：身份一没，怪物的下一 tick 就打进来了（它的攻击冷却一直在走，
            // 生效期间只是被 IdentityAttackRules 拦住），退出后再取就等于把第一下挨打算进基准里，检查点必红。
            // 2026-10-07 第一轮就是这么红的（报告 Logs/verify/identity/20261007-060415），改成退出前取。
            int healthBeforeExit = player.Health;
            IdentityExitReason exitReason = IdentityExitReason.None;
            yield return Step("摘下面具：主动退出身份（01_换皮与附身.md:165 R26 原文没写主动退出，占位口径）",
                () => exitReason = identityRules.TryExit(identityState), 0f);
            yield return Check($"身份已退出（TryExit 返回 {exitReason}，回到本体）",
                () => exitReason == IdentityExitReason.Voluntary && !identityState.IsInEffect && !identityState.IsBorrowing);

            yield return Check($"身份一失效，怪物立刻恢复攻击（生命从 {healthBeforeExit} 往下掉，玩家仍活着）",
                () => player.Health < healthBeforeExit && player.Health > 0, 4f);
            yield return Snapshot("身份失效后恢复挨打");
        }

        // ───────────────────────── 进场与驱动 ─────────────────────────

        /// <summary>
        /// 标题「开始」进世界（EnterDemoWorld），等容器里的输入服务、玩家 / 怪物模型与身份三件套都可解析。
        /// 身份三件套取不到就是接线不够（IdentityInstaller 没挂上 Boot / 没拖配置），前置条件不成立，直接中断。
        /// </summary>
        private IEnumerator EnterWorld()
        {
            yield return EnterDemoWorld("遭遇逻辑在跑、输入服务与玩家 / 怪物 / 身份服务可用", () =>
            {
                inputService = ResolveService<IInputService>();
                player = ResolveService<PlayerModel>();
                monster = ResolveService<MonsterModel>();
                identityRules = ResolveService<IdentityRules>();
                identityState = ResolveService<IdentityState>();
                identityLedger = ResolveService<IdentityLedger>();
                return inputService != null && inputService.Actions != null
                       && player != null && monster != null
                       && identityRules != null && identityState != null && identityLedger != null;
            }, "进世界后容器里取不到 PlayerModel / MonsterModel / IInputService / IdentityRules / IdentityState / IdentityLedger，"
               + "后续步骤无法驱动（检查 Boot 场景 GameBootstrap 上的 IdentityInstaller 与它的 Config 字段）");

            demoIdentity = IdentityId.From(DemoIdentityId);
            if (!identityRules.Catalog.TryGet(demoIdentity, out _))
            {
                Assert.Fail($"IdentityConfig.asset 的 definitions 里没有身份 \"{DemoIdentityId}\"："
                            + "本回放演示的就是「借一个身份」，表是空的就什么都演不出来。"
                            + "补一条定义（本波补的占位定义见 identity-module-guide.md 的「验证入口」一节）。");
            }
        }
    }
}
