// 现有运动规则测试不创建层级；这里覆盖拆件动画与整体交互回弹的接线边界。
using System.Reflection;
using Game.CharacterPuppet;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.CharacterPuppet
{
    public sealed class ChibiPuppetPartsTests
    {
        [Test]
        public void Opacity_PreservesPartColorsAndTint_WhenRestored()
        {
            var root = new GameObject("OpacityTest");
            try
            {
                root.SetActive(false);
                var part = root.AddComponent<SpriteRenderer>();
                part.color = new Color(.8f, .6f, .4f, .7f);
                var puppet = root.AddComponent<ChibiPuppet>();
                typeof(ChibiPuppet).GetField("parts", BindingFlags.NonPublic | BindingFlags.Instance)
                    .SetValue(puppet, new[] { null, part });
                typeof(ChibiPuppet).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(puppet, null);
                var tint = new Color(.5f, 1f, .5f, .8f);
                puppet.SetTint(tint);
                Color normal = part.color;
                puppet.SetOpacity(.45f);
                Assert.That(part.color.a, Is.EqualTo(normal.a * .45f).Within(.0001f));
                puppet.SetOpacity(.45f);
                Assert.That(part.color.a, Is.EqualTo(normal.a * .45f).Within(.0001f));
                puppet.SetTint(tint);
                Assert.That(part.color.r, Is.EqualTo(normal.r).Within(.0001f));
                puppet.SetOpacity(1f);
                Assert.That(part.color, Is.EqualTo(normal));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void MotionOpacity_FollowsReboundSource_AndRestoresOnDisable()
        {
            var root = new GameObject("ReboundOpacityTest");
            var sourceObject = new GameObject("FacingSource");
            var config = ScriptableObject.CreateInstance<ChibiPuppetConfig>();
            try
            {
                root.SetActive(false);
                var part = root.AddComponent<SpriteRenderer>();
                var source = sourceObject.AddComponent<SpriteRenderer>();
                source.color = new Color(1f, 1f, 1f, .45f);
                var puppet = root.AddComponent<ChibiPuppet>();
                const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
                typeof(ChibiPuppet).GetField("parts", flags).SetValue(puppet, new[] { part });
                typeof(ChibiPuppet).GetMethod("Awake", flags).Invoke(puppet, null);
                var motion = root.AddComponent<ChibiPuppetMotion>();
                typeof(ChibiPuppetMotion).GetField("puppet", flags).SetValue(motion, puppet);
                typeof(ChibiPuppetMotion).GetField("config", flags).SetValue(motion, config);
                var update = typeof(ChibiPuppetMotion).GetMethod("LateUpdate", flags);
                motion.BindMotion(root.transform, source);
                update.Invoke(motion, null);
                Assert.That(part.color.a, Is.EqualTo(.45f).Within(.0001f));
                motion.BindMotion(root.transform, null);
                update.Invoke(motion, null);
                Assert.That(part.color.a, Is.EqualTo(1f));
                motion.BindMotion(root.transform, source);
                update.Invoke(motion, null);
                typeof(ChibiPuppetMotion).GetMethod("OnDisable", flags).Invoke(motion, null);
                Assert.That(part.color.a, Is.EqualTo(1f));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(sourceObject);
                Object.DestroyImmediate(config);
            }
        }

        [TestCase(-1)]
        [TestCase(0)]
        [TestCase(3)]
        public void PulseTarget_UsesWholeVisualForFlatAndNestedParts(int depth)
        {
            var root = new GameObject("PuppetTest");
            try
            {
                root.SetActive(false);
                var puppet = root.AddComponent<ChibiPuppet>();
                var visual = new GameObject("Sprite").transform;
                visual.SetParent(root.transform, false);
                Transform parent = visual;
                for (int i = 0; i < depth; i++)
                {
                    var joint = new GameObject("Joint").transform;
                    joint.SetParent(parent, false);
                    parent = joint;
                }

                var part = (depth < 0 ? root : parent.gameObject).AddComponent<SpriteRenderer>();
                typeof(ChibiPuppet).GetField("parts", BindingFlags.NonPublic | BindingFlags.Instance)
                    .SetValue(puppet, new[] { null, part });
                var method = typeof(ChibiPuppet).GetMethod("ResolvePulseTarget", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.AreSame(depth < 0 ? null : visual, method.Invoke(puppet, null));
            }
            finally { Object.DestroyImmediate(root); }
        }

    }
}
