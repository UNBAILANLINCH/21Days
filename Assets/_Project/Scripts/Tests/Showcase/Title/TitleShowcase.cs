// 职责：在 Boot 的真实标题面板上预览阳 → 阴 → 阳，确认共用图片与 Canvas。
// 为什么新建：Session 回放验证存读流程，没有标题色调的视觉检查；复用回放框架，不增加验证场景。
using System.Collections;
using Cysharp.Threading.Tasks;
using Game.Core.Platform;
using Game.Core.Save;
using Game.Core.Flow;
using Game.Core.UI;
using Game.Core.UI.Views;
using Game.Session;
using Game.Player;
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
        protected override float BootShutdownSettleSeconds => 0.5f;

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

            TitleEntranceMotion entrance = title.GetComponent<TitleEntranceMotion>();
            yield return WaitUntil("拆件入场落定后再检查主题切换", () => entrance == null || !entrance.IsRunning, 9f);

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

            var events = UnityEngine.EventSystems.EventSystem.current;
            var pointer = new UnityEngine.EventSystems.PointerEventData(events);
            string[] menus = { "SettingsButton", "LoadButton", "ContinueButton", "QuitButton" };
            foreach (string menu in menus)
            {
                Button button = RequireTitleButton(menu);
                GameObject overlay = button.transform.Find("底部渐变").gameObject;
                events.SetSelectedGameObject(null);
                yield return Check(menu + " 普通态保留文字渐变", () => overlay.activeInHierarchy);
                UnityEngine.EventSystems.ExecuteEvents.Execute(button.gameObject, pointer,
                    UnityEngine.EventSystems.ExecuteEvents.pointerEnterHandler);
                yield return Check(menu + " 悬停用原高亮色，不叠白雾", () => !overlay.activeSelf
                    && button.image.overrideSprite == button.spriteState.highlightedSprite);
                UnityEngine.EventSystems.ExecuteEvents.Execute(button.gameObject, pointer,
                    UnityEngine.EventSystems.ExecuteEvents.pointerDownHandler);
                yield return Check(menu + " 按下也不叠白雾", () => !overlay.activeSelf
                    && button.image.overrideSprite == button.spriteState.pressedSprite);
                UnityEngine.EventSystems.ExecuteEvents.Execute(button.gameObject, pointer,
                    UnityEngine.EventSystems.ExecuteEvents.pointerUpHandler);
                UnityEngine.EventSystems.ExecuteEvents.Execute(button.gameObject, pointer,
                    UnityEngine.EventSystems.ExecuteEvents.pointerExitHandler);
                yield return Check(menu + " 移出但仍被选中时保留原高亮色", () => !overlay.activeSelf
                    && button.image.overrideSprite == button.spriteState.selectedSprite);
                title.SetAlternateAppearance(true);
                title.SetAlternateAppearance(false);
                yield return Check(menu + " 切换主题后选中状态仍不叠白雾", () => !overlay.activeSelf);
                yield return new WaitForEndOfFrame();
                Color selectedUpper = ReadPaperColor(button.image.rectTransform, button.image.rectTransform.rect.height, 0.65f);
                Color selectedLower = ReadPaperColor(button.image.rectTransform, button.image.rectTransform.rect.height, 0.12f);
                yield return Check(menu + " 选中纸面保留上青蓝、下黄绿的渐变（"
                    + ColorUtility.ToHtmlStringRGB(selectedUpper) + " → " + ColorUtility.ToHtmlStringRGB(selectedLower) + "）",
                    () => selectedUpper.b - selectedLower.b > 0.1f && selectedLower.r - selectedUpper.r > 0.04f);
                if (menu == "QuitButton") yield return Snapshot("退出选中原始配色");
                events.SetSelectedGameObject(null);
                yield return Check(menu + " 取消选中恢复普通态渐变", () => overlay.activeInHierarchy
                    && button.image.overrideSprite == button.image.sprite);
            }
            yield return Snapshot("取消选中恢复文字渐变");
        }

        [UnityTest]
        public IEnumerator StartHighlight_HoverExitAndSelectionFollowBothThemes()
        {
            var mouse = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Mouse>("TitlePointer");
            try
            {
            UnityEngine.InputSystem.InputSystem.QueueDeltaStateEvent(mouse.position, new Vector2(Screen.width * 0.5f, 10f));
            Input.Prime();
            TitleView title = null;
            yield return WaitUntil("开始按钮入场结束", () =>
            {
                var ui = ResolveService<IUIService>();
                title = ui == null ? null : ui.Get<TitleView>();
                return title != null && !title.GetComponent<TitleEntranceMotion>().IsRunning;
            }, 20f);
            if (title == null) yield break;
            var start = RequireTitleButton("StartButton");
            var events = UnityEngine.EventSystems.EventSystem.current;
            var pointer = new UnityEngine.EventSystems.PointerEventData(events);
            title.SetAlternateAppearance(false);
            Sprite yang = start.spriteState.highlightedSprite;
            Sprite normal = start.image.sprite;

            yield return Step("未操作时，阳版开始按钮保持原白色普通图");
            yield return Check("打开标题不会自动选中或显示高亮", () => events.currentSelectedGameObject == null
                && start.image.overrideSprite == normal && normal != yang);
            yield return Snapshot("阳版默认普通图");

            yield return Step("阳版：指针移入开始，显示正式高亮", () =>
            {
                UnityEngine.EventSystems.ExecuteEvents.Execute(start.gameObject, pointer,
                    UnityEngine.EventSystems.ExecuteEvents.pointerEnterHandler);
            });
            yield return Check("阳版高亮实际可见，拆件没有盖住它", () => start.image.enabled
                && start.image.gameObject.activeInHierarchy && start.image.overrideSprite == yang && yang != normal
                && !start.transform.Find("EntranceArtwork/文字_开始游戏").gameObject.activeSelf);
            yield return Snapshot("阳版开始悬停");

            yield return Step("指针移出，恢复普通开始按钮", () =>
                UnityEngine.EventSystems.ExecuteEvents.Execute(start.gameObject, pointer,
                    UnityEngine.EventSystems.ExecuteEvents.pointerExitHandler));
            yield return Check("未被选中的按钮移出后恢复普通图", () => start.image.overrideSprite == normal);
            yield return Snapshot("开始移出还原");

            yield return Step("切换阴版但不悬停，保持阴版普通图", () => title.SetAlternateAppearance(true));
            yield return Check("阴版也不会自动显示高亮", () => events.currentSelectedGameObject == null
                && start.image.overrideSprite == start.image.sprite && start.image.sprite != start.spriteState.highlightedSprite);
            yield return Snapshot("阴版默认普通图");

            yield return Step("阴版：移入开始，使用阴版高亮", () =>
            {
                UnityEngine.EventSystems.ExecuteEvents.Execute(start.gameObject, pointer,
                    UnityEngine.EventSystems.ExecuteEvents.pointerEnterHandler);
            });
            yield return Check("阴版悬停实际可见且图片来自阴版", () => start.image.enabled
                && start.image.gameObject.activeInHierarchy && start.image.overrideSprite == start.spriteState.highlightedSprite
                && start.spriteState.highlightedSprite != yang);
            yield return Snapshot("阴版开始悬停");

            yield return Step("玩家使用手柄方向后才选中开始，显示高亮", () =>
            {
                UnityEngine.EventSystems.ExecuteEvents.Execute(start.gameObject, pointer,
                    UnityEngine.EventSystems.ExecuteEvents.pointerExitHandler);
            }, 0f);
            yield return Input.HoldStick(Vector2.down, 0.06f);
            yield return Check("真实手柄输入仍可选中开始，选中高亮与阴版匹配", () =>
                events.currentSelectedGameObject == start.gameObject && start.image.overrideSprite == start.spriteState.selectedSprite);
            yield return Snapshot("阴版开始选中");

            yield return Step("恢复阳版，选中高亮也切回阳版", () => title.SetAlternateAppearance(false));
            yield return Check("恢复阳版高亮并保留原按压反馈", () => start.image.enabled
                && start.image.overrideSprite == yang && start.GetComponent<UIButtonFeedback>() != null);
            yield return Snapshot("阳版开始选中");
            }
            finally { UnityEngine.InputSystem.InputSystem.RemoveDevice(mouse); }
        }

        [UnityTest]
        public IEnumerator MouseStart_EmptySlots_PlaysTransition() => MouseStart(false);

        [UnityTest]
        public IEnumerator MouseStart_FullSlots_ConfirmedOverwritePlaysTransition() => MouseStart(true);

        private IEnumerator MouseStart(bool fullSlots)
        {
            var mouse = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Mouse>("StartClickPointer");
            try
            {
                Input.Prime();
                IUIService ui = null;
                TitleView title = null;
                yield return WaitUntil("标题入场结束，开始按钮可点", () =>
                {
                    ui = ResolveService<IUIService>();
                    title = ui == null ? null : ui.Get<TitleView>();
                    return title != null && !title.GetComponent<TitleEntranceMotion>().IsRunning
                        && RequireTitleButton("StartButton").IsInteractable();
                }, 20f);
                if (title == null) yield break;
                var transition = title.GetComponent<TitleStartTransition>();
                if (fullSlots)
                {
                    // 回放只填本次临时槽，不触碰玩家真实存档。
                    Assert.That(PlatformServiceBase.SaveRootOverride, Is.Not.Null.And.Not.Empty);
                    var saves = ResolveService<ISaveService>();
                    saves.Get<SessionSaveData>().SavedAtUtcTicks = System.DateTime.UtcNow.Ticks;
                    saves.Get<SessionSaveData>().ProgressText = "转场回放临时存档";
                    for (int slot = 1; slot <= ResolveService<SessionConfig>().SlotCount; slot++)
                        yield return saves.SaveAsync(slot).ToCoroutine();
                }
                yield return Step(fullSlots ? "三个临时档位已满，鼠标点击开始" : "临时档位为空，鼠标点击开始", hold: 0f);
                yield return MouseClick(RequireTitleButton("StartButton"), mouse);
                if (fullSlots)
                {
                    yield return WaitUntil("满槽时显示新游戏选槽面板", () => ui.Get<SaveSlotsView>() != null
                        && ResolveService<SaveSlotsController>().IsOpen, 5f);
                    Button row = null;
                    foreach (Button button in ui.Get<SaveSlotsView>().GetComponentsInChildren<Button>())
                        if (button.transform.Find("Slot") != null) { row = button; break; }
                    Assert.That(row, Is.Not.Null);
                    yield return Step("鼠标选择一个临时存档，先弹覆盖确认", hold: 0f);
                    yield return MouseClick(row, mouse);
                    yield return WaitUntil("覆盖确认可操作", () => ui.Get<ConfirmView>() != null
                        && ui.Get<ConfirmView>().GetComponent<CanvasGroup>().alpha > 0.99f, 5f);
                    yield return Check("确认之前不播放转场、不初始化新游戏", () => !transition.IsRunning
                        && ResolveService<GameSession>().CurrentSlot == 0);
                    yield return Snapshot("满槽覆盖确认");
                    yield return Step("鼠标确认覆盖，选槽面板让位给标题转场", hold: 0f);
                    yield return MouseClick(FindDeep<Button>(ui.Get<ConfirmView>().transform, "ConfirmButton"), mouse);
                }
                yield return WaitUntil("实际鼠标开局开始播放镜转", () => transition.IsRunning, 5f);
                yield return Check("标题实际可见且开局演出没有被选槽面板遮住", () => title.gameObject.activeInHierarchy
                    && ui.Get<SaveSlotsView>() == null && ResolveService<GameSession>().CurrentSlot == 0);
                yield return Step("水波、模糊与速度线逐渐增强", hold: 0f);
                yield return WaitUntil("真实鼠标点击后的水波进入强拍", () => transition.HasCapture && transition.Progress > 0.62f, 5f);
                yield return Snapshot(fullSlots ? "满槽开局水波转场" : "鼠标开局水波转场");
                yield return Step("转场结束接操作说明，再进入场景", hold: 0f);
                yield return WaitUntil("操作说明出现", () => ui.Get<TutorialView>() != null, 5f);
                float deadline = Time.realtimeSinceStartup + 8f;
                while (ui.Get<TutorialView>() != null && Time.realtimeSinceStartup < deadline)
                {
                    yield return Input.PressKey(UnityEngine.InputSystem.Key.F8);
                    yield return new WaitForSecondsRealtime(0.2f);
                }
                yield return WaitUntil("新游戏场景与玩家模型就绪", () => ui.Get<TitleView>() == null
                    && ResolveService<PlayerModel>() != null, 20f);
                yield return WaitCurtainRevealed();
                yield return Snapshot(fullSlots ? "满槽开局进入场景" : "鼠标开局进入场景");
            }
            finally { UnityEngine.InputSystem.InputSystem.RemoveDevice(mouse); }
        }

        private IEnumerator MouseClick(Button button, UnityEngine.InputSystem.Mouse mouse)
        {
            Assert.That(button, Is.Not.Null);
            yield return null;
            var rect = (RectTransform)button.transform;
            Canvas canvas = button.GetComponentInParent<Canvas>();
            Vector2 point = RectTransformUtility.WorldToScreenPoint(canvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null : canvas.worldCamera, rect.TransformPoint(rect.rect.center));
            UnityEngine.InputSystem.InputSystem.QueueDeltaStateEvent(mouse.position, point);
            yield return null;
            yield return null;
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(mouse,
                new UnityEngine.InputSystem.LowLevel.MouseState { position = point, buttons = 1 });
            // 按真实时长保持，避免高帧率时按下 / 抬起落在同一输入采样周期。
            float pressedAt = Time.realtimeSinceStartup;
            int frames = 0;
            while (frames < 2 || Time.realtimeSinceStartup - pressedAt < 0.05f)
            {
                yield return null;
                frames++;
            }
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(mouse,
                new UnityEngine.InputSystem.LowLevel.MouseState { position = point, buttons = 0 });
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator Start_MirrorTurnsThenWaterWarpEntersWorld()
        {
            TitleView title = null;
            yield return WaitUntil("标题入场完成，可以点击开始", () =>
            {
                IUIService ui = ResolveService<IUIService>();
                title = ui == null ? null : ui.Get<TitleView>();
                return title != null && !title.GetComponent<TitleEntranceMotion>().IsRunning
                    && RequireTitleButton("StartButton").IsInteractable();
            }, 20f);
            if (title == null) yield break;
            var transition = title.GetComponent<TitleStartTransition>();
            var mirror = FindDeep<Image>(title.transform, "铜镜_本体").rectTransform;
            Quaternion original = mirror.localRotation;
            Input.Prime();
            yield return Step("从未选中状态按确认开始游戏，镜子缓缓转动", hold: 0f);
            yield return Input.Press(ResolveService<Game.Core.Input.IInputService>().Actions.UI.Submit);
            yield return WaitUntil("镜子开始旋转且菜单锁定", () => transition.IsRunning
                && Quaternion.Angle(original, mirror.localRotation) > 5f, 3f);
            yield return Check("转场未结束时不会提前初始化新游戏", () => !RequireTitleButton("StartButton").IsInteractable()
                && ResolveService<GameSession>().CurrentSlot == 0);
            yield return Snapshot("开始镜转");

            yield return Step("水波开始扭曲，画面逐渐模糊", hold: 0f);
            yield return WaitUntil("水波进入第一拍", () => transition.Progress > 0.22f, 4f);
            yield return Snapshot("水波初起");
            yield return Step("径向速度线自然显现，扭曲继续增强", hold: 0f);
            yield return WaitUntil("速度线与模糊进入强拍", () => transition.Progress > 0.65f, 3f);
            yield return Snapshot("水波与速度线");

            yield return Step("暗幕接住原操作说明，转场截图释放", hold: 0f);
            yield return WaitUntil("操作说明显示", () => ResolveService<IUIService>().Get<TutorialView>() != null, 5f);
            CanvasGroup tutorialGroup = ResolveService<IUIService>().Get<TutorialView>().GetComponent<CanvasGroup>();
            yield return Check("操作说明完成淡入后，标题只留黑底且没有残留截图", () =>
                tutorialGroup.alpha > 0.99f
                && transition.IsCovered && !transition.HasCapture, 3f);
            yield return Snapshot("转场接操作说明");
            float deadline = Time.realtimeSinceStartup + 8f;
            while (ResolveService<IUIService>().Get<TutorialView>() != null && Time.realtimeSinceStartup < deadline)
            {
                yield return Input.PressKey(UnityEngine.InputSystem.Key.F8);
                yield return new WaitForSecondsRealtime(0.2f);
            }
            yield return Step("确认操作说明，过渡进入正式场景", hold: 0f);
            yield return WaitUntil("场景和玩家模型就绪", () => !(ResolveService<IGameFlow>().Current is TitleState)
                && ResolveService<PlayerModel>() != null, 20f);
            yield return WaitCurtainRevealed();
            yield return Snapshot("开始转场后场景");
        }

        [UnityTest]
        public IEnumerator Entrance_RevealsPartsInRhythmAndRestoresMenuInteraction()
        {
            TitleView title = null;
            yield return WaitUntil("标题拆件已经开始入场", () =>
            {
                IUIService ui = ResolveService<IUIService>();
                title = ui == null ? null : ui.Get<TitleView>();
                return title != null && title.GetComponent<TitleEntranceMotion>() != null;
            }, 20f);
            if (title == null) yield break;
            var motion = title.GetComponent<TitleEntranceMotion>();
            var mirror = FindDeep<Image>(title.transform, "铜镜_本体");
            var logo = FindDeep<Image>(title.transform, "标题_镜非台");
            var lotus = FindDeep<Image>(title.transform, "莲花纹");
            var left = FindDeep<RectMask2D>(title.transform, "丝带_左_揭露遮罩");
            var right = FindDeep<RectMask2D>(title.transform, "丝带_右_揭露遮罩");
            var start = title.transform.Find("Buttons/StartButton/EntranceArtwork").GetComponent<CanvasGroup>();
            var loadPaper = (RectTransform)title.transform.Find("Buttons/LoadButton/EntranceArtwork/卷轴底");
            var continuePaper = (RectTransform)title.transform.Find("Buttons/ContinueButton/EntranceArtwork/卷轴底");
            var settingsPaper = (RectTransform)title.transform.Find("Buttons/SettingsButton/EntranceArtwork/卷轴底");
            var quitPaper = (RectTransform)title.transform.Find("Buttons/QuitButton/EntranceArtwork/卷轴底");
            Vector2 loadTop = loadPaper.anchoredPosition;
            float loadHeight = loadPaper.GetComponent<Image>().sprite.rect.height;
            float visibleHeightForLoad = RequireTitleButton("LoadButton").image.sprite.rect.height;
            var loadText = loadPaper.Find("文字").GetComponent<CanvasGroup>();
            var leftStone = FindDeep<Image>(title.transform, "石头_左中景").rectTransform;
            var rightStone = FindDeep<Image>(title.transform, "石头_右中景").rectTransform;
            var leftOuter = FindDeep<Image>(title.transform, "石头_左远景").rectTransform;
            var rightOuter = FindDeep<Image>(title.transform, "石头_右前景").rectTransform;
            var leftFog = FindDeep<Image>(title.transform, "雾气_左").GetComponent<CanvasGroup>();
            var whiteCloud = FindDeep<Image>(title.transform, "云_右").GetComponent<CanvasGroup>();

            yield return Step("水面先升，镜子带旋转缓缓落下", hold: 0f);
            yield return WaitUntil("天空已经显露且铜镜可见", () => title.GetComponent<CanvasGroup>().alpha > 0.95f
                && mirror.GetComponent<CanvasGroup>().alpha > 0.45f, 2f);
            yield return Check("入场中按钮不可操作，镜子尚在下落且开始按钮未出现", () => motion.IsRunning
                && !RequireTitleButton("StartButton").IsInteractable()
                && mirror.rectTransform.anchoredPosition.y > 50f && start.alpha < 0.01f, 1f);
            yield return Check("四块石头保持原尺寸，中景从外侧向镜子靠近", () =>
                FindDeep<Image>(title.transform, "石头_左中景").rectTransform.localScale == Vector3.one
                && FindDeep<Image>(title.transform, "石头_右中景").rectTransform.localScale == Vector3.one
                && FindDeep<Image>(title.transform, "石头_左远景").rectTransform.localScale == Vector3.one
                && FindDeep<Image>(title.transform, "石头_右前景").rectTransform.localScale == Vector3.one
                && leftStone.anchoredPosition.x < -254f && rightStone.anchoredPosition.x > 257f);
            yield return Snapshot("水升镜落");

            yield return Step("中景石头向内收拢，云雾沿整段动作淡入", hold: 0f);
            yield return WaitUntil("石头尚在靠近镜子，雾气没有提前变为不透明", () =>
                leftStone.anchoredPosition.x < -254f && rightStone.anchoredPosition.x > 257f
                && leftFog.alpha > 0.1f && leftFog.alpha < 0.85f, 2f);
            yield return Snapshot("石头收拢与云雾淡入");

            yield return Step("标题紧随其后，莲花纹从镜后舒展开", hold: 0f);
            yield return WaitUntil("标题可见，莲花纹已经展开", () => logo.GetComponent<CanvasGroup>().alpha > 0.9f
                && lotus.rectTransform.localScale.x > 0.6f, 5f);
            yield return Check("外围石头从左右画面外滑入，尚在移动与淡入", () =>
                leftOuter.anchoredPosition.x < -849.5f && rightOuter.anchoredPosition.x > 1049f
                && Mathf.Abs(leftOuter.anchoredPosition.y + 151f) < 0.1f
                && Mathf.Abs(rightOuter.anchoredPosition.y + 202.5f) < 0.1f
                && leftOuter.GetComponent<CanvasGroup>().alpha > 0.1f
                && leftOuter.GetComponent<CanvasGroup>().alpha < 0.95f
                && rightOuter.GetComponent<CanvasGroup>().alpha > 0.1f
                && rightOuter.GetComponent<CanvasGroup>().alpha < 0.95f);
            yield return Snapshot("标题与云石展开");

            yield return Step("左右丝带按相反方向揭露", hold: 0f);
            yield return WaitUntil("左右遮罩均揭露一部分但未全部揭开", () =>
                left.padding.z > 1f && left.padding.z < left.rectTransform.rect.width * 0.45f
                && right.padding.x > 1f && right.padding.x < right.rectTransform.rect.width * 0.45f, 4f);
            yield return Check("镜旁白色云纹仍在缓慢淡入，没有随移动结束直接变实", () => whiteCloud.alpha > 0.2f
                && whiteCloud.alpha < 0.6f);
            yield return Snapshot("丝带揭露");

            // 临时存档为空时正式继续按钮隐藏；回放完整四卷轴，仍使用真实时间轴。
            title.SetContinueVisible(true);
            yield return Step("中间两张卷轴先向下展开，外侧随后跟上", hold: 0f);
            yield return WaitUntil("中间已经展开，外侧纸面尚未出现", () =>
                loadPaper.GetComponent<CanvasGroup>().alpha > 0.8f
                && continuePaper.GetComponent<CanvasGroup>().alpha > 0.15f
                && settingsPaper.GetComponent<CanvasGroup>().alpha < 0.01f
                && quitPaper.GetComponent<CanvasGroup>().alpha < 0.01f, 2f);
            yield return Check("纸面顶部和宽度固定，填充同步展开，文字还未出现", () => loadPaper.anchoredPosition == loadTop
                && loadPaper.localScale == Vector3.one && loadText.alpha < 0.01f
                && loadPaper.GetComponent<Image>().type == Image.Type.Sliced);
            yield return Snapshot("中间卷轴先开");
            // 顶部回放提示条高 78 像素，采样区必须先移到提示条以下。
            yield return WaitUntil("文字出现前，纸面渐变已展开到提示条下方", () =>
                loadPaper.sizeDelta.y > loadHeight - visibleHeightForLoad + 210f && loadText.alpha < 0.01f, 1f);
            yield return new WaitForEndOfFrame();
            Color earlyBottom = ReadPaperColor(loadPaper, 1f, 35f);
            Color earlyUpper = ReadPaperColor(loadPaper, 1f, 105f);
            yield return Check("横轴上方的白色渐变在文字出现前已跟随展开（底部 "
                + ColorUtility.ToHtmlStringRGB(earlyBottom) + "，上方 " + ColorUtility.ToHtmlStringRGB(earlyUpper) + "）",
                () => earlyBottom.r - earlyUpper.r > 0.05f);
            yield return Snapshot("纸面内部渐变随卷轴展开");
            yield return Step("卷轴下沿展开稍过，再被轻轻扯回", hold: 0f);
            yield return WaitUntil("纸面下沿略过最终长度", () => loadPaper.sizeDelta.y > loadHeight * 1.02f, 2f);
            yield return Snapshot("卷轴展开过冲");
            yield return WaitUntil("纸面发生一次回拉，顶部仍固定", () =>
                loadPaper.sizeDelta.y < loadHeight * 0.965f && loadPaper.anchoredPosition == loadTop, 1f);
            yield return Snapshot("卷轴轻轻回拉");
            yield return Step("纸面展开后半段，花纹和文字依次显现", hold: 0f);
            var text = settingsPaper.Find("文字").GetComponent<CanvasGroup>();
            yield return WaitUntil("设置文字已经出现，开始按钮仍在等末拍", () => text.alpha > 0.7f && start.alpha < 0.5f, 4f);
            yield return Snapshot("卷轴文字落定");
            // 参数跟随不能证明最终填充已出现，直接比结束切图前后的画面颜色。
            float visiblePaperHeight = RequireTitleButton("ContinueButton").image.sprite.rect.height;
            yield return new WaitForEndOfFrame();
            Color fillBefore = ReadPaperColor(continuePaper, visiblePaperHeight);
            Color bottomBefore = ReadPaperColor(continuePaper, visiblePaperHeight, 0.09f);

            yield return Step("开始按钮最后浮现，交还菜单操作", hold: 0f);
            yield return WaitUntil("全部入场完成，开始按钮可操作", () => !motion.IsRunning
                && start.alpha > 0.99f && RequireTitleButton("StartButton").IsInteractable(), 5f);
            yield return Snapshot("入场完成");
            yield return Check("外围石头回到原位置、原尺寸与完整透明度", () =>
                Mathf.Abs(leftOuter.anchoredPosition.x + 849.5f) < 0.1f
                && Mathf.Abs(rightOuter.anchoredPosition.x - 1049f) < 0.1f
                && leftOuter.localScale == Vector3.one && rightOuter.localScale == Vector3.one
                && leftOuter.GetComponent<CanvasGroup>().alpha == 1f
                && rightOuter.GetComponent<CanvasGroup>().alpha == 1f);
            yield return new WaitForEndOfFrame();
            Color fillAfter = ReadPaperColor(continuePaper, visiblePaperHeight);
            Color bottomAfter = ReadPaperColor(continuePaper, visiblePaperHeight, 0.09f);
            yield return Check("整张浅色填充提前出现，结束换图不跳色（" + ColorUtility.ToHtmlStringRGB(fillBefore)
                + " → " + ColorUtility.ToHtmlStringRGB(fillAfter) + "）", () => fillBefore.r > 0.4f
                && Mathf.Abs(fillBefore.r - fillAfter.r) < 0.04f
                && Mathf.Abs(fillBefore.g - fillAfter.g) < 0.04f
                && Mathf.Abs(fillBefore.b - fillAfter.b) < 0.04f);
            yield return Check("底部白色渐变保留，结束换图也不跳色（" + ColorUtility.ToHtmlStringRGB(bottomBefore)
                + " → " + ColorUtility.ToHtmlStringRGB(bottomAfter) + "）", () => bottomBefore.r > fillBefore.r
                && Mathf.Abs(bottomBefore.r - bottomAfter.r) < 0.04f
                && Mathf.Abs(bottomBefore.g - bottomAfter.g) < 0.04f
                && Mathf.Abs(bottomBefore.b - bottomAfter.b) < 0.04f);
            yield return Check("原按钮的选中图继续有效", () => RequireTitleButton("LoadButton").spriteState.selectedSprite != null);
            string[] menuNames = { "SettingsButton", "LoadButton", "ContinueButton", "QuitButton" };
            yield return new WaitForEndOfFrame();
            Texture2D covered = ScreenCapture.CaptureScreenshotAsTexture();
            foreach (string menu in menuNames) title.transform.Find("Buttons/" + menu + "/底部渐变").gameObject.SetActive(false);
            yield return new WaitForEndOfFrame();
            Texture2D uncovered = ScreenCapture.CaptureScreenshotAsTexture();
            foreach (string menu in menuNames) title.transform.Find("Buttons/" + menu + "/底部渐变").gameObject.SetActive(true);
            try
            {
                foreach (string menu in menuNames)
                {
                    var textRect = (RectTransform)title.transform.Find("Buttons/" + menu + "/EntranceArtwork/卷轴底/文字");
                    int changed = CountBrightenedShadows(textRect, covered, uncovered);
                    yield return Check(menu + " 的渐变确实盖在文字阴影上（" + changed + " 像素）", () => changed > 10);
                }
            }
            finally { Object.Destroy(covered); Object.Destroy(uncovered); }
            yield return Snapshot("四卷轴渐变覆盖文字");
        }

        private static int CountBrightenedShadows(RectTransform text, Texture2D covered, Texture2D uncovered)
        {
            Canvas canvas = text.GetComponentInParent<Canvas>();
            Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            var corners = new Vector3[4];
            text.GetWorldCorners(corners);
            Vector2 min = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
            Vector2 max = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
            int changed = 0;
            for (int y = Mathf.Max(0, Mathf.CeilToInt(min.y)); y < Mathf.Min(uncovered.height, max.y); y++)
                for (int x = Mathf.Max(0, Mathf.CeilToInt(min.x)); x < Mathf.Min(uncovered.width, max.x); x++)
                {
                    Color before = uncovered.GetPixel(x, y);
                    if (before.r < 0.4f && covered.GetPixel(x, y).r - before.r > 0.03f) changed++;
                }
            return changed;
        }

        private static Color ReadPaperColor(RectTransform paper, float visibleHeight, float heightFraction = 0.7f)
        {
            Canvas canvas = paper.GetComponentInParent<Canvas>();
            Vector3 point = paper.TransformPoint(new Vector3(paper.rect.xMin + paper.rect.width * 0.2f,
                paper.rect.yMin + visibleHeight * heightFraction, 0f));
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(canvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null : canvas.worldCamera, point);
            Texture2D frame = ScreenCapture.CaptureScreenshotAsTexture();
            try { return frame.GetPixel(Mathf.RoundToInt(screen.x), Mathf.RoundToInt(screen.y)); }
            finally { Object.Destroy(frame); }
        }
    }
}
