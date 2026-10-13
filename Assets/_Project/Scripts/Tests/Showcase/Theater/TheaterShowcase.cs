// 职责：真实借身份和音游输入走验身、调巡逻、完整演奏、回店与重试；不修改谱面资产。
using System.Collections;
using System.Collections.Generic;
using Game.Core.UI;
using Game.Rhythm;
using Game.Tests.Showcase.ShopPlaytest;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
namespace Game.Tests.Showcase.Theater
{
    [Category("Showcase")]
    public sealed class TheaterShowcase : ShopPlaytestShowcase
    {
        protected override string Module => "Theater";
        [UnityTest]
        public IEnumerator Roles_FourLanePerformanceReturnsAndRetries()
        {
            yield return Ready(0);
            yield return Step("未验身先请求演奏", () => Click("Command2"));
            yield return Check("前置不足，音游未打开", () => !ResolveService<RhythmState>().IsActivityOpen);
            yield return Borrow(0);
            yield return Step("学徒验身", () => Click("Command0"));
            yield return Borrow(1);
            yield return Step("都知调巡逻", () => Click("Command1"));
            yield return Check("验身和巡逻路线已改变", () => State.Theater.Entered && State.Theater.PatrolRedirected);
            yield return Snapshot("巡逻调开");
            yield return Borrow(2);
            yield return Step("乐正打开现有四轨下落音游", () => Click("Command2"), 0);
            var rhythm = ResolveService<RhythmState>();
            yield return WaitUntil("演奏准备完成", () => rhythm.IsActivityOpen && !View.gameObject.activeSelf && ResolveService<IUIService>().Get<RhythmView>() != null, 10);
            var rhythmView = ResolveService<IUIService>().Get<RhythmView>();
            yield return Step("点击开始并按当前谱面真实输入", null, 0);
            yield return ClickWhenReady("开始按钮已生效", () => FindDeep<Button>(rhythmView.transform, "StartButton"), () => rhythm.IsPlaying, 8);
            yield return WaitUntil("倒数开始", () => rhythm.IsPlaying, 8);
            var edges = new List<Edge>();
            for (int i = 0; i < rhythm.Rules.Count; i++)
            {
                edges.Add(new Edge(rhythm.Rules.NoteTime(i) - .02, rhythm.Rules.NoteLane(i), true));
                edges.Add(new Edge(rhythm.Rules.NoteType(i) == RhythmNoteType.Hold ? rhythm.Rules.NoteEnd(i) + .02 : rhythm.Rules.NoteTime(i) + .07, rhythm.Rules.NoteLane(i), false));
            }
            edges.Sort((a, b) => a.Time.CompareTo(b.Time));
            foreach (var edge in edges)
            {
                if (!rhythm.IsPlaying) break;
                yield return WaitUntil("下一个按键时刻", () => !rhythm.IsPlaying || rhythm.SongSeconds >= edge.Time, 8);
                if (!rhythm.IsPlaying) break;
                if (edge.Press) yield return Input.Hold(rhythm.LaneAction(edge.Lane));
                else yield return Input.Release(rhythm.LaneAction(edge.Lane));
            }
            yield return Check("完整演奏达标并返回戏班", () => State.Theater.Completed && !rhythm.IsActivityOpen && View.gameObject.activeSelf, 12);
            yield return Snapshot("演奏完成回店");
            yield return Check("原主场景保持乐正控制与成果", () => State.Theater.Completed && State.CurrentRole == "musician");
            yield return CheckPreplacedObjects();
        }
        [UnityTest]
        public IEnumerator LowScore_AndPauseExit_ReturnToPlayableTheater()
        {
            yield return Ready(0);
            yield return Borrow(0);
            yield return Step("学徒验身", () => Click("Command0"));
            yield return Borrow(1);
            yield return Step("都知调巡逻", () => Click("Command1"));
            yield return Borrow(2);
            yield return Step("乐正演奏", () => Click("Command2"), 0);
            var rhythm = ResolveService<RhythmState>();
            yield return WaitUntil("演奏界面完全就绪", () => rhythm.IsActivityOpen && !View.gameObject.activeSelf, 10);
            var rhythmView = ResolveService<IUIService>().Get<RhythmView>();
            yield return Step("开始后不按键，等待低分结算", null, 0);
            yield return ClickWhenReady("开始已生效", () => FindDeep<Button>(rhythmView.transform, "StartButton"), () => rhythm.IsPlaying, 8);
            yield return Check("低分正常返回戏班，前置保留可重新演奏", () => !rhythm.IsActivityOpen && View.gameObject.activeSelf &&
                !State.Theater.Completed && State.Theater.CanPerform(State.CurrentRole), 55);
            yield return Snapshot("低分回店");
            yield return Step("重新打开演奏并开始", () => Click("Command2"), 0);
            yield return WaitUntil("重开的界面完全就绪", () => rhythm.IsActivityOpen && !View.gameObject.activeSelf, 10);
            rhythmView = ResolveService<IUIService>().Get<RhythmView>();
            yield return ClickWhenReady("重开已生效", () => FindDeep<Button>(rhythmView.transform, "StartButton"), () => rhythm.IsPlaying, 8);
            yield return Step("点暂停，再从暂停页退出演奏", () => FindDeep<Button>(rhythmView.transform, "PlayExit").onClick.Invoke(), 0);
            yield return Check("暂停页已出现", () => rhythm.IsPaused, 4);
            yield return Step("主动退出并回店", () => FindDeep<Button>(rhythmView.transform, "PauseExit").onClick.Invoke(), 0);
            yield return Check("取消返回后仍可操作、未误判通关", () => !rhythm.IsActivityOpen && View.gameObject.activeSelf &&
                !State.Theater.Completed && State.Theater.CanPerform(State.CurrentRole), 10);
            yield return Snapshot("主动退出回店");
            yield return CheckPreplacedObjects();
        }
        private readonly struct Edge
        {
            public Edge(double time, int lane, bool press) { Time = time; Lane = lane; Press = press; }
            public double Time { get; }
            public int Lane { get; }
            public bool Press { get; }
        }
    }
}
