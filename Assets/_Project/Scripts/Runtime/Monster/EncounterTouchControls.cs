// 职责：为触屏遭遇战创建与 Gameplay 输入映射相同的虚拟手柄。
using UnityEngine;
using UnityEngine.InputSystem.OnScreen;
using UnityEngine.UI;

namespace Game.Monster
{
    public static class EncounterTouchControls
    {
        public static GameObject Create()
        {
            var root = new GameObject("Encounter Touch Controls", typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            GameObject stickObject = AddControl(root.transform, "Move", new Vector2(140f, 140f),
                new Vector2(240f, 240f), new Color(0.25f, 0.5f, 0.9f, 0.45f), false);
            OnScreenStick stick = stickObject.AddComponent<OnScreenStick>();
            stick.controlPath = "<Gamepad>/leftStick";
            stick.movementRange = 95f;
            stick.useIsolatedInputActions = true;

            AddButton(root.transform, "潜行", "<Gamepad>/leftShoulder", new Vector2(-400f, 140f));
            AddButton(root.transform, "伪装", "<Gamepad>/buttonNorth", new Vector2(-255f, 245f));
            AddButton(root.transform, "攻击", "<Gamepad>/buttonWest", new Vector2(-140f, 140f));
            return root;
        }

        private static void AddButton(Transform parent, string label, string path, Vector2 position)
        {
            GameObject button = AddControl(parent, label, position, new Vector2(118f, 118f),
                new Color(0.9f, 0.5f, 0.2f, 0.6f), true);
            button.AddComponent<OnScreenButton>().controlPath = path;
        }

        private static GameObject AddControl(Transform parent, string label, Vector2 position, Vector2 size,
            Color color, bool right)
        {
            var control = new GameObject(label, typeof(RectTransform), typeof(Image));
            control.transform.SetParent(parent, false);
            RectTransform rect = control.GetComponent<RectTransform>();
            rect.anchorMin = right ? Vector2.one : Vector2.zero;
            rect.anchorMax = rect.anchorMin;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            control.GetComponent<Image>().color = color;

            var caption = new GameObject("Label", typeof(RectTransform), typeof(Text));
            caption.transform.SetParent(control.transform, false);
            RectTransform captionRect = caption.GetComponent<RectTransform>();
            captionRect.anchorMin = Vector2.zero;
            captionRect.anchorMax = Vector2.one;
            captionRect.offsetMin = Vector2.zero;
            captionRect.offsetMax = Vector2.zero;
            Text text = caption.GetComponent<Text>();
            text.text = label;
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.fontSize = 26;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.raycastTarget = false;
            return control;
        }
    }
}
