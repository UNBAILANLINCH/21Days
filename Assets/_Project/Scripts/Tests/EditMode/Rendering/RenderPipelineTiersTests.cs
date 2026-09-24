// 职责：守住渲染分档配置——高档 / 手游档两份 URP 资产的 Renderer List、Quality 六档挂载、软阴影开关。
// 为什么新建：既有 EditMode 测试都针对玩法规则，没有渲染配置的守卫；这是独立的工程配置契约，不属于任何玩法模块。
// 刻意读真实资产路径：它守的就是这几份资产本身，被人误改或挂错档位时要立刻红。
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Game.Tests.EditMode.Rendering
{
    public sealed class RenderPipelineTiersTests
    {
        private const string HighTierPath = "Assets/Settings/UniversalRP.asset";
        private const string MobileTierPath = "Assets/Settings/UniversalRP_Mobile.asset";
        private const int QualityLevelCount = 6;
        private const int MobileLevelCount = 3;

        [TestCase(HighTierPath)]
        [TestCase(MobileTierPath)]
        public void RendererList_Index1_IsUniversalRendererData(string assetPath)
        {
            UniversalRenderPipelineAsset asset = LoadTier(assetPath);
            SerializedProperty list = new SerializedObject(asset).FindProperty("m_RendererDataList");

            Assert.That(list.arraySize, Is.GreaterThanOrEqualTo(2), $"{assetPath} 的 Renderer List 少于两项");
            // 固定假设：索引 0 是 Boot / UI 沿用的 2D Renderer（Renderer2D.asset），索引 1 是探索场景用的 3D 透视 Universal Renderer，
            // m_DefaultRendererIndex 保持 0，探索相机在 UniversalAdditionalCameraData 上显式选 1；有人在 Inspector 里调换顺序，这条会红。
            Assert.That(list.GetArrayElementAtIndex(1).objectReferenceValue, Is.InstanceOf<UniversalRendererData>());
        }

        [Test]
        public void QualityLevels_FirstThreeMobile_LastThreeHighTier()
        {
            UniversalRenderPipelineAsset high = LoadTier(HighTierPath);
            UniversalRenderPipelineAsset mobile = LoadTier(MobileTierPath);

            Assert.That(QualitySettings.names.Length, Is.EqualTo(QualityLevelCount));
            for (int i = 0; i < QualityLevelCount; i++)
            {
                UniversalRenderPipelineAsset expected = i < MobileLevelCount ? mobile : high;
                Assert.That(QualitySettings.GetRenderPipelineAssetAt(i), Is.SameAs(expected),
                    $"Quality 第 {i} 档（{QualitySettings.names[i]}）挂错了 URP 资产");
            }
        }

        [Test]
        public void SoftShadows_OnForHighTier_OffForMobile()
        {
            Assert.That(LoadTier(HighTierPath).supportsSoftShadows, Is.True);
            Assert.That(LoadTier(MobileTierPath).supportsSoftShadows, Is.False);
        }

        private static UniversalRenderPipelineAsset LoadTier(string assetPath)
        {
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(assetPath);
            Assert.That(asset != null, Is.True, $"找不到 {assetPath}");
            return asset;
        }
    }
}
