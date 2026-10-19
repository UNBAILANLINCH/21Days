// 职责：用独立相机和 RectTransform 检查气泡四边约束、近镜头缩小和回到正常构图后的恢复。
// 复用气泡本身的投影逻辑；既有交互测试只测事件，无法覆盖表现位置，故单列组件测试，不依赖场景资产。
using System.Reflection;
using Game.Dialogue;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests.EditMode.Dialogue
{
    public sealed class DialogueSpeechBubbleTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void Disable_DuringEnterOrExit_CancelsMotionAndRestoresState(bool exiting)
        {
            var npc = new GameObject("Speaker", typeof(DialogueInteractable));
            var host = new GameObject("Bubble", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
            host.SetActive(false);
            host.transform.SetParent(npc.transform, false);
            try
            {
                var content = new GameObject("Content", typeof(RectTransform));
                content.transform.SetParent(host.transform, false);
                var bubble = host.AddComponent<DialogueSpeechBubble>();
                var group = host.GetComponent<CanvasGroup>();
                var serialized = new SerializedObject(bubble);
                serialized.FindProperty("root").objectReferenceValue = content;
                serialized.FindProperty("group").objectReferenceValue = group;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Invoke(bubble, "Awake");
                bubble.Show("第一句");
                var field = typeof(DialogueSpeechBubble).GetField("fade", BindingFlags.Instance | BindingFlags.NonPublic);
                object first = field.GetValue(bubble);
                var active = first.GetType().Assembly.GetType("LitMotion.MotionHandleExtensions").GetMethod("IsActive");
                Assert.That(active.Invoke(null, new[] { first }), Is.True);
                bubble.Show("第二句");
                Assert.That(active.Invoke(null, new[] { first }), Is.False, "重复触发须取消旧入场");
                if (exiting) Invoke(bubble, "BeginFade");
                object current = field.GetValue(bubble);
                Assert.That(active.Invoke(null, new[] { current }), Is.True);

                Invoke(bubble, "OnDisable");

                Assert.That(active.Invoke(null, new[] { current }), Is.False, "禁用后不得留下迟到补间");
                Assert.That(bubble.IsShowing, Is.False);
                Assert.That(content.activeSelf, Is.False);
                Assert.That(group.alpha, Is.EqualTo(1f));
            }
            finally
            {
                Object.DestroyImmediate(npc);
            }
        }

        [TestCase(false, -0.1f, 0.5f, 0.0035f)]
        [TestCase(false, 1.1f, 0.5f, 0.0035f)]
        [TestCase(false, 0.5f, -0.1f, 0.0035f)]
        [TestCase(false, 0.5f, 1.1f, 0.0035f)]
        [TestCase(true, -0.1f, 0.5f, 0.0035f)]
        [TestCase(true, 1.1f, 0.5f, 0.0035f)]
        [TestCase(true, 0.5f, -0.1f, 0.0035f)]
        [TestCase(true, 0.5f, 1.1f, 0.0035f)]
        [TestCase(false, 0.5f, 0.5f, 0.1f)]
        [TestCase(true, 0.5f, 0.5f, 0.1f)]
        public void VisibleBubble_FitsViewport_AndRestoresAnchor(bool orthographic, float x, float y, float scale)
        {
            var cameraObject = new GameObject("BubbleCamera", typeof(Camera));
            var npc = new GameObject("Speaker", typeof(DialogueInteractable));
            var host = new GameObject("Bubble", typeof(RectTransform), typeof(Canvas));
            host.SetActive(false);
            host.transform.SetParent(npc.transform, false);
            try
            {
                Camera camera = cameraObject.GetComponent<Camera>();
                camera.orthographic = orthographic;
                camera.orthographicSize = 5f;
                camera.fieldOfView = 60f;
                camera.aspect = 16f / 9f;
                Canvas canvas = host.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.worldCamera = camera;
                var rect = (RectTransform)host.transform;
                rect.sizeDelta = new Vector2(400f, 160f);
                rect.localPosition = Vector3.up;
                rect.localScale = Vector3.one * scale;
                var content = new GameObject("Content", typeof(RectTransform));
                content.transform.SetParent(host.transform, false);
                var bubble = host.AddComponent<DialogueSpeechBubble>();
                var serialized = new SerializedObject(bubble);
                serialized.FindProperty("root").objectReferenceValue = content;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Invoke(bubble, "Awake");
                npc.transform.position = camera.ViewportToWorldPoint(new Vector3(x, y, 10f));
                bubble.Show(string.Empty);
                Invoke(bubble, "LateUpdate");

                var corners = new Vector3[4];
                rect.GetWorldCorners(corners);
                foreach (Vector3 corner in corners)
                {
                    Vector3 viewport = camera.WorldToViewportPoint(corner);
                    Assert.That(viewport.x, Is.InRange(-0.0001f, 1.0001f));
                    Assert.That(viewport.y, Is.InRange(-0.0001f, 1.0001f));
                }
                Vector3 first = rect.position;
                Invoke(bubble, "LateUpdate");
                Assert.That(Vector3.Distance(first, rect.position), Is.LessThan(0.0001f), "重复更新不能累积漂移");

                camera.orthographicSize = 50f;
                camera.fieldOfView = 150f;
                npc.transform.position = camera.ViewportToWorldPoint(new Vector3(0.5f, 0.5f, 10f));
                Invoke(bubble, "LateUpdate");
                Assert.That(Vector3.Distance(rect.localPosition, Vector3.up), Is.LessThan(0.0001f));
                Assert.That(Vector3.Distance(rect.localScale, Vector3.one * scale), Is.LessThan(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(npc);
                Object.DestroyImmediate(cameraObject);
            }
        }

        private static void Invoke(DialogueSpeechBubble bubble, string method)
        {
            typeof(DialogueSpeechBubble).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(bubble, null);
        }
    }
}
