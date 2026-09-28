// 职责：钉住结果画面文案组装——照人 / 照物给名字、模糊轮廓不给任何名字、真形给表里的名与描述、照不到 / 自照只有说明、
//   每种结果都带镜缘刻痕（PRD V1 V2 V3 V4 V6 的内容侧）。
// 为什么新建：一个被测类一个测试类；MirrorResultInfo.Compose 是纯函数，只依赖配置默认值。
using Game.Mirror;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Mirror
{
    /// <summary><see cref="MirrorResultInfo"/> 的 EditMode 测试。</summary>
    public sealed class MirrorResultInfoTests
    {
        private MirrorConfig config;

        [SetUp]
        public void SetUp()
        {
            config = ScriptableObject.CreateInstance<MirrorConfig>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(config);
        }

        [Test]
        public void Compose_Human_ShowsSubjectName()
        {
            MirrorResultInfo info = MirrorResultInfo.Compose(MirrorResultKind.Human, config, "阿福", "不该出现", "不该出现");

            Assert.That(info.Title, Is.EqualTo(config.HumanTitle));
            Assert.That(info.Name, Is.EqualTo("阿福"));
            Assert.That(info.Body, Is.Empty);
        }

        [Test]
        public void Compose_Object_ShowsSubjectName()
        {
            MirrorResultInfo info = MirrorResultInfo.Compose(MirrorResultKind.Object, config, "物资箱", null, null);

            Assert.That(info.Title, Is.EqualTo(config.ObjectTitle));
            Assert.That(info.Name, Is.EqualTo("物资箱"));
        }

        [Test]
        public void Compose_Blurry_NeverShowsAnyName()
        {
            MirrorResultInfo info = MirrorResultInfo.Compose(MirrorResultKind.Blurry, config, "井边妇人", "真形名", "真形描述");

            Assert.That(info.Name, Is.Empty, "模糊轮廓不显示真形名（也不显示化形名）");
            Assert.That(info.Body, Is.EqualTo(config.BlurryBody));
            Assert.That(info.Body, Does.Not.Contain("真形名"));
            Assert.That(info.Blurred, Is.True);
        }

        [Test]
        public void Compose_TrueForm_ShowsTrueNameAndDesc()
        {
            MirrorResultInfo info = MirrorResultInfo.Compose(MirrorResultKind.TrueForm, config, "井边妇人", "真形名", "真形描述");

            Assert.That(info.Title, Is.EqualTo(config.TrueFormTitle));
            Assert.That(info.Name, Is.EqualTo("真形名"));
            Assert.That(info.Body, Is.EqualTo("真形描述"));
            Assert.That(info.Blurred, Is.False);
        }

        [Test]
        public void Compose_Self_BlankMirrorWithoutName()
        {
            MirrorResultInfo info = MirrorResultInfo.Compose(MirrorResultKind.Self, config, "阿福", "x", "y");

            Assert.That(info.BlankMirror, Is.True);
            Assert.That(info.Name, Is.Empty);
            Assert.That(info.Title, Is.EqualTo(config.SelfTitle));
        }

        [Test]
        public void Compose_Nothing_OnlyExplanation()
        {
            MirrorResultInfo info = MirrorResultInfo.Compose(MirrorResultKind.Nothing, config, "阿福", "x", "y");

            Assert.That(info.Title, Is.EqualTo(config.NothingTitle));
            Assert.That(info.Name, Is.Empty);
            Assert.That(info.Body, Is.EqualTo(config.NothingBody));
        }

        [TestCase(MirrorResultKind.Human)]
        [TestCase(MirrorResultKind.Object)]
        [TestCase(MirrorResultKind.Blurry)]
        [TestCase(MirrorResultKind.TrueForm)]
        [TestCase(MirrorResultKind.Nothing)]
        [TestCase(MirrorResultKind.Self)]
        public void Compose_EveryKind_CarriesEngraving(MirrorResultKind kind)
        {
            MirrorResultInfo info = MirrorResultInfo.Compose(kind, config, "阿福", "x", "y");

            Assert.That(info.Engraving, Is.Not.Empty, "每个结果画面都要有镜缘刻痕（V6）");
            Assert.That(info.Engraving, Does.Contain(config.Engraving[0]));
        }

        [Test]
        public void Compose_NullConfig_EmptyTextsNoThrow()
        {
            MirrorResultInfo info = MirrorResultInfo.Compose(MirrorResultKind.TrueForm, null, null, "真形名", null);

            Assert.That(info.Title, Is.Empty);
            Assert.That(info.Name, Is.EqualTo("真形名"));
            Assert.That(info.Body, Is.Empty);
            Assert.That(info.Engraving, Is.Empty);
        }
    }
}
