// 职责：对作者分组执行一次可撤销的整体平移，关联对象保持原有层级和引用。
// 复用：沿用 Unity EditorTool、PositionHandle 和 Undo；不增加运行时移动驱动。
// 扩展不足：原点位窗口没有独立 Scene 移动工具入口，故新增工具，由窗口激活并复用同一移动方法。
using System;
using System.Collections.Generic;
using Game.World;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Game.Editor.World
{
    [EditorTool("关卡整组移动")]
    public sealed class LevelGroupMoveTool : EditorTool
    {
        private LevelLayoutGroup selectedGroup;
        public override GUIContent toolbarIcon => new GUIContent(EditorGUIUtility.IconContent("MoveTool")) { tooltip = "整组移动（包括关联建筑和点位）" };

        public override void OnActivated()
        {
            Selection.selectionChanged += RefreshSelection;
            RefreshSelection();
        }

        public override void OnWillBeDeactivated() => Selection.selectionChanged -= RefreshSelection;

        private void RefreshSelection()
        {
            GameObject selected = Selection.activeGameObject;
            selectedGroup = selected != null ? selected.GetComponent<LevelLayoutGroup>() : null;
        }

        public override void OnToolGUI(EditorWindow window)
        {
            if (!(window is SceneView) || EditorApplication.isPlayingOrWillChangePlaymode) return;
            LevelLayoutGroup group = selectedGroup;
            if (group == null) return;
            Handles.Label(group.transform.position, group.DisplayName + " · 整组移动");
            EditorGUI.BeginChangeCheck();
            Vector3 position = Handles.PositionHandle(group.transform.position, Quaternion.identity);
            if (!EditorGUI.EndChangeCheck()) return;
            try { Move(group, position); }
            catch (InvalidOperationException error) { Debug.LogWarning(error.Message); }
        }

        public static List<Transform> MovingRoots(LevelLayoutGroup group)
        {
            if (group == null || !group.gameObject.scene.IsValid() || !group.gameObject.scene.isLoaded)
                throw new InvalidOperationException("请选场景中的关卡分组，不能移动预制体资产。");
            var candidates = new List<Transform> { group.transform };
            foreach (Transform member in group.LinkedMembers)
            {
                if (member == null) throw new InvalidOperationException("分组存在空关联对象，请先移除或重新指定。");
                if (member.gameObject.scene != group.gameObject.scene)
                    throw new InvalidOperationException("关联对象必须与分组处于同一场景。");
                if (member != group.transform && group.transform.IsChildOf(member))
                    throw new InvalidOperationException("不能关联分组的祖先（例如整张地图），否则会移动其他店铺。");
                if (!candidates.Contains(member)) candidates.Add(member);
            }
            // 父子引用只移动最高层一次，避免重复平移人员或建筑子节点。
            var roots = new List<Transform>();
            foreach (Transform candidate in candidates)
            {
                bool nested = false;
                foreach (Transform other in candidates)
                    if (candidate != other && candidate.IsChildOf(other)) { nested = true; break; }
                if (!nested) roots.Add(candidate);
            }
            foreach (GameObject root in group.gameObject.scene.GetRootGameObjects())
                foreach (LevelLayoutGroup other in root.GetComponentsInChildren<LevelLayoutGroup>(true))
                {
                    if (other == group) continue;
                    foreach (Transform candidate in roots)
                    {
                        if (Overlaps(candidate, other.transform))
                            throw new InvalidOperationException("分组包含另一分组，不能整体移动：" + other.name);
                        foreach (Transform member in other.LinkedMembers)
                            if (member != null && Overlaps(candidate, member))
                                throw new InvalidOperationException("两个分组共享对象，请明确归属后再移动：" + member.name);
                    }
                }
            return roots;
        }

        private static bool Overlaps(Transform a, Transform b) => a == b || a.IsChildOf(b) || b.IsChildOf(a);

        public static void Move(LevelLayoutGroup group, Vector3 position)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("播放中不能修改关卡摆位。");
            if (!Finite(position.x) || !Finite(position.y) || !Finite(position.z)) throw new InvalidOperationException("位置必须是有限数值。");
            List<Transform> roots = MovingRoots(group);
            Vector3 offset = position - group.transform.position;
            if (offset == Vector3.zero) return;
            Undo.RecordObjects(roots.ToArray(), "整组移动 " + group.name);
            foreach (Transform root in roots)
            {
                root.position += offset;
                PrefabUtility.RecordPrefabInstancePropertyModifications(root);
            }
            EditorSceneManager.MarkSceneDirty(group.gameObject.scene);
            SceneView.RepaintAll();
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
