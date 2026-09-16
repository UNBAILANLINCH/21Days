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
//   -releaseBuild         强制 Android 走 IL2CPP + 仅 ARM64，出完包立刻把设置改回原样。
//                         工程默认已是这套配置（2026-09-16 起），本开关只防「默认被人改回 32 位」
//
// 新建理由（project-root.md「加能力的顺序」）：工程此前没有任何编辑器脚本，
// 既没有可复用的现成工具，也没有职责相符、能塞进去的已有文件，只能新建。

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
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

            // -releaseBuild 改的是 Android 的脚本后端与 CPU 架构，对别的平台无意义。
            bool releaseBuild = HasFlag("-releaseBuild");
            if (releaseBuild && target != BuildTarget.Android)
            {
                Log($"-releaseBuild 只对 Android 有意义（它切的是 Android 的脚本后端与 CPU 架构），{target} 忽略此开关。");
                releaseBuild = false;
            }

            // Addressables 内容必须在 BuildPlayer 之前构建：包体里的资源目录是这一步产出的，
            // 跳过它出来的包能启动但所有 LoadAsync 都拿不到东西（真机上是静默失败，最难查的一类）。
            if (!BuildAddressableContent())
            {
                return;
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

            // 临时改工程设置前，把原值和 ProjectSettings.asset 的原始字节一起存下来。
            // 为什么连字节都要存：「语义恢复」不等于「文本恢复」。scriptingBackend 原本是空字典 {}，
            // 用 SetScriptingBackend 写回 Mono2x 会留下一条显式的 Android: 0 —— 语义一模一样，
            // 但文件多了一行，工作区就凭空多出一条谁也不想要的 diff。
            ScriptingImplementation originalBackend = default(ScriptingImplementation);
            AndroidArchitecture originalArchitectures = default(AndroidArchitecture);
            byte[] originalProjectSettings = null;
            bool settingsTouched = false;

            BuildReport report = null;
            try
            {
                if (releaseBuild)
                {
                    originalBackend = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android);
                    originalArchitectures = PlayerSettings.Android.targetArchitectures;
                    originalProjectSettings = ReadProjectSettingsBytes();

                    // 先立旗再改：哪怕下面两句只成功了一半、或中间抛了异常，finally 也会照原值写回一遍。
                    settingsTouched = true;

                    PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);

                    // 只勾 ARM64，不勾「ARMv7 + ARM64」双架构：IL2CPP 的原生产物（libil2cpp.so / libunity.so）
                    // 按架构各打一份进 APK，双架构体积接近翻倍；何况骁龙 8 Gen 3 那一代之后的手机
                    // 大核去掉了 AArch32，32 位包在那些机器上根本装不上。
                    // 真要兼容 32 位老设备：改 ProjectSettings 里的默认值，不是改这一行 ——
                    // 这里只在带了 -releaseBuild 时生效，且出完包立刻恢复，改了也留不住。
                    PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
                }

                if (target == BuildTarget.Android)
                {
                    LogAndroidConfiguration(releaseBuild);
                }

                report = BuildPipeline.BuildPlayer(playerOptions);
            }
            finally
            {
                if (settingsTouched)
                {
                    RestoreAndroidSettings(originalBackend, originalArchitectures, originalProjectSettings);
                }
            }

            // ReportResult 必须留在 try/finally 之外：它内部的 Quit 在批处理模式下调 EditorApplication.Exit，
            // 那是直接终止进程、不展开调用栈的，写进 try 里 finally 永远跑不到，
            // IL2CPP + ARM64 就会永久留在工作区 —— 这是本功能最容易翻车的地方。
            ReportResult(report);
        }

        /// <summary>
        /// 把本次真正生效的 Android 配置打进日志，供 build.ps1 回显，也方便事后从日志确认出的是哪种包。
        /// 读设置的当前值而不是照着开关硬写，免得日志和实际出的包对不上。
        /// </summary>
        /// <param name="releaseBuild">本次是否带了 -releaseBuild。</param>
        private static void LogAndroidConfiguration(bool releaseBuild)
        {
            ScriptingImplementation backend = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android);
            AndroidArchitecture architectures = PlayerSettings.Android.targetArchitectures;
            bool has64Bit = (architectures & AndroidArchitecture.ARM64) != 0;

            string profile = releaseBuild ? "Release（强制）" : "默认";
            string verdict = has64Bit
                ? "能装纯 64 位手机；导出仍是 APK 不是 AAB，要上架另说"
                : "32 位包：骁龙 8 Gen 3 那一代之后的纯 64 位手机装不上";

            Log($"配置：{profile}（{backend} + {architectures}，{verdict}）");
        }

        /// <summary>
        /// 把 -releaseBuild 临时改掉的 Android 设置写回原样，并保证 ProjectSettings.asset 在磁盘上
        /// 与构建前逐字节一致。构建成功、失败、抛异常都会走到这里。
        /// </summary>
        /// <param name="backend">构建前的脚本后端。</param>
        /// <param name="architectures">构建前的 CPU 架构。</param>
        /// <param name="originalProjectSettings">构建前 ProjectSettings.asset 的原始字节，读不到就是 null。</param>
        private static void RestoreAndroidSettings(
            ScriptingImplementation backend,
            AndroidArchitecture architectures,
            byte[] originalProjectSettings)
        {
            try
            {
                PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, backend);
                PlayerSettings.Android.targetArchitectures = architectures;

                // 先让 Unity 把内存里的设置刷到磁盘，再比对字节。顺序反了就会把 Unity 随后的写入
                // 当成「已经恢复好了」，白忙一场。
                AssetDatabase.SaveAssets();
                RestoreProjectSettingsBytes(originalProjectSettings);

                Log($"已恢复为 {backend} + {architectures}（-releaseBuild 只在本次构建期间生效）");
            }
            catch (Exception exception)
            {
                // 恢复失败必须喊出来：工作区里会留着 IL2CPP + ARM64 的改动，人得知道去手动还原。
                Debug.LogError($"{LogPrefix} 恢复 Android 构建设置失败：{exception.Message}；"
                               + "请手动执行 git checkout -- ProjectSettings/ProjectSettings.asset 还原。");
            }
        }

        /// <summary>读 ProjectSettings.asset 的原始字节；文件不在就返回 null，那种情况下只做语义恢复。</summary>
        private static byte[] ReadProjectSettingsBytes()
        {
            string path = GetProjectSettingsPath();
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return null;
            }

            return File.ReadAllBytes(path);
        }

        /// <summary>
        /// 磁盘上的 ProjectSettings.asset 与构建前不一致时，按原始字节整体写回。
        /// Unity 序列化「默认值」的写法和原文件不一定一样（空字典 {} vs 显式一条 Android: 0），
        /// 只按 API 恢复语义会留下纯文本层面的 diff，这一步把它抹平。
        /// </summary>
        /// <param name="original">构建前的原始字节，null 表示没存到，跳过。</param>
        private static void RestoreProjectSettingsBytes(byte[] original)
        {
            if (original == null)
            {
                return;
            }

            string path = GetProjectSettingsPath();
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return;
            }

            if (BytesEqual(File.ReadAllBytes(path), original))
            {
                return;
            }

            File.WriteAllBytes(path, original);
            Log("ProjectSettings.asset 与构建前不一致，已按原始内容整体回写，工作区不留 diff。");
        }

        /// <summary>ProjectSettings.asset 的绝对路径，由工程根推出来，不写死任何本机路径。</summary>
        private static string GetProjectSettingsPath()
        {
            return ToAbsoluteProjectPath("ProjectSettings/ProjectSettings.asset");
        }

        /// <summary>逐字节比较两段内容；任一为 null 或长度不同都算不相等。</summary>
        private static bool BytesEqual(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
            {
                return false;
            }

            for (int i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i])
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 构建 Addressables 内容。返回 false 表示已经报过错、打包要中止。
        /// 配置表数据与 UI 预制体都走 Addressables，这一步失败就别继续出一个必然跑不起来的包。
        /// </summary>
        private static bool BuildAddressableContent()
        {
            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Fail("找不到 Addressables 设置（Assets/AddressableAssetsData/）。"
                     + "打开 Window > Asset Management > Addressables > Groups，点 Create Addressables Settings 生成后重试。");
                return false;
            }

            Log("构建 Addressables 内容");

            AddressablesPlayerBuildResult result;
            AddressableAssetSettings.BuildPlayerContent(out result);

            if (result != null && !string.IsNullOrEmpty(result.Error))
            {
                Fail($"Addressables 内容构建失败：{result.Error}");
                return false;
            }

            if (result != null)
            {
                Log($"Addressables 内容构建完成，耗时 {result.Duration:F1} 秒，产物 {result.OutputPath}");
            }

            return true;
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
            // 只出 APK：本地装机、发测试包都直接用它。Google Play 对新应用要的是 AAB，
            // 这是 -releaseBuild **没有**覆盖到的一条缺口 —— 它解决的是 64 位（IL2CPP + ARM64），
            // 导出格式还是 APK，所以「加了 -Release」不等于「能上架」。
            // 真要上架时在这里加一个 -appBundle 之类的开关把它翻成 true，顺带配签名 keystore。
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
                // 不用 summary.totalSize：它统计的是构建过程里所有中间产物的合计，
                // 实测 Android 一次 IL2CPP 构建报 906.9 MB 而 APK 只有 41.6 MB，差二十倍，
                // 看日志的人会以为包体失控。这里直接量磁盘上的真实产物。
                Log($"打包成功：{summary.outputPath}");
                Log($"体积 {MeasureOutputMb(summary.outputPath):F1} MB，耗时 {summary.totalTime.TotalSeconds:F1} 秒，"
                    + $"警告 {summary.totalWarnings} 条");
                Quit(0);
                return;
            }

            Debug.LogError($"{LogPrefix} 打包失败：结果 {summary.result}，错误 {summary.totalErrors} 条，"
                           + $"耗时 {summary.totalTime.TotalSeconds:F1} 秒");
            LogFirstErrors(report);
            Quit(1);
        }

        /// <summary>
        /// 量磁盘上真实产物的体积，单位 MB。
        /// Android 的产物是单个 apk，量它自己；Windows 的产物是一个目录
        /// （exe 只是几百 KB 的启动器，数据在同级的 *_Data 里），量整个目录。
        /// 量不到就返回 0，只是日志少一个数字，不该因此让打包失败。
        /// </summary>
        private static double MeasureOutputMb(string outputPath)
        {
            try
            {
                if (string.IsNullOrEmpty(outputPath))
                {
                    return 0d;
                }

                long bytes;
                if (Directory.Exists(outputPath))
                {
                    bytes = DirectorySizeBytes(outputPath);
                }
                else if (File.Exists(outputPath))
                {
                    string dir = Path.GetDirectoryName(outputPath);
                    // Windows：产物是目录里的一个 exe，整个目录才是包体
                    bytes = Path.GetExtension(outputPath).Equals(".exe", StringComparison.OrdinalIgnoreCase)
                            && !string.IsNullOrEmpty(dir)
                        ? DirectorySizeBytes(dir)
                        : new FileInfo(outputPath).Length;
                }
                else
                {
                    return 0d;
                }

                return bytes / 1024d / 1024d;
            }
            catch (Exception e)
            {
                Log($"量产物体积失败（不影响打包结果）：{e.Message}");
                return 0d;
            }
        }

        private static long DirectorySizeBytes(string dir)
        {
            long total = 0L;
            foreach (string file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                total += new FileInfo(file).Length;
            }

            return total;
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
