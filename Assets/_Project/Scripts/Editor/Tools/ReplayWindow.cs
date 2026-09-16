// 职责：菜单「21Days/工程/回放窗口」——回放系统的人机界面。选一份回放文件、放/暂停/单步/停/变速，
//   把播放器的状态（tick、进度、漂移统计）显示出来。
//
// 本窗口不含任何回放逻辑：载入、推进、漂移检测、从快照拉回正轨，全都在 Game.Core.Replay.ReplayPlayer 里，
//   这里只做两件事——读它的属性画出来、把点击变成对它的状态修改（Play / Pause / StepOnce / Stop / Speed）。
//   刻意如此：Showcase 回放场景与 EditMode 测试不开这个窗口，走的是同一个 ReplayPlayer。
//   一旦窗口自己推一格 tick 或自己判一次漂移，「窗口里能复现、跑测试复现不了」就会开始出现。
//
// 为什么新建（project-root.md「加能力的顺序」）：
//   1. 复用不行：工程里没有任何窗口碰过运行时容器里的服务。AssetAuditWindow 是编辑期只读扫资产，
//      跟运行期状态无关；GameFlowRestartShortcut 只是个一次性按钮，没有需要持续显示的状态。
//   2. 扩展不行：塞进 AssetAuditWindow 名实不符（那个叫资产体检，且只在没播放时用）；
//      塞进 GameFlowRestartShortcut 也不行——工具栏按钮没有地方画 tick、进度与八项漂移统计。

using System;
using System.IO;
using Game.Core.Boot;
using Game.Core.Replay;
using UnityEditor;
using UnityEngine;
using VContainer;

namespace Game.Editor
{
    /// <summary>
    /// 回放窗口。<b>必须在 Play 模式下用</b>：回放是把录下来的输入真的重跑一遍逻辑，
    /// 没进 Play 就没有推进器、没有世界状态，也就没有可放的东西。没进 Play 时窗口不会静默失效，
    /// 而是把控制区整块灰掉并说明原因。
    /// <para>
    /// <b>播放器从哪来</b>：<see cref="ReplayPlayer"/> 由 <see cref="GameLifetimeScope"/> 注册在
    /// VContainer 容器里（<c>RegisterEntryPoint&lt;ReplayPlayer&gt;().AsSelf()</c>），
    /// 本窗口找到场景里那个 LifetimeScope，用 <c>Container.TryResolve</c> 取出来并缓存。
    /// 取不到（没进 Play、当前场景没有 GameLifetimeScope、启动还没跑完）时显示提示并灰掉控制区，
    /// 每 <see cref="ResolveIntervalSeconds"/> 秒自己重试一次，不抛异常、不刷日志。
    /// </para>
    /// <para>
    /// 关掉窗口<b>不会</b>停止正在放的回放：窗口只是界面，播放由播放器自己每渲染帧驱动。
    /// 要停就按「停止」。
    /// </para>
    /// </summary>
    public sealed class ReplayWindow : EditorWindow
    {
        /// <summary>
        /// 存档根目录的子目录名。<see cref="Game.Core.Platform.PlatformServiceBase"/> 里同名常量是 private，
        /// 而且编辑器态下根本拿不到运行中的 <c>IPlatformService</c>（没进 Play 时容器还不存在），
        /// 所以这里按同一个拼法推算：<c>persistentDataPath/saves/Replays</c>。
        /// 那边改了这里要跟着改——两处都动不了对方，只能靠这条注释。
        /// </summary>
        private const string SaveFolderName = "saves";

        /// <summary>拿不到播放器时的重试间隔（秒）。太密没意义：容器是在 Play 开始那一下建好的。</summary>
        private const double ResolveIntervalSeconds = 0.5d;

        private const float ButtonWidth = 72f;
        private const float ButtonHeight = 22f;

        /// <summary>运行中的播放器。null 表示没连上（没进 Play，或容器里还没有它）。</summary>
        private ReplayPlayer player;

