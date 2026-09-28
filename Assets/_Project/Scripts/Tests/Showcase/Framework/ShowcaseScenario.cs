// ShowcaseScenario —— 所有模块回放场景的抽象基类：场景加载、Step/Check/Snapshot/Wait 四件套、
// 异常捕获、报告写盘与 TearDown 统一断言。模块作者只写「做什么 / 应该看到什么」，其余全在这里。
//
// 做什么：把「进 Play → 加载场景 → 按节奏驱动模块 → 逐步停顿让人看清 → 检查点判定 → 截图 → 出报告」
//         这条固定流程收敛成一个基类，每个模块的 Showcase 只剩一串 yield return Step/Check。
//         SetUp 临时放开输入焦点限制，TearDown / 退出 Play 恢复；Game 视图聚焦仅用于展示和截图。
//         本类按职责拆成四个 partial 文件：本文件 = 报告节奏引擎；ShowcaseScenario.BootFlow.cs = 从标题「开始」进世界 /
//         收尾退回标题并销毁根作用域；ShowcaseScenario.PlayerDrive.cs = 虚拟输入（Input）、走路（WalkTo / Walk）与补点按钮（ClickWhenReady）；
//         ShowcaseScenario.DemoScene.cs = SampleScene demo 内容的坐标常量与共用操作（路线、观察点、和巡逻怪交手）。
//
// 为什么新建（project-root.md「加能力的顺序」）：
//   复用 —— 回放引擎本身就是复用 Unity Test Framework（[UnityTest] + [UnitySetUp]/[UnityTearDown]），
//           这个文件只是 UTF 之上的一层薄壳；UTF 不提供停顿节奏、Overlay、截图编号与人可读报告。
//   扩展 —— 没有可扩展的已有测试基类（Scripts/Tests/ 此前是空的）；写进各模块的测试类里则每个模块复制一遍。

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using Game.Core.Platform;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests.Showcase
{
    /// <summary>
    /// 模块回放的基类。派生类只需给出 Module / ScenePath，再用 Step / Check / Snapshot / Wait 串出流程。
    /// 检查点失败一律只记录 + Debug.LogWarning，不中断也不 LogError（UTF 会把未预期的 LogError 判成
    /// 测试失败并当场打断，报告就写不出来了）；所有失败在 TearDown 里汇总成一次 Assert.Fail。
    /// </summary>
    public abstract partial class ShowcaseScenario
    {
        /// <summary>根作用域的程序集限定类型名，供 <see cref="ResolveService{T}"/> 反射查找（理由见该方法）。</summary>
        private const string ScopeTypeName = "Game.Core.Boot.GameLifetimeScope, Game.Core";

        private readonly List<UnityEngine.Object> tracked = new List<UnityEngine.Object>();

        // 本条用例「预期会出现」的错误日志判定（坏档回放故意读坏文件，框架层按约定打 Error）。命中的不计入异常。
        private readonly List<Func<string, bool>> expectedErrors = new List<Func<string, bool>>();
        private int expectedErrorHits;

        private ShowcaseReport report;
        private ShowcaseOverlay overlay;
        private float testStartTime;
        private int stepIndex;
        private bool capturing;
        private bool bootLoaded;

        /// <summary>本条用例专用的存档根目录覆盖（临时缓存目录下），SetUp 里建、TearDown 里删。</summary>
        private string showcaseSaveRoot;
#if UNITY_EDITOR
        // 回放期间锁住程序集重载（见 AcquireReloadLock）。静态计数 = 本类当前持有的锁数，防止嵌套 / 重复解锁；
        // 实例标记保证一条用例最多加一次、解一次。
        private static int reloadLockCount;
        private static bool exitPlayHookInstalled;
        private bool holdsReloadLock;
        private static InputSettings originalInputSettings;
        private static InputSettings showcaseInputSettings;
        private static bool originalRunInBackground;
#endif

        /// <summary>模块名，PascalCase（报告目录用它的小写形式）。</summary>
        protected abstract string Module { get; }

        /// <summary>
        /// 验证场景路径。非空则 SetUp 里加载；返回 null 表示场景在代码里搭（配合 EnsureCamera）。
        /// </summary>
        protected virtual string ScenePath
        {
            get { return null; }
        }

        /// <summary>要不要先加载常驻 Boot 场景。Boot 文件不存在时自动跳过，不算失败。</summary>
        protected virtual bool LoadBootScene
        {
            get { return true; }
        }

        private float Elapsed
        {
            get { return Time.realtimeSinceStartup - testStartTime; }
        }

        /// <summary>
        /// 等 Boot 场景就绪。默认只等一帧；框架的就绪信号（DI 容器装配完、存档读完等）落地后，
        /// 由框架层或模块作者覆写成等那个信号。
        /// </summary>
        protected virtual IEnumerator WaitForBootReady()
        {
            yield return null;
        }

        /// <summary>捕获从场景加载前就开始：场景加载与场景内对象 Awake 期间抛的异常也要被记进报告。</summary>
        [UnitySetUp]
        public IEnumerator ShowcaseSetUp()
        {
            string testName = TestContext.CurrentContext.Test.Name;

            // 必须在加载 Boot 场景之前设置：容器建出的平台服务第一次读 SaveRoot 就要拿到这个值，
            // 否则回放会把 slot1..N.json 写进玩家真实存档目录，几条用例跑下来就把真实存档槽写满
            // （ai-docs/pitfalls.md「从『开始』进场景的回放把玩家真实存档写满了」）。
            // 目录按模块 + 用例名区分，同一 Play 会话里连着跑同模块的多条用例也不会互相残留。
            showcaseSaveRoot = Path.Combine(
                Application.temporaryCachePath, "showcase-saves", Module.ToLowerInvariant() + "-" + testName);
            if (Directory.Exists(showcaseSaveRoot))
            {
                // 上一次回放没清理成功（编辑器中途崩了），先清空再用，别让残留文件影响这一次的判定。
                Directory.Delete(showcaseSaveRoot, true);
            }

            Directory.CreateDirectory(showcaseSaveRoot);
            PlatformServiceBase.SaveRootOverride = showcaseSaveRoot;

            AcquireReloadLock();
            ConfigureBackgroundInput();
            stepIndex = 0;
            bootLoaded = false;
            expectedErrors.Clear();
            expectedErrorHits = 0;
            testStartTime = Time.realtimeSinceStartup;

            report = ShowcaseReport.Open(Module);
            report.BeginTest(testName);
            Log($"开始回放「{testName}」，节奏 x{ShowcaseOptions.HoldScale.ToString("0.##", CultureInfo.InvariantCulture)}");

            BeginCapture();
            FocusGameView();
            yield return LoadScenes();

            overlay = ShowcaseOverlay.Create(Module);
        }

        [UnityTearDown]
        public IEnumerator ShowcaseTearDown()
        {
            // try/finally：写报告、销毁物体或 Assert.Fail 抛出时也要解锁，否则编辑器会一直不编译。
            try
            {
                // 先收虚拟输入与 Boot 流程（此时仍在捕获日志：退回标题途中报的错照样进报告），再走报告收尾。
                // 两者都幂等：子类自己的 [UnityTearDown] 先跑、已经销毁过根作用域时这里静默跳过。
                DisposeInput();
                if (LoadBootScene)
                {
                    yield return ShutdownBootFlow();
                }

                EndCapture();
                if (expectedErrors.Count > 0)
                {
                    Log($"预期内的错误日志 {expectedErrorHits} 条已按约定忽略");
                    expectedErrors.Clear();
                    LogAssert.ignoreFailingMessages = false;
                }

                int failures = report == null ? 0 : report.CurrentTestFailureCount;
                int exceptions = report == null ? 0 : report.CurrentTestExceptionCount;
                string reportPath = report == null ? "(未生成)" : report.Write();

                if (overlay != null)
                {
                    UnityEngine.Object.Destroy(overlay.gameObject);
                    overlay = null;
                }

                DestroyTracked();
                yield return null;

                Log($"回放结束：检查点失败 {failures} 个，异常 {exceptions} 条，报告 {reportPath}");
                if (failures > 0 || exceptions > 0)
                {
                    Assert.Fail($"{failures} 个检查点失败，{exceptions} 条异常；报告：{reportPath}");
                }
            }
            finally
            {
                // 收尾中途抛异常也要把虚拟设备摘掉，否则它会留到下一条用例甚至下一次 Play。
                DisposeInput();

                RestoreBackgroundInput();

                // 存档根目录覆盖必须无条件清掉：留着的话下一条用例（甚至下一次 Play）会继续读到这次的临时目录。
                PlatformServiceBase.SaveRootOverride = null;
                if (!string.IsNullOrEmpty(showcaseSaveRoot) && Directory.Exists(showcaseSaveRoot))
                {
                    try
                    {
                        Directory.Delete(showcaseSaveRoot, true);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"{ShowcaseOptions.Prefix}[{Module}] 清理临时存档目录失败："
                                         + $"{showcaseSaveRoot}，{e.GetType().Name}：{e.Message}");
                    }
                }

                ReleaseReloadLock();
            }
        }

        /// <summary>
        /// 回放期间锁住程序集重载。回放在 Play 模式里跑，别的会话此时保存 .cs 会触发「Play 中重编译 + 域重载」，
        /// UTF 的测试协程随域重载被丢掉，用例既不失败也不结束、进度永远卡住（2026-09-26 实测：Dialogue 回放卡 4 分钟）。
        /// 锁住后改动只排队，回放结束解锁时再编译。只在编辑器里生效。
        /// </summary>
        private void AcquireReloadLock()
        {
#if UNITY_EDITOR
            if (holdsReloadLock)
            {
                return;
            }

            holdsReloadLock = true;
            reloadLockCount++;
            UnityEditor.EditorApplication.LockReloadAssemblies();

            // 兜底：SetUp 中途抛异常、TearDown 没跑到时，退出 Play 模式把本类加的锁全部还掉。
            if (!exitPlayHookInstalled)
            {
                exitPlayHookInstalled = true;
                UnityEditor.EditorApplication.playModeStateChanged += ReleaseAllOnExitPlay;
            }
#endif
        }

        /// <summary>与 <see cref="AcquireReloadLock"/> 对称；本实例没加过锁、或计数已归零时什么都不做（防重复解锁）。</summary>
        private void ReleaseReloadLock()
        {
#if UNITY_EDITOR
            if (!holdsReloadLock)
            {
                return;
            }

            holdsReloadLock = false;
            if (reloadLockCount <= 0)
            {
                return;
            }

            reloadLockCount--;
            UnityEditor.EditorApplication.UnlockReloadAssemblies();
#endif
        }

        /// <summary>
        /// 显示 Game 视图以便观看与截图；输入已由 ConfigureBackgroundInput 独立于窗口焦点。
        /// </summary>
        private static void FocusGameView()
        {
#if UNITY_EDITOR
            if (Application.isBatchMode)
            {
                return;
            }

            try
            {
                if (!UnityEditor.EditorApplication.ExecuteMenuItem("Window/General/Game"))
                {
                    Debug.LogWarning($"{ShowcaseOptions.Prefix} 切 Game 视图焦点失败：菜单 Window/General/Game 不存在");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{ShowcaseOptions.Prefix} 切 Game 视图焦点时抛出 {e.GetType().Name}：{e.Message}");
            }
#endif
        }

        // 用临时副本覆盖，避免将测试设置写回项目资产；退出 Play 的既有回调也负责兜底恢复。
        private static void ConfigureBackgroundInput()
        {
#if UNITY_EDITOR
            if (showcaseInputSettings != null) return;
            originalInputSettings = InputSystem.settings;
            originalRunInBackground = Application.runInBackground;
            showcaseInputSettings = UnityEngine.Object.Instantiate(originalInputSettings);
            showcaseInputSettings.hideFlags = HideFlags.HideAndDontSave;
            showcaseInputSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            showcaseInputSettings.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            Application.runInBackground = true;
            InputSystem.settings = showcaseInputSettings;
#endif
        }

        private static void RestoreBackgroundInput()
        {
#if UNITY_EDITOR
            if (showcaseInputSettings == null) return;
            InputSystem.settings = originalInputSettings;
            Application.runInBackground = originalRunInBackground;
            UnityEngine.Object.DestroyImmediate(showcaseInputSettings);
            showcaseInputSettings = null;
            originalInputSettings = null;
#endif
        }

#if UNITY_EDITOR
        private static void ReleaseAllOnExitPlay(UnityEditor.PlayModeStateChange change)
        {
            if (change != UnityEditor.PlayModeStateChange.ExitingPlayMode)
            {
                return;
            }

            RestoreBackgroundInput();
            while (reloadLockCount > 0)
            {
                reloadLockCount--;
                UnityEditor.EditorApplication.UnlockReloadAssemblies();
            }
        }
#endif

        /// <summary>
        /// 声明本条用例会出现、且属于被测行为本身的错误日志（例如故意读坏档时存档服务打的 Error）。
        /// 命中 <paramref name="match"/> 的 Error 不计入报告异常；其余 Error 照常计入并在 TearDown 判失败。
        /// 同时打开 <c>LogAssert.ignoreFailingMessages</c>，否则 UTF 会在第一条 Error 处当场打断用例（TearDown 里恢复）。
        /// </summary>
        protected void ExpectErrorLogs(string reason, Func<string, bool> match)
        {
            if (match == null)
            {
                return;
            }

            expectedErrors.Add(match);
            LogAssert.ignoreFailingMessages = true;
            Log($"本条用例预期会出现错误日志：{reason}（命中的不计入异常）");
        }

        /// <summary>
        /// 走一步：记进报告、更新 Overlay、打日志、执行 act，然后停顿让开发者看清这一步的表现。
        /// act 抛异常只把这一步记成失败，不中断后面的步骤——一步炸了后面的表现往往还有诊断价值。
        /// </summary>
        /// <param name="title">这一步「做了什么」，中文，能念给策划听。</param>
        /// <param name="act">要执行的动作，可为 null（只想停一下看画面时）。</param>
        /// <param name="hold">停顿秒数；负数表示用 ShowcaseOptions.DefaultHold。实际停顿还要乘节奏倍率。</param>
        protected IEnumerator Step(string title, Action act = null, float hold = -1f)
        {
            stepIndex++;
            string content = $"第{stepIndex}步 · {title}";
            ShowcaseReport.EntryResult result = ShowcaseReport.EntryResult.None;

            Debug.Log($"{ShowcaseOptions.Prefix}[{Module}] ▶ 第{stepIndex}步 {title}");
            if (overlay != null)
            {
                overlay.Show(stepIndex, title);
            }

            if (act != null)
            {
                try
                {
                    act();
                }
                catch (Exception e)
                {
                    result = ShowcaseReport.EntryResult.Fail;
                    content = $"第{stepIndex}步 · {title} —— 执行时抛出 {e.GetType().Name}：{e.Message}";
                    Debug.LogWarning($"{ShowcaseOptions.Prefix}[{Module}] ✗ 第{stepIndex}步 {title} 执行时抛异常：{e}");
                }
            }

            if (report != null)
            {
                report.Add(ShowcaseReport.EntryKind.Step, content, result, Elapsed);
            }

            float seconds = (hold < 0f ? ShowcaseOptions.DefaultHold : hold) * ShowcaseOptions.HoldScale;
            yield return new WaitForSecondsRealtime(seconds);
        }

        /// <summary>
        /// 检查点：期望「应该看到什么」。timeout 为 0 时只判一次，大于 0 则逐帧轮询到超时。
        /// 失败只记录 + LogWarning + 多停一会儿让人看清红字，不 Assert（理由见类注释）。
        /// </summary>
        protected IEnumerator Check(string expect, Func<bool> cond, float timeout = 0f)
        {
            bool ok = Evaluate(cond, expect);
            if (!ok && timeout > 0f)
            {
                float deadline = Time.realtimeSinceStartup + timeout;
                while (!ok && Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                    ok = Evaluate(cond, expect);
                }
            }

            yield return Judge(ShowcaseReport.EntryKind.Check, expect, expect, ok);
        }

        /// <summary>
        /// 截一张图落到本次 run 目录，文件名 NN-名字.png（NN 为整个 run 内连续的两位序号）。
        /// 批处理没有图形设备，截不出东西，直接跳过并在报告里写明。
        /// </summary>
        protected IEnumerator Snapshot(string name)
        {
            if (Application.isBatchMode)
            {
                if (report != null)
                {
                    report.AddSnapshot($"{name} —— 已跳过（批处理无图形）", null, Elapsed);
                }

                Log($"截图「{name}」已跳过（批处理无图形）");
                yield break;
            }

            int index = report == null ? 1 : report.NextSnapshotIndex();
            string fileName = $"{index.ToString("00", CultureInfo.InvariantCulture)}-{name}.png";
            string directory = report == null ? ShowcaseOptions.ReportRoot : report.RunDirectory;

            // 必须等到本帧渲染完再截，否则抓到的是上一帧、甚至是这一步动作生效之前的画面。
            yield return new WaitForEndOfFrame();

            try
            {
                Directory.CreateDirectory(directory);
                ScreenCapture.CaptureScreenshot(Path.Combine(directory, fileName));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{ShowcaseOptions.Prefix}[{Module}] 截图「{name}」失败："
                                 + $"{e.GetType().Name}：{e.Message}");
                if (report != null)
                {
                    report.AddSnapshot($"{name} —— 截图失败：{e.Message}", null, Elapsed);
                }

                yield break;
            }

            // CaptureScreenshot 是异步落盘的，这里多等两帧，免得 TearDown 写完报告图还没出来。
            yield return null;
            yield return null;

            if (report != null)
            {
                report.AddSnapshot(name, fileName, Elapsed);
            }

            Log($"截图 {fileName}");
        }

        /// <summary>单纯等一会儿（等动画播完、等特效散掉）。实际时长按节奏倍率缩放。</summary>
        protected IEnumerator Wait(float seconds)
        {
            if (report != null)
            {
                report.Add(
                    ShowcaseReport.EntryKind.Wait,
                    $"等待 {seconds.ToString("0.##", CultureInfo.InvariantCulture)} 秒",
                    ShowcaseReport.EntryResult.None,
                    Elapsed);
            }

            yield return new WaitForSecondsRealtime(seconds * ShowcaseOptions.HoldScale);
        }

        /// <summary>
        /// 等到某个条件成立，最多等 timeout 秒。超时按检查点失败处理（记录 + LogWarning），继续往下走。
        /// 注意 timeout 不乘节奏倍率：它是「这件事最多该花多久」的业务上限，不是给人看的停顿。
        /// </summary>
        protected IEnumerator WaitUntil(string what, Func<bool> cond, float timeout)
        {
            float deadline = Time.realtimeSinceStartup + timeout;
            bool ok = Evaluate(cond, what);
            while (!ok && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                ok = Evaluate(cond, what);
            }

            string content = ok
                ? $"等到「{what}」"
                : $"等「{what}」超时（{timeout.ToString("0.##", CultureInfo.InvariantCulture)} 秒）";
            yield return Judge(ShowcaseReport.EntryKind.Wait, content, what, ok);
        }

        /// <summary>
        /// 按名字取场上对象的组件。找不到属于前置条件不成立，直接 Assert.Fail 中断——
        /// 场景都不对，后面的步骤全是噪音。
        /// </summary>
        protected T FindRequired<T>(string name) where T : Component
        {
            GameObject found = GameObject.Find(name);
            if (found == null)
            {
                Assert.Fail($"场上找不到名为「{name}」的对象。检查验证场景 {ScenePath ?? "(代码搭建)"} "
                            + "里对象名是否一致，以及它是否处于激活状态。");
                return null;
            }

            T component = found.GetComponent<T>();
            if (component == null)
            {
                Assert.Fail($"对象「{name}」上没有 {typeof(T).Name} 组件。");
                return null;
            }

            return component;
        }

        /// <summary>
        /// 从运行中的根容器解析一个服务，拿不到（没 Boot、容器没建完、没注册）一律返回 null。
        /// <para>**只能走反射**：本程序集没引用 VContainer，而 <c>GameLifetimeScope</c> 的基类在那个程序集里，
        /// 源码里写出这个类型名就是 CS0012。写法照搬 <c>ReplayShowcase.TryConnectContainer / Resolve&lt;T&gt;</c>：
        /// 按类型名找场上的 <c>GameLifetimeScope</c> → 取 <c>Container</c> 属性 →
        /// 反射调 <c>IObjectResolver.TryResolve(Type, out object, object)</c>（<c>TryResolve&lt;T&gt;</c> 的泛型外壳底下就是它；
        /// 别找 <c>Resolve(Type)</c>，它带可选 key 参数，按单参数签名找不到）。</para>
        /// <para>不缓存容器：同一 Play 会话里每条用例都会重新加载 Boot，缓存会拿到上一条的旧容器。
        /// 异常吞掉不打日志——常在 WaitForBootReady 里逐帧轮询，打日志会刷爆控制台。</para>
        /// </summary>
        protected T ResolveService<T>() where T : class
        {
            Type scopeType = Type.GetType(ScopeTypeName);
            if (scopeType == null)
            {
                return null;
            }

            UnityEngine.Object scopeObject = UnityEngine.Object.FindObjectOfType(scopeType);
            if (scopeObject == null)
            {
                return null;
            }

            try
            {
                PropertyInfo containerProperty = scopeType.GetProperty("Container");
                object resolver = containerProperty == null ? null : containerProperty.GetValue(scopeObject);
                if (resolver == null)
                {
                    return null;
                }

                MethodInfo method = resolver.GetType().GetMethod(
                    "TryResolve",
                    new[] { typeof(Type), typeof(object).MakeByRefType(), typeof(object) });
                if (method == null)
                {
                    return null;
                }

                object[] args = { typeof(T), null, null };
                bool resolved = (bool)method.Invoke(resolver, args);
                return resolved ? args[1] as T : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// 保证场上有主相机，没有就建一个正交相机。给「代码搭场景」的作者用：
        /// 没相机的话 Game 视图一片漆黑，截图也只有黑底，看不出任何表现。
        /// 相机放在 z=-10（与 2D 模板的 Main Camera 一致），否则 z=0 的 2D 对象落在近裁剪面之内画不出来。
        /// </summary>
        protected Camera EnsureCamera()
        {
            Camera existing = Camera.main;
            if (existing != null)
            {
                return existing;
            }

            GameObject host = new GameObject("ShowcaseCamera");
            host.transform.position = new Vector3(0f, 0f, -10f);
            host.tag = "MainCamera";
            Track(host);

            Camera camera = host.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 5f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.16f, 0.16f, 0.18f);
            Log("场上没有主相机，已临时建一个正交相机（ShowcaseCamera）");
            return camera;
        }

        /// <summary>
        /// 登记一个「回放期间造出来的」对象，TearDown 里统一销毁。
        /// 同一个 Play 会话会连着跑同类里的多条用例，不清干净上一条的残留会污染下一条
        /// （unity-tests.md：PlayMode 测试创建的对象在 TearDown 清理）。
        /// </summary>
        protected T Track<T>(T target) where T : UnityEngine.Object
        {
            if (target != null)
            {
                tracked.Add(target);
            }

            return target;
        }

        /// <summary>检查点 / 等待的统一判定收尾：记报告、刷 Overlay、打日志，失败就多停一会儿。</summary>
        private IEnumerator Judge(ShowcaseReport.EntryKind kind, string content, string overlayText, bool ok)
        {
            if (report != null)
            {
                report.Add(
                    kind,
                    content,
                    ok ? ShowcaseReport.EntryResult.Pass : ShowcaseReport.EntryResult.Fail,
                    Elapsed);
            }

            if (overlay != null)
            {
                overlay.Result(ok, overlayText);
            }

            if (ok)
            {
                Debug.Log($"{ShowcaseOptions.Prefix}[{Module}] ✓ {content}");
                yield break;
            }

            // 只能 LogWarning：LogError 会被 UTF 当成未预期错误、把用例当场判失败，TearDown 的报告就写不成了。
            Debug.LogWarning($"{ShowcaseOptions.Prefix}[{Module}] ✗ {content}");
            yield return new WaitForSecondsRealtime(ShowcaseOptions.FailHold * ShowcaseOptions.HoldScale);
        }

        /// <summary>
        /// 求值检查条件。条件本身抛异常（引用了已销毁的对象等）按「不通过」算，不让它掀翻整条回放。
        /// </summary>
        private bool Evaluate(Func<bool> cond, string what)
        {
            if (cond == null)
            {
                Debug.LogWarning($"{ShowcaseOptions.Prefix}[{Module}] 检查「{what}」没给判定条件，按不通过算。");
                return false;
            }

            try
            {
                return cond();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{ShowcaseOptions.Prefix}[{Module}] 检查「{what}」求值时抛出 "
                                 + $"{e.GetType().Name}：{e.Message}");
                return false;
            }
        }

        private IEnumerator LoadScenes()
        {
            if (LoadBootScene)
            {
                string bootFull = Path.Combine(ShowcaseOptions.ProjectRoot, ShowcaseOptions.BootScenePath);
                if (File.Exists(bootFull))
                {
                    yield return LoadSceneAndWait(ShowcaseOptions.BootScenePath, LoadSceneMode.Single);
                    bootLoaded = true;
                    yield return WaitForBootReady();
                }
                else
                {
                    Log($"没有常驻场景 {ShowcaseOptions.BootScenePath}，跳过 Boot 加载（框架落地前这是正常的）");
                }
            }

            string scenePath = ScenePath;
            if (string.IsNullOrEmpty(scenePath))
            {
                yield break;
            }

            string sceneFull = Path.Combine(ShowcaseOptions.ProjectRoot, scenePath);
            if (!File.Exists(sceneFull))
            {
                Assert.Fail($"验证场景不存在：{scenePath}。用 Unity MCP 在编辑器里把它建出来，"
                            + "或者把 ScenePath 改成 null 在代码里搭场景（配合 EnsureCamera）。");
                yield break;
            }

            // Boot 已经占了 Single，模块场景只能 Additive 叠上去，再把它设为 active，
            // 这样后续 Instantiate / new GameObject 都落在模块场景里，卸载时一起干净。
            yield return LoadSceneAndWait(scenePath, bootLoaded ? LoadSceneMode.Additive : LoadSceneMode.Single);
        }

        /// <summary>
        /// 加载一个不在 Build Settings 里的场景并等它就绪。
        /// 只有 EditorSceneManager.LoadSceneInPlayMode 能做到这点，所以回放天生是编辑器里的事。
        /// </summary>
        private IEnumerator LoadSceneAndWait(string path, LoadSceneMode mode)
        {
#if UNITY_EDITOR
            Scene scene = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(
                path,
                new LoadSceneParameters(mode));

            while (!scene.isLoaded)
            {
                yield return null;
            }

            if (mode == LoadSceneMode.Additive)
            {
                SceneManager.SetActiveScene(scene);
            }

            Log($"已加载场景 {path}（{mode}）");
#else
            Assert.Inconclusive("Showcase 只在编辑器里跑");
            yield break;
#endif
        }

        private void BeginCapture()
        {
            if (capturing)
            {
                return;
            }

            Application.logMessageReceived += OnLogMessage;
            capturing = true;
        }

        private void EndCapture()
        {
            if (!capturing)
            {
                return;
            }

            Application.logMessageReceived -= OnLogMessage;
            capturing = false;
        }

        /// <summary>
        /// 只收 Error / Exception / Assert，并且滤掉带 [VERIFY] 前缀的自家日志——
        /// 检查点失败的 LogWarning 本来就记在报告里了，再当「运行时异常」算一遍会重复计数。
        /// </summary>
        private void OnLogMessage(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)
            {
                return;
            }

            if (!string.IsNullOrEmpty(condition) && condition.Contains(ShowcaseOptions.Prefix))
            {
                return;
            }

            if (IsExpectedError(condition))
            {
                expectedErrorHits++;
                return;
            }

            if (report != null)
            {
                report.AddException(condition, stackTrace);
            }
        }

        /// <summary>日志回调里调用：不打日志（会递归进回调），判定抛异常按「不是预期错误」算。</summary>
        private bool IsExpectedError(string condition)
        {
            for (int i = 0; i < expectedErrors.Count; i++)
            {
                try
                {
                    if (expectedErrors[i](condition ?? string.Empty))
                    {
                        return true;
                    }
                }
                catch (Exception)
                {
                    // 见方法注释。
                }
            }

            return false;
        }

        private void DestroyTracked()
        {
            for (int i = 0; i < tracked.Count; i++)
            {
                if (tracked[i] != null)
                {
                    UnityEngine.Object.Destroy(tracked[i]);
                }
            }

            tracked.Clear();
        }

        private void Log(string message)
        {
            Debug.Log($"{ShowcaseOptions.Prefix}[{Module}] {message}");
        }

        /// <summary>
        /// 每次进入 Play 时清掉会话级静态状态。关掉「Reload Domain」后静态字段能跨 Play 存活，
        /// 不清的话第二轮回放会把报告追加进上一轮的 run 目录。SubsystemRegistration 无论域重载开关都会走。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetShowcaseSession()
        {
            ShowcaseReport.ResetSession();
        }
    }
}
