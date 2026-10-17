// 职责：启动时清理额外场景，防止游戏流程叠加加载同一场景并产生重复叙事目标。
// 为什么新建：现有 GameFlowTests 使用假场景句柄，无法覆盖真实场景卸载；其纯逻辑测试不适合承载 PlayMode 生命周期。
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Boot;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests.PlayMode
{
    public sealed class GameBootstrapTests
    {
        private readonly List<Scene> created = new List<Scene>();

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (Scene scene in created)
                if (scene.IsValid() && scene.isLoaded)
                    yield return SceneManager.UnloadSceneAsync(scene);
            created.Clear();
        }

        [UnityTest]
        public IEnumerator Startup_WithExtraScenes_UnloadsThemAndKeepsBoot()
        {
            Scene boot = SceneManager.GetActiveScene();
            Scene first = SceneManager.CreateScene("BootstrapExtraFirst");
            Scene second = SceneManager.CreateScene("BootstrapExtraSecond");
            created.Add(first);
            created.Add(second);
            SceneManager.SetActiveScene(second);
            var target = new GameObject("ExtraSceneTarget");

            yield return UnloadStartupScenes(boot).ToCoroutine();

            Assert.That(boot.isLoaded, Is.True);
            Assert.That(first.isLoaded, Is.False);
            Assert.That(second.isLoaded, Is.False);
            Assert.That(target == null, Is.True, "旧场景对象必须在服务扫描前销毁");
        }

        [UnityTest]
        public IEnumerator Startup_WithOnlyBoot_KeepsSceneAndObjects()
        {
            Scene boot = SceneManager.GetActiveScene();
            var target = new GameObject("BootSceneTarget");
            try
            {
                yield return UnloadStartupScenes(boot).ToCoroutine();
                Assert.That(boot.isLoaded, Is.True);
                Assert.That(target != null, Is.True);
            }
            finally
            {
                if (target != null) Object.Destroy(target);
            }
        }

        private static UniTask UnloadStartupScenes(Scene boot)
        {
            MethodInfo method = typeof(GameBootstrap).GetMethod("UnloadStartupScenesAsync", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            return (UniTask)method.Invoke(null, new object[] { boot, CancellationToken.None });
        }
    }
}
