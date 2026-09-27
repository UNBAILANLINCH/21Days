// 职责：钉住 SupplyCrateFocus.SelectNearest 的选择规则（半径内最近、跳过已开 / 未激活 / 空项、半径外无焦点）。
// 为什么新建：一个被测类一个测试类；逐帧 Tick 要容器与场景，纯选择函数可在 EditMode 直接覆盖。
using System.Collections.Generic;
using Game.Loot;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Loot
{
    public sealed class SupplyCrateFocusTests
    {
        private readonly List<GameObject> created = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < created.Count; i++)
            {
                if (created[i] != null) Object.DestroyImmediate(created[i]);
            }
            created.Clear();
        }

        [Test]
        public void SelectNearest_PicksClosestInsideRadius()
        {
            SupplyCrate far = CreateCrate(new Vector3(1.2f, 0f, 0f));
            SupplyCrate near = CreateCrate(new Vector3(0.5f, 0f, 0f));

            SupplyCrate result = SupplyCrateFocus.SelectNearest(Vector3.zero, 1.5f, new[] { far, near });

            Assert.That(result, Is.SameAs(near));
        }

        [Test]
        public void SelectNearest_OutsideRadius_ReturnsNull()
        {
            SupplyCrate crate = CreateCrate(new Vector3(3f, 0f, 0f));

            Assert.That(SupplyCrateFocus.SelectNearest(Vector3.zero, 1.5f, new[] { crate }), Is.Null);
        }

        [Test]
        public void SelectNearest_SkipsOpenedInactiveAndNull()
        {
            SupplyCrate opened = CreateCrate(new Vector3(0.2f, 0f, 0f));
            opened.SetOpened(true);
            SupplyCrate inactive = CreateCrate(new Vector3(0.3f, 0f, 0f));
            inactive.gameObject.SetActive(false);
            SupplyCrate valid = CreateCrate(new Vector3(1f, 0f, 0f));

            SupplyCrate result = SupplyCrateFocus.SelectNearest(Vector3.zero, 1.5f, new[] { null, opened, inactive, valid });

            Assert.That(result, Is.SameAs(valid));
        }

        private SupplyCrate CreateCrate(Vector3 position)
        {
            var go = new GameObject("TestCrate");
            created.Add(go);
            go.transform.position = position;
            return go.AddComponent<SupplyCrate>();
        }
    }
}
