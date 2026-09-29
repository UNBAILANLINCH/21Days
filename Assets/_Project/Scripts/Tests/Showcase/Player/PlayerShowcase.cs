// 职责：玩家自己的动作回放——走、走跑切换、潜行、伪装开关、普通攻击、受击掉血与死亡。
// 舞台是 SampleScene，走 Boot 真实流程（标题「开始」进场），虚拟手柄推摇杆、虚拟键盘按动作驱动场景里的真实玩家；
//   读状态只读容器里的 PlayerModel / MonsterModel，不 new 规则、不瞬移、不直接调 PlayerRules。
// 本次重写理由：原版不加载 Boot，自己 new PlayerRules / MonsterRules + 默认值 ScriptableObject、代码生成占位图，
//   与 demo 场景内容脱节（看不到 SampleScene 里的纸片、镜头、碰撞），验的是 PlayerConfig 默认值而不是场景里调好的数值资产。
// 死亡怎么演：真实受击到死。PlayerConfig.asset 生命 3、MonsterConfig.asset 伤害 1 / 冷却 1 秒，被敌对怪物贴身约 3 秒即死，
//   远小于 10 秒，所以不需要用 PlayerRules.ApplyDamage 补刀。
//   2026-09-28 起（PRP/mirror-core）战败由镜模块接管：第三下击中 = 镜碎，MirrorCrackPresenter 结束本场遭遇、打开只有「镜碎」二字的
//   镜碎页，按确认后重进遭遇（回出生点、生命回满）。本用例断言镜碎页出现，收尾前按 UI/Submit 走完重开，
//   不再按旧的「死亡后场景不切换」断言；镜碎页本身的细节（按键保护、进度保留）归 MirrorShowcase。
// 路线：村口演出触发区正挡在出生点与巡逻怪之间，去怪物那边一律走北侧路线到巡逻线北侧观察点（GoToPatrolLookout）；
//   坐标与理由见 Framework/ShowcaseScenario.DemoScene.cs（RouteToPatrol / PatrolLookout），追怪出手用同文件的 StrikeMonster。
using System.Collections;
using Game.Core.Input;
using Game.Core.UI;
using Game.Mirror;
using Game.Monster;
using Game.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests.Showcase.Player
{
    [Category("Showcase")]
    public sealed class PlayerShowcase : ShowcaseScenario
    {
        /// <summary>
        /// 走 / 跑对比的推杆时长。步行 3、奔跑 5：出生点 (-4,3.4) 右走本应 2.1 米，但长者（x -1）挡人，实测停在 x≈-2.12（1.88 米）；
        /// 再左跑 3.5 米，终点 x≈-5.62，碰不到左墙（x -6，胶囊半径 0.3 停在 -5.7）。奔跑仍明显大于步行，比较照样成立。
        /// </summary>
        private const float CompareSeconds = 0.7f;

        /// <summary>镜碎页交回后重进遭遇（卸载并重载场景）的上限（真实秒），同 MirrorShowcase。</summary>
        private const float RestartTimeoutSeconds = 20f;

        private IInputService inputService;
        private PlayerModel player;
        private MonsterModel monster;
        private PlayerConfig playerConfig;
        private MonsterConfig monsterConfig;
        private IUIService ui;
        private EncounterStep step;
        private MirrorConfig mirrorConfig;

        protected override string Module => "Player";

        /// <summary>世界由流程加载（标题「开始」→ MonsterEncounterState → SampleScene），基类不再叠加载一份。</summary>
        protected override string ScenePath => null;

        [UnityTest]
        public IEnumerator WalkThenRunToggle_RunCoversMoreGround()
        {
            yield return EnterWorld();
            yield return Check("初始为步行模式", () => !player.IsRunning, 3f);

            float walked = 0f;
            Vector2 start = player.Position;
            yield return Step($"摇杆向右推 {CompareSeconds} 秒（步行）", null, 0f);
            yield return Walk(Vector2.right, CompareSeconds);
            walked = Vector2.Distance(start, player.Position);
            yield return Check($"步行有明显位移（{walked:0.00} 米）", () => walked > 1f);
            yield return Snapshot("步行");

            yield return Step("按一下走跑键（Gameplay/Run）", null, 0f);
            yield return Input.PressUntil(inputService.Actions.Gameplay.Run, () => player.IsRunning);
            yield return Check("切到奔跑模式", () => player.IsRunning, 2f);

            float ran = 0f;
            yield return Step($"同样推 {CompareSeconds} 秒，这次向左（奔跑）", () => start = player.Position, 0f);
            yield return Walk(Vector2.left, CompareSeconds);
            ran = Vector2.Distance(start, player.Position);
            yield return Check($"奔跑位移（{ran:0.00} 米）明显大于步行（{walked:0.00} 米）", () => ran > walked * 1.3f);
            yield return Snapshot("奔跑");

            yield return Step("再按一下走跑键", null, 0f);
            yield return Input.PressUntil(inputService.Actions.Gameplay.Run, () => !player.IsRunning);
            yield return Check("回到步行模式", () => !player.IsRunning, 2f);
            yield return Snapshot("回到步行");
        }

        [UnityTest]
        public IEnumerator SneakHeld_MovesSlowerThenReleases()
        {
            yield return EnterWorld();

            yield return Step("按住潜行键（Gameplay/Sneak）", null, 0f);
            yield return Input.Hold(inputService.Actions.Gameplay.Sneak);
            yield return Check("进入潜行（状态栏「潜行 True」，状态色变青）", () => player.IsSneaking, 2f);

            float sneaked = 0f;
            bool heldThroughout = true;
            Vector2 start = player.Position;
            yield return Step("按住潜行，摇杆向右推 0.8 秒", null, 0f);
            float until = Time.realtimeSinceStartup + 0.8f;
            while (Time.realtimeSinceStartup < until)
            {
                Input.SetStick(Vector2.right);
                yield return null;
                heldThroughout &= player.IsSneaking;
            }

            Input.ReleaseStick();
            yield return WaitPlayerStable();
            sneaked = Vector2.Distance(start, player.Position);
            // 潜行 1.5、步行 3：0.8 秒应走约 1.2 米，远小于步行的 2.4 米。
            yield return Check($"全程保持潜行（实测 {(heldThroughout ? "是" : "否")}），在走但明显慢于步行（{sneaked:0.00} 米，步行同时长约 {playerConfig.MoveSpeed * 0.8f:0.0} 米）",
                () => heldThroughout && sneaked > 0.4f && sneaked < playerConfig.MoveSpeed * 0.8f * 0.75f);
            yield return Snapshot("潜行移动");

            yield return Step("松开潜行键", null, 0f);
            yield return Input.Release(inputService.Actions.Gameplay.Sneak);
            yield return Check("退出潜行", () => !player.IsSneaking, 2f);
            yield return Snapshot("松开潜行");
        }

        [UnityTest]
        public IEnumerator DisguiseKey_TogglesDisguiseOnAndOff()
        {
            yield return EnterWorld();
            yield return Check("初始未伪装", () => !player.IsDisguised, 3f);

            yield return Step("按一下伪装键（Gameplay/Disguise）", null, 0f);
            yield return Input.PressUntil(inputService.Actions.Gameplay.Disguise, () => player.IsDisguised);
            yield return Check("开启伪装（状态栏「伪装 True」，状态色变绿）", () => player.IsDisguised, 2f);
            yield return Snapshot("伪装开启");

            yield return Step("再按一下伪装键", null, 0f);
            yield return Input.PressUntil(inputService.Actions.Gameplay.Disguise, () => !player.IsDisguised);
            yield return Check("关闭伪装，状态色恢复", () => !player.IsDisguised, 2f);
            yield return Snapshot("伪装关闭");
        }

        [UnityTest]
        public IEnumerator AttackThenTakeHits_HealthDropsToDeath()
        {
            yield return EnterWorld();
            yield return GoToPatrolLookout();

            int monsterMax = monsterConfig.MaxHealth;
            yield return Step("走向巡逻怪，按攻击键挥出一记普通攻击", null, 0f);
            yield return StrikeMonster();
            yield return Check($"怪物生命 {monsterMax} → {monsterMax - 1}，被打后转敌对（状态色变红；出手 {StrikeSwings} 次）",
                () => monster.Health == monsterMax - 1 && monster.Mode == MonsterMode.Hostile, 2f);
            yield return Snapshot("攻击命中");

            int playerMax = playerConfig.MaxHealth;
            yield return Step("站着不动，任由敌对的怪物贴身攻击", () => Input.ReleaseStick(), 0f);
            yield return Check($"玩家受击掉血（生命低于 {playerMax}）", () => player.Health < playerMax, 5f);
            yield return Snapshot("受击掉血");
            yield return Check("继续挨打直到生命归零（第三下击中）", () => player.Health == 0, 8f);
            yield return Check($"镜碎：出现只有「{mirrorConfig.ShatterText}」二字的镜碎页，本场遭遇停下",
                () => ui.Get<MirrorShatterView>() != null && !step.IsActive, 3f);

            Vector2 deadAt = player.Position;
            yield return Step("镜碎页上摇杆向右推 0.5 秒", null, 0f);
            yield return Walk(Vector2.right, 0.5f);
            yield return Check("镜碎期间推摇杆也不移动", () => Vector2.Distance(deadAt, player.Position) < 0.01f);
            yield return Snapshot("死亡·镜碎页");

            // 收尾走完重开：镜碎页开着时 Gameplay 图是关的，按的是 UI/Submit；页面出现后有按键保护时间。
            yield return Step("等按键保护时间过去，按确认键（Enter）交回镜碎页", null, 0f);
            yield return new WaitForSecondsRealtime(mirrorConfig.ShatterInputDelay + 0.15f);
            for (int attempt = 0; attempt < 3 && ui.Get<MirrorShatterView>() != null && !step.IsActive; attempt++)
            {
                yield return Input.Press(inputService.Actions.UI.Submit);
                float until = Time.realtimeSinceStartup + 1.5f;
                while (ui.Get<MirrorShatterView>() != null && !step.IsActive && Time.realtimeSinceStartup < until)
                {
                    yield return null;
                }
            }

            yield return WaitUntil("重开本场：场景重进、遭遇重新开始、生命回满",
                () => step.IsActive && player.Health == playerMax, RestartTimeoutSeconds);
            yield return Check("重开后镜碎页已关，玩家生命回满、可以再走动",
                () => step.IsActive && player.Health == playerMax && ui.Get<MirrorShatterView>() == null, 10f);
            yield return Snapshot("重开·回到出生点");
        }

        // ───────────────────────── 进场与驱动 ─────────────────────────

        /// <summary>标题「开始」进世界（EnterDemoWorld），等容器里的输入服务、玩家 / 怪物模型与配置可用。</summary>
        private IEnumerator EnterWorld()
        {
            yield return EnterDemoWorld("遭遇逻辑在跑、输入服务与玩家 / 怪物模型可用", () =>
            {
                inputService = ResolveService<IInputService>();
                player = ResolveService<PlayerModel>();
                monster = ResolveService<MonsterModel>();
                playerConfig = ResolveService<PlayerConfig>();
                monsterConfig = ResolveService<MonsterConfig>();
                ui = ResolveService<IUIService>();
                step = ResolveService<EncounterStep>();
                mirrorConfig = ResolveService<MirrorConfig>();
                // PlayerConfig / MonsterConfig / MirrorConfig 是 ScriptableObject，判空只用 != null。
                return inputService != null && inputService.Actions != null
                       && player != null && monster != null && playerConfig != null && monsterConfig != null
                       && ui != null && step != null && mirrorConfig != null;
            }, "进世界后容器里取不到 PlayerModel / MonsterModel / IInputService / IUIService / EncounterStep / 配置"
               + "（含 MirrorConfig：Boot 的 GameBootstrap 应挂 MirrorInstaller），后续步骤无法驱动");
        }
    }
}
