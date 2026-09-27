// 职责：钉住 PerformanceStage 演员名单的头像查找——命中、旁白空串、未登记、重名取第一条、头像为空、头像侧（默认左 / 配了右）。
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

            Assert.That(stage.TryGetAvatar("阿米娅", out Sprite avatar, out _), Is.True);
            Assert.That(avatar, Is.SameAs(amiya));
        }

        [TestCase(null)]
        [TestCase("")]
        public void TryGetAvatar_Narration_ReturnsFalse(string speaker)
        {
            SetCast(("阿米娅", amiya));

            Assert.That(stage.TryGetAvatar(speaker, out Sprite avatar, out _), Is.False);
            Assert.That(avatar, Is.Null);
        }

        [Test]
        public void TryGetAvatar_UnregisteredOrDifferentCase_ReturnsFalse()
        {
            SetCast(("Amiya", amiya));

            Assert.That(stage.TryGetAvatar("德克萨斯", out _, out _), Is.False);
            Assert.That(stage.TryGetAvatar("amiya", out _, out _), Is.False, "严格相等，区分大小写");
            Assert.That(stage.TryGetAvatar("Amiya ", out _, out _), Is.False, "不去空白");
        }

        [Test]
        public void TryGetAvatar_DuplicateSpeaker_TakesFirstEntry()
        {
            SetCast(("阿米娅", amiya), ("阿米娅", amiyaAlt));

            Assert.That(stage.TryGetAvatar("阿米娅", out Sprite avatar, out _), Is.True);
            Assert.That(avatar, Is.SameAs(amiya));
        }

        [Test]
        public void TryGetAvatar_FirstEntryHasNoAvatar_ReturnsFalse()
        {
            SetCast(("阿米娅", null), ("阿米娅", amiyaAlt));

            Assert.That(stage.TryGetAvatar("阿米娅", out Sprite avatar, out _), Is.False, "重名只认第一条，第一条没头像就是没头像");
            Assert.That(avatar, Is.Null);
        }

        [Test]
        public void TryGetAvatar_SideConfigured_ReturnsSideElseDefaultsLeft()
        {
            // 第一条从空名单新增、不写 side：取字段默认值，模拟没配头像侧的条目（排在最前，免得新增元素抄到上一条的值）。
            AppendCast("德克萨斯", amiya, null);
            AppendCast("陈", amiyaAlt, PerformanceAvatarSide.Right);
            AppendCast("阿米娅", amiya, PerformanceAvatarSide.Left);

            Assert.That(stage.TryGetAvatar("陈", out _, out PerformanceAvatarSide chen), Is.True);
            Assert.That(chen, Is.EqualTo(PerformanceAvatarSide.Right), "名单里配了 Right 的说话者返回 Right");
            Assert.That(stage.TryGetAvatar("阿米娅", out _, out PerformanceAvatarSide amiyaSide), Is.True);
            Assert.That(amiyaSide, Is.EqualTo(PerformanceAvatarSide.Left));
            Assert.That(stage.TryGetAvatar("德克萨斯", out _, out PerformanceAvatarSide texas), Is.True);
            Assert.That(texas, Is.EqualTo(PerformanceAvatarSide.Left), "没配头像侧的条目默认 Left");
            Assert.That(stage.TryGetAvatar("未登记", out _, out PerformanceAvatarSide missing), Is.False);
            Assert.That(missing, Is.EqualTo(PerformanceAvatarSide.Left), "没命中时头像侧为 Left");
        }

        [Test]
        public void CastEntry_DefaultSide_IsLeft()
        {
            Assert.That(new PerformanceCastEntry().Side, Is.EqualTo(PerformanceAvatarSide.Left));
            Assert.That(new PerformanceCastEntry("阿米娅", amiya).Side, Is.EqualTo(PerformanceAvatarSide.Left));
            Assert.That(new PerformanceCastEntry("陈", amiya, PerformanceAvatarSide.Right).Side, Is.EqualTo(PerformanceAvatarSide.Right));
        }

        [Test]
        public void TryGetAvatar_EmptyCast_ReturnsFalse()
        {
            Assert.That(stage.Cast, Is.Empty);
            Assert.That(stage.TryGetAvatar("阿米娅", out _, out _), Is.False);
        }

        [Test]
        public void Mode_DefaultsToOverlay()
        {
            Assert.That(stage.Mode, Is.EqualTo(PerformanceStageMode.Overlay), "旧演出预制体没有该字段，默认必须是叠加模式");
        }

        private void SetCast(params (string speaker, Sprite avatar)[] entries)
        {
            var sided = new (string speaker, Sprite avatar, PerformanceAvatarSide side)[entries.Length];
            for (int i = 0; i < entries.Length; i++) sided[i] = (entries[i].speaker, entries[i].avatar, PerformanceAvatarSide.Left);
            SetCast(sided);
        }

        private void SetCast(params (string speaker, Sprite avatar, PerformanceAvatarSide side)[] entries)
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
                    element.FindPropertyRelative("side").enumValueIndex = (int)entries[i].side;
                }
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        // 在名单末尾追加一条；side 为 null 时不写该字段（新增元素会抄上一条的值，所以「不写」只在空名单的第一条上等于默认值）。
        private void AppendCast(string speaker, Sprite avatar, PerformanceAvatarSide? side)
        {
            using (var so = new SerializedObject(stage))
            {
                SerializedProperty list = so.FindProperty("cast");
                int index = list.arraySize;
                list.arraySize = index + 1;
                SerializedProperty element = list.GetArrayElementAtIndex(index);
                element.FindPropertyRelative("speaker").stringValue = speaker;
                element.FindPropertyRelative("avatar").objectReferenceValue = avatar;
                if (side.HasValue) element.FindPropertyRelative("side").enumValueIndex = (int)side.Value;
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
