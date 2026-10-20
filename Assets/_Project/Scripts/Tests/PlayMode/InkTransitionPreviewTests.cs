// 职责：验证水墨预览及正式加载表现的等待、播放和取消清理。
using System.Collections;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.InkTransition;
using Game.Monster;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Game.Tests.PlayMode
{
    public sealed class InkTransitionPreviewTests
    {
        [UnityTest]
        public IEnumerator LoadingPresentation_WaitsForReady_AndCancellationClearsOwnedOverlay()
        {
            var root = new GameObject("InkLoadingTest", typeof(RectTransform));
            root.SetActive(false);
            var source = new Material(Shader.Find("21Days/UI/InkWashTransition"));
            try
            {
                var overlay = Child<RawImage>(root, "Overlay");
                var backdrop = Child<Image>(root, "Backdrop");
                var preview = overlay.gameObject.AddComponent<InkTransitionPreview>();
                Bind(preview, "controlledOnly", true);
                Bind(preview, "inkOverlay", overlay);
                Bind(preview, "inkMaterial", source);
                Bind(preview, "duration", .1f);
                var presentation = root.AddComponent<InkLoadingPresentation>();
                typeof(InkLoadingPresentation).GetField("preview", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(presentation, preview);
                typeof(InkLoadingPresentation).GetField("backdrop", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(presentation, backdrop);
                root.SetActive(true);
                yield return null;
                UniTask cover = presentation.CoverAsync(CancellationToken.None);
                Assert.That(cover.Status, Is.EqualTo(UniTaskStatus.Pending));
                Assert.That(backdrop.enabled, Is.False);
                Assert.That(overlay.raycastTarget, Is.True, "加载水墨必须挡住画布点击");
                yield return new WaitForSecondsRealtime(.12f);
                Assert.That(cover.Status, Is.EqualTo(UniTaskStatus.Succeeded));
                Assert.That(preview.AwaitingReveal, Is.True);
                yield return new WaitForSecondsRealtime(.15f);
                Assert.That(preview.Progress, Is.EqualTo(.5f).Within(.001f), "加载未就绪时不能自行揭幕");
                UniTask reveal = presentation.RevealAsync(CancellationToken.None);
                yield return new WaitForSecondsRealtime(.12f);
                Assert.That(reveal.Status, Is.EqualTo(UniTaskStatus.Succeeded));
                Assert.That(overlay.enabled, Is.False);
                Assert.That(backdrop.enabled, Is.True);
                UniTask stopped = presentation.CoverAsync(CancellationToken.None);
                preview.StopPreview();
                Assert.Catch<System.OperationCanceledException>(() => stopped.GetAwaiter().GetResult());
                Assert.That(overlay.enabled, Is.False);
                Assert.That(backdrop.enabled, Is.True, "中断盖幕不能被当作已完成盖幕");
                using (var cts = new CancellationTokenSource())
                {
                    UniTask cancelled = presentation.CoverAsync(cts.Token);
                    cts.Cancel();
                    Assert.Catch<System.OperationCanceledException>(() => cancelled.GetAwaiter().GetResult());
                    Assert.That(overlay.enabled, Is.False);
                    Assert.That(preview.HasWorldEffect, Is.False);
                    Assert.That(backdrop.enabled, Is.True);
                }
            }
            finally { Object.Destroy(root); Object.Destroy(source); }
        }

        [UnityTest]
        public IEnumerator ActorReplay_UsesLatestControlObject_AndClearsOnlyOwnedInk()
        {
            var root = new GameObject("ActorInkTest", typeof(RectTransform), typeof(Canvas));
            root.SetActive(false);
            root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var cameraObject = new GameObject("ActorInkCamera", typeof(Camera));
            var camera = cameraObject.GetComponent<Camera>();
            camera.transform.position = new Vector3(0, 0, -10);
            camera.orthographic = true;
            camera.orthographicSize = 5f;
            var actorA = new GameObject("ActorA", typeof(SpriteRenderer));
            var actorB = new GameObject("ActorB", typeof(SpriteRenderer));
            actorA.transform.position = Vector3.right * 2f;
            actorB.transform.position = Vector3.left * 2f;
            var viewObject = new GameObject("EncounterView", typeof(EncounterSceneView));
            var view = viewObject.GetComponent<EncounterSceneView>();
            var inkSource = new Material(Shader.Find("21Days/UI/InkWashTransition"));
            var actorSource = new Material(Shader.Find("21Days/SpriteDepthClip"));
            var sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 2, 2), Vector2.one * .5f, 2f);
            var a = actorA.GetComponent<SpriteRenderer>();
            var b = actorB.GetComponent<SpriteRenderer>();
            a.sprite = sprite; b.sprite = sprite;
            a.sharedMaterial = actorSource; b.sharedMaterial = actorSource;
            var block = new MaterialPropertyBlock();
            int tint = Shader.PropertyToID("_Color");
            try
            {
                var preview = root.AddComponent<InkTransitionPreview>();
                var background = Child<RawImage>(root, "Background");
                var overlay = Child<RawImage>(root, "Overlay");
                overlay.rectTransform.anchorMin = Vector2.zero;
                overlay.rectTransform.anchorMax = Vector2.one;
                overlay.rectTransform.offsetMin = Vector2.zero;
                overlay.rectTransform.offsetMax = Vector2.zero;
                Bind(preview, "background", background);
                Bind(preview, "firstBackground", Texture2D.whiteTexture);
                Bind(preview, "secondBackground", Texture2D.blackTexture);
                Bind(preview, "inkOverlay", overlay);
                Bind(preview, "inkMaterial", inkSource);
                Bind(preview, "progressSlider", Child<Slider>(root, "Progress"));
                Bind(preview, "replayButton", Child<Button>(root, "Replay"));
                Bind(preview, "pauseButton", Child<Button>(root, "Pause"));
                Bind(preview, "actorCamera", camera);
                Bind(preview, "encounterView", view);
                typeof(EncounterSceneView).GetField("playerBody", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(view, actorA.transform);
                root.SetActive(true);
                yield return null;
                Canvas.ForceUpdateCanvases();
                preview.Replay();
                preview.SetProgress(.07f);
                Assert.That(preview.HasActorOrigin, Is.True);
                Assert.That(background.enabled, Is.False, "真实角色试播不能被截图背景挡住");
                float firstOrigin = overlay.material.GetVector("_OriginUV").x;
                Assert.That(preview.HasWorldEffect, Is.True, "真实角色使用周身世界墨层，不再涂黑衣服");
                a.GetPropertyBlock(block);
                block.SetColor(tint, Color.magenta);
                a.SetPropertyBlock(block);
                typeof(EncounterSceneView).GetField("playerBody", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(view, actorB.transform);
                yield return null;
                Assert.That(overlay.enabled, Is.False, "试播中途换控制对象应终止旧墨痕");
                a.GetPropertyBlock(block);
                Assert.That(preview.HasWorldEffect, Is.False);
                preview.Replay();
                preview.SetProgress(.12f);
                Assert.That(overlay.material.GetVector("_OriginUV").x, Is.LessThan(firstOrigin));
                a.GetPropertyBlock(block);
                Assert.That(block.GetColor(tint), Is.EqualTo(Color.magenta));
                Assert.That(preview.HasWorldEffect, Is.True);
                Assert.That(actorSource.HasProperty("_InkSeedStrength"), Is.False, "已撤除旧衣服墨斑分支");
                camera.enabled = false;
                yield return null;
                Assert.That(preview.HasWorldEffect, Is.False, "相机禁用时中止试播并清墨");
                Assert.That(overlay.enabled, Is.False);
                camera.enabled = true;
                preview.Replay();
                preview.SetProgress(.12f);
                preview.StopPreview();
                Assert.That(preview.HasWorldEffect, Is.False);
                Assert.That(overlay.enabled, Is.False);
                Bind(preview, "duration", .1f);
                int coveredCount = 0;
                int revealedCount = 0;
                int cancelledCount = 0;
                Assert.That(preview.BeginCover(() => coveredCount++, () => revealedCount++, () => cancelledCount++), Is.True);
                Assert.That(preview.RevealAfterReady(), Is.False, "尚未全遮时拒绝提前揭幕");
                yield return new WaitForSecondsRealtime(.12f);
                Assert.That(coveredCount, Is.EqualTo(1));
                Assert.That(preview.AwaitingReveal, Is.True);
                Assert.That(preview.Progress, Is.EqualTo(.5f).Within(.001f));
                preview.SetProgress(0f);
                preview.Replay();
                preview.TogglePause();
                Assert.That(preview.Progress, Is.EqualTo(.5f).Within(.001f), "UI不得绕过加载等待");
                Assert.That(preview.BeginCover(() => coveredCount++), Is.False, "已有受控流程时拒绝隐式替换");
                camera.enabled = false;
                yield return null;
                Assert.That(overlay.enabled, Is.True, "全遮后角色或相机卸载应保持屏幕遮蔽");
                Assert.That(preview.HasWorldEffect, Is.False);
                Assert.That(preview.RevealAfterReady(), Is.True);
                Assert.That(preview.RevealAfterReady(), Is.False, "重复完成信号不得启动第二次揭幕");
                yield return new WaitForSecondsRealtime(.12f);
                Assert.That(preview.Progress, Is.EqualTo(1f).Within(.001f));
                Assert.That(coveredCount, Is.EqualTo(1));
                Assert.That(revealedCount, Is.EqualTo(1));
                Assert.That(cancelledCount, Is.Zero);
                camera.enabled = true;
                Assert.That(preview.BeginCover(() => coveredCount++, null, () => cancelledCount++), Is.True);
                preview.StopPreview();
                yield return new WaitForSecondsRealtime(.12f);
                Assert.That(coveredCount, Is.EqualTo(1), "取消后不得收到旧盖幕回调");
                Assert.That(cancelledCount, Is.EqualTo(1));
                preview.Replay();
                preview.SetProgress(.12f);
                root.SetActive(false);
                Assert.That(preview.HasWorldEffect, Is.False, "停用清除世界墨层");
            }
            finally
            {
                Object.Destroy(root); Object.Destroy(cameraObject); Object.Destroy(actorA); Object.Destroy(actorB);
                Object.Destroy(viewObject); Object.Destroy(inkSource); Object.Destroy(actorSource); Object.Destroy(sprite);
            }
        }

        [UnityTest]
        public IEnumerator ReplayScrubAndDisable_PreserveBackgroundAndReleaseMaterial()
        {
            var root = new GameObject("InkPreviewTest", typeof(RectTransform));
            root.SetActive(false);
            var shader = Shader.Find("21Days/UI/InkTransition");
            Assert.That(shader, Is.Not.Null);
            var source = new Material(shader);
            try
            {
                var preview = root.AddComponent<InkTransitionPreview>();
                var background = Child<RawImage>(root, "Background");
                var overlay = Child<RawImage>(root, "Overlay");
                var slider = Child<Slider>(root, "Progress");
                var replay = Child<Button>(root, "Replay");
                var pause = Child<Button>(root, "Pause");
                Bind(preview, "background", background);
                Bind(preview, "firstBackground", Texture2D.whiteTexture);
                Bind(preview, "secondBackground", Texture2D.blackTexture);
                Bind(preview, "inkOverlay", overlay);
                Bind(preview, "inkMaterial", source);
                Bind(preview, "progressSlider", slider);
                Bind(preview, "replayButton", replay);
                Bind(preview, "pauseButton", pause);
                Bind(preview, "duration", 30f);
                root.SetActive(true);
                yield return null;
                Assert.That(preview.IsPlaying, Is.False, "挂入场景不能自动遮屏");
                Assert.That(overlay.enabled, Is.False);
                preview.SetProgress(.1f);
                Assert.That(overlay.enabled, Is.True, "首次拖动也应初始化预览");
                preview.StopPreview();
                preview.Replay();
                Assert.That(preview.IsPlaying, Is.True);
                pause.onClick.Invoke();
                Assert.That(preview.IsPlaying, Is.False);
                pause.onClick.Invoke();
                Assert.That(preview.IsPlaying, Is.True);

                preview.SetProgress(-1f);
                Assert.That(preview.Progress, Is.Zero);
                Assert.That(preview.IsPlaying, Is.False);
                Assert.That(background.texture, Is.SameAs(Texture2D.whiteTexture));
                slider.value = .5f;
                Assert.That(background.texture, Is.SameAs(Texture2D.blackTexture));
                preview.SetProgress(2f);
                Assert.That(preview.Progress, Is.EqualTo(1f));
                replay.onClick.Invoke();
                Assert.That(preview.IsPlaying, Is.True);
                Assert.That(background.texture, Is.SameAs(Texture2D.blackTexture));
                preview.SetProgress(.5f);
                Assert.That(background.texture, Is.SameAs(Texture2D.whiteTexture));

                var runtime = overlay.material;
                Assert.That(runtime, Is.Not.SameAs(source));
                root.SetActive(false);
                slider.value = .8f;
                Assert.That(preview.Progress, Is.Zero);
                Assert.That(preview.HasRuntimeMaterial, Is.False);
                Assert.That(preview.IsPlaying, Is.False);
                Assert.That(overlay.enabled, Is.False);
                yield return null;
                Assert.That(runtime == null, Is.True, "停用后须释放运行时材质");
                Assert.That(source != null, Is.True, "不能销毁源材质");
            }
            finally { Object.Destroy(root); Object.Destroy(source); }
        }

        private static T Child<T>(GameObject root, string name) where T : Component
        {
            var child = new GameObject(name, typeof(RectTransform), typeof(T));
            child.transform.SetParent(root.transform, false);
            return child.GetComponent<T>();
        }

        private static void Bind(InkTransitionPreview preview, string field, object value) =>
            typeof(InkTransitionPreview).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(preview, value);
    }
}
