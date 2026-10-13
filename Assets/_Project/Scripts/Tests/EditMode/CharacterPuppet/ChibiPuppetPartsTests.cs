// 现有运动规则测试不创建层级；这里覆盖拆件动画与整体交互回弹的接线边界。
using System.Reflection;
using Game.CharacterPuppet;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.CharacterPuppet
{
    public sealed class ChibiPuppetPartsTests
    {
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
