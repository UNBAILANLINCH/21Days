// BuildScript —— 21Days 的批处理打包入口，Windows 与 Android 共用同一套内容与同一条流程。
//
// 为什么放在 Assets/_Project/Scripts/Editor/：
//   这里的代码编进 Game.Editor 程序集，只在编辑器里编译，不进包体；
//   BuildPipeline / EditorUserBuildSettings / PlayerSettings 全部来自 UnityEditor 命名空间，
//   放进 Scripts/Runtime/ 会让运行时程序集反向依赖编辑器 API，打包当场失败。
//
// 两个公开入口，各对应一个平台：
//   BuildWindows —— BuildTarget.StandaloneWindows64，默认产物 Builds/Windows/21Days.exe
//   BuildAndroid —— BuildTarget.Android，只出 APK（不出 AAB），默认产物 Builds/Android/21Days.apk
//
// 三种调用方式都落到这两个入口上，行为一致：
//   1. 本机     scripts/build.ps1 -Target Windows|Android
//   2. CI       Unity -batchmode -quit -executeMethod Game.Editor.BuildScript.BuildWindows
//   3. 编辑器   菜单 21Days/打包/...（只打日志，不退出编辑器）
//
// 批处理模式下可透传的命令行参数（scripts/build.ps1 负责拼装）：
//   -outputPath <路径>    产物路径，相对工程根；不给就用上面的默认值
//   -buildVersion <字符串> 写入 PlayerSettings.bundleVersion
//   -buildNumber <整数>   Android 的 bundleVersionCode；不给而给了 -buildVersion 时改为自增
//   -development          打开 BuildOptions.Development
//
// 新建理由（project-root.md「加能力的顺序」）：工程此前没有任何编辑器脚本，
// 既没有可复用的现成工具，也没有职责相符、能塞进去的已有文件，只能新建。

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Game.Editor
{
    /// <summary>
    /// 批处理打包流程：取场景 → 读命令行参数 → 平台专属检查 → 切平台 → 出包 → 汇报。
    /// </summary>
    public static class BuildScript
    {
        /// <summary>所有日志统一前缀，方便从 Unity 的长日志里 grep 出打包相关的行。</summary>
        private const string LogPrefix = "[Build]";

        private const string DefaultWindowsOutput = "Builds/Windows/21Days.exe";
        private const string DefaultAndroidOutput = "Builds/Android/21Days.apk";

        /// <summary>失败时最多回显几条错误，多了在 CI 日志里刷屏，真正的根因还得看完整日志。</summary>
        private const int MaxReportedErrors = 5;

        /// <summary>Unity 模板的占位包名，原样上架会被 Unity 自己拦下来，提前报更清楚。</summary>
        private const string PlaceholderIdentifier = "com.Company.ProductName";

        [MenuItem("21Days/打包/Windows 64 位")]
        public static void BuildWindows()
        {
            Build(BuildTarget.StandaloneWindows64, BuildTargetGroup.Standalone, DefaultWindowsOutput);
        }

        [MenuItem("21Days/打包/Android APK")]
        public static void BuildAndroid()
        {
            Build(BuildTarget.Android, BuildTargetGroup.Android, DefaultAndroidOutput);
        }

        /// <summary>
        /// 真正的打包流程。两个平台只在「默认产物路径」和「平台专属设置」上分叉，其余完全共用。
        /// </summary>
        /// <param name="target">目标平台。</param>
        /// <param name="group">目标平台所属的平台组，切平台与 BuildPlayerOptions 都要用。</param>
        /// <param name="defaultOutput">没给 -outputPath 时的默认产物路径，相对工程根。</param>
        private static void Build(BuildTarget target, BuildTargetGroup group, string defaultOutput)
        {
            // 场景清单只认 Build Settings 里勾上的，避免脚本里再维护一份会和编辑器对不上的列表。
            string[] scenes = GetEnabledScenes();
            if (scenes.Length == 0)
            {
                Fail("Build Settings 里没有任何启用的场景。打开 File > Build Settings，"
                     + "把要进包的场景拖进 Scenes In Build 并勾选，再重新打包。");
                return;
            }

            Log($"目标平台 {target}，进包场景 {scenes.Length} 个：{string.Join(", ", scenes)}");

            string outputPath = GetArg("-outputPath");
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                outputPath = defaultOutput;
            }

            string fullOutputPath = ToAbsoluteProjectPath(outputPath);
            if (string.IsNullOrEmpty(fullOutputPath))
            {
                Fail($"产物路径解析失败：{outputPath}");
                return;
            }

            string version = GetArg("-buildVersion");
            if (!string.IsNullOrWhiteSpace(version))
            {
                PlayerSettings.bundleVersion = version;
                Log($"版本号（bundleVersion）设为 {version}");
            }

            // 平台专属设置放在切平台之前：这些字段本身就是分平台存的，不依赖当前激活平台，
            // 而切平台可能要重新导入全部资产、动辄几分钟，能提前失败就别等到那之后。
            if (target == BuildTarget.Android && !ConfigureAndroid(version))
            {
                return;
            }

            if (!SwitchPlatformIfNeeded(target, group))
            {
                return;
            }

            string outputDirectory = Path.GetDirectoryName(fullOutputPath);
            if (!string.IsNullOrEmpty(outputDirectory) && !Directory.Exists(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
                Log($"已创建输出目录 {outputDirectory}");
            }

            BuildOptions options = BuildOptions.None;
            if (HasFlag("-development"))
            {
                options |= BuildOptions.Development;
                Log("开发版构建：带 Development 标记（可连 Profiler、允许调试，体积更大，别用来发版）");
            }

            BuildPlayerOptions playerOptions = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = fullOutputPath,
                target = target,
                targetGroup = group,
                options = options,
            };

            Log($"开始打包，产物 {fullOutputPath}");
            BuildReport report = BuildPipeline.BuildPlayer(playerOptions);
            ReportResult(report);
        }

        /// <summary>取 Build Settings 里勾选启用的场景路径。</summary>
        private static string[] GetEnabledScenes()
        {
            List<string> scenes = new List<string>();
            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
            {
                if (scene.enabled)
                {
                    scenes.Add(scene.path);
                }
            }

            return scenes.ToArray();
        }

        /// <summary>
        /// Android 专属设置与前置检查。返回 false 表示已经报过错、流程要中止。
        /// </summary>
        /// <param name="version">本次 -buildVersion 的值，没给就是 null。</param>
        private static bool ConfigureAndroid(string version)
        {
            // 先出 APK：本地装机、发测试包都直接用它；上 Google Play 才需要 AAB，
            // 那时再加一个 -appBundle 之类的参数打开这里，顺便把脚本后端切 IL2CPP、
            // 架构勾 ARM64（Play 商店的硬要求）。现在保持工程默认，不在这里偷偷改工程设置。
            EditorUserBuildSettings.buildAppBundle = false;

            // 包名分平台存，用 NamedBuildTarget 显式读 Android 的那一份，
            // 不用 PlayerSettings.applicationIdentifier —— 那个只认当前激活平台，切平台前后读到的可能不是一回事。
            string identifier = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
            if (string.IsNullOrWhiteSpace(identifier))
            {
                Fail("Android 包名（applicationIdentifier）是空的。打开 Edit > Project Settings > Player > "
                     + "Android > Other Settings > Identification，把 Package Name 填成 com.<公司>.<产品> 的形式。");
                return false;
            }

            if (identifier.Contains(" "))
            {
                Fail($"Android 包名含空格：\"{identifier}\"。包名只允许字母、数字、下划线和点，"
                     + "去 Project Settings > Player > Android > Identification 改掉。");
                return false;
            }

            if (string.Equals(identifier, PlaceholderIdentifier, StringComparison.Ordinal))
            {
                Fail($"Android 包名还是 Unity 的占位值 {PlaceholderIdentifier}，Unity 自己也会拒绝打包。"
                     + "去 Project Settings > Player > Android > Identification 换成真实包名。");
                return false;
            }

            Log($"Android 包名 {identifier}，输出格式 APK");

            string buildNumber = GetArg("-buildNumber");
            if (!string.IsNullOrWhiteSpace(buildNumber))
            {
                int versionCode;
                if (!int.TryParse(buildNumber, out versionCode))
                {
                    Fail($"-buildNumber 要求整数，收到的是 \"{buildNumber}\"。");
                    return false;
                }

                PlayerSettings.Android.bundleVersionCode = versionCode;
                Log($"bundleVersionCode 按 -buildNumber 设为 {versionCode}");
            }
            else if (!string.IsNullOrWhiteSpace(version))
            {
                // 发版才会带 -buildVersion。Android 要求每次上传的 versionCode 严格递增，
                // 没显式指定就在原值上 +1，省得手改工程设置。日常本地打包不带版本号，这里不动。
                PlayerSettings.Android.bundleVersionCode += 1;
                Log($"bundleVersionCode 自增为 {PlayerSettings.Android.bundleVersionCode}");
            }

            return true;
        }

        /// <summary>
        /// 当前激活平台不是目标平台时切过去。返回 false 表示切换失败、已经报过错。
        /// </summary>
        private static bool SwitchPlatformIfNeeded(BuildTarget target, BuildTargetGroup group)
        {
            if (EditorUserBuildSettings.activeBuildTarget == target)
            {
                return true;
            }

            Log($"当前激活平台是 {EditorUserBuildSettings.activeBuildTarget}，先切到 {target}"
                + "（首次切换要按新平台重新导入全部资产，慢是正常的）");

            if (EditorUserBuildSettings.SwitchActiveBuildTarget(group, target))
            {
                return true;
            }

            Fail($"切换到 {target} 失败。多半是这个平台的构建模块没装："
                 + "Unity Hub > 安装 > 对应版本 > 添加模块，勾上对应平台后重试。");
            return false;
        }

        /// <summary>把 BuildReport 翻译成人能看的结论，并在批处理模式下决定退出码。</summary>
        private static void ReportResult(BuildReport report)
        {
            if (report == null)
            {
                Fail("BuildPipeline.BuildPlayer 没有返回报告，打包未正常执行。");
                return;
            }

            BuildSummary summary = report.summary;
            if (summary.result == BuildResult.Succeeded)
            {
                double sizeMb = summary.totalSize / 1024d / 1024d;
                Log($"打包成功：{summary.outputPath}");
                Log($"体积 {sizeMb:F1} MB，耗时 {summary.totalTime.TotalSeconds:F1} 秒，"
                    + $"警告 {summary.totalWarnings} 条");
                Quit(0);
                return;
            }

            Debug.LogError($"{LogPrefix} 打包失败：结果 {summary.result}，错误 {summary.totalErrors} 条，"
                           + $"耗时 {summary.totalTime.TotalSeconds:F1} 秒");
            LogFirstErrors(report);
            Quit(1);
        }

        /// <summary>回显构建报告里的前几条错误，剩下的去完整日志里翻。</summary>
        private static void LogFirstErrors(BuildReport report)
        {
            int shown = 0;
            foreach (BuildStep step in report.steps)
            {
                foreach (BuildStepMessage message in step.messages)
                {
                    if (message.type != LogType.Error && message.type != LogType.Exception)
                    {
                        continue;
                    }

                    Debug.LogError($"{LogPrefix}   [{step.name}] {message.content}");
                    shown++;
                    if (shown >= MaxReportedErrors)
                    {
                        Debug.LogError($"{LogPrefix}   错误过多，只显示前 {MaxReportedErrors} 条，其余见完整日志。");
                        return;
                    }
                }
            }

            if (shown == 0)
            {
                Debug.LogError($"{LogPrefix}   构建报告里没有带错误级别的消息，直接看完整日志定位。");
            }
        }

        /// <summary>
        /// 从命令行取 <paramref name="name"/> 后面紧跟的值；没有这个参数或它后面没跟值就返回 null。
        /// Unity 会把自己不认识的参数原样留在 GetCommandLineArgs 里，自定义参数就是这么传进来的。
        /// </summary>
        private static string GetArg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (!string.Equals(args[i], name, StringComparison.Ordinal))
                {
                    continue;
                }

                if (i + 1 < args.Length)
                {
                    return args[i + 1];
                }

                Debug.LogWarning($"{LogPrefix} 命令行参数 {name} 后面没跟值，按没给处理。");
                return null;
            }

            return null;
        }

        /// <summary>命令行里有没有这个开关（不带值的参数，如 -development）。</summary>
        private static bool HasFlag(string name)
        {
            return Array.IndexOf(Environment.GetCommandLineArgs(), name) >= 0;
        }

        /// <summary>
        /// 把相对工程根的路径转成绝对路径；本来就是绝对路径的原样返回。
        /// 工程根由 Application.dataPath（即 &lt;工程根&gt;/Assets）往上一级推出来，不写死任何本机路径。
        /// </summary>
        private static string ToAbsoluteProjectPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            if (Path.IsPathRooted(path))
            {
                return Path.GetFullPath(path);
            }

            DirectoryInfo projectRoot = Directory.GetParent(Application.dataPath);
            if (projectRoot == null)
            {
                return null;
            }

            return Path.GetFullPath(Path.Combine(projectRoot.FullName, path));
        }

        /// <summary>
        /// 批处理模式下用退出码告诉调用方成败；编辑器里从菜单调用时什么都不做 —— 不能把用户的编辑器关掉。
        /// </summary>
        private static void Quit(int exitCode)
        {
            if (!Application.isBatchMode)
            {
                return;
            }

            EditorApplication.Exit(exitCode);
        }

        /// <summary>报错并结束：批处理模式退出码 1，编辑器里只打日志，由调用处 return 收尾。</summary>
        private static void Fail(string message)
        {
            Debug.LogError($"{LogPrefix} {message}");
            Quit(1);
        }

        private static void Log(string message)
        {
            Debug.Log($"{LogPrefix} {message}");
        }
    }
}
