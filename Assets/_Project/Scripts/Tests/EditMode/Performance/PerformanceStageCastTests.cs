// 职责：钉住 PerformanceStage 演员名单的头像查找——命中、旁白空串、未登记、重名取第一条、头像为空。
// 为什么新建（复用 → 扩展 → 新建）：现有 Performance 测试分别测规则 / 策略 / 存档 / 触发判定，都不涉及舞台组件；
//   名单查找是 PerformanceStage 的新能力，按「一个被测类一个测试类」新建。
using System.Collections.Generic;
using Game.Performance;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests.EditMode.Performance
{
    public sealed class PerformanceStageCastTests
    {
        private readonly List<Object> created = new List<Object>();
        private PerformanceStage stage;
        private Sprite amiya;
        private Sprite amiyaAlt;

        [SetUp]
        public void SetUp()
        {
            var go = Track(new GameObject("perf_cast_test"));
            stage = go.AddComponent<PerformanceStage>();
            amiya = MakeSprite("amiya");
            amiyaAlt = MakeSprite("amiya_alt");
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = created.Count - 1; i >= 0; i--)
            {
                if (created[i] != null) Object.DestroyImmediate(created[i]);
            }
            created.Clear();
        }

        [Test]
        public void TryGetAvatar_RegisteredSpeaker_ReturnsAvatar()
        {
            SetCast(("阿米娅", amiya), ("陈", null));

            Assert.That(stage.TryGetAvatar("阿米娅", out Sprite avatar), Is.True);
            Assert.That(avatar, Is.SameAs(amiya));
        }

        [TestCase(null)]
        [TestCase("")]
        public void TryGetAvatar_Narration_ReturnsFalse(string speaker)
        {
            SetCast(("阿米娅", amiya));

            Assert.That(stage.TryGetAvatar(speaker, out Sprite avatar), Is.False);
            Assert.That(avatar, Is.Null);
        }

        [Test]
        public void TryGetAvatar_UnregisteredOrDifferentCase_ReturnsFalse()
        {
            SetCast(("Amiya", amiya));

            Assert.That(stage.TryGetAvatar("德克萨斯", out _), Is.False);
            Assert.That(stage.TryGetAvatar("amiya", out _), Is.False, "严格相等，区分大小写");
            Assert.That(stage.TryGetAvatar("Amiya ", out _), Is.False, "不去空白");
        }

        [Test]
        public void TryGetAvatar_DuplicateSpeaker_TakesFirstEntry()
        {
            SetCast(("阿米娅", amiya), ("阿米娅", amiyaAlt));

            Assert.That(stage.TryGetAvatar("阿米娅", out Sprite avatar), Is.True);
            Assert.That(avatar, Is.SameAs(amiya));
        }

        [Test]
        public void TryGetAvatar_FirstEntryHasNoAvatar_ReturnsFalse()
        {
            SetCast(("阿米娅", null), ("阿米娅", amiyaAlt));

            Assert.That(stage.TryGetAvatar("阿米娅", out Sprite avatar), Is.False, "重名只认第一条，第一条没头像就是没头像");
            Assert.That(avatar, Is.Null);
        }

        [Test]
        public void TryGetAvatar_EmptyCast_ReturnsFalse()
        {
            Assert.That(stage.Cast, Is.Empty);
            Assert.That(stage.TryGetAvatar("阿米娅", out _), Is.False);
        }

        [Test]
        public void Mode_DefaultsToOverlay()
        {
            Assert.That(stage.Mode, Is.EqualTo(PerformanceStageMode.Overlay), "旧演出预制体没有该字段，默认必须是叠加模式");
        }

        private void SetCast(params (string speaker, Sprite avatar)[] entries)
        {
            using (var so = new SerializedObject(stage))
            {
                SerializedProperty list = so.FindProperty("cast");
                list.arraySize = entries.Length;
                for (int i = 0; i < entries.Length; i++)
                {
                    SerializedProperty element = list.GetArrayElementAtIndex(i);
                    element.FindPropertyRelative("speaker").stringValue = entries[i].speaker;
                    element.FindPropertyRelative("avatar").objectReferenceValue = entries[i].avatar;
                }
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private Sprite MakeSprite(string spriteName)
        {
            var texture = Track(new Texture2D(4, 4));
            Sprite sprite = Track(Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f)));
            sprite.name = spriteName;
            return sprite;
        }

        private T Track<T>(T obj) where T : Object
        {
            created.Add(obj);
            return obj;
        }
    }
}
