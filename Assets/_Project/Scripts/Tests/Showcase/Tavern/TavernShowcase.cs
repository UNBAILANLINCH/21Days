// 职责：真实饮酒按钮、控制角色减速、灌酒、现有战斗返回与离区清理。
using System.Collections;
using Game.Battle;
using Game.Core.Flow;
using Game.Core.Input;
using Game.Core.Timing;
using Game.Dialogue;
using Game.Narrative;
using Game.ShopPlaytest;
using Game.Tests.Showcase.ShopPlaytest;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
namespace Game.Tests.Showcase.Tavern
{
    [Category("Showcase")]
    public sealed class TavernShowcase : ShopPlaytestShowcase
    {
        protected override string Module => "Tavern";
        [UnityTest]
        public IEnumerator Drink_ExplorationEffectsBattleAndLeaveReset()
        {
            yield return Ready(4);
            yield return Borrow(0);
            yield return Step("自己喝一杯", () => Click("Command0"));
            yield return Check("出现减速与禁潜行状态", () => State.Tavern.PlayerDrunk == 10 && !State.Tavern.CanSneak);
            Vector2 before = State.Taming.GetTarget(State.Taming.CurrentControlId).Model.Position;
            yield return Step("实际移动借来的酒客", null, 0);
            yield return Input.HoldStick(Vector2.right, .5f); Input.ReleaseStick();
            yield return Check("移动角色已减速，实际位移小于正常一秒速度", () =>
            { float distance = Vector2.Distance(before, State.Taming.GetTarget(State.Taming.CurrentControlId).Model.Position); return distance > .1f && distance < 1f; });
            yield return ApproachTarget("shop_boss");
            yield return Step("走近BOSS灌到100", () => { for (int i = 0; i < 10; i++) Click("Command1"); });
            yield return ApproachTarget("shop_customer_a");
            yield return Step("走近客人甲灌到20", () => { for (int i = 0; i < 3; i++) Click("Command2"); });
            yield return Check("场外上限与两客人互不串值", () => State.Tavern.BossDrunk == 100 && State.Tavern.CustomerDrunkFor("shop_customer_a") == 20 && State.Tavern.CustomerDrunkFor("shop_customer_b") == 0 && State.Tavern.BossAwarenessMultiplier == .5f);
            yield return Snapshot("场外醉酒与范围");
            yield return ApproachTarget("shop_boss");
            yield return Step("近距离挑战BOSS，推进开战对白", () => Click("Command3"), 0);
            var dialogue = ResolveService<DialogueService>(); var rules = ResolveService<DialogueRules>(); var input = ResolveService<IInputService>();
            float deadline = Time.realtimeSinceStartup + 20;
            while (dialogue.IsRunning && Time.realtimeSinceStartup < deadline)
            { if (rules.Phase == DialogueSaveData.Phase.AwaitAdvance) yield return Input.Press(input.Actions.Dialogue.Advance); else yield return null; }
            var battle = ResolveService<BattleFlow>(); var presenter = ResolveService<IBattlePresenter>() as BattleScenePresenter;
            yield return Check("现有战斗舞台和界面已打开", () => battle.IsBattleEngaged && presenter != null && presenter.View != null, 15);
            yield return Check("开战醉酒继承现场100，覆盖原配置占位50", () =>
            {
                if (presenter.View == null) return false;
                var bar = FindDeep<BattleBarWidget>(presenter.View.transform, "DrunkBar");
                return bar != null && bar.GetComponentInChildren<TMP_Text>(true).text.StartsWith("醉酒 100");
            }, 5);
            yield return WaitCurtainRevealed(10);
            yield return Snapshot("战斗开场");
            yield return Step("经真实出招键打完本场", null, 0);
            deadline = Time.realtimeSinceStartup + 90;
            while (battle.IsBattleEngaged && Time.realtimeSinceStartup < deadline)
            {
                if (presenter.IsAwaitingCommand && presenter.View != null)
                    yield return Input.Press(presenter.View.IsSkillLit(2) ? input.Actions.Dialogue.Choice3 : input.Actions.Dialogue.Choice1);
                else yield return null;
            }
            yield return Check("收场交还原主场景、输入与暂停", () => !battle.IsBattleEngaged && ResolveService<IGameFlow>().Current is Game.Monster.MonsterEncounterState && !ResolveService<IWorldPauseService>().IsPaused, 10);
            var bossActor = System.Array.Find(Object.FindObjectsOfType<ShopPlaytestActor>(true), actor => actor.StableId == "shop_boss");
            string outcome = ResolveService<NarrativeService>().Capture().Outcome;
            yield return Check("战斗结案结果已消费：胜利退场，失败保留BOSS", () => bossActor != null &&
                (outcome == "Success" ? !bossActor.gameObject.activeSelf : outcome == "Downed" && bossActor.gameObject.activeSelf));
            yield return Check("酒肆结案不写原示例BOSS的击败标记", () => !ResolveService<NarrativeService>().HasStoryFlag("world.sampleboss.defeated"));
            yield return Snapshot("战斗结束回店");
            Vector3 exitStation = State.World.Station(4).position;
            Vector2 exitPosition = new Vector2(exitStation.x, exitStation.z) + MapDirection(Vector3.left) * 5.5f;
            yield return MoveTo(new Vector2(exitPosition.x, State.ControlledPosition.y));
            yield return Check("离区场外醉酒清零、控制人物保留", () => State.Tavern.PlayerDrunk + State.Tavern.BossDrunk + State.Tavern.CustomerDrunkFor("shop_customer_a") == 0 && State.Tavern.CanSneak && State.Taming.CurrentControlId == "shop_guest");
            yield return Snapshot("离区清零");
            yield return CheckPreplacedObjects();
        }
        private IEnumerator ApproachTarget(string id)
        {
            Vector2 target = State.Taming.GetTarget(id).Model.Position;
            Vector2 station = UnityEngine.Object.FindObjectOfType<Game.Monster.EncounterSceneView>().ToLogicPosition(State.World.Station(4).position);
            yield return MoveTo(Vector2.Lerp(target, station, .35f));
        }
    }
}
