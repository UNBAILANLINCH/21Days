// 职责：钉住战斗表现层收场时「恢复键盘出招借用的 Dialogue 图」在输入服务已释放时安全跳过（PRP/combat-iteration W7c）——
//   W6b Monster 回放（批次 20261016-000310）在升级出来的回合制进行中收尾：根作用域先释放了 InputService，
//   之后战斗收场才跑到 BattleScenePresenter.UnhookKeys → input.DisableMap("Dialogue")，服务打一条「InputService 还没初始化」的警告。
//   两条：① 服务已释放：收场不抛、不再去碰它（不出那条警告）；② 负对照：服务还在，收场照常把进来前关着的 Dialogue 图关回去。
// 为什么新建：BattleScenePresenter 之前没有 EditMode 测试（开场要叠加加载的战斗场景、舞台与真界面，靠回放看）。
//   这里只验挂键 / 解键这一段：开场 OpenAsync 搭不起来，挂键用反射调私有的 HookKeys、把「已开场」标记置上，再走公开的 CloseAsync
//   （Game.Runtime 不对测试程序集开 InternalsVisibleTo，同 BattleActorTests 经反射赋值的做法）。输入用真实 InputService（同 BattleFlowTests）。
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Game.Battle;
using Game.Core.Input;
using Game.TurnBased;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Battle
{
    public sealed class BattleScenePresenterKeysTests
    {
        /// <summary>InputService.FindMap 在服务没初始化 / 已释放时打的警告里的字样。</summary>
        private const string NotInitializedWarning = "还没初始化";

        private InputService input;
        private BattleScenePresenter presenter;
        private readonly List<string> warnings = new List<string>();

        [SetUp]
        public void SetUp()
        {
            input = new InputService();
            input.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
            presenter = new BattleScenePresenter(new BattleFakes.UI(), input, BattleSettings.PlaceholderDefault, null);
            warnings.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= RecordWarning;
            input?.Dispose();
            input = null;
        }

        [Test]
        public void CloseAsync_AfterInputServiceDisposed_SkipsTheMapQuietly()
        {
            HookKeysAsIfOpened();
            Assert.That(input.Actions.Dialogue.enabled, Is.True, "前置：战斗期间借用的 Dialogue 图（键盘 1/2/3）已打开");

            input.Dispose(); // 根作用域先一步释放了输入服务（W6b 收尾的顺序）

            Application.logMessageReceived += RecordWarning;
            Assert.DoesNotThrow(() => presenter.CloseAsync(CancellationToken.None).GetAwaiter().GetResult(), "收场不抛");
            Application.logMessageReceived -= RecordWarning;
            Assert.That(warnings, Has.None.Contains(NotInitializedWarning), "不再去关一份已释放的动作图（服务不出「还没初始化」的警告）");
        }

        [Test]
        public void CloseAsync_WithInputServiceAlive_RestoresDialogueMapToDisabled()
        {
            HookKeysAsIfOpened();
            Assert.That(input.Actions.Dialogue.enabled, Is.True, "前置：战斗期间借用的 Dialogue 图已打开");

            presenter.CloseAsync(CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(input.Actions.Dialogue.enabled, Is.False, "负对照：服务还在，收场照常把进来前关着的 Dialogue 图关回去");
        }

        private void HookKeysAsIfOpened()
        {
            MethodInfo hook = typeof(BattleScenePresenter).GetMethod("HookKeys", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo opened = typeof(BattleScenePresenter).GetField("opened", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(hook, Is.Not.Null, "前置：BattleScenePresenter 挂键的私有方法名是 HookKeys");
            Assert.That(opened, Is.Not.Null, "前置：BattleScenePresenter 的「已开场」字段名是 opened");
            hook.Invoke(presenter, null);
            opened.SetValue(presenter, true);
        }

        private void RecordWarning(string message, string stackTrace, LogType type)
        {
            if (type == LogType.Warning) warnings.Add(message);
        }
    }
}