        /// <summary>选中的回放文件路径（还没载入）。</summary>
        private string selectedPath = string.Empty;

        /// <summary>连不上播放器时给人看的一句话。</summary>
        private string connectHint = string.Empty;

        /// <summary>最近一次载入的结果，成功是一句摘要、失败是播放器给出的原因。</summary>
        private string loadMessage = string.Empty;

        private MessageType loadMessageType = MessageType.None;

        /// <summary>配置指纹对不上时是否照样载入。默认关：结果不可信的重放不该悄悄发生。</summary>
        private bool ignoreConfigMismatch;

        /// <summary>没连上播放器时滑条显示的速度。连上之后以播放器的 <c>Speed</c> 为准。</summary>
        private float speedDraft = 1f;

        private double nextResolveTime;
        private Vector2 scroll;

        [MenuItem("21Days/工程/回放窗口", false, 230)]
        public static void Open()
        {
            ReplayWindow window = GetWindow<ReplayWindow>(false, "回放", true);
            window.minSize = new Vector2(420f, 360f);
            window.Show();
        }

        private void OnEnable()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            connectHint = string.Empty;
            nextResolveTime = 0d;
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;

            // 只丢引用，不 Stop：窗口是界面，关窗不该把别人正在放的回放掐掉。
            player = null;
        }

        /// <summary>
        /// 编辑器窗口的 Update 约每秒 10 次。这里做两件事：把连不上的播放器重连回来、
        /// 在播放中触发重绘（不重绘的话 tick 不会动，窗口看着像死的）。
        /// </summary>
        private void Update()
        {
            if (!EditorApplication.isPlaying)
            {
                if (player != null)
                {
                    // 退出 Play 之后容器已经销毁，缓存的播放器是个死对象，必须丢掉。
                    player = null;
                    Repaint();
                }

                return;
            }

            if (player == null)
            {
                if (EditorApplication.timeSinceStartup < nextResolveTime)
                {
                    return;
                }

                nextResolveTime = EditorApplication.timeSinceStartup + ResolveIntervalSeconds;
                ResolvePlayer();
                Repaint();
                return;
            }

            // 只要还占着推进器就重绘：播放中 tick 在走，暂停中也可能有排队的单步要推。
            if (player.IsActive)
            {
                Repaint();
            }
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);

            DrawConnection();
            DrawFilePicker();

            bool connected = player != null;
            using (new EditorGUI.DisabledScope(!connected))
            {
                DrawTransport();
            }

            DrawStatus();

