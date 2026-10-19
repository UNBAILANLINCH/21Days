// 职责：显式调整两份现有纸片材质的环境融合参数，保留 GUID、深度偏移和角色接线。
// 复用现有 SpriteDepthClip；FramePuppetGenerator 负责生成动画，不应承担全项目材质调色，所以独立窗口。
// 执行载体：仅菜单/按钮；状态锚点：两份材质 Inspector 的四项融合数值；关闭窗口即无活动，无自动扫描。
using System;
using UnityEditor;
using UnityEngine;

namespace Game.Editor.CharacterPuppet
{
    public sealed class PaperEnvironmentWindow : EditorWindow
    {
        private const string MaterialDirectory = "Assets/_Project/Art/Materials/Character/";
        private const string ShaderName = "21Days/SpriteDepthClip";
        private static readonly string[] Properties =
            { "_EnvironmentStrength", "_ShadowStrength", "_LightFloor", "_Saturation" };

        [SerializeField] private float environmentStrength;
        [SerializeField] private float shadowStrength;
        [SerializeField] private float lightFloor = 0.45f;
        [SerializeField] private float saturation = 1f;

        [MenuItem("21Days/角色/纸片环境融合…")]
        private static void Open()
        {
            GetWindow<PaperEnvironmentWindow>("纸片环境融合");
        }

        private void OnEnable()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialDirectory + "M_SpriteDepthClip.mat");
            if (material == null || material.shader == null || material.shader.name != ShaderName) return;
            foreach (string property in Properties)
                if (!material.HasProperty(property)) return;
            environmentStrength = material.GetFloat(Properties[0]);
            shadowStrength = material.GetFloat(Properties[1]);
            lightFloor = material.GetFloat(Properties[2]);
            saturation = material.GetFloat(Properties[3]);
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox("只修改现有基础/玩家两份共享材质，所有引用者（含演出/战斗）都会受影响。" +
                "不移动角色、不生成阴影、不保存场景。先备份两份材质；每次只调整一项并比较亮处/屋檐/台阶。", MessageType.Info);
            environmentStrength = EditorGUILayout.Slider("环境色混合", environmentStrength, 0f, 1f);
            shadowStrength = EditorGUILayout.Slider("主光阴影混合", shadowStrength, 0f, 1f);
            lightFloor = EditorGUILayout.Slider("环境下限（混合前）", lightFloor, 0f, 1f);
            saturation = EditorGUILayout.Slider("原画饱和度", saturation, 0f, 1f);
            if (GUILayout.Button("填入保守试用值（尚不应用）"))
            {
                environmentStrength = 0.3f;
                shadowStrength = 0.3f;
                lightFloor = 0.45f;
                saturation = 1f;
            }
            if (GUILayout.Button("填入旧版表现值（尚不应用）"))
            {
                environmentStrength = 0f;
                shadowStrength = 0f;
                lightFloor = 0.45f;
                saturation = 1f;
            }
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (GUILayout.Button("应用并仅保存两份角色材质（支持 Undo）")) ApplyProjectMaterials();
            }
        }

        private void ApplyProjectMaterials()
        {
            var materials = new[]
            {
                AssetDatabase.LoadAssetAtPath<Material>(MaterialDirectory + "M_SpriteDepthClip.mat"),
                AssetDatabase.LoadAssetAtPath<Material>(MaterialDirectory + "M_SpriteDepthClip_Player.mat")
            };
            try
            {
                ApplyToMaterials(materials, environmentStrength, shadowStrength, lightFloor, saturation, true);
                foreach (Material material in materials) AssetDatabase.SaveAssetIfDirty(material);
                SceneView.RepaintAll();
                Debug.Log("纸片融合：已保存两份现有角色材质，未修改场景/预制体。Undo 后请保存项目以落盘撤销。");
            }
            catch (ArgumentException exception)
            {
                EditorUtility.DisplayDialog("未应用", exception.Message, "确定");
            }
        }

        /// <summary>先验证全部目标与数值，再写入四项表现参数；不改 shader、队列、Tint、Cutoff 或 DepthBias。</summary>
        public static void ApplyToMaterials(Material[] materials, float environment, float shadow, float floor, float colorSaturation,
            bool rejectDirty = false)
        {
            if (materials == null || materials.Length == 0) throw new ArgumentException("没有目标材质。");
            var values = new[] { environment, shadow, floor, colorSaturation };
            foreach (float value in values)
                if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f || value > 1f)
                    throw new ArgumentException("融合数值必须为 0 到 1 的有限数。");
            foreach (Material material in materials)
            {
                if (material == null || material.shader == null || material.shader.name != ShaderName)
                    throw new ArgumentException("目标缺失或不是 SpriteDepthClip 材质；未写入任何目标。");
                if (rejectDirty && EditorUtility.IsDirty(material))
                    throw new ArgumentException("角色材质有尚未保存的修改，请先自行保存或撤销后重试；本次未写入任何目标。");
                foreach (string property in Properties)
                    if (!material.HasProperty(property)) throw new ArgumentException("缺少属性 " + property + "，请先导入新版 shader。");
            }
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("纸片环境融合");
            Undo.RecordObjects(materials, "纸片环境融合");
            foreach (Material material in materials)
            {
                for (int index = 0; index < Properties.Length; index++) material.SetFloat(Properties[index], values[index]);
                EditorUtility.SetDirty(material);
            }
            Undo.FlushUndoRecordObjects();
            Undo.CollapseUndoOperations(undoGroup);
            Undo.IncrementCurrentGroup();
        }
    }
}
