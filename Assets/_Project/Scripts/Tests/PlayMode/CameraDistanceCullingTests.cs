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
