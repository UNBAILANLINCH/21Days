// 职责：编辑器侧的场景地址校验——世界表里标了 implemented=true 的场景，其 scene_address 必须**真的**登记在
//   Addressables 的 Scenes 组里；对不上就报出「哪个场景、写了什么地址、组里有什么」。
// 为什么只在编辑器做（PRP/world-scenes §2.4）：Runtime 不许 `using UnityEditor`（project-root.md 依赖方向），
//   所以「地址真的存在」这一半只能放编辑器；表内自洽那一半（未实装不许写地址）在 WorldCatalogValidator 里。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：AssetAuditWindow 是资产体检窗口、PlaceholderAssetGuard 判的是「占位素材进包」，
//      两者都不看世界表的 implemented 列。
//   2. 扩展不行：往 WorldCatalog / WorldCatalogValidator 里加 Addressables 查询会让 Runtime 依赖编辑器程序集。
//   3. 所以单独一个编辑器工具 + 菜单入口（菜单写法照 GenerateTablesMenu / PlaceholderAssetGuard）。
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Game.Core.Config;
using Game.World;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace Game.Editor.World
{
    /// <summary>
    /// 世界表 → Addressables 的地址校验器。菜单：<c>21Days/世界/校验场景地址</c>。
    /// <para>
    /// 判定分两层：<see cref="Compare"/> 是**纯判定**（给定已实装场景与组内地址集合，报出对不上的），
    /// 可以在 EditMode 里直接测；<see cref="Run"/> 负责把真表与真 Addressables 设置喂给它。
    /// </para>
    /// </summary>
    public static class WorldAddressValidator
    {
        /// <summary>场景条目所在的 Addressables 组名（与 Assets/AddressableAssetsData/AssetGroups/Scenes.asset 同名）。</summary>
        public const string ScenesGroupName = "Scenes";

        private const string LogPrefix = "[世界表·场景地址]";

        /// <summary>一个「已实装场景」的地址要求：场景键 + 表里写的地址。</summary>
        public readonly struct ImplementedScene
        {
            public ImplementedScene(string sceneKey, string address)
            {
                SceneKey = sceneKey;
                Address = address;
            }

            /// <summary>场景键（<c>TbScene.scene_key</c>）。</summary>
            public string SceneKey { get; }

            /// <summary>表里写的 Addressables 地址（<c>TbScene.scene_address</c>）。</summary>
            public string Address { get; }
        }

        /// <summary>
        /// 纯判定：每个已实装场景的地址都必须出现在 <paramref name="registeredAddresses"/> 里。
        /// 返回问题清单（空列表 = 全过），每条都带「哪个场景、写了什么地址、组里有什么、怎么修」。
        /// </summary>
        public static IReadOnlyList<string> Compare(IReadOnlyList<ImplementedScene> implementedScenes,
            IReadOnlyCollection<string> registeredAddresses, string groupName)
        {
            var problems = new List<string>();
            if (implementedScenes == null)
            {
                return problems;
            }

            for (int i = 0; i < implementedScenes.Count; i++)
            {
                ImplementedScene scene = implementedScenes[i];
                if (Contains(registeredAddresses, scene.Address))
                {
                    continue;
                }

                problems.Add($"场景「{scene.SceneKey}」标着 implemented=true，但它写的地址「{scene.Address}」"
                             + $"不在 Addressables 的「{groupName}」组里。该组现有地址：{Describe(registeredAddresses)}。"
                             + "修法二选一：① 把场景登记进该组、地址写成表里这一列的值；"
                             + "② 还没实装就把 Tables/Data/world/scene/ 里这一行的 implemented 改回 false（并清空 scene_address）。");
            }

            return problems;
        }

        // 手写集合包含判定：入参只承诺 IReadOnlyCollection（不保证实现 ICollection<T>），
        // 拿它当 ICollection 用会在编译期就过不去，运行时强转更会炸——所以只遍历、不改它。
        private static bool Contains(IReadOnlyCollection<string> addresses, string value)
        {
            if (addresses == null || string.IsNullOrEmpty(value))
            {
                return false;
            }

            foreach (string address in addresses)
            {
                if (string.Equals(address, value, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 对着真实配置跑一遍：读生成的表字节 → 世界表校验 → 取 Scenes 组地址 → <see cref="Compare"/>。
        /// 结果同时打到 Console，并作为报告文本返回（脚本与测试都能读）。
        /// </summary>
        /// <param name="errorCount">问题条数（0 = 通过）。</param>
        public static string Run(out int errorCount)
        {
            var report = new StringBuilder();

            WorldCatalog catalog = BuildCatalog(report, out errorCount);
            if (catalog == null)
            {
                LogReport(report, errorCount);
                return report.ToString();
            }

            // 先过表内校验：表自己就不自洽时，地址对不对没有意义（也没法可靠读出 implemented 列）。
            WorldValidationResult validation = catalog.Validate();
            if (!validation.Passed)
            {
                errorCount += validation.Problems.Count;
                report.AppendLine($"世界表本身没通过校验（{validation.Problems.Count} 条），先修表再看地址：{validation.Describe()}");
                LogReport(report, errorCount);
                return report.ToString();
            }

            if (!TryCollectScenesGroupAddresses(out List<string> addresses, out string groupError))
            {
                errorCount++;
                report.AppendLine(groupError);
                LogReport(report, errorCount);
                return report.ToString();
            }

            List<ImplementedScene> implemented = CollectImplementedScenes(catalog);
            if (implemented.Count == 0)
            {
                report.AppendLine($"世界表里没有 implemented=true 的场景；Scenes 组现有 {addresses.Count} 条地址（未实装场景不校验地址）。");
            }

            IReadOnlyList<string> problems = Compare(implemented, addresses, ScenesGroupName);
            errorCount += problems.Count;
            for (int i = 0; i < problems.Count; i++)
            {
                report.AppendLine(problems[i]);
            }

            if (problems.Count == 0)
            {
                report.AppendLine($"通过：{implemented.Count} 个已实装场景的地址都在「{ScenesGroupName}」组里"
                                  + $"（组内 {addresses.Count} 条地址）。");
            }

            LogReport(report, errorCount);
            return report.ToString();
        }

        [MenuItem("21Days/世界/校验场景地址", false, 500)]
        public static void ValidateFromMenu()
        {
            Run(out int errorCount);
            if (errorCount == 0)
            {
                Debug.Log($"{LogPrefix} 校验通过。");
            }
        }

        /// <summary>读 Data/Config 下的表字节并建目录；失败时把原因写进报告并返回 null。</summary>
        private static WorldCatalog BuildCatalog(StringBuilder report, out int errorCount)
        {
            errorCount = 0;
            string configDir = Path.Combine(Application.dataPath, "_Project/Data/Config");
            if (!Directory.Exists(configDir))
            {
                errorCount = 1;
                report.AppendLine($"找不到配置表数据目录 {configDir}（先跑一次 scripts/gen-tables.ps1）。");
                return null;
            }

            string[] files = Directory.GetFiles(configDir, "*.bytes", SearchOption.AllDirectories);
            if (files.Length == 0)
            {
                errorCount = 1;
                report.AppendLine($"{configDir} 下一个 .bytes 都没有（先跑一次 scripts/gen-tables.ps1）。");
                return null;
            }

            var tableBytes = new Dictionary<string, byte[]>(files.Length, StringComparer.Ordinal);
            for (int i = 0; i < files.Length; i++)
            {
                tableBytes[Path.GetFileNameWithoutExtension(files[i])] = File.ReadAllBytes(files[i]);
            }

            try
            {
                return new WorldCatalog(new TableConfigService(ConfigService.BuildTables(tableBytes)));
            }
            catch (Exception e)
            {
                errorCount = 1;
                report.AppendLine($"读配置表失败：{e.Message}");
                return null;
            }
        }

        /// <summary>表里标了 implemented=true 的场景（顺序与表一致）；地址为空也收进来——那一条由表校验报，这里只备查。</summary>
        private static List<ImplementedScene> CollectImplementedScenes(WorldCatalog catalog)
        {
            IReadOnlyList<global::cfg.world.Scene> scenes = catalog.AllScenes;
            var implemented = new List<ImplementedScene>(scenes.Count);
            for (int i = 0; i < scenes.Count; i++)
            {
                if (scenes[i].Implemented)
                {
                    implemented.Add(new ImplementedScene(scenes[i].SceneKey, scenes[i].SceneAddress ?? string.Empty));
                }
            }

            return implemented;
        }

        /// <summary>取 Scenes 组的全部地址。组不存在 / 工程没有 Addressables 设置对象时返回 false 并给出原因。</summary>
        private static bool TryCollectScenesGroupAddresses(out List<string> addresses, out string error)
        {
            addresses = new List<string>();

            if (!AddressableAssetSettingsDefaultObject.SettingsExists)
            {
                error = "工程里没有 Addressables 设置对象（Assets/AddressableAssetsData），没法校验地址。";
                return false;
            }

            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                error = "Addressables 设置对象为 null（可能是资产损坏），没法校验地址。";
                return false;
            }

            bool foundGroup = false;
            foreach (AddressableAssetGroup group in settings.groups)
            {
                if (group == null || group.Name != ScenesGroupName)
                {
                    continue;
                }

                foundGroup = true;
                foreach (AddressableAssetEntry entry in group.entries)
                {
                    if (entry != null && !string.IsNullOrEmpty(entry.address) && !addresses.Contains(entry.address))
                    {
                        addresses.Add(entry.address);
                    }
                }
            }

            if (!foundGroup)
            {
                error = $"Addressables 里没有名为「{ScenesGroupName}」的组（见 Assets/AddressableAssetsData/AssetGroups/）。";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private static string Describe(IReadOnlyCollection<string> addresses)
        {
            if (addresses == null || addresses.Count == 0)
            {
                return "一条都没有";
            }

            var text = new StringBuilder();
            foreach (string address in addresses)
            {
                if (text.Length > 0)
                {
                    text.Append('、');
                }

                text.Append(address);
            }

            return text.ToString();
        }

        // 报告逐行打 Console：错误行用 LogError，其余用 Log（同 PlaceholderAssetGuard 的写法）。
        private static void LogReport(StringBuilder report, int errorCount)
        {
            string[] lines = report.ToString().Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].TrimEnd('\r');
                if (line.Length == 0)
                {
                    continue;
                }

                if (errorCount > 0)
                {
                    Debug.LogError($"{LogPrefix} {line}");
                }
                else
                {
                    Debug.Log($"{LogPrefix} {line}");
                }
            }
        }

        /// <summary>只递一份现成的 <c>cfg.Tables</c>：编辑器工具不需要启动 ConfigService（它要 Addressables 初始化）。</summary>
        private sealed class TableConfigService : IConfigService
        {
            public TableConfigService(global::cfg.Tables tables) => Tables = tables;

            public global::cfg.Tables Tables { get; }

            public ulong ContentHash => throw new NotSupportedException("本工具不提供内容指纹");
        }
    }
}
