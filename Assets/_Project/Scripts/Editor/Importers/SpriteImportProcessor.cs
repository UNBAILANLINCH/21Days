// 职责：给 Assets/_Project/Art/Sprites/ 下新导入的贴图套一份「高清手绘纸片 + 3D 透视场景」的默认导入设置。
// 为什么新建（project-root.md「加能力的顺序」）：
//   1. 复用不行：Unity 的 Preset 也能做默认值，但 Preset 要每台机器在 Project Settings 里手动挂，
//      新同事拉下工程不会自动生效；AssetPostprocessor 是代码，跟着仓库走。
//   2. 扩展不行：这是 AssetPostprocessor 的回调，Unity 按类型反射调用，必须是独立的类。
//
// 只在**首次导入**时设（importSettingsMissing 为真）：之后别人在 Inspector 上手调的参数不被覆盖。
// 想整批重置就删掉那些贴图的 .meta 重新导入，或在 Inspector 里改——这个脚本不抢方向盘。

using System;
using UnityEditor;
using UnityEngine;

namespace Game.Editor
{
    /// <summary>
    /// 2D 贴图导入规则。命中路径前缀 <see cref="SpriteRoot"/> 的贴图，首次导入时设成：
    /// Sprite 类型、每单位 <see cref="PixelsPerUnit"/> 像素、Bilinear 过滤、压缩、生成 mipmap。
    /// <para>
    /// 为什么是这几项：美术是高清手绘，纸片放在固定俯角的透视相机下，远近尺寸一直在变——
    /// 没有 mipmap 远处的纸片会缩小采样而闪烁；Bilinear 让缩放后的手绘边缘保持平滑，Point 会出锯齿；
    /// 高清贴图不压缩会把手游包体撑大，交给平台默认压缩格式控体积。
    /// 真要改这套默认值就改这里的常量，不要一张张手调。
    /// </para>
    /// </summary>
    public sealed class SpriteImportProcessor : AssetPostprocessor
    {
        /// <summary>只管这个目录下的贴图。工程外、模板自带、第三方包里的一律不碰。</summary>
        public const string SpriteRoot = "Assets/_Project/Art/Sprites/";

        /// <summary>每世界单位多少像素。改它等于改全工程 Sprite 的尺寸基准，改之前先想清楚。</summary>
        public const int PixelsPerUnit = 100;

        private void OnPreprocessTexture()
        {
            string path = assetPath.Replace("\\", "/");
            if (!path.StartsWith(SpriteRoot, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            TextureImporter importer = assetImporter as TextureImporter;
            if (importer == null)
            {
                return;
            }

            // importSettingsMissing 只有「这个资产还没有自己的 .meta 导入设置」时才为真，
            // 也就是**首次导入**。少了这一判，每次 Reimport 都会把别人手调的参数抹掉。
            if (!importer.importSettingsMissing)
            {
                return;
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spritePixelsPerUnit = PixelsPerUnit;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.mipmapEnabled = true;

            Debug.Log($"[贴图导入] {path} 已套用纸片默认设置（Sprite / PPU {PixelsPerUnit} / Bilinear / 压缩 / mipmap）。"
                      + "要改就在 Inspector 上改，之后不会再被覆盖。");
        }
    }
}
