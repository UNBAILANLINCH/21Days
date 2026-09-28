// 职责：验证相机剔除组件的启停恢复；需要实际 Unity 生命周期，不能由纯配置测试替代。
using System.Collections;
using Game.IsometricExploration;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests.PlayMode
{
    public sealed class CameraDistanceCullingTests
    {
        private GameObject root;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Object.Destroy(root);
            yield return null;
        }

        [TestCase(0)]
        [TestCase(31)]
        [TestCase(33)]
        public void ApplySettings_InvalidCachedLength_RecapturesAndRestoresBaseline(int length)
        {
            root = new GameObject("DistanceCullingReloadTest", typeof(Camera));
            Camera camera = root.GetComponent<Camera>();
            var baseline = new float[32];
            baseline[8] = 70f;
            camera.layerCullDistances = baseline;
            var culling = root.AddComponent<CameraDistanceCulling>();
            var cache = typeof(CameraDistanceCulling).GetField("originalDistances",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            // 注入实际观察到的空缓存，以及其他无效长度，不依赖编辑器触发域重载。
            cache.SetValue(culling, new float[length]);
            culling.ApplySettings(new[] { new CameraLayerCullSettings(1 << 31, 20f) });
            Assert.That(camera.layerCullDistances.Length, Is.EqualTo(32));
            Assert.That(camera.layerCullDistances[8], Is.EqualTo(70f));
            Assert.That(camera.layerCullDistances[31], Is.EqualTo(20f));
            culling.enabled = false;
            CollectionAssert.AreEqual(baseline, camera.layerCullDistances);

            culling.enabled = true;
            cache.SetValue(culling, new float[length]);
            culling.enabled = false;
            CollectionAssert.AreEqual(baseline, camera.layerCullDistances);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator ApplySettings_ReapplyDisableReenable_RestoresCameraAndKeepsPhysics()
        {
            root = new GameObject("DistanceCullingTest", typeof(Camera), typeof(BoxCollider));
            Camera camera = root.GetComponent<Camera>();
            var baseline = new float[32];
            baseline[8] = 70f;
            camera.layerCullDistances = baseline;
            camera.layerCullSpherical = true;
            var culling = root.AddComponent<CameraDistanceCulling>();
            CollectionAssert.AreEqual(baseline, camera.layerCullDistances);
            culling.ApplySettings(new[] { new CameraLayerCullSettings((1 << 8) | (1 << 31), 30f),
                null, new CameraLayerCullSettings(1 << 8, 20f) });
            Assert.That(camera.layerCullDistances[8], Is.EqualTo(20f));
            Assert.That(camera.layerCullDistances[31], Is.EqualTo(30f));
            Assert.That(camera.layerCullSpherical, Is.True);
            Assert.That(root.activeSelf && root.GetComponent<BoxCollider>().enabled, Is.True);
            culling.ApplySettings(new[] { new CameraLayerCullSettings(1 << 31, 0f) });
            CollectionAssert.AreEqual(baseline, camera.layerCullDistances);
            culling.ApplySettings(new[] { new CameraLayerCullSettings(1 << 8, 10f) });
            culling.enabled = false;
            CollectionAssert.AreEqual(baseline, camera.layerCullDistances);
            culling.ApplySettings(new[] { new CameraLayerCullSettings(1 << 8, 1f) });
            CollectionAssert.AreEqual(baseline, camera.layerCullDistances);
            baseline[8] = 60f;
            camera.layerCullDistances = baseline;
            culling.enabled = true;
            culling.ApplySettings(new[] { new CameraLayerCullSettings(1 << 8, 10f) });
            culling.enabled = false;
            CollectionAssert.AreEqual(baseline, camera.layerCullDistances);
            yield return null;
        }
    }
}
