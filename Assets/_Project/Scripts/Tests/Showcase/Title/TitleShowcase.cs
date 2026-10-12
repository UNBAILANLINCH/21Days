// 职责：在 Boot 的真实标题面板上预览阳 → 阴 → 阳，确认共用图片与 Canvas。
// 为什么新建：Session 回放验证存读流程，没有标题色调的视觉检查；复用回放框架，不增加验证场景。
using System.Collections;
using Game.Core.Flow;
using Game.Core.UI;
using Game.Core.UI.Views;
using Game.Session;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Game.Tests.Showcase.Title
{
    [Category("Showcase")]
    public sealed class TitleShowcase : ShowcaseScenario
    {
        protected override string Module => "Title";
        protected override string ScenePath => null;

        [UnityTest]
        public IEnumerator Appearance_ChangesTintAndRestoresWithoutExtraCanvas()
        {
            TitleView title = null;
            yield return WaitUntil("Boot 标题页已就绪", () =>
            {
                IUIService ui = ResolveService<IUIService>();
                IGameFlow flow = ResolveService<IGameFlow>();
                title = ui == null ? null : ui.Get<TitleView>();
                return title != null && flow != null && flow.Current is TitleState;
            }, 20f);
            if (title == null) yield break;

            // 正式剧情节点尚未定案，直接预览表现接口，不写剧情分区或配置资产。
            Image background = FindDeep<Image>(title.transform, "Background");
            if (background == null)
            {
                yield return Check("标题背景已接线", () => false);
                yield break;
            }
            yield return Step("显示阳版原色", () => title.SetAppearanceTint(Color.white));
            Color normal = background.color;
            int canvases = title.GetComponentsInChildren<Canvas>(true).Length;
            int images = title.GetComponentsInChildren<Image>(true).Length;
            Color yin = ResolveService<SessionConfig>().TitleYinTint;
            yield return Snapshot("阳版");

            yield return Step("以占位色调预览阴版", () => title.SetAppearanceTint(yin));
            yield return Check("背景变为阴版色调", () => background.color == normal * yin);
            yield return Snapshot("阴版预览");

            yield return Step("恢复阳版", () => title.SetAppearanceTint(Color.white));
            yield return Check("原色恢复，图片和 Canvas 数量不变", () => background.color == normal
                && title.GetComponentsInChildren<Canvas>(true).Length == canvases
                && title.GetComponentsInChildren<Image>(true).Length == images);
            yield return Snapshot("恢复阳版");
        }
    }
}
