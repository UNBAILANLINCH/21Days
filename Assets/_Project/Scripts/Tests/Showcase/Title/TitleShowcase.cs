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
        public IEnumerator Appearance_ChangesSpritesAndRestoresWithoutExtraCanvas()
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
            yield return WaitUntil("标题存档状态已刷新：无存档时继续隐藏", () =>
                !RequireTitleButton("ContinueButton").gameObject.activeSelf, 3f);
            yield return Step("显示阳版正式素材（预览完整菜单）", () =>
            {
                title.SetAlternateAppearance(false);
                title.SetContinueVisible(true);
                title.SetAppearanceTint(Color.white);
            });
            Color normal = background.color;
            Sprite normalBackground = background.sprite;
            int canvases = title.GetComponentsInChildren<Canvas>(true).Length;
            int images = title.GetComponentsInChildren<Image>(true).Length;
            yield return Snapshot("阳版");

            bool applied = false;
            yield return Step("切换阴版正式素材", () => applied = title.SetAlternateAppearance(true));
            yield return Check("背景已替换为阴版，未叠加预览乘色", () => applied
                && background.sprite != normalBackground && background.color == normal);
            yield return Snapshot("阴版");

            yield return Step("恢复阳版正式素材", () => title.SetAlternateAppearance(false));
            yield return Check("阳版背景恢复，图片和 Canvas 数量不变", () => background.sprite == normalBackground
                && background.color == normal
                && title.GetComponentsInChildren<Canvas>(true).Length == canvases
                && title.GetComponentsInChildren<Image>(true).Length == images);
            yield return Snapshot("恢复阳版");

            UnityEngine.UI.Button load = RequireTitleButton("LoadButton");
            yield return Step("键盘 / 手柄选中选择存档", () =>
                UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(load.gameObject));
            yield return Check("选中图已应用", () => load.image.overrideSprite == load.spriteState.selectedSprite
                && load.spriteState.selectedSprite != null);
            yield return Snapshot("选中选择存档");
        }
    }
}
