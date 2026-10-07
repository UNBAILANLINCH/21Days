using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.LailaFaceRecognition
{
    // 职责：展示当前实时识别/等待/错误，按钮可手动刷新；不提供额外调参控件。
    // 新建原因：项目没有表情识别结果UI。
    public sealed class LailaExpressionPanel : MonoBehaviour
    {
        [SerializeField] private LailaExpressionRecognizer recognizer;
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private TMP_Text resultText;
        [SerializeField] private TMP_Text detailsText;
        [SerializeField] private Button recognizeButton;
        [SerializeField] private Button detailsButton;
        private bool expanded;
        private RectTransform panelRect;
        private float originalPanelHeight;
        private float originalDetailsHeight;
        // 调试折叠内容增加原始／展示／原因三行，保留完整energy与五类分数。
        private const float FeedbackDetailsHeight = 240;

        private void Awake()
        {
            panelRect = GetComponent<RectTransform>();
            if (panelRect != null) originalPanelHeight = panelRect.sizeDelta.y;
            if (detailsText != null) originalDetailsHeight = detailsText.rectTransform.sizeDelta.y;
            if (recognizeButton == null) return;
            var label = recognizeButton.GetComponentInChildren<TMP_Text>();
            if (label != null) label.text = "重新识别";
        }

        private void OnEnable()
        {
            if (recognizer != null)
            {
                recognizer.OnChanged += Refresh;
                if (Application.isPlaying) recognizer.enabled = true;
            }
            if (recognizeButton != null) recognizeButton.onClick.AddListener(Recognize);
            if (detailsButton != null) detailsButton.onClick.AddListener(ToggleDetails);
            Refresh();
        }
        private void OnDisable()
        {
            if (recognizer != null)
            {
                recognizer.OnChanged -= Refresh;
                if (Application.isPlaying) recognizer.enabled = false;
            }
            if (recognizeButton != null) recognizeButton.onClick.RemoveListener(Recognize);
            if (detailsButton != null) detailsButton.onClick.RemoveListener(ToggleDetails);
        }
        private void Recognize() { if (recognizer != null) recognizer.Recognize(); }
        private void ToggleDetails() { expanded = !expanded; Refresh(); }
        private void Refresh()
        {
            if (statusText != null) statusText.text = recognizer != null ? recognizer.Status : "模型未就绪";
            if (resultText != null) resultText.text = recognizer != null ? recognizer.Result : "识别不可用";
            if (detailsText != null)
            {
                bool stable = recognizer != null && recognizer.UsesStableFeedback;
                float height = stable ? FeedbackDetailsHeight : originalDetailsHeight;
                var rect = detailsText.rectTransform;
                rect.sizeDelta = new Vector2(rect.sizeDelta.x, height);
                if (panelRect != null)
                    panelRect.sizeDelta = new Vector2(panelRect.sizeDelta.x, originalPanelHeight + (expanded ? height - originalDetailsHeight : 0));
                detailsText.gameObject.SetActive(expanded);
                detailsText.text = recognizer != null ? recognizer.Details : "未绑定识别组件";
            }
            if (recognizeButton != null) recognizeButton.interactable = recognizer != null && recognizer.IsReady;
        }
    }
}
