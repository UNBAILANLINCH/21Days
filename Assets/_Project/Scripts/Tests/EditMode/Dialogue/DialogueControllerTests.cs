// 职责：钉住对白历史转成通用记录面板输入的规则——选择项记为说话者「选择」、其余原样；转换后拼出的文字与改用通用面板前逐字相同。
//   另钉住节点前插播演出的摆放：传了演出锚点 → 演出服务收到锚点的世界位姿；不传 → 收到 PerformancePlacement.None。
//   插播期间场景角色的显隐由演出服务统一处理（PerformanceTriggerRules.HideSceneCharacters，见 PerformanceServiceWorldTests），
//   本类不再重复覆盖。
// 为什么新建：DialogueController 依赖 UI / 资源 / 时钟，没有现成的测试类；这里测它公开的纯静态转换 BuildTranscript，
//   以及插播摆放（真实对白面板预制体 + 假 UI / 假演出服务，演出挂起在第一句前，不走立绘加载与打字），
//   按「被测类 + Tests」单独成文件，其余表现逻辑仍由 Dialogue 回放覆盖。
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Assets;
using Game.Core.Config;
using Game.Core.Timing;
using Game.Core.UI;
using Game.Core.UI.Views;
using Game.Dialogue;
using Game.Performance;
using Game.Tests.EditMode.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Tests.EditMode.Dialogue
{
    public sealed class DialogueControllerTests
    {
        private const string ViewPrefabPath = "Assets/_Project/Prefabs/UI/DialogueView.prefab";
        private const string InterludeId = "perf_test_interlude";

        private readonly List<UnityEngine.Object> created = new List<UnityEngine.Object>();
        private DialogueRules rules;
        private DialogueConfig config;
        private DialoguePlaybackPolicy playback;
        private RecordingPerformanceService performance;
        private DialogueController controller;

        [SetUp]
        public void SetUp()
        {
            var canvas = Track(new GameObject("DialogueControllerTests_Canvas", typeof(RectTransform), typeof(Canvas)));
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ViewPrefabPath);
            Assert.That(prefab, Is.Not.Null, "找不到对白面板预制体：" + ViewPrefabPath);
            GameObject instance = Track(UnityEngine.Object.Instantiate(prefab, canvas.transform, false));
            DialogueView view = instance.GetComponent<DialogueView>();
            Assert.That(view, Is.Not.Null, "预制体根上没有 DialogueView");

            // 第一句就带插播演出：PresentAsync 同步走到插播点，假演出服务挂起，不会进立绘加载与打字。
            rules = new DialogueRules(new DialogueReadData(), 500, null);
            rules.Start(new DialogueContent("interlude_test", "l1", new[]
            {
                new DialogueContent.Node
                {
                    Id = "l1", Kind = DialogueContent.NodeKind.Line, Text = "……", Next = "e", PerformanceId = InterludeId,
                },
                new DialogueContent.Node { Id = "e", Kind = DialogueContent.NodeKind.End, Outcome = "Done" },
            }));
            config = ScriptableObject.CreateInstance<DialogueConfig>();
            created.Add(config);
            playback = new DialoguePlaybackPolicy(config.ToPlaybackSettings());
            var catalog = new DialogueCatalog(new FakeConfigService(ConfigService.BuildTables(ConfigServiceTests.ReadAllTableBytes())));
            performance = new RecordingPerformanceService();
            controller = new DialogueController(rules, catalog, new ViewUIService(view), new FakeAssetService(), new FakeClock(),
                null, performance);
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = created.Count - 1; i >= 0; i--)
            {
                if (created[i] != null) UnityEngine.Object.DestroyImmediate(created[i]);
            }
            created.Clear();
        }

        [Test]
        public void PresentAsync_WithAnchor_PerformanceReceivesAnchorPose()
        {
            Transform anchor = Track(new GameObject("InterludeAnchor")).transform;
            anchor.SetPositionAndRotation(new Vector3(3f, 1.5f, -2f), Quaternion.Euler(0f, 90f, 0f));
            using var cts = new CancellationTokenSource();

            UniTask<string> present = controller.PresentAsync(new DefaultDialogueConditionSource(), "dialogue:interlude_test",
                playback, anchor, cts.Token);

            Assert.That(controller.Performing, Is.True, "第一句前应进入「演出中」");
            Assert.That(performance.Calls, Is.EqualTo(1), "第一句前应插播一次演出");
            Assert.That(performance.LastId, Is.EqualTo(InterludeId));
            Assert.That(performance.LastPlacement.HasValue, Is.True, "传了锚点 → 插播带摆放");
            Assert.That(performance.LastPlacement.Position, Is.EqualTo(anchor.position), "摆放位置等于锚点世界位置");
            Assert.That(performance.LastPlacement.Rotation, Is.EqualTo(anchor.rotation), "摆放朝向等于锚点世界朝向");

            cts.Cancel();
            Assert.That(Capture(present), Is.InstanceOf<OperationCanceledException>());
        }

        [Test]
        public void PresentAsync_WithoutAnchor_PerformanceReceivesNone()
        {
            using var cts = new CancellationTokenSource();

            UniTask<string> present = controller.PresentAsync(new DefaultDialogueConditionSource(), "dialogue:interlude_test",
                playback, cts.Token);

            Assert.That(performance.Calls, Is.EqualTo(1), "第一句前应插播一次演出");
            Assert.That(performance.LastPlacement.HasValue, Is.False, "不传锚点 → PerformancePlacement.None（与旧版不摆放一致）");

            cts.Cancel();
            Assert.That(Capture(present), Is.InstanceOf<OperationCanceledException>());
        }

        private static DialogueSaveData.HistoryEntry Entry(string speaker, string text, bool choice = false) =>
            new DialogueSaveData.HistoryEntry { Speaker = speaker, Text = text, IsChoice = choice };

        [Test]
        public void BuildTranscript_ChoiceEntry_MapsToChoiceSpeaker()
        {
            var entries = new List<DialogueSaveData.HistoryEntry>
            {
                Entry("老者", "人都跑光了，对谁负责去？！"),
                Entry("旅人", "接受", choice: true),
                Entry(string.Empty, "风停了。"),
            };

            List<TranscriptLine> lines = DialogueController.BuildTranscript(entries);

            Assert.That(lines.Count, Is.EqualTo(3));
            Assert.That(lines[0].Speaker, Is.EqualTo("老者"));
            Assert.That(lines[0].Text, Is.EqualTo("人都跑光了，对谁负责去？！"));
            Assert.That(lines[1].Speaker, Is.EqualTo("选择"), "选择项不论谁选，一律记为「选择」");
            Assert.That(lines[1].Text, Is.EqualTo("接受"));
            Assert.That(lines[2].Speaker, Is.Empty, "旁白说话者为空");
            Assert.That(lines[2].Text, Is.EqualTo("风停了。"));
        }

        [Test]
        public void BuildTranscript_ThenFormat_MatchesPreviousHistoryText()
        {
            var entries = new List<DialogueSaveData.HistoryEntry>
            {
                Entry("老者", "人都跑光了，对谁负责去？！"),
                Entry("老者", "我去看看", choice: true),
                Entry(string.Empty, "风停了。"),
            };

            string formatted = TranscriptView.Format(DialogueController.BuildTranscript(entries), true);

            Assert.That(formatted, Is.EqualTo(PreviousHistoryText(entries, true)));
        }

        [Test]
        public void BuildTranscript_NullOrEmpty_ReturnsEmptyList()
        {
            Assert.That(DialogueController.BuildTranscript(null), Is.Empty);
            Assert.That(DialogueController.BuildTranscript(new List<DialogueSaveData.HistoryEntry>()), Is.Empty);
        }

        // 改用通用记录面板之前，对白历史面板 Show 的原实现（逐行照抄），作逐字比对的基准。
        private static string PreviousHistoryText(IReadOnlyList<DialogueSaveData.HistoryEntry> entries, bool truncated)
        {
            var text = new StringBuilder();
            if (truncated) text.AppendLine("更早的记录已省略。\n");
            foreach (DialogueSaveData.HistoryEntry entry in entries)
            {
                if (entry.IsChoice) text.Append("选择：");
                else if (!string.IsNullOrEmpty(entry.Speaker)) text.Append(entry.Speaker).Append("：");
                text.AppendLine(entry.Text).AppendLine();
            }
            return text.ToString();
        }

        private T Track<T>(T obj) where T : UnityEngine.Object
        {
            created.Add(obj);
            return obj;
        }

        // 观察一个已完成的 UniTask：返回它抛出的异常（成功返回 null）。假服务都同步收尾，未完成视为用例失败。
        private static Exception Capture(UniTask<string> task)
        {
            Assert.That(task.Status.IsCompleted(), Is.True, "假服务同步收尾，PresentAsync 此时应已结束");
            try
            {
                task.GetAwaiter().GetResult();
                return null;
            }
            catch (Exception e)
            {
                return e;
            }
        }

        /// <summary>PlayAsync 挂起直到 ct 取消；记录调用次数、最后一次的 id 与摆放。</summary>
        private sealed class RecordingPerformanceService : IPerformanceService
        {
            public int Calls { get; private set; }
            public string LastId { get; private set; }
            public PerformancePlacement LastPlacement { get; private set; }
            public bool IsRunning { get; private set; }
            public string CurrentId => IsRunning ? LastId : null;
            public bool HasPlayed(string id) => false;

            public UniTask<PerformanceResult> PlayAsync(string id, CancellationToken ct = default) =>
                PlayAsync(id, PerformancePlacement.None, ct);

            public UniTask<PerformanceResult> PlayAsync(string id, PerformancePlacement placement, CancellationToken ct = default)
            {
                Calls++;
                LastId = id;
                LastPlacement = placement;
                IsRunning = true;
                var source = new UniTaskCompletionSource<PerformanceResult>();
                ct.Register(() =>
                {
                    IsRunning = false;
                    source.TrySetCanceled(ct);
                });
                return source.Task;
            }

            public void Confirm() { }
            public void Skip() { }
        }

        /// <summary>只开对白面板（直接交出预先实例化的真实预制体）；关闭一律空操作；各层恒可见。</summary>
        private sealed class ViewUIService : IUIService
        {
            private readonly UIView view;

            public ViewUIService(UIView view)
            {
                this.view = view;
            }

            public UniTask<T> OpenAsync<T>(object arg = null, CancellationToken ct = default) where T : UIView
            {
                if (view is T typed) return UniTask.FromResult(typed);
                return UniTask.FromException<T>(new InvalidOperationException("假 UI 只开对白面板，不开 " + typeof(T).Name));
            }

            public UniTask CloseAsync(UIView closing, CancellationToken ct = default) => UniTask.CompletedTask;
            public UniTask CloseTopAsync(CancellationToken ct = default) => UniTask.CompletedTask;
            public T Get<T>() where T : UIView => null;
            public void SetLayerVisible(UILayer layer, bool visible) { }
            public bool IsLayerVisible(UILayer layer) => true;
        }

        /// <summary>本文件的用例停在插播点，走不到立绘加载；被调到就说明流程不对。</summary>
        private sealed class FakeAssetService : IAssetService
        {
            public UniTask<AssetHandle<T>> LoadAsync<T>(string key, CancellationToken ct = default) where T : UnityEngine.Object =>
                throw new NotSupportedException("假资源服务不加载");

            public UniTask<IReadOnlyList<AssetHandle<T>>> LoadAllAsync<T>(string label, CancellationToken ct = default)
                where T : UnityEngine.Object => throw new NotSupportedException("假资源服务不加载");

            public UniTask<GameObject> InstantiateAsync(string key, Transform parent = null, CancellationToken ct = default) =>
                throw new NotSupportedException("假资源服务不实例化");

            public void ReleaseInstance(GameObject instance) => throw new NotSupportedException("假资源服务不实例化");

            public UniTask<SceneHandle> LoadSceneAsync(string key, LoadSceneMode mode, CancellationToken ct = default) =>
                throw new NotSupportedException("假资源服务不加载场景");
        }

        /// <summary>静止的时钟。</summary>
        private sealed class FakeClock : IClock
        {
            public DateTime UtcNow => DateTime.UnixEpoch;
            public float GameTime => 0f;
            public float UnscaledTime => 0f;
            public float DeltaTime => 0f;
            public float UnscaledDeltaTime => 0f;
        }

        /// <summary>只递一份现成 <c>cfg.Tables</c> 的配置服务；不提供内容指纹。</summary>
        private sealed class FakeConfigService : IConfigService
        {
            public FakeConfigService(global::cfg.Tables tables)
            {
                Tables = tables;
            }

            public global::cfg.Tables Tables { get; }
            public ulong ContentHash => throw new NotSupportedException("假配置服务不提供内容指纹");
        }
    }
}
