// 职责：验证纸片环境材质默认兼容、增量写入及错误输入不产生部分更新。
// 不扩展深度场景接线测试：此处只创建临时材质，不打开或修改正式场景/资产。
using System;
using Game.Editor.CharacterPuppet;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests.EditMode.CharacterPuppet
{
    public sealed class PaperEnvironmentWindowTests
    {
        private Material baseMaterial;
        private Material playerMaterial;

        [SetUp]
        public void SetUp()
        {
            Shader shader = Shader.Find("21Days/SpriteDepthClip");
            Assert.That(shader, Is.Not.Null);
            baseMaterial = new Material(shader);
            playerMaterial = new Material(shader);
            playerMaterial.SetFloat("_DepthBias", 0.02f);
        }

        [TearDown]
        public void TearDown()
        {
            if (baseMaterial != null)
            {
                Undo.ClearUndo(baseMaterial);
                UnityEngine.Object.DestroyImmediate(baseMaterial);
            }
            if (playerMaterial != null)
            {
                Undo.ClearUndo(playerMaterial);
                UnityEngine.Object.DestroyImmediate(playerMaterial);
            }
        }

        [Test]
        public void Shader_Defaults_KeepLegacyAppearance()
        {
            Assert.That(baseMaterial.GetFloat("_EnvironmentStrength"), Is.Zero);
            Assert.That(baseMaterial.GetFloat("_ShadowStrength"), Is.Zero);
            Assert.That(baseMaterial.GetFloat("_Saturation"), Is.EqualTo(1f));
            Assert.That(baseMaterial.renderQueue, Is.EqualTo(2450));
            Assert.That(ShaderUtil.ShaderHasError(baseMaterial.shader), Is.False);
            foreach (string pass in new[] { "UniversalForward", "ShadowCaster", "DepthOnly", "DepthNormals" })
                Assert.That(baseMaterial.FindPass(pass), Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void Apply_ValidPair_OnlyChangesIntegrationProperties()
        {
            Color tint = new Color(0.7f, 0.8f, 0.9f, 0.6f);
            playerMaterial.SetColor("_Color", tint);
            playerMaterial.SetFloat("_Cutoff", 0.4f);
            PaperEnvironmentWindow.ApplyToMaterials(new[] { baseMaterial, playerMaterial }, 0.3f, 0.25f, 0.45f, 0.95f);
            foreach (Material material in new[] { baseMaterial, playerMaterial })
            {
                Assert.That(material.GetFloat("_EnvironmentStrength"), Is.EqualTo(0.3f));
                Assert.That(material.GetFloat("_ShadowStrength"), Is.EqualTo(0.25f));
                Assert.That(material.GetFloat("_LightFloor"), Is.EqualTo(0.45f));
                Assert.That(material.GetFloat("_Saturation"), Is.EqualTo(0.95f));
                Assert.That(material.renderQueue, Is.EqualTo(2450));
            }
            Assert.That(baseMaterial.GetFloat("_DepthBias"), Is.Zero);
            Assert.That(playerMaterial.GetFloat("_DepthBias"), Is.EqualTo(0.02f));
            Assert.That(playerMaterial.GetFloat("_Cutoff"), Is.EqualTo(0.4f));
            Assert.That(playerMaterial.GetColor("_Color"), Is.EqualTo(tint));
        }

        [Test]
        public void Apply_MissingSecondTarget_DoesNotChangeFirst()
        {
            Assert.Throws<ArgumentException>(() => PaperEnvironmentWindow.ApplyToMaterials(
                new Material[] { baseMaterial, null }, 0.3f, 0.3f, 0.45f, 1f));
            Assert.That(baseMaterial.GetFloat("_EnvironmentStrength"), Is.Zero);
        }

        [Test]
        public void Apply_DirtyTargetWithGuard_DoesNotChangeFirst()
        {
            EditorUtility.SetDirty(playerMaterial);
            Assert.Throws<ArgumentException>(() => PaperEnvironmentWindow.ApplyToMaterials(
                new[] { baseMaterial, playerMaterial }, 0.3f, 0.3f, 0.45f, 1f, true));
            Assert.That(baseMaterial.GetFloat("_EnvironmentStrength"), Is.Zero);
        }

        [Test]
        public void Apply_WrongSecondShader_DoesNotChangeFirst()
        {
            Shader shader = Shader.Find("Hidden/InternalErrorShader");
            Assert.That(shader, Is.Not.Null);
            playerMaterial.shader = shader;
            Assert.Throws<ArgumentException>(() => PaperEnvironmentWindow.ApplyToMaterials(
                new[] { baseMaterial, playerMaterial }, 0.3f, 0.3f, 0.45f, 1f));
            Assert.That(baseMaterial.GetFloat("_EnvironmentStrength"), Is.Zero);
        }

        [Test]
        public void Apply_Undo_RestoresOnlyTemporaryMaterials()
        {
            Undo.IncrementCurrentGroup();
            PaperEnvironmentWindow.ApplyToMaterials(new[] { baseMaterial, playerMaterial }, 0.3f, 0.25f, 0.6f, 0.95f);
            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo();
            foreach (Material material in new[] { baseMaterial, playerMaterial })
            {
                Assert.That(material.GetFloat("_EnvironmentStrength"), Is.Zero);
                Assert.That(material.GetFloat("_ShadowStrength"), Is.Zero);
                Assert.That(material.GetFloat("_LightFloor"), Is.EqualTo(0.45f));
                Assert.That(material.GetFloat("_Saturation"), Is.EqualTo(1f));
            }
            Assert.That(playerMaterial.GetFloat("_DepthBias"), Is.EqualTo(0.02f));
            Undo.IncrementCurrentGroup();
        }

        [TestCase(-0.1f)]
        [TestCase(1.1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void Apply_InvalidValue_RejectsWithoutChanges(float value)
        {
            Assert.Throws<ArgumentException>(() => PaperEnvironmentWindow.ApplyToMaterials(
                new[] { baseMaterial, playerMaterial }, value, 0.3f, 0.45f, 1f));
            Assert.That(baseMaterial.GetFloat("_EnvironmentStrength"), Is.Zero);
        }

        [Test]
        public void Apply_LegacyValues_RestoresAppearanceControls()
        {
            var materials = new[] { baseMaterial, playerMaterial };
            PaperEnvironmentWindow.ApplyToMaterials(materials, 0.3f, 0.3f, 0.45f, 0.95f);
            PaperEnvironmentWindow.ApplyToMaterials(materials, 0f, 0f, 0.45f, 1f);
            Assert.That(playerMaterial.GetFloat("_EnvironmentStrength"), Is.Zero);
            Assert.That(playerMaterial.GetFloat("_ShadowStrength"), Is.Zero);
            Assert.That(playerMaterial.GetFloat("_Saturation"), Is.EqualTo(1f));
            Assert.That(playerMaterial.GetFloat("_DepthBias"), Is.EqualTo(0.02f));
        }
    }
}
