// 职责：显示当前区域的一条教程，不暂停、不挡射线、不持有玩法服务。
// 通知会超时消失，任务 HUD 已有唯一写入者，故复用 Hud 层另放一条操作提示。
using Game.Core.UI;
using TMPro;
using UnityEngine;
namespace Game.Narrative
{
    public sealed class StoryGuideHudView : UIView
    {
        [SerializeField] private TMP_Text label;
        [SerializeField] private GameObject content;
        private string current;
        public override UILayer Layer => UILayer.Hud;
        public string Text => current ?? "";
        public void Show(string text)
        {
            if (current == text) return;
            current = text;
            if (label != null) label.text = text ?? "";
            if (content != null) content.SetActive(!string.IsNullOrEmpty(text));
        }
    }
}
