// 职责：特写使用非缩放时间；完成或取消均恢复原相机和跟随状态。
// 原跟随测试不调用章节特写；在本文件覆盖它独有的接管和取消收尾。
#if UNITY_EDITOR
using System.Collections;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.IsometricExploration;
using Game.Narrative;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests.PlayMode
{
    public sealed class SpotlightWindowShotTests
    {
        private GameObject root;
        private SpotlightStoryConfig config;
        private float timeScale;
        [SetUp] public void SetUp() { timeScale = Time.timeScale; }
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = timeScale;
            Object.Destroy(root); Object.Destroy(config);
            yield return null;
        }
        [UnityTest] public IEnumerator Completed_RestoresCameraAndFollow() => Run(false, true);
        [UnityTest] public IEnumerator Cancelled_RestoresCameraAndFollow() => Run(true, true);
        [UnityTest] public IEnumerator Completed_KeepsOriginallyDisabledFollowDisabled() => Run(false, false);
        private IEnumerator Run(bool cancel, bool following)
        {
            root = new GameObject("WindowShotTest");
            var bindings = root.AddComponent<SpotlightSceneBindings>();
            config = ScriptableObject.CreateInstance<SpotlightStoryConfig>();
            var figure = new GameObject("WindowFigure"); figure.transform.SetParent(root.transform);
            figure.transform.position = new Vector3(6.15f, 6.35f, 4.8f);
            typeof(SpotlightSceneBindings).GetField("config", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(bindings, config);
            typeof(SpotlightSceneBindings).GetField("windowFigure", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(bindings, figure);
            var cameraRoot = new GameObject("ShotCamera"); cameraRoot.transform.SetParent(root.transform);
            var camera = cameraRoot.AddComponent<Camera>();
            var follow = cameraRoot.AddComponent<SmoothCameraFollow>();
            follow.enabled = following;
            Vector3 position = new Vector3(9.35f, 16.86f, -8.3f);
            Quaternion rotation = Quaternion.Euler(38f, 0f, 0f);
            cameraRoot.transform.SetPositionAndRotation(position, rotation); camera.fieldOfView = 20f;
            Time.timeScale = 0f;
            using (var cancellation = new CancellationTokenSource())
            {
                var task = bindings.PlayWindowShotAsync(camera, cancellation.Token).SuppressCancellationThrow();
                Assert.That(follow.enabled, Is.False);
                float deadline = Time.realtimeSinceStartup + 3f;
                while (camera.fieldOfView != config.WindowFieldOfView && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.That(camera.fieldOfView, Is.EqualTo(config.WindowFieldOfView));
                Vector3 screen = camera.WorldToViewportPoint(figure.transform.position + Vector3.up * config.WindowLookHeight);
                Assert.That(screen.x, Is.EqualTo(.5f).Within(.001f));
                Assert.That(screen.y, Is.EqualTo(.5f).Within(.001f));
                if (cancel) cancellation.Cancel();
                bool cancelled = false;
                yield return task.ToCoroutine(value => cancelled = value);
                Assert.That(cancelled, Is.EqualTo(cancel));
                Assert.That(Vector3.Distance(cameraRoot.transform.position, position), Is.LessThan(.001f));
                Assert.That(Quaternion.Angle(cameraRoot.transform.rotation, rotation), Is.LessThan(.001f));
                Assert.That(camera.fieldOfView, Is.EqualTo(20f));
                Assert.That(follow.enabled, Is.EqualTo(following));
            }
        }
    }
}
#endif
