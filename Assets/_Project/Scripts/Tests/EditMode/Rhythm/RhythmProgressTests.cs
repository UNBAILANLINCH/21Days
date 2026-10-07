// 职责：纯 C# 曲库进度与异步换曲资格回归；现有音符判定测试不覆盖这些新流程。
using System;
using System.Collections.Generic;
using Game.Rhythm;
using NUnit.Framework;
namespace Game.Tests.EditMode.Rhythm
{
    public sealed class RhythmProgressTests
    {
        private static readonly string Intro = RhythmProgressRules.ProgressKey("intro", "chongerfei-demo", "20261004-v3");
        private static readonly string Guitar = RhythmProgressRules.ProgressKey("guitar", "guitar-test", "1");
        private static readonly string Attention = RhythmProgressRules.ProgressKey("attention", "attention-test", "1");
        [TestCaseSource(nameof(Cases))]
        public void Progress_CandidateRulesRemainConsistent(Action scenario) => scenario();
        public static IEnumerable<TestCaseData> Cases()
        {
            yield return new TestCaseData(new Action(() => Equal(34800, RhythmProgressRules.RequiredScore(58, 60 / 100d)))).SetName("Progress_00_58 音符 60% 门槛准确为 34800");
            yield return new TestCaseData(new Action(() => Equal(1001, RhythmProgressRules.RequiredScore(3, 0.3334)))).SetName("Progress_01_非整数分门槛向上取整");
            yield return new TestCaseData(new Action(() => { Throws(() => RhythmProgressRules.RequiredScore(0, 0.6)); Throws(() => RhythmProgressRules.RequiredScore(int.MaxValue, 0.6)); })).SetName("Progress_02_空谱与溢出音符数量拒绝");
            yield return new TestCaseData(new Action(() => { foreach (double ratio in new[] { double.NaN, double.PositiveInfinity, 0d, -0.1, 1.01 }) Throws(() => RhythmProgressRules.RequiredScore(58, ratio)); })).SetName("Progress_03_NaN/Infinity/零/超范围比例拒绝");
            yield return new TestCaseData(new Action(() => { var data = new RhythmProgressData(); True(RhythmProgressRules.IsUnlocked(data, null)); False(RhythmProgressRules.IsUnlocked(data, Intro)); })).SetName("Progress_04_新档只有无前置入门可选");
            yield return new TestCaseData(new Action(() => { var data = new RhythmProgressData(); False(Record(data, 34500)); Equal(34500, data.BestScores[Intro]); False(RhythmProgressRules.IsUnlocked(data, Intro)); })).SetName("Progress_05_未达标完整结算保存最佳但不解锁");
            yield return new TestCaseData(new Action(() => { var data = new RhythmProgressData(); True(Record(data, 34800)); True(RhythmProgressRules.IsUnlocked(data, Intro)); True(data.ClearedCharts.Contains(Intro)); })).SetName("Progress_06_恰好门槛解锁两曲");
            yield return new TestCaseData(new Action(() => { var data = new RhythmProgressData(); False(RhythmProgressRules.Record(data, Intro, 58, 0.6, 58000, 58, false)); Equal(0, data.BestScores.Count); Equal(0, data.ClearedCharts.Count); })).SetName("Progress_07_高分中断不记录不解锁");
            yield return new TestCaseData(new Action(() => { var data = new RhythmProgressData(); False(RhythmProgressRules.Record(data, Intro, 58, 0.6, 40000, 40, true)); Equal(0, data.BestScores.Count); })).SetName("Progress_08_未结算全部音符不能以完成标志通关");
            yield return new TestCaseData(new Action(() => { var data = new RhythmProgressData(); True(Record(data, 50000)); False(Record(data, 30000)); Equal(50000, data.BestScores[Intro]); True(data.ClearedCharts.Contains(Intro)); })).SetName("Progress_09_最佳成绩只增且通关不撤销");
            yield return new TestCaseData(new Action(() => { var data = new RhythmProgressData(); True(Record(data, 58000)); True(Record(data, 58000)); Equal(1, data.BestScores.Count); Equal(1, data.ClearedCharts.Count); })).SetName("Progress_10_重复结算幂等");
            yield return new TestCaseData(new Action(() => { var data = new RhythmProgressData(); True(Record(data, 58000)); RhythmProgressRules.Record(data, Guitar, 100, 0.6, 75000, 100, true); RhythmProgressRules.Record(data, Attention, 120, 0.6, 60000, 120, true); Equal(58000, data.BestScores[Intro]); Equal(75000, data.BestScores[Guitar]); Equal(60000, data.BestScores[Attention]); False(data.ClearedCharts.Contains(Attention)); })).SetName("Progress_11_三曲成绩隔离");
            yield return new TestCaseData(new Action(() => { var data = new RhythmProgressData(); Record(data, 58000); foreach (string key in new[] { RhythmProgressRules.ProgressKey("intro-2", "chongerfei-demo", "20261004-v3"), RhythmProgressRules.ProgressKey("intro", "other-chart", "20261004-v3"), RhythmProgressRules.ProgressKey("intro", "chongerfei-demo", "20261004-v4") }) { False(data.BestScores.ContainsKey(key)); False(RhythmProgressRules.IsUnlocked(data, key)); } })).SetName("Progress_12_曲目/谱面/修订变更不共享进度");
            yield return new TestCaseData(new Action(() => { False(RhythmProgressRules.ProgressKey("a:b", "c", "d") == RhythmProgressRules.ProgressKey("a", "b:c", "d")); False(RhythmProgressRules.ProgressKey("a%3Ab", "c", "d") == RhythmProgressRules.ProgressKey("a:b", "c", "d")); Throws(() => RhythmProgressRules.ProgressKey("", "c", "d")); })).SetName("Progress_13_冒号及转义文本不产生组合键碰撞");
            yield return new TestCaseData(new Action(() => { var data = new RhythmProgressData(); foreach (int score in new[] { -1, 58001 }) Throws(() => RhythmProgressRules.Record(data, Intro, 58, 0.6, score, 58, true)); foreach (int resolved in new[] { -1, 59 }) Throws(() => RhythmProgressRules.Record(data, Intro, 58, 0.6, 0, resolved, true)); Equal(0, data.BestScores.Count); })).SetName("Progress_14_非法分数/已决数拒绝且不写成绩");
            yield return new TestCaseData(new Action(() => { var data = Deserialize<RhythmProgressData>("{}"); RhythmProgressRules.Normalize(data); Equal(2, data.Version); Equal(0, data.BestScores.Count); data.BestScores = null; data.ClearedCharts = null; RhythmProgressRules.Normalize(data); Equal(0, data.ClearedCharts.Count); })).SetName("Progress_15_旧缺字段与显式 null 容器恢复默认");
            yield return new TestCaseData(new Action(() => { var data = new RhythmProgressData(); Record(data, 58000); var restored = Deserialize<RhythmProgressData>(Serialize(data)); Equal(58000, restored.BestScores[Intro]); True(RhythmProgressRules.IsUnlocked(restored, Intro)); })).SetName("Progress_16_进度 JSON 内存往返保留解锁和最佳");
            yield return new TestCaseData(new Action(() => { var data = new RhythmProgressData(); Record(data, 34800); var snapshot = data.Copy(); Record(data, 58000); RhythmProgressRules.Record(data, Guitar, 100, 0.6, 100000, 100, true); Equal(34800, snapshot.BestScores[Intro]); False(snapshot.BestScores.ContainsKey(Guitar)); False(snapshot.ClearedCharts.Contains(Guitar)); })).SetName("Progress_17_异步写入快照与后续内存进度隔离");
            yield return new TestCaseData(new Action(() => RhythmProgressRules.ValidateCatalog(new[] { "intro", "guitar", "attention" }, new[] { "", "intro", "intro" }))).SetName("Progress_18_有效双进阶前置曲库通过");
            yield return new TestCaseData(new Action(() => { Throws(() => RhythmProgressRules.ValidateCatalog(Array.Empty<string>(), Array.Empty<string>())); Throws(() => RhythmProgressRules.ValidateCatalog(new[] { "intro" }, Array.Empty<string>())); Throws(() => RhythmProgressRules.ValidateCatalog(new[] { "intro", "intro" }, new[] { "", "" })); Throws(() => RhythmProgressRules.ValidateCatalog(new[] { " " }, new[] { "" })); })).SetName("Progress_19_空/数组错位/重复/空白曲库拒绝");
            yield return new TestCaseData(new Action(() => { Throws(() => RhythmProgressRules.ValidateCatalog(new[] { "a" }, new[] { "missing" })); Throws(() => RhythmProgressRules.ValidateCatalog(new[] { "a" }, new[] { "a" })); Throws(() => RhythmProgressRules.ValidateCatalog(new[] { "a", "b" }, new[] { "b", "a" })); })).SetName("Progress_20_缺失前置/自环/双曲循环拒绝");
            yield return new TestCaseData(new Action(() => { var rules = Ready(Intro); long before = rules.Generation; False(rules.TrySelect(Guitar, false, out _)); Equal(before, rules.Generation); Equal(Intro, rules.SelectedKey); })).SetName("Progress_21_锁定选曲拒绝且不会打断当前曲");
            yield return new TestCaseData(new Action(() => { var rules = new RhythmSelectionRules(); True(rules.TrySelect(Intro, true, out long ticket)); False(rules.TrySelect(Guitar, true, out long second)); Equal(ticket, second); True(rules.CompleteSelection(ticket)); Equal(Intro, rules.SelectedKey); })).SetName("Progress_22_准备中重复点击拒绝且保留首个请求");
            yield return new TestCaseData(new Action(() => { var rules = new RhythmSelectionRules(); rules.TrySelect(Intro, true, out long ticket); rules.LeaveSelection(); False(rules.CompleteSelection(ticket)); Equal(null, rules.SelectedKey); })).SetName("Progress_23_退出阻止陈旧选曲完成");
            yield return new TestCaseData(new Action(() => { var rules = new RhythmSelectionRules(); rules.TrySelect(Intro, true, out long old); rules.Invalidate(); rules.TrySelect(Guitar, true, out long current); False(rules.CompleteSelection(old)); True(rules.CompleteSelection(current)); Equal(Guitar, rules.SelectedKey); })).SetName("Progress_24_换曲中的旧完成不能覆盖新选择");
            yield return new TestCaseData(new Action(() => { var rules = new RhythmSelectionRules(); rules.TrySelect(Intro, true, out long ticket); False(rules.TryStart(false, out _)); True(rules.CompleteSelection(ticket)); True(rules.TryStart(false, out _)); })).SetName("Progress_25_曲目准备完成前不能开局");
            yield return new TestCaseData(new Action(() => { var rules = Ready(Intro); rules.TryStart(false, out long old); rules.TryStart(false, out long retry); False(rules.TryFinish(old, Intro, true, out bool ignored)); False(ignored); True(rules.TryFinish(retry, Intro, true, out bool record)); True(record); })).SetName("Progress_26_重试使旧结算失效");
            yield return new TestCaseData(new Action(() => { var rules = Ready(Intro); rules.TryStart(false, out long old); rules.TrySelect(Guitar, true, out long select); rules.CompleteSelection(select); rules.TryStart(false, out long current); False(rules.TryFinish(old, Intro, true, out _)); False(rules.TryFinish(current, Intro, true, out _)); True(rules.TryFinish(current, Guitar, true, out bool record)); True(record); })).SetName("Progress_27_旧曲结算不能写入新曲");
            yield return new TestCaseData(new Action(() => { var rules = Ready(Intro); rules.TryStart(false, out long ticket); rules.Invalidate(); False(rules.TryFinish(ticket, Intro, true, out _)); False(rules.IsPlaying); })).SetName("Progress_28_暂停失焦中断使晚到完成失效");
            yield return new TestCaseData(new Action(() => { var rules = Ready(Intro); rules.TryStart(true, out long ticket); True(rules.TryFinish(ticket, Intro, true, out bool record)); False(record); })).SetName("Progress_29_练习完整结束不能获得歌曲成绩资格");
            yield return new TestCaseData(new Action(() => { var rules = Ready(Intro); rules.TryStart(false, out long ticket); True(rules.TryFinish(ticket, Intro, false, out bool record)); False(record); })).SetName("Progress_30_自然未完成轮次不能获得通关资格");
            yield return new TestCaseData(new Action(() => { var rules = Ready(Intro); rules.TryStart(false, out long ticket); True(rules.TryFinish(ticket, Intro, true, out _)); False(rules.TryFinish(ticket, Intro, true, out _)); })).SetName("Progress_31_一次完成只能消费一次");
        }
    // 测试程序集只显式引用 NUnit；按实际已加载 JSON 后端反射测试，避免为测试修改 asmdef。
    private static T Deserialize<T>(string text)
    {
        var json = System.Reflection.Assembly.Load("Newtonsoft.Json").GetType("Newtonsoft.Json.JsonConvert", true);
        return (T)json.GetMethod("DeserializeObject", new[] { typeof(string), typeof(Type) }).Invoke(null, new object[] { text, typeof(T) });
    }
    private static string Serialize(object value)
    {
        var json = System.Reflection.Assembly.Load("Newtonsoft.Json").GetType("Newtonsoft.Json.JsonConvert", true);
        return (string)json.GetMethod("SerializeObject", new[] { typeof(object) }).Invoke(null, new[] { value });
    }
    private static RhythmSelectionRules Ready(string key) { var rules = new RhythmSelectionRules(); rules.TrySelect(key, true, out long ticket); True(rules.CompleteSelection(ticket)); return rules; }
    private static bool Record(RhythmProgressData data, int score) => RhythmProgressRules.Record(data, Intro, 58, 0.6, score, 58, true);
    private static void True(bool value) { if (!value) throw new Exception("expected true"); }
    private static void False(bool value) => True(!value);
    private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception("expected " + expected + ", actual " + actual); }
    private static void Throws(Action body) { try { body(); } catch (ArgumentException) { return; } throw new Exception("expected ArgumentException"); }

    }
}
