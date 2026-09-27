// 职责：覆盖 GameQuitHooks 的三条规则——按登记顺序执行、单个钩子抛异常不阻断后面的、总超时到了就返回。
// 为什么新建：GameQuitHooks 是新类，没有现成测试可扩展；它是「退出前最后一次存档」的执行器，
//   顺序错了或一个钩子卡死整个退出，都只在玩家点退出那一刻暴露。计时器可注入，所以全部用同步断言，不等真实时间。

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Boot;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests.EditMode.Core
{
    /// <summary>
    /// GameQuitHooks 的 EditMode 测试。计时器换成手动放行的完成源：测试决定「超时什么时候到」，
    /// 钩子全部同步完成，所以 RunAllAsync 返回的任务在断言时已经有结果，不需要等帧。
    /// </summary>
    public sealed class GameQuitHooksTests
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

        private UniTaskCompletionSource timer;
        private GameQuitHooks hooks;

        [SetUp]
        public void SetUp()
        {
            timer = new UniTaskCompletionSource();
            hooks = new GameQuitHooks((_, ct) =>
            {
                // 钩子先跑完时执行器会取消这个令牌；照真实计时器的样子把取消传出去。
                ct.Register(() => timer.TrySetCanceled());
                return timer.Task;
            });
        }

        [Test]
        public void RunAllAsync_WithSeveralHooks_RunsInRegistrationOrder()
        {
            var order = new List<int>();
            hooks.Register(() => { order.Add(1); return UniTask.CompletedTask; });
            hooks.Register(() => { order.Add(2); return UniTask.CompletedTask; });
            hooks.Register(() => { order.Add(3); return UniTask.CompletedTask; });

            UniTask run = hooks.RunAllAsync(Timeout);

            Assert.That(run.Status, Is.EqualTo(UniTaskStatus.Succeeded));
            Assert.That(order, Is.EqualTo(new[] { 1, 2, 3 }));
        }

        [Test]
        public void RunAllAsync_WhenHookAwaitsAsync_WaitsForItBeforeNext()
        {
            var order = new List<string>();
            var gate = new UniTaskCompletionSource();
            hooks.Register(async () =>
            {
                order.Add("1.start");
                await gate.Task;
                order.Add("1.end");
            });
            hooks.Register(() => { order.Add("2"); return UniTask.CompletedTask; });

            UniTask run = hooks.RunAllAsync(Timeout);
            Assert.That(order, Is.EqualTo(new[] { "1.start" }), "前一个钩子没完成，后一个不能开始");
            Assert.That(run.Status, Is.EqualTo(UniTaskStatus.Pending));

            gate.TrySetResult();

            Assert.That(order, Is.EqualTo(new[] { "1.start", "1.end", "2" }));
            Assert.That(run.Status, Is.EqualTo(UniTaskStatus.Succeeded));
        }

        [Test]
        public void RunAllAsync_WhenHookThrows_LogsErrorAndRunsTheRest()
        {
            var order = new List<int>();
            hooks.Register(() => { order.Add(1); return UniTask.CompletedTask; });
            hooks.Register(() => throw new InvalidOperationException("同步抛"));
            hooks.Register(async () =>
            {
                await UniTask.CompletedTask;
                throw new InvalidOperationException("异步抛");
            });
            hooks.Register(() => { order.Add(4); return UniTask.CompletedTask; });

            LogAssert.Expect(LogType.Error, new Regex("退出前钩子 #2.*同步抛"));
            LogAssert.Expect(LogType.Error, new Regex("退出前钩子 #3.*异步抛"));

            UniTask run = hooks.RunAllAsync(Timeout);

            Assert.That(run.Status, Is.EqualTo(UniTaskStatus.Succeeded), "钩子的异常不能冒到 RunAllAsync 外面");
            Assert.That(order, Is.EqualTo(new[] { 1, 4 }));
        }

        [Test]
        public void RunAllAsync_WhenTimeoutElapses_ReturnsAndSkipsRemainingHooks()
        {
            var stuck = new UniTaskCompletionSource();
            bool secondRan = false;
            hooks.Register(() => stuck.Task);
            hooks.Register(() => { secondRan = true; return UniTask.CompletedTask; });

            UniTask run = hooks.RunAllAsync(Timeout);
            Assert.That(run.Status, Is.EqualTo(UniTaskStatus.Pending), "超时前要一直等卡住的钩子");

            LogAssert.Expect(LogType.Warning, new Regex("退出前钩子超过"));
            timer.TrySetResult();

            Assert.That(run.Status, Is.EqualTo(UniTaskStatus.Succeeded), "超时到了就返回，不再等卡住的钩子");

            // 卡住的钩子事后完成，后面的钩子也不该再被启动。
            stuck.TrySetResult();
            Assert.That(secondRan, Is.False);
        }

        [Test]
        public void Register_WhenHandleDisposed_HookNoLongerRuns()
        {
            int calls = 0;
            IDisposable handle = hooks.Register(() => { calls++; return UniTask.CompletedTask; });
            handle.Dispose();
            handle.Dispose();

            hooks.RunAllAsync(Timeout).Forget();

            Assert.That(calls, Is.EqualTo(0));
            Assert.That(hooks.Count, Is.EqualTo(0));
        }
    }
}
