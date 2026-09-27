// PlaceholderAssetGuard —— 占位素材闸门：出包前检查进包内容有没有引用仅限开发期的占位素材
// （当前是 Art/Sprites/Characters/Ark/ 下的明日方舟小人序列帧，版权归鹰角，正式包不得包含）。
//
// 进包内容 = Build Settings 里启用的场景 + Addressables 各组条目（文件夹条目展开到其中资产），
// 各自用 AssetDatabase.GetDependencies 递归取全部依赖，交给 PlaceholderAssetGuardRules 判定。
//
// 三个入口：
//   1. BuildScript.Build 在切平台、构建 Addressables 之前调 CheckBeforeBuild —— 批处理出包尽早失败，
//      Release 命中时 BuildScript 以退出码 1 结束，scripts/build.ps1 据此报失败。
//   2. IPreprocessBuildWithReport 兜底 —— 编辑器里手点 File > Build 也会被拦（抛 BuildFailedException）；
//      BuildScript 那条路已经查过，BuildPlayer 期间用 SetPreprocessSkipped 跳过，避免同一份报告打两遍。
//   3. 菜单 21Days/打包/检查占位素材引用 —— 不出包也能查，报告打到控制台。
//
// 为什么新建而不是塞进 BuildScript（复用 → 扩展 → 新建）：职责是「占位素材闸门」而不是「打包流程」，
// 且要被编辑器手动 Build（预处理回调）和菜单复用；塞进 BuildScript 这两个入口就只能反向依赖打包脚本的私有方法。

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Game.Editor
{
    /// <summary>
    /// 占位素材闸门的收集层与接线：把工程里「将要进包」的根与依赖整理出来，按规则判定并打日志。
    /// </summary>
    public sealed class PlaceholderAssetGuard : IPreprocessBuildWithReport
    {
        private const string LogPrefix = "[Build][占位素材闸门]";

        /// <summary>Built In Data 组里代表 Build Settings 场景列表的伪条目 guid。</summary>
        private const string EditorSceneListEntryGuid = "EditorSceneList";

        /// <summary>BuildScript 已经预检过、正处于 BuildPlayer 调用期间时为 true，预处理回调据此跳过。</summary>
        private static bool preprocessSkipped;

        /// <summary>预处理回调顺序；靠前执行，早拦早好。</summary>
        public int callbackOrder => -100;

        /// <summary>
        /// 打包前检查。命中时 Release 打错误日志并返回 false（调用方中止打包），开发版逐条警告后返回 true。
        /// </summary>
        /// <param name="scenes">本次进包的场景路径。</param>
        /// <param name="development">是否开发版（BuildOptions.Development）。</param>
        public static bool CheckBeforeBuild(IEnumerable<string> scenes, bool development)
        {
            List<PlaceholderAssetGuardRules.Hit> hits = Scan(scenes);
            if (hits.Count == 0)
            {
                Debug.Log($"{LogPrefix} {PlaceholderAssetGuardRules.BuildReport(hits, !development)}");
                return true;
            }

            LogHits(hits, !development);
            return development;
        }

        /// <summary>
        /// 由 BuildScript 在调 BuildPipeline.BuildPlayer 前后成对调用：期间预处理回调不再重复检查。
        /// </summary>
        public static void SetPreprocessSkipped(bool skipped)
        {
            preprocessSkipped = skipped;
        }

        /// <summary>扫描：收集进包根与依赖，返回命中清单。</summary>
        /// <param name="scenes">进包场景；传 null 取 Build Settings 里启用的场景。</param>
        public static List<PlaceholderAssetGuardRules.Hit> Scan(IEnumerable<string> scenes)
        {
            List<KeyValuePair<string, IEnumerable<string>>> roots = CollectRoots(scenes ?? GetEnabledScenes());
            return PlaceholderAssetGuardRules.FindHits(roots, PlaceholderAssetGuardRules.DefaultForbiddenPrefixes);
        }

        /// <summary>手动检查：不出包，只把报告打到控制台。返回报告文本，方便脚本调用。</summary>
        [MenuItem("21Days/打包/检查占位素材引用")]
        public static string RunManualCheck()
        {
            List<PlaceholderAssetGuardRules.Hit> hits = Scan(null);
            string report = PlaceholderAssetGuardRules.BuildReport(hits, false);
            if (hits.Count == 0)
            {
                Debug.Log($"{LogPrefix} {report}");
            }
            else
            {
                Debug.LogWarning($"{LogPrefix} {report}");
            }

            return report;
        }

        /// <summary>编辑器里手点 Build 的兜底：Release 命中直接让构建失败。</summary>
        public void OnPreprocessBuild(BuildReport report)
        {
            if (preprocessSkipped)
            {
                return;
            }

            bool development = (report.summary.options & BuildOptions.Development) != 0;
            if (!CheckBeforeBuild(null, development))
            {
                throw new BuildFailedException(
                    "占位素材闸门：进包内容引用了仅限开发期的占位素材，Release 构建已中止（引用链见控制台）。");
            }
        }

        /// <summary>
        /// 命中时的日志：首行是结论与汇总，随后每条引用链单独一行（grep / build.ps1 回显日志尾部时一眼能看到），
        /// Release 用错误级别，开发版用警告级别。
        /// </summary>
        private static void LogHits(List<PlaceholderAssetGuardRules.Hit> hits, bool blocking)
        {
            string report = PlaceholderAssetGuardRules.BuildReport(hits, blocking);
            foreach (string line in report.Split('\n'))
            {
                string trimmed = line.TrimEnd('\r');
                if (trimmed.Length == 0)
                {
                    continue;
                }

                if (blocking)
                {
                    Debug.LogError($"{LogPrefix} {trimmed}");
                }
                else
                {
                    Debug.LogWarning($"{LogPrefix} {trimmed}");
                }
            }
        }

        /// <summary>
        /// 收集进包根：场景 + Addressables 条目。每个根带上自己的递归依赖（GetDependencies 含根自身）。
        /// </summary>
        private static List<KeyValuePair<string, IEnumerable<string>>> CollectRoots(IEnumerable<string> scenes)
        {
            List<KeyValuePair<string, IEnumerable<string>>> roots = new List<KeyValuePair<string, IEnumerable<string>>>();

            foreach (string scene in scenes)
            {
                if (string.IsNullOrEmpty(scene))
                {
                    continue;
                }

                roots.Add(new KeyValuePair<string, IEnumerable<string>>(
                    $"场景 {scene}",
                    AssetDatabase.GetDependencies(scene, true)));
            }

            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.Log($"{LogPrefix} 工程没有 Addressables 设置对象，只检查 Build Settings 里的场景。");
                return roots;
            }

            List<AddressableAssetEntry> entries = new List<AddressableAssetEntry>();
            foreach (AddressableAssetGroup group in settings.groups)
            {
                if (group == null)
                {
                    continue;
                }

                foreach (AddressableAssetEntry entry in group.entries)
                {
                    // EditorSceneList 就是 Build Settings 场景列表，上面已经按场景查过
                    // （Addressables 把这个名字定义成 internal 常量，只能按字面比）
                    if (entry == null || entry.guid == EditorSceneListEntryGuid)
                    {
                        continue;
                    }

                    // 文件夹条目、Resources 条目展开到其中的每个资产；普通条目 includeSelf 即自身
                    entries.Clear();
                    entry.GatherAllAssets(entries, true, true, false);
                    foreach (AddressableAssetEntry asset in entries)
                    {
                        if (asset == null || string.IsNullOrEmpty(asset.AssetPath)
                            || AssetDatabase.IsValidFolder(asset.AssetPath))
                        {
                            continue;
                        }

                        roots.Add(new KeyValuePair<string, IEnumerable<string>>(
                            $"Addressables「{group.Name}」{asset.address}（{asset.AssetPath}）",
                            AssetDatabase.GetDependencies(asset.AssetPath, true)));
                    }
                }
            }

            return roots;
        }

        private static IEnumerable<string> GetEnabledScenes()
        {
            return EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path);
        }
    }
}
