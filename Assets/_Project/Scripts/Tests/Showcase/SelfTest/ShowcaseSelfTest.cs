// ShowcaseSelfTest —— 回放框架的自检场景，同时是模块 Showcase 的复制模板。
//
// 做什么：不依赖任何玩法模块，在代码里造一个白色方块，移动它、把它变红，一路截图。
//         跑通它就说明「场景加载 + 节奏停顿 + 叠加层 + 检查点 + 截图 + 报告」这条链是好的；
//         跑不通就是框架的问题，不是模块的问题——排查时先跑 /verify-module SelfTest。
//
// 当模板用：复制本文件到 Showcase/<Module>/<Module>Showcase.cs，改命名空间为 Game.Tests.Showcase.<Module>、
//         类名为 <Module>Showcase、Module 返回模块名、ScenePath 指向 Scenes/Verify/<Module>.unity（或保持 null 在代码里搭），
//         然后把下面的步骤换成「调模块公开接口 + 检查看得见的变化」。
//
// 为什么新建：框架必须有一条不依赖玩法的自检用例，否则第一个模块出问题时分不清是模块坏了还是框架坏了；
//         这条用例又天然是最好的模板，两个用途合在一个文件里比分两份更不容易走样。

using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests.Showcase.SelfTest
{
    /// <summary>
    /// 回放框架自检。刻意不用 GameObject.CreatePrimitive：那条路径依赖内置管线的默认材质，
    /// 在 URP 下会渲成品红，看不出「变红」这一步到底生效没有。这里全程走 2D 的 SpriteRenderer。
    /// </summary>
    [Category("Showcase")]
    public class ShowcaseSelfTest : ShowcaseScenario
    {
        /// <summary>方块的边长（世界单位）。相机正交 size 5 时这个大小在画面里一眼能看到。</summary>
        private const float SquareSize = 2f;

        /// <summary>方块贴图的边长（像素）。只要是纯色，4×4 就够，省得造大图。</summary>
        private const int TextureSize = 4;

        private SpriteRenderer square;

        protected override string Module
        {
            get { return "SelfTest"; }
        }

        /// <summary>自检不用场景资产：场景在代码里搭，这样框架在空工程里也能自检。</summary>
        protected override string ScenePath
        {
            get { return null; }
        }

        /// <summary>自检刻意绕开 Boot：它要验的是回放框架本身，不该被框架层的就绪逻辑牵连。</summary>
        protected override bool LoadBootScene
        {
            get { return false; }
        }

        [UnityTest]
        public IEnumerator SpawnMoveRecolor_FrameworkWorks()
        {
            EnsureCamera();

            yield return Step("生成白色方块", () => square = CreateSquare());
            yield return Check("方块在场上", () => square != null && square.gameObject.activeInHierarchy);
            // isVisible 由渲染器在上一帧渲染后更新，Step 停顿过至少一帧，这里读到的是真实可见性；相机没对上时这条会红。
            yield return Check("方块在相机画面里", () => square != null && square.isVisible);
            yield return Snapshot("生成后");

            // x=2 而不是更远：竖屏 Game 视图（正交 size 5）半宽只有约 2.8 单位，方块边长 2，再远就出画面了。
            yield return Step("移到 x=2", () => square.transform.position = new Vector3(2f, 0f, 0f));
            yield return Check("x 坐标为 2", () => Mathf.Approximately(square.transform.position.x, 2f));

            yield return Step("变红", () => square.color = Color.red);
            yield return Snapshot("变红后");
            yield return Check("颜色为红", () => square.color == Color.red);
        }

        /// <summary>
        /// 运行时造一个纯白方块：纯色贴图 → Sprite → SpriteRenderer。
        /// 贴图和 Sprite 都登记给基类，TearDown 里一起销毁，不给下一条用例留垃圾。
        /// </summary>
        private SpriteRenderer CreateSquare()
        {
            Texture2D texture = Track(new Texture2D(TextureSize, TextureSize));
            texture.filterMode = FilterMode.Point;
            Color[] pixels = new Color[TextureSize * TextureSize];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = Color.white;
            }

            texture.SetPixels(pixels);
            texture.Apply();

            // pixelsPerUnit = 贴图边长 / 想要的世界边长，算出来的 Sprite 正好是 SquareSize 见方。
            Sprite sprite = Track(Sprite.Create(
                texture,
                new Rect(0f, 0f, TextureSize, TextureSize),
                new Vector2(0.5f, 0.5f),
                TextureSize / SquareSize));
            sprite.name = "ShowcaseSquareSprite";

            GameObject host = Track(new GameObject("ShowcaseSquare"));
            SpriteRenderer renderer = host.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = Color.white;
            return renderer;
        }
    }
}