            EditorGUILayout.EndScrollView();
        }

        /// <summary>画「能不能用」那一段：没进 Play、或进了 Play 但容器里取不到播放器。</summary>
        private void DrawConnection()
        {
            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "回放需要先进入 Play 模式。\n"
                    + "回放不是播一段录像，而是把录下来的输入重新喂给逻辑真跑一遍——没进 Play 就没有推进器、"
                    + "没有世界状态，控制区因此全部灰着。可以先选好文件，按下 Play 之后再载入。",
                    MessageType.Warning);
                return;
            }

            if (player == null)
            {
                EditorGUILayout.HelpBox(
                    (string.IsNullOrEmpty(connectHint) ? "还没连上回放播放器。" : connectHint)
                    + "\n窗口会每半秒自己重试一次，也可以按下面的「重新连接」。",
                    MessageType.Warning);

                if (GUILayout.Button("重新连接", GUILayout.Width(120f), GUILayout.Height(ButtonHeight)))
                {
                    ResolvePlayer();
                }

                return;
            }

            EditorGUILayout.HelpBox(
                "已连上运行中的回放播放器。播放、单步与漂移检测都由它自己做，本窗口只改它的状态。",
                MessageType.None);
        }

        /// <summary>画选文件那一行。选文件在没进 Play 时也允许——先挑好再按 Play 是常见用法。</summary>
        private void DrawFilePicker()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("回放文件", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.TextField(
                        string.IsNullOrEmpty(selectedPath) ? "（还没选文件）" : selectedPath);
                }

                if (GUILayout.Button("选择…", GUILayout.Width(ButtonWidth), GUILayout.Height(ButtonHeight)))
                {
                    PickFile();
                }

                if (GUILayout.Button("打开目录", GUILayout.Width(ButtonWidth), GUILayout.Height(ButtonHeight)))
                {
                    EditorUtility.RevealInFinder(DefaultReplayDirectory());
                }
            }

            ignoreConfigMismatch = EditorGUILayout.ToggleLeft(
                "配置指纹对不上也载入（结果不可信，只用来看画面）",
                ignoreConfigMismatch);

            if (!string.IsNullOrEmpty(loadMessage))
            {
                EditorGUILayout.HelpBox(loadMessage, loadMessageType);
            }
        }

        /// <summary>画载入与播放控制。整块的可用性由外层 DisabledScope 管，这里只管更细的那几层。</summary>
        private void DrawTransport()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("控制", EditorStyles.boldLabel);

            bool loaded = player != null && player.IsLoaded;
            bool active = player != null && player.IsActive;
            bool finished = player != null && player.IsFinished;
            bool playing = player != null && player.IsPlaying;

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(selectedPath)))
                {
                    if (GUILayout.Button("载入", GUILayout.Width(ButtonWidth), GUILayout.Height(ButtonHeight)))
                    {
                        LoadSelected();
                    }
                }

                using (new EditorGUI.DisabledScope(!active || finished || playing))
                {
                    if (GUILayout.Button("播放", GUILayout.Width(ButtonWidth), GUILayout.Height(ButtonHeight)))
                    {
                        player.Play();
                    }
                }

                using (new EditorGUI.DisabledScope(!playing))
                {
                    if (GUILayout.Button("暂停", GUILayout.Width(ButtonWidth), GUILayout.Height(ButtonHeight)))
                    {
                        player.Pause();
                    }
                }

                using (new EditorGUI.DisabledScope(!active || finished))
                {
                    if (GUILayout.Button("单步", GUILayout.Width(ButtonWidth), GUILayout.Height(ButtonHeight)))
                    {
                        player.StepOnce();
                    }
                }

                using (new EditorGUI.DisabledScope(!active))
                {
                    if (GUILayout.Button("停止", GUILayout.Width(ButtonWidth), GUILayout.Height(ButtonHeight)))
                    {
                        player.Stop();
                    }
                }
            }

            float current = player == null ? speedDraft : player.Speed;
            float next = EditorGUILayout.Slider(
                "播放速度", current, ReplayPlayer.MinSpeed, ReplayPlayer.MaxSpeed);
            if (next != current)
            {
                speedDraft = next;
                if (player != null)
                {
                    player.Speed = next;
                }
            }

            EditorGUILayout.LabelField(
                loaded
                    ? "变速只改「这一帧推几个 tick」，不改每个 tick 里发生的事：8 倍速与 1 倍速结果逐位相同。"
                    : "先选一份文件再按「载入」。放完之后不能倒带，要再看一遍就重新载入。",
                EditorStyles.wordWrappedMiniLabel);
        }

        /// <summary>画播放器的状态：进度、tick、漂移与几项输入统计。没连上或没载入时只说一句。</summary>
        private void DrawStatus()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("状态", EditorStyles.boldLabel);

            if (player == null)
            {
                EditorGUILayout.LabelField("    没连上播放器，没有状态可显示。", EditorStyles.miniLabel);
                return;
            }

            if (!player.IsLoaded)
            {
                EditorGUILayout.LabelField("    还没载入回放。", EditorStyles.miniLabel);
                return;
            }

            Rect bar = EditorGUILayout.GetControlRect(false, 18f);
            EditorGUI.ProgressBar(
                bar,
                player.Progress,
                $"{player.Progress * 100f:F1}%   tick {player.CurrentTick} / {player.EndTick}");

            EditorGUILayout.LabelField("播放状态", DescribePlayState());
            EditorGUILayout.LabelField("tick 区间", $"{player.StartTick} ~ {player.EndTick}（当前 {player.CurrentTick}）");
            EditorGUILayout.LabelField("输入条数", player.InputCount.ToString());

            DrawDrift();

            EditorGUILayout.LabelField(
                "已校验哈希点", $"{player.CheckedHashCount} / {player.StateHashCount}");
            EditorGUILayout.LabelField("从快照拉回次数", player.ResyncCount.ToString());
            EditorGUILayout.LabelField("缺失的输入", player.MissingInputCount.ToString());
            EditorGUILayout.LabelField("跳过的输入", player.SkippedInputCount.ToString());

            if (player.MissingInputCount > 0L)
            {
                EditorGUILayout.HelpBox(
                    $"这份录像有 {player.MissingInputCount} 个 tick 缺输入，那几 tick 按「没按键」放的，"
                    + "结果和录制时不同。",
                    MessageType.Warning);
            }

            if (player.SkippedInputCount > 0L)
            {
                EditorGUILayout.HelpBox(
                    $"有 {player.SkippedInputCount} 条输入被跳过——说明推进器在播放器之外也被推了格，"
                    + "从那里往后的重放结果不可信。",
                    MessageType.Warning);
            }

            if (player.ResultsUntrusted)
            {
                EditorGUILayout.HelpBox(
                    "这次是带着「配置指纹对不上」的标注在放：同样的输入在两版数值下本来就会推出不同结果，"
                    + "本次重放的任何结论都不能当证据用。",
                    MessageType.Warning);
            }

            if (player.IsTailTruncated)
            {
                EditorGUILayout.HelpBox(
                    "这份文件尾部残缺。崩溃时自动保存的回放通常如此，不是错误。",
                    MessageType.Info);
            }

            if (player.StateHashCount == 0)
            {
                EditorGUILayout.HelpBox(
                    "这份录像里没有状态哈希，做不了漂移检测——放得出画面，但查不出「哪一 tick 开始不对」。",
                    MessageType.Info);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("文件头", EditorStyles.miniBoldLabel);
            EditorGUILayout.LabelField($"    {player.Header}", EditorStyles.wordWrappedMiniLabel);
            if (!string.IsNullOrEmpty(player.SourcePath))
            {
                EditorGUILayout.LabelField($"    {player.SourcePath}", EditorStyles.wordWrappedMiniLabel);
            }
        }

        /// <summary>漂移是这个窗口最该显眼的东西，所以单独一块，有漂移就是 Warning 级。</summary>
        private void DrawDrift()
        {
            if (player.HasDrifted)
            {
                EditorGUILayout.HelpBox(
                    $"状态漂移：首次在 tick {player.FirstDriftTick}，共 {player.DriftCount} 个校验点对不上，"
                    + $"从完整快照拉回 {player.ResyncCount} 次。\n"
                    + "说明逻辑里有一处不确定来源（读了渲染帧时间 / UnityEngine.Random / 未进快照的状态，"
                    + "或者用了随机的容器遍历顺序）。首次那个 tick 才是查因的起点，后面的对不上多半是它的连锁。",
                    MessageType.Warning);
                return;
            }

            EditorGUILayout.HelpBox(
                player.CheckedHashCount > 0
                    ? $"到目前为止零漂移（已校验 {player.CheckedHashCount} 个状态哈希点）。"
                    : "还没走到第一个状态哈希校验点，漂移与否暂时判不了。",
                MessageType.None);
        }

        private string DescribePlayState()
        {
            if (player.IsFinished)
            {
                return "已放完（不能倒带，要再看一遍请重新载入）";
            }

            if (!player.IsActive)
            {
                return "已停止（推进器与输入源已还给实时玩法，统计仍可查）";
            }

            return player.IsPlaying ? $"播放中（{player.Speed:0.##}×）" : "暂停";
        }

        private void PickFile()
        {
            // OpenFilePanel 的扩展名参数不带点，所以这里把 ReplayFormat 的常量去掉前导点再传。
            string extension = ReplayFormat.FileExtension.TrimStart('.');
            string picked = EditorUtility.OpenFilePanel("选择回放文件", DefaultReplayDirectory(), extension);
            if (string.IsNullOrEmpty(picked))
            {
                return;
            }

            selectedPath = picked;
            loadMessage = string.Empty;
            loadMessageType = MessageType.None;
        }

        private void LoadSelected()
        {
            if (player == null || string.IsNullOrEmpty(selectedPath))
            {
                return;
            }

            try
            {
                string error;
                if (player.Load(selectedPath, ignoreConfigMismatch, out error))
                {
                    loadMessage = $"已载入 {Path.GetFileName(selectedPath)}："
                        + $"输入 {player.InputCount} 条、状态哈希 {player.StateHashCount} 条、"
                        + $"完整快照 {player.SnapshotCount} 个。按「播放」开始。";
                    loadMessageType = MessageType.Info;
                }
                else
                {
                    loadMessage = error;
                    loadMessageType = MessageType.Error;
                }
            }
            catch (Exception e)
            {
                // 载入失败正常情况下走的是 out error，这里兜的是它没接住的那种（磁盘 IO 之类）。
                // 编辑器窗口不该因为选错一个文件就往控制台刷异常栈。
                loadMessage = $"载入时出了异常：{e.GetType().Name}：{e.Message}";
                loadMessageType = MessageType.Error;
            }
        }

        /// <summary>
        /// 从运行中的容器里取播放器。**刻意不放在 <see cref="Update"/> 里**：查找有代价，
        /// 由 Update 按 <see cref="ResolveIntervalSeconds"/> 节流调用，连上之后一次都不再查。
        /// 全程只写提示、不抛异常、不打日志——连不上是常态（没进 Play、启动还没跑完），
        /// 每半秒往控制台刷一条才是真的坏事。
        /// </summary>
        private void ResolvePlayer()
        {
            player = null;

            if (!EditorApplication.isPlaying)
            {
                connectHint = "还没进入 Play 模式。";
                return;
            }

            try
            {
                // 容器挂在 GameBootstrap 物体上（DontDestroyOnLoad），播放期间一直找得到。
                GameLifetimeScope scope = UnityEngine.Object.FindObjectOfType<GameLifetimeScope>();
                if (scope == null)
                {
                    connectHint = "当前场景里没有 GameLifetimeScope——多半是没开主场景。";
                    return;
                }

                if (scope.Container == null)
                {
                    connectHint = "GameLifetimeScope 的容器还没建好，启动流程可能还在跑。";
                    return;
                }

                ReplayPlayer resolved;
                if (!scope.Container.TryResolve(out resolved) || resolved == null)
                {
                    connectHint = "容器里没有 ReplayPlayer，回放服务没接上。";
                    return;
                }

                player = resolved;
                connectHint = string.Empty;
                speedDraft = resolved.Speed;
            }
            catch (Exception e)
            {
                // 容器正在销毁（退出 Play 的那一下）时解析会抛，这属于正常时序，按「暂时连不上」处理。
                connectHint = $"取回放播放器时出错：{e.GetType().Name}：{e.Message}";
            }
        }

        private void OnPlayModeChanged(PlayModeStateChange change)
        {
            // 不管进还是出，缓存都作废：出 Play 时容器已销毁，进 Play 时还没建好。
            player = null;
            connectHint = string.Empty;
            nextResolveTime = 0d;
            Repaint();
        }

        /// <summary>回放文件的默认目录：<c>persistentDataPath/saves/Replays</c>，不存在就退回上一级。</summary>
        private static string DefaultReplayDirectory()
        {
            string root = Path.Combine(Application.persistentDataPath, SaveFolderName);
            string replays = Path.Combine(root, ReplayRecorder.ReplayFolderName);
            if (Directory.Exists(replays))
            {
                return replays;
            }

            return Directory.Exists(root) ? root : Application.persistentDataPath;
        }
    }
}
