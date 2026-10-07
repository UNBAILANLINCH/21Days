using System;
using System.Collections.Generic;
using Game.LailaFaceRecognition;
using NUnit.Framework;

namespace Game.Tests.EditMode.LailaFaceRecognition
{
    public sealed class LailaExpressionSamplerTests
    {
        [Test]
        public void Capture_RendererOnOtherNode_UsesSameExplicitBindingAsSampler()
        {
            var root = new UnityEngine.GameObject("capture-controller");
            var child = new UnityEngine.GameObject("bound-renderer");
            try
            {
                child.transform.SetParent(root.transform);
                var stale = root.AddComponent<UnityEngine.SkinnedMeshRenderer>();
                var expected = child.AddComponent<UnityEngine.SkinnedMeshRenderer>();
                var face = root.AddComponent<Game.LailaFace.FaceBlendShapeController>();
                var data = new UnityEditor.SerializedObject(face);
                data.FindProperty("faceRenderer").objectReferenceValue = expected;
                data.ApplyModifiedPropertiesWithoutUndo();
                var method = typeof(Game.Editor.Tools.LailaRecognitionTools).GetMethod("CaptureRenderer",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                Assert.That(method.Invoke(null, new object[] { face }), Is.SameAs(expected));
                Assert.That(expected, Is.Not.SameAs(stale));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void Sample_PreservesSegmentAndUnilateralLipInformation()
        {
            var weights = new Dictionary<string, float> {
                {"Brow_L_Inner_Up", 70}, {"Brow_L_Outer_Down", 40},
                {"Mouth_UpperLipL_Up", 60}, {"Mouth_R_In", 30} };
            var result = new float[17]; string error;
            Assert.That(LailaExpressionSampler.TryRead(_ => true,
                key => weights.TryGetValue(key, out float value) ? value : 0, result, out error), Is.True, error);
            Assert.That(result[0], Is.EqualTo(.7f)); Assert.That(result[2], Is.EqualTo(-.4f));
            Assert.That(result[14], Is.EqualTo(.6f)); Assert.That(result[15], Is.Zero);
            Assert.That(result[13], Is.EqualTo(-.3f));
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(101f)]
        public void Sample_InvalidWeightRejectsWithoutPartialWrite(float value)
        {
            var result = new float[17]; result[0] = 123; string error;
            Assert.That(LailaExpressionSampler.TryRead(_ => true, _ => value, result, out error), Is.False);
            Assert.That(result[0], Is.EqualTo(123));
        }

        [Test]
        public void Sample_PairConflictAndMissingKeyReject()
        {
            var result = new float[17]; string error;
            Assert.That(LailaExpressionSampler.TryRead(_ => true, _ => 20, result, out error), Is.False);
            StringAssert.Contains("冲突", error);
            Assert.That(LailaExpressionSampler.TryRead(key => key != "Mouth_UpperLipR_Up", _ => 0, result, out error), Is.False);
            StringAssert.Contains("缺少", error);
        }

        [Test]
        public void Decision_ThresholdEqualityAndTiesMatchPython()
        {
            float[] p = { .4f, .4f, .1f, .05f, .05f };
            Assert.That(LailaExpressionRecognizer.Decide(p, -2, -2, .4f), Is.Zero);
            Assert.That(LailaExpressionRecognizer.Decide(p, -1.99f, -2, .4f), Is.EqualTo(-1));
            Assert.That(LailaExpressionRecognizer.Decide(p, -2, -2, .41f), Is.EqualTo(-1));
            Assert.Throws<InvalidOperationException>(() => LailaExpressionRecognizer.Decide(new float[5], -2, -2, .4f));
        }

        [Test]
        public void Sample_ReusableBuffersRejectLateFailureWithoutChangingSnapshot()
        {
            var result = new float[17]; result[0] = 123;
            var scratch = new float[17]; string error;
            Assert.That(LailaExpressionSampler.TryRead(_ => true,
                key => key == "Mouth_LowerLip_Down" ? float.NaN : 0,
                result, scratch, out error), Is.False);
            Assert.That(result[0], Is.EqualTo(123));
            Assert.That(LailaExpressionSampler.TryRead(_ => true, _ => 0, result, result, out error), Is.False);
        }

        [Test]
        public void Sample_ValidRealtimePollingAllocatesNoManagedMemory()
        {
            var result = new float[17]; var scratch = new float[17]; string error;
            Func<string, bool> exists = _ => true; Func<string, float> read = _ => 0;
            LailaExpressionSampler.TryRead(exists, read, result, scratch, out error);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) LailaExpressionSampler.TryRead(exists, read, result, scratch, out error);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero);
        }

        [TestCase(float.NaN)]
        [TestCase(20f)]
        public void Sample_RepeatedInputErrorsAllocateNoManagedMemory(float value)
        {
            var result = new float[17]; var scratch = new float[17]; string error;
            Func<string, bool> exists = _ => true; Func<string, float> read = _ => value;
            LailaExpressionSampler.TryRead(exists, read, result, scratch, out error);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) LailaExpressionSampler.TryRead(exists, read, result, scratch, out error);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero);
        }
    }
}
