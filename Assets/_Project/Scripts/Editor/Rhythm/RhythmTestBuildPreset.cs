// 职责：独立音游测试包的临时场景和 Addressables 范围。复用 BuildScript 的构建与闸门，
// 原场景生成器不应承担构建事务，故新增预设作用域；只显式调用，不自动运行、不实际出包。
using System;
using System.Collections.Generic;
using System.IO;
using Game.Core.Platform;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

namespace Game.Editor.Rhythm
{
    public sealed class RhythmTestBuildPreset : IDisposable
    {
        public static string OutputPath => "Builds/RhythmTest/21Days-RhythmTest.exe";
        public static string ProductName => "21Days-RhythmTest";
        public static string[] Scenes => new[] { RhythmDemoBuilder.ScenePath };
        public static string[] Defines => new[] { PlatformServiceBase.IsolatedBuildDefine };
        private static readonly HashSet<string> RequiredUI = new HashSet<string>(StringComparer.Ordinal)
            { "RhythmView", "TitleView", "SettingsView", "PauseMenuView", "NotificationView", "ConfirmView", "SaveSlotsView" };
        private readonly AddressableAssetSettings settings;
        private readonly AddressableAssetGroup originalDefault;
        private readonly string originalSettingsJson;
        private readonly string originalVersion;
        private readonly string originalProductName;
        private readonly Dictionary<BundledAssetGroupSchema, bool> includes = new Dictionary<BundledAssetGroupSchema, bool>();
        private readonly Dictionary<PlayerDataGroupSchema, bool> builtInScenes = new Dictionary<PlayerDataGroupSchema, bool>();
        private readonly Dictionary<AddressableAssetEntry, AddressableAssetGroup> moved = new Dictionary<AddressableAssetEntry, AddressableAssetGroup>();
        private readonly Dictionary<UnityEngine.Object, bool> dirty = new Dictionary<UnityEngine.Object, bool>();
        private readonly Dictionary<string, byte[]> files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        private bool disposed;

        public RhythmTestBuildPreset()
        {
            if (EditorApplication.isPlaying || EditorApplication.isCompiling) throw new InvalidOperationException("预设只能在编辑器空闲时应用");
            settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) throw new InvalidOperationException("缺少 Addressables 配置");
            var ui = settings.FindGroup("UI");
            var config = settings.FindGroup("Config");
            var parking = settings.FindGroup("Default Local Group");
            if (ui == null || config == null || parking == null || ui.GetSchema<BundledAssetGroupSchema>() == null ||
                config.GetSchema<BundledAssetGroupSchema>() == null || parking.GetSchema<BundledAssetGroupSchema>() == null)
                throw new InvalidOperationException("测试预设需要现有 UI、Config 和 Default Local Group 的打包 Schema");
            var found = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in ui.entries) if (RequiredUI.Contains(entry.address)) found.Add(entry.address);
            if (found.Count != RequiredUI.Count || config.entries.Count == 0) throw new InvalidOperationException("测试包 UI 或配置表地址不完整");
            var chart = AssetDatabase.LoadAssetAtPath<Game.Rhythm.RhythmConfig>("Assets/_Project/Data/Rhythm/ChongErFei.asset");
            RhythmDemoBuilder.ValidateReusableChart(chart);
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(RhythmDemoBuilder.ScenePath) == null) throw new InvalidOperationException("缺少 RhythmDemo 场景");
            originalDefault = settings.DefaultGroup;
            originalSettingsJson = EditorJsonUtility.ToJson(settings);
            originalVersion = PlayerSettings.bundleVersion;
            originalProductName = PlayerSettings.productName;
            Capture(settings);
            CaptureFile("ProjectSettings/EditorBuildSettings.asset");
            CaptureFile("ProjectSettings/ProjectSettings.asset");
            foreach (var group in settings.groups)
            {
                if (group == null) continue;
                Capture(group);
                foreach (var schema in group.Schemas) Capture(schema);
                var bundle = group.GetSchema<BundledAssetGroupSchema>();
                if (bundle != null) includes.Add(bundle, bundle.IncludeInBuild);
                var playerData = group.GetSchema<PlayerDataGroupSchema>();
                if (playerData != null) builtInScenes.Add(playerData, playerData.IncludeBuildSettingsScenes);
            }
            try
            {
                // 保留同一 Entry 对象及其地址、标签、只读值；停车组不进入包体。
                foreach (var entry in new List<AddressableAssetEntry>(ui.entries))
                {
                    if (RequiredUI.Contains(entry.address)) continue;
                    moved.Add(entry, ui);
                    settings.MoveEntry(entry, parking, entry.ReadOnly, false);
                }
                foreach (var pair in includes) pair.Key.IncludeInBuild = pair.Key.Group == ui || pair.Key.Group == config;
                foreach (var pair in builtInScenes) pair.Key.IncludeBuildSettingsScenes = false;
                settings.DefaultGroup = ui;
                // 场景只经 BuildPlayerOptions 指定。改 EditorBuildSettings 会触发 Addressables 自动移除 Entry。
                // Windows 的 persistentDataPath 和 Unity 偏好注册表也使用独立产品名。
                PlayerSettings.productName = ProductName;
            }
            catch { Dispose(); throw; }
        }

        public string Validate()
        {
            // BuildScript 和预检共用真实内容范围与原占位闸门，不放宽禁止规则。
            if (!PlaceholderAssetGuard.CheckBeforeBuild(Scenes, false)) throw new InvalidOperationException("测试包占位素材闸门未通过");
            return "RhythmDemo 单场景；UI 七个地址与 Config 标签表；独立产品名 21Days-RhythmTest；GAME_ISOLATED_TEST_BUILD；isolated-test/saves；正式构建配置在 Dispose 恢复";
        }

        private void Capture(UnityEngine.Object asset)
        {
            dirty.Add(asset, EditorUtility.IsDirty(asset));
            CaptureFile(AssetDatabase.GetAssetPath(asset));
        }
        private void CaptureFile(string path)
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path) && !files.ContainsKey(path)) files.Add(path, File.ReadAllBytes(path));
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            try
            {
                foreach (var pair in moved) settings.MoveEntry(pair.Key, pair.Value, pair.Key.ReadOnly, false);
            }
            finally
            {
                foreach (var pair in includes) pair.Key.IncludeInBuild = pair.Value;
                foreach (var pair in builtInScenes) pair.Key.IncludeBuildSettingsScenes = pair.Value;
                settings.DefaultGroup = originalDefault;
                // 默认组切换会使序列化 currentHash 失效，连同原设置快照一起恢复。
                EditorJsonUtility.FromJsonOverwrite(originalSettingsJson, settings);
                PlayerSettings.bundleVersion = originalVersion;
                PlayerSettings.productName = originalProductName;
                foreach (var pair in files)
                    if (!System.Linq.Enumerable.SequenceEqual(File.ReadAllBytes(pair.Key), pair.Value))
                        File.WriteAllBytes(pair.Key, pair.Value);
                foreach (var pair in dirty)
                    if (pair.Value) EditorUtility.SetDirty(pair.Key); else EditorUtility.ClearDirty(pair.Key);
            }
        }

        [MenuItem("21Days/音游/检查独立 Windows 测试预设（不构建）")]
        public static string ValidateOnly()
        {
            using (var preset = new RhythmTestBuildPreset()) return preset.Validate();
        }
    }
}
