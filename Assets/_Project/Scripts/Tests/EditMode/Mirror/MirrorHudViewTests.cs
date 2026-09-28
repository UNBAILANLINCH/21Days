// 职责：钉住镜图标裂痕叠图的取级（0 不显示、1..3 对应三张累积图、越界夹取、图不够时按图数夹），
//   以及对外只读的 ShownCracks 跟着 SetCracks 走（回放据此判镜图标上的裂痕，不找子物体）。
// 为什么新建：一个被测类一个测试类；预制体接线由回放截图核对。
using Game.Mirror;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Mirror
{
    /// <summary><see cref="MirrorHudView.SelectCrackIndex"/> 的 EditMode 测试。</summary>
    public sealed class MirrorHudViewTests
    {
        [TestCase(0, 3, 0)]
        [TestCase(1, 3, 1)]
        [TestCase(2, 3, 2)]
        [TestCase(3, 3, 3)]
        [TestCase(5, 3, 3)]
        [TestCase(-1, 3, 0)]
        [TestCase(3, 2, 2)]
        [TestCase(2, 0, 0)]
        public void SelectCrackIndex_ClampsToCracksAndSprites(int hitCracks, int spriteCount, int expected)
        {
            Assert.That(MirrorHudView.SelectCrackIndex(hitCracks, spriteCount), Is.EqualTo(expected));
        }

        [Test]
        public void ShownCracks_ZeroBeforeOpen_FollowsSetCracksClamped()
        {
            var host = new GameObject("mirror_hud_test", typeof(RectTransform));
            try
            {
                // 不接叠图 Image：SetCracks 先记级数，叠图为空时只跳过换图。裂痕图数组保持默认长度 3。
                MirrorHudView hud = host.AddComponent<MirrorHudView>();
                Assert.That(hud.ShownCracks, Is.EqualTo(0), "还没打开过");

                hud.SetCracks(2);
                Assert.That(hud.ShownCracks, Is.EqualTo(2));

                hud.SetCracks(9);
                Assert.That(hud.ShownCracks, Is.EqualTo(MirrorCrackRules.MaxHitCracks), "越界夹到三道");

                hud.SetCracks(0);
                Assert.That(hud.ShownCracks, Is.EqualTo(0));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }
    }
}
