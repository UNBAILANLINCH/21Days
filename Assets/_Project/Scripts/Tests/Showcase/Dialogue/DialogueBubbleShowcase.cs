// 职责：从标题进入真实探索，走到村民与井边妇人身边，经交互输入检查各自气泡的文字和屏幕边界。
// 复用 ShowcaseScenario 的进场、移动、输入与收尾；既有 DialogueShowcase 只叠加场景并瞬移玩家，
// 不能复现实际探索的跟随相机，故将这条场景资产回归单列，避免改动其它对白用例的启动方式。
using System.Collections;
using Game.Core.Input;
using Game.Core.UI;
using Game.Dialogue;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests.Showcase.Dialogue
{
    [Category("Showcase")]
    public sealed class DialogueBubbleShowcase : ShowcaseScenario
    {
        protected override string Module => "Dialogue";
        protected override float BootShutdownSettleSeconds => 0.5f;

        [UnityTest]
        public IEnumerator Exploration_BothSpeakers_KeepBubbleInsideViewport()
        {
            yield return EnterWorldFromTitle();
            IInputService input = ResolveService<IInputService>();
            DialogueService dialogue = ResolveService<DialogueService>();
            IUIService ui = ResolveService<IUIService>();
            DialogueInteractionFocus focus = ResolveService<DialogueInteractionFocus>();
            var overlay = FindRequired<ShowcaseOverlay>("ShowcaseOverlay");

            int visit = 0;
            foreach (string speakerName in new[] { "Npc_Villager", "Yao_WellWoman", "Yao_WellWoman" })
            {
                var npc = FindRequired<DialogueInteractable>(speakerName);
                var bubble = FindDeep<DialogueSpeechBubble>(npc.transform, "SpeechBubble");
                Assert.That(bubble, Is.Not.Null, $"{speakerName} 缺少气泡");
                var body = FindDeep<TMP_Text>(bubble.transform, "Body");
                Assert.That(body, Is.Not.Null);

                yield return Step($"走到{npc.DisplayName}身边");
                // 从村民绕北侧和桌子东侧前往井边，避开村口演出触发区与桌面碰撞。
                if (visit == 1)
                {
                    yield return WalkTo(new Vector2(4f, 6f), 0.25f);
                    yield return WalkTo(new Vector2(9f, 6f), 0.25f);
                }
                Vector3 position = npc.transform.position;
                float side = speakerName == "Yao_WellWoman" ? 1f : -1f;
                if (visit == 2)
                {
                    yield return WalkTo(new Vector2(9f, 7.4f), 0.15f);
                    yield return WalkTo(new Vector2(position.x, 7.4f), 0.15f);
                }
                else
                    yield return WalkTo(new Vector2(position.x + side, position.z), 0.25f);
                yield return Check($"交互焦点落在{npc.DisplayName}", () => focus.Current == npc, 3f);
                yield return Step($"按交互键与{npc.DisplayName}说话", hold: 0f);
                yield return Input.Press(input.Actions.Gameplay.Interact);
                yield return Check("气泡文字已完整打出，探索仍在运行",
                    () => bubble.IsShowing && body.textInfo.characterCount > 0
                        && body.maxVisibleCharacters >= body.textInfo.characterCount
                        && Mathf.Approximately(Time.timeScale, 1f)
                        && !dialogue.IsRunning && ui.Get<DialogueView>() == null, 3f);

                var corners = new Vector3[4];
                TMP_Text[] labels = bubble.GetComponentsInChildren<TMP_Text>();
                yield return Check($"{npc.DisplayName}气泡文字无溢出，完整位于相机视口内",
                    () => FitsViewport(bubble, labels, corners), 1f);
                // 边缘气泡可能位于回放信息条下面；截图时只隐藏测试条，保留完整游戏画面。
                overlay.enabled = false;
                try
                {
                    yield return Snapshot($"{npc.DisplayName}·{(visit == 2 ? "正面接近" : "侧面接近")}气泡");
                }
                finally
                {
                    overlay.enabled = true;
                }
                yield return Check("停留后气泡正常淡出", () => !bubble.IsShowing, bubble.HoldSeconds + 2f);
                visit++;
            }
        }

        private static bool FitsViewport(DialogueSpeechBubble bubble, TMP_Text[] labels, Vector3[] corners)
        {
            if (!bubble.IsShowing || Camera.main == null) return false;
            foreach (TMP_Text label in labels)
            {
                if (label.isTextOverflowing || label.preferredHeight > label.rectTransform.rect.height + 0.5f)
                    return false;
            }

            ((RectTransform)bubble.transform).GetWorldCorners(corners);
            foreach (Vector3 corner in corners)
            {
                Vector3 viewport = Camera.main.WorldToViewportPoint(corner);
                if (viewport.z <= 0f || viewport.x < 0f || viewport.x > 1f || viewport.y < 0f || viewport.y > 1f)
                    return false;
            }
            return true;
        }
    }
}
