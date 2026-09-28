// 职责：验证临时视角切换后仍跟随当前目标，并能恢复原构图。
// 现有距离剔除测试不覆盖镜头位姿，单独验证 SmoothCameraFollow 的切换往返。
#if UNITY_EDITOR
using System.Collections;
using Game.IsometricExploration;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests.PlayMode
{
    public sealed class SmoothCameraFollowTests
    {
        private GameObject cameraRoot;
        private GameObject targetRoot;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Object.Destroy(cameraRoot);
            Object.Destroy(targetRoot);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ComparisonAngle_RoundTripAfterTargetMoves_RestoresRelativePose()
        {
            targetRoot = new GameObject("CameraTarget");
            cameraRoot = new GameObject("ComparisonCamera", typeof(Camera));
            Vector3 originalOffset = new Vector3(0f, 11.8116f, -14f);
            Quaternion originalRotation = Quaternion.Euler(38f, 0f, 0f);
            cameraRoot.transform.SetPositionAndRotation(originalOffset, originalRotation);
            var follow = cameraRoot.AddComponent<SmoothCameraFollow>();
            follow.SetTarget(targetRoot.transform);
            yield return null;

            follow.SelectComparisonAngle(1);
            Assert.That(cameraRoot.transform.eulerAngles.x, Is.EqualTo(30f).Within(0.001f));
            Vector3 referenceOffset = cameraRoot.transform.position;
            Assert.That(referenceOffset.y, Is.EqualTo(9.756012f).Within(0.001f));
            Assert.That(referenceOffset.z, Is.EqualTo(-15.396271f).Within(0.001f));

            targetRoot.transform.position = new Vector3(5f, 2f, 3f);
            Vector3 originalChestInCamera = Quaternion.Inverse(originalRotation)
                * (Vector3.up * 0.8f - originalOffset);
            foreach (int index in new[] { 2, 3, 2, 1, 3 })
            {
                follow.SelectComparisonAngle(index);
                float expectedPitch = index == 1 ? 30f : index == 2 ? 25f : 20f;
                Assert.That(cameraRoot.transform.eulerAngles.x, Is.EqualTo(expectedPitch).Within(0.001f));
                Vector3 chestInCamera = cameraRoot.transform.InverseTransformPoint(
                    targetRoot.transform.position + Vector3.up * 0.8f);
                Assert.That(Vector3.Distance(chestInCamera, originalChestInCamera), Is.LessThan(0.001f));
            }
            follow.SelectComparisonAngle(0);
            Assert.That(Vector3.Distance(cameraRoot.transform.position,
                targetRoot.transform.position + originalOffset), Is.LessThan(0.001f));
            Assert.That(Quaternion.Angle(cameraRoot.transform.rotation, originalRotation), Is.LessThan(0.001f));
            follow.SelectComparisonAngle(1);
            Assert.That(Vector3.Distance(cameraRoot.transform.position,
                targetRoot.transform.position + referenceOffset), Is.LessThan(0.001f));
        }
    }
}
#endif
