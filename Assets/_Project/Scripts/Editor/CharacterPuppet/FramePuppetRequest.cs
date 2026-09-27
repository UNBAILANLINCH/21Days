// 职责：「序列帧目录 → 小人」一次生成的输入参数，窗口与脚本（MCP execute_code 批量调用）共用。
// 为什么新建（project-root.md「加能力的顺序」）：参数有五六个且会增长，写成方法参数列表脚本调用不好读；
//   一个文件一个类，所以不塞进 FramePuppetGenerator。
namespace Game.Editor.CharacterPuppet
{
    /// <summary>
    /// 生成请求。最少只填 <see cref="FrameDirectory"/>，其余有默认值：
    /// <code>FramePuppetGenerator.Generate(new FramePuppetRequest { FrameDirectory = "Assets/_Project/Art/Sprites/Characters/Ark/amiya" });</code>
    /// </summary>
    public sealed class FramePuppetRequest
    {
        /// <summary>帧目录（工程内 Assets/ 开头的路径），内含 chr_&lt;名字&gt;_&lt;状态&gt;_&lt;NN&gt;.png 与可选 meta.json。</summary>
        public string FrameDirectory { get; set; }

        /// <summary>角色名；留空取帧目录名。决定生成物命名 chr_&lt;名字&gt;* 与 Chibi_&lt;名字&gt;.prefab。</summary>
        public string CharacterName { get; set; }

        /// <summary>整张画布在场景里的高度（世界单位），PPU = 画布高像素 / 它。</summary>
        public float TargetHeight { get; set; } = FramePuppetRules.DefaultTargetHeight;

        /// <summary>帧率；≤ 0 表示取 meta.json 的 fps，没有 meta 用 12。</summary>
        public float Fps { get; set; }

        /// <summary>
        /// 美术画的是朝左：给 Sprite 子物体开 flipX，让预制体整体仍按「默认朝右」的约定工作
        /// （ChibiPuppet 以根 localScale.x 为正表示朝右，翻面只改根缩放）。
        /// </summary>
        public bool DefaultFacesLeft { get; set; }

        /// <summary>为该角色目录建一个 Sprite Atlas（V2）；Sprite Packer 关着时自动跳过。</summary>
        public bool CreateAtlas { get; set; } = true;
    }
}
