// 职责：场景点位导航、原生 Inspector 编辑、巡逻接线与测试动线标注；位置真源始终是场景 Transform。
// 复用：沿用 SpawnAnchor、QuestLocation 和现有角色/遭遇序列化字段，不增加运行时点位格式。
// 扩展不行：任务编辑器编辑任务表，SceneMapBuilder 生成 HUD，都不负责场景摆位，故新增独立窗口。
using System;
using System.Collections.Generic;
using Game.Quest;
using Game.World;
using Game.ShopPlaytest;
using Game.Taming;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Editor.World
{
    public sealed class LevelPointEditorWindow : EditorWindow
    {
        public const string RoutePrefix = "测试动线_";
        private readonly List<Transform> points = new List<Transform>();
        private readonly List<string> labels = new List<string>();
        private readonly List<Transform> routes = new List<Transform>();
        private readonly List<LevelLayoutGroup> groups = new List<LevelLayoutGroup>();
        [SerializeField] private LevelLayoutGroup selectedGroup;
        private ShopPlaytestWorld shopWorld;
        private bool membersFoldout;
        private bool groupPointsFoldout;
        private Vector2 scroll;
        private Vector2 windowScroll;
        private string pointKey = "";
        private string search = "";
        private string status = "";
        private Transform route;
        private Component patrolOwner;
        private UnityEditor.Editor inspector;
        private bool showLabels = true;
        private bool placeOnClick;
        private int createKind;
        private static readonly string[] CreateLabels = { "出生点", "任务地点", "测试动线点" };
        private static readonly string[] ShopNames = { "戏班", "衣肆", "金银行", "客栈", "酒肆" };

        [MenuItem("21Days/关卡/点位与动线编辑器", false, 410)]
        public static void Open()
        {
            var window = GetWindow<LevelPointEditorWindow>("关卡点位");
            window.minSize = new Vector2(420, 520);
            window.Show();
        }

        private void OnEnable()
        {
            EditorApplication.hierarchyChanged += Refresh;
            Selection.selectionChanged += RefreshInspector;
            SceneView.duringSceneGui += DrawScene;
            Undo.undoRedoPerformed += Refresh;
            Refresh();
        }

        private void OnDisable()
        {
            EditorApplication.hierarchyChanged -= Refresh;
            Selection.selectionChanged -= RefreshInspector;
            SceneView.duringSceneGui -= DrawScene;
            Undo.undoRedoPerformed -= Refresh;
            if (inspector != null) DestroyImmediate(inspector);
        }

        private void Refresh()
        {
            points.Clear(); labels.Clear(); routes.Clear(); groups.Clear(); shopWorld = null;
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded) return;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t.CompareTag("EditorOnly") && t.name.StartsWith(RoutePrefix, StringComparison.Ordinal))
                    {
                        routes.Add(t);
                        foreach (Transform child in t) AddPoint(child, "动线");
                    }
                }
                foreach (MonoBehaviour component in root.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (component == null) continue;
                    string type = component.GetType().FullName;
                    if (component is LevelLayoutGroup group) groups.Add(group);
                    if (component is ShopPlaytestWorld world) shopWorld = world;
                    if (type == "Game.World.SpawnAnchor") AddPoint(component.transform, "出生");
                    else if (type == "Game.Quest.QuestLocation") AddPoint(component.transform, "任务");
                    else if (type == "Game.World.PortalAnchor") AddPoint(component.transform, "传送");
                    else if (type == "Game.Dialogue.DialogueInteractable") AddPoint(component.transform, "NPC");
                    else if (type == "Game.Taming.TamingActor") AddPoint(component.transform, "角色/怪物");
                    if (type != "Game.Taming.TamingActor" && type != "Game.Monster.EncounterSceneView") continue;
                    var data = new SerializedObject(component);
                    SerializedProperty spawn = data.FindProperty("playerSpawn");
                    if (spawn != null) AddPoint(spawn.objectReferenceValue as Transform, "遭遇出生");
                    SerializedProperty monster = data.FindProperty("monsterBody");
                    if (monster != null) AddPoint(monster.objectReferenceValue as Transform, "怪物");
                    SerializedProperty patrol = data.FindProperty("patrolPoints");
                    if (patrol == null) continue;
                    for (int i = 0; i < patrol.arraySize; i++)
                        AddPoint(patrol.GetArrayElementAtIndex(i).objectReferenceValue as Transform, "巡逻");
                }
            }
            RefreshInspector();
            Repaint(); SceneView.RepaintAll();
        }

        private void AddPoint(Transform point, string category)
        {
            if (point == null || point.gameObject.scene != SceneManager.GetActiveScene() || points.Contains(point)) return;
            points.Add(point); labels.Add("[" + category + "] " + point.name);
        }

        private void RefreshInspector()
        {
            if (inspector != null) DestroyImmediate(inspector);
            GameObject selected = Selection.activeGameObject;
            inspector = selected != null && selected.scene == SceneManager.GetActiveScene()
                ? UnityEditor.Editor.CreateEditor(selected.transform) : null;
            Repaint();
        }

        private void OnGUI()
        {
            windowScroll = EditorGUILayout.BeginScrollView(windowScroll);
            var scene = SceneManager.GetActiveScene();
            EditorGUILayout.LabelField("当前场景：" + scene.name, EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("选择点位后直接用移动工具拖动。改动保存到场景，Ctrl+Z 撤销。\n任务内容仍在任务编辑器配置；新增出生 ID 还须登记到世界表。", MessageType.Info);
            bool readOnly = EditorApplication.isPlayingOrWillChangePlaymode;
            if (readOnly) EditorGUILayout.HelpBox("播放期间只读。退出播放后摆位；测试须从 Boot 进入。", MessageType.Warning);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("刷新列表")) Refresh();
                showLabels = GUILayout.Toggle(showLabels, "场景标签");
                using (new EditorGUI.DisabledScope(readOnly))
                    if (GUILayout.Button("保存当前场景")) EditorSceneManager.SaveScene(scene);
            }
            DrawGroups(readOnly);
            search = EditorGUILayout.TextField("搜索", search);
            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MinHeight(120), GUILayout.MaxHeight(240));
            for (int i = 0; i < points.Count; i++)
            {
                if (points[i] == null || labels[i].IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (GUILayout.Button(labels[i], Selection.activeTransform == points[i] ? EditorStyles.miniButton : EditorStyles.label))
                {
                    Selection.activeGameObject = points[i].gameObject;
                    if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.FrameSelected();
                    UnityEditor.Tools.current = Tool.Move;
                }
            }
            EditorGUILayout.EndScrollView();
            using (new EditorGUI.DisabledScope(readOnly))
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("新建点位（在场景表面点击落点）", EditorStyles.boldLabel);
                if (selectedGroup != null && selectedGroup.gameObject.scene == scene)
                    EditorGUILayout.LabelField("新建地点 / 动线归属：" + selectedGroup.DisplayName, EditorStyles.miniLabel);
                createKind = EditorGUILayout.Popup("类型", createKind, CreateLabels);
                pointKey = EditorGUILayout.TextField("ID / 名称", pointKey);
                if (GUILayout.Button(placeOnClick ? "取消放置" : "开始放置"))
                {
                    placeOnClick = !placeOnClick;
                    SceneView.RepaintAll();
                }
                EditorGUILayout.LabelField("测试动线", EditorStyles.boldLabel);
                foreach (Transform candidate in routes)
                    if (candidate != null && GUILayout.Button(candidate.name)) { route = candidate; SceneView.RepaintAll(); }
                route = (Transform)EditorGUILayout.ObjectField("当前路线", route, typeof(Transform), true);
                if (route != null && route.gameObject.scene == scene)
                    EditorGUILayout.LabelField("按子物体顺序连接", $"{route.childCount} 点 · {RouteLength(route):F1} 米（直线段）");
                if (GUILayout.Button("新建测试动线"))
                {
                    Transform parent = selectedGroup != null && selectedGroup.gameObject.scene == scene ? selectedGroup.transform : null;
                    route = CreateObject(RoutePrefix + (string.IsNullOrWhiteSpace(pointKey) ? "路线" : pointKey.Trim()), Vector3.zero, parent).transform;
                    route.gameObject.tag = "EditorOnly";
                    EditorSceneManager.MarkSceneDirty(scene);
                    Refresh();
                }
                EditorGUILayout.HelpBox("动线是人工走测参考线，不代表寻路或碰撞可达。调整层级中子物体顺序即可调整路线。", MessageType.None);
                patrolOwner = (Component)EditorGUILayout.ObjectField("巡逻角色 / 遭遇视图", patrolOwner, typeof(Component), true);
                if (GUILayout.Button("把选中角色设为巡逻编辑对象"))
                {
                    GameObject selected = Selection.activeGameObject;
                    if (selected != null)
                        foreach (MonoBehaviour c in selected.GetComponents<MonoBehaviour>()) // lint-ok: 仅点击按钮时查找，不是每帧扫描
                            if (c != null && new SerializedObject(c).FindProperty("patrolPoints") != null) { patrolOwner = c; break; }
                }
                if (patrolOwner != null && patrolOwner.gameObject.scene == scene)
                {
                    var data = new SerializedObject(patrolOwner);
                    var property = data.FindProperty("patrolPoints");
                    if (property != null)
                    {
                        EditorGUILayout.HelpBox("巡逻角色初始位置取巡逻点；调整怪物出生位置时一并移动首个巡逻点。引用点不能挂在移动角色下面。", MessageType.Info);
                        data.Update(); EditorGUILayout.PropertyField(property, new GUIContent("巡逻点（按顺序）"), true); data.ApplyModifiedProperties();
                        if (GUILayout.Button("增加巡逻点（放在选中位置）"))
                        {
                            Vector3 position = Selection.activeTransform != null ? Selection.activeTransform.position : patrolOwner.transform.position;
                            // 点位不能挂在移动角色下，否则角色移动会带着巡逻目标一起走。
                            LevelLayoutGroup group = patrolOwner.GetComponentInParent<LevelLayoutGroup>(); // lint-ok: 仅点击增加巡逻点时查归属
                            Transform point = CreateObject(patrolOwner.name + "_巡逻点" + property.arraySize, position, group != null ? group.transform : null).transform;
                            data.Update(); int index = property.arraySize; property.arraySize++;
                            property.GetArrayElementAtIndex(index).objectReferenceValue = point;
                            data.ApplyModifiedProperties(); Refresh();
                        }
                    }
                    else EditorGUILayout.HelpBox("请选有巡逻点字段的 TamingActor 或 EncounterSceneView。", MessageType.Warning);
                }
                if (inspector != null && (selectedGroup == null || Selection.activeTransform != selectedGroup.transform))
                {
                    EditorGUILayout.LabelField("选中点位坐标", EditorStyles.boldLabel);
                    inspector.OnInspectorGUI();
                    GameObject selected = Selection.activeGameObject;
                    Component pointComponent = selected.GetComponent<SpawnAnchor>(); // lint-ok: 编辑器选中对象的属性界面，无运行时开销
                    if (pointComponent == null) pointComponent = selected.GetComponent<QuestLocation>(); // lint-ok: 编辑器属性界面
                    if (pointComponent != null)
                    {
                        var data = new SerializedObject(pointComponent);
                        data.Update();
                        SerializedProperty key = data.FindProperty("spawnId");
                        if (key == null) key = data.FindProperty("locationKey");
                        EditorGUILayout.PropertyField(key, new GUIContent("点位 ID"));
                        SerializedProperty radius = data.FindProperty("radius");
                        if (radius != null) EditorGUILayout.PropertyField(radius, new GUIContent("抵达半径"));
                        data.ApplyModifiedProperties();
                    }
                }
            }
            if (!string.IsNullOrEmpty(status)) EditorGUILayout.HelpBox(status, MessageType.Info);
            if (GUILayout.Button("打开任务编辑器")) Game.Editor.Quest.QuestEditorWindow.Open();
            EditorGUILayout.EndScrollView();
        }

        private void DrawGroups(bool readOnly)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("整店 / 分组移动", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(readOnly))
            {
                if (GUILayout.Button("把选中物体设为摆位分组"))
                {
                    GameObject selected = Selection.activeGameObject;
                    if (selected != null && selected.scene == SceneManager.GetActiveScene())
                    {
                        selectedGroup = selected.GetComponent<LevelLayoutGroup>(); // lint-ok: 仅点击建立分组按钮时查找
                        if (selectedGroup == null) selectedGroup = Undo.AddComponent<LevelLayoutGroup>(selected);
                        Refresh();
                    }
                    else status = "请先选择当前关卡中的物体。";
                }
                if (shopWorld != null && GUILayout.Button("关联现有五店建筑和人员（保留当前位置）"))
                {
                    try { InitializeShopGroups(shopWorld); status = "五店已关联。选择店名，拖动整店手柄即可。"; Refresh(); }
                    catch (InvalidOperationException error) { status = error.Message; }
                }
                foreach (LevelLayoutGroup group in groups)
                {
                    if (group == null) continue;
                    if (GUILayout.Button(group.DisplayName, selectedGroup == group ? EditorStyles.miniButton : EditorStyles.miniButtonMid))
                    {
                        selectedGroup = group;
                        Selection.activeGameObject = group.gameObject;
                        ToolManager.SetActiveTool<LevelGroupMoveTool>();
                        if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.FrameSelected();
                    }
                }
                if (selectedGroup == null || selectedGroup.gameObject.scene != SceneManager.GetActiveScene()) return;
                EditorGUILayout.HelpBox("整店手柄同时移动建筑、人员和点位。W 切回普通移动工具后仅移动选中物体；需要整店移动请再次点店名。", MessageType.Info);
                if (GUILayout.Button("退出店铺编辑（新增点位不归店）"))
                {
                    selectedGroup = null;
                    UnityEditor.Tools.current = Tool.Move;
                    return;
                }
                EditorGUI.BeginChangeCheck();
                Vector3 position = EditorGUILayout.Vector3Field("整组世界位置", selectedGroup.transform.position);
                if (EditorGUI.EndChangeCheck())
                    try { LevelGroupMoveTool.Move(selectedGroup, position); }
                    catch (InvalidOperationException error) { status = error.Message; }
                groupPointsFoldout = EditorGUILayout.Foldout(groupPointsFoldout, "人员 / 点位（点击后单独移动）", true);
                if (groupPointsFoldout)
                {
                    for (int i = 0; i < points.Count; i++)
                    {
                        Transform point = points[i];
                        if (point == null) continue;
                        bool belongs = point.IsChildOf(selectedGroup.transform);
                        foreach (Transform member in selectedGroup.LinkedMembers)
                            if (member != null && point.IsChildOf(member)) belongs = true;
                        if (!belongs || !GUILayout.Button(labels[i], EditorStyles.miniButton)) continue;
                        Selection.activeGameObject = point.gameObject;
                        UnityEditor.Tools.current = Tool.Move;
                        if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.FrameSelected();
                    }
                }
                membersFoldout = EditorGUILayout.Foldout(membersFoldout, "关联对象（外部建筑和点位）", true);
                if (membersFoldout)
                {
                    var data = new SerializedObject(selectedGroup);
                    data.Update();
                    EditorGUILayout.PropertyField(data.FindProperty("displayName"), new GUIContent("分组名称"));
                    EditorGUILayout.PropertyField(data.FindProperty("linkedMembers"), new GUIContent("关联对象"), true);
                    data.ApplyModifiedProperties();
                }
                try
                {
                    List<Transform> roots = LevelGroupMoveTool.MovingRoots(selectedGroup);
                    EditorGUILayout.LabelField("整体移动范围：" + roots.Count + " 个根对象（含全部子物体）");
                    foreach (Transform root in roots) EditorGUILayout.LabelField("· " + root.name, EditorStyles.miniLabel);
                }
                catch (InvalidOperationException error) { EditorGUILayout.HelpBox(error.Message, MessageType.Warning); }
            }
        }

        public static void InitializeShopGroups(ShopPlaytestWorld world)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || world == null || !world.gameObject.scene.IsValid())
                throw new InvalidOperationException("请退出播放，并打开包含五店的关卡。");
            // 最新草图：中层金 / 酒 / 客，下层衣 / 戏；不沿用最初截图的店名分配。
            string[] buildingNames = { "桌子.008", "尖叶.1868", "立方体.310", "立方体.339", "立方体.261" };
            var shopRoots = new Transform[5];
            var buildings = new Transform[5];
            foreach (GameObject root in world.gameObject.scene.GetRootGameObjects())
            {
                if (root.name != "Environment_FiveShopsPlaceholder") continue;
                foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
                    for (int i = 0; i < buildingNames.Length; i++)
                        if (candidate.name == buildingNames[i])
                        {
                            if (buildings[i] != null) throw new InvalidOperationException("建筑名称重复，不能自动确定归属：" + buildingNames[i]);
                            buildings[i] = candidate;
                        }
            }
            for (int i = 0; i < shopRoots.Length; i++)
            {
                Transform station = world.Station(i);
                if (station == null) throw new InvalidOperationException("缺少店铺交互点：" + ShopNames[i]);
                Transform root = station;
                while (root.parent != null && root.parent != world.transform) root = root.parent;
                if (root.parent != world.transform) throw new InvalidOperationException("店铺根不在现有五店分组下：" + ShopNames[i]);
                shopRoots[i] = root;
                if (buildings[i] == null) throw new InvalidOperationException("没有找到已确认的建筑：" + buildingNames[i] + "；请手动建立分组关联。");
            }
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("关联五店整体摆位");
            try
            {
                for (int i = 0; i < shopRoots.Length; i++)
                {
                    LevelLayoutGroup group = shopRoots[i].GetComponent<LevelLayoutGroup>();
                    if (group == null) group = Undo.AddComponent<LevelLayoutGroup>(shopRoots[i].gameObject);
                    var members = new List<Transform>();
                    foreach (Transform member in group.LinkedMembers)
                        if (member != null && Array.IndexOf(buildings, member) < 0 && !members.Contains(member)) members.Add(member);
                    if (!members.Contains(buildings[i])) members.Add(buildings[i]);
                    foreach (ShopPlaytestActor actor in world.Actors)
                    {
                        if (actor == null || actor.Shop != i) continue;
                        if (!actor.transform.IsChildOf(shopRoots[i]) && !members.Contains(actor.transform)) members.Add(actor.transform);
                        TamingActor tame = actor.GetComponent<TamingActor>();
                        if (tame == null) continue;
                        SerializedProperty patrol = new SerializedObject(tame).FindProperty("patrolPoints");
                        for (int n = 0; n < patrol.arraySize; n++)
                        {
                            Transform point = patrol.GetArrayElementAtIndex(n).objectReferenceValue as Transform;
                            if (point != null && !point.IsChildOf(shopRoots[i]) && !members.Contains(point)) members.Add(point);
                        }
                    }
                    var data = new SerializedObject(group);
                    data.FindProperty("displayName").stringValue = ShopNames[i];
                    SerializedProperty links = data.FindProperty("linkedMembers");
                    links.arraySize = members.Count;
                    for (int n = 0; n < members.Count; n++) links.GetArrayElementAtIndex(n).objectReferenceValue = members[n];
                    data.ApplyModifiedProperties();
                }
                foreach (Transform root in shopRoots) LevelGroupMoveTool.MovingRoots(root.GetComponent<LevelLayoutGroup>());
                EditorSceneManager.MarkSceneDirty(world.gameObject.scene);
                Undo.CollapseUndoOperations(undoGroup);
            }
            catch
            {
                Undo.RevertAllDownToGroup(undoGroup);
                throw;
            }
        }

        private void DrawScene(SceneView view)
        {
            if (showLabels)
                for (int i = 0; i < points.Count; i++)
                    if (points[i] != null) Handles.Label(points[i].position, labels[i]);
            Color oldColor = Handles.color;
            Handles.color = Color.cyan;
            if (route != null)
                for (int i = 0; i < route.childCount; i++)
                {
                    Transform point = route.GetChild(i);
                    Handles.Label(point.position, "动线 " + (i + 1));
                    if (i > 0) Handles.DrawLine(route.GetChild(i - 1).position, point.position);
                }
            Handles.color = oldColor;
            if (patrolOwner != null && patrolOwner.gameObject.scene == SceneManager.GetActiveScene())
            {
                SerializedProperty patrol = new SerializedObject(patrolOwner).FindProperty("patrolPoints");
                Transform previous = null;
                if (patrol != null)
                    for (int i = 0; i < patrol.arraySize; i++)
                    {
                        Transform point = patrol.GetArrayElementAtIndex(i).objectReferenceValue as Transform;
                        if (point == null) { previous = null; continue; }
                        Handles.color = Color.yellow;
                        Handles.Label(point.position, "巡逻 " + (i + 1));
                        if (previous != null) Handles.DrawLine(previous.position, point.position);
                        previous = point;
                    }
                Handles.color = oldColor;
            }
            if (!placeOnClick || EditorApplication.isPlayingOrWillChangePlaymode) return;
            Event e = Event.current;
            if (e.keyCode == KeyCode.Escape && e.type == EventType.KeyDown) { placeOnClick = false; e.Use(); Repaint(); return; }
            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            if (e.type != EventType.MouseDown || e.button != 0 || e.alt) return;
            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            if (!Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, ~0, QueryTriggerInteraction.Ignore))
            { status = "没有命中带碰撞体的地面，请先为地面添加 Collider。"; Repaint(); return; }
            if (hit.collider.gameObject.scene != SceneManager.GetActiveScene())
            { status = "命中其他场景，请先把目标关卡设为活动场景。"; Repaint(); return; }
            string key = pointKey.Trim();
            if (key.Length == 0) { status = "请填写 ID / 名称。"; Repaint(); return; }
            if (createKind == 0)
            {
                var scene = SceneManager.GetActiveScene();
                bool encounterScene = false;
                bool worldScene = false;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    if (root.GetComponentInChildren<SpawnAnchor>(true) != null) worldScene = true;
                    foreach (MonoBehaviour component in root.GetComponentsInChildren<MonoBehaviour>(true))
                        if (component != null && component.GetType().FullName == "Game.Monster.EncounterSceneView") encounterScene = true;
                }
                // 世界驱动按 SpawnAnchor 识别场景，不能把遭遇原型误变成世界场景。
                if (encounterScene && !worldScene)
                { status = "遭遇场景请调整列表中的【遭遇出生】点；新增世界出生组件会改变运行驱动，已阻止。"; Repaint(); return; }
            }
            if (createKind == 2 && (route == null || route.gameObject.scene != SceneManager.GetActiveScene() || !route.CompareTag("EditorOnly") || !route.name.StartsWith(RoutePrefix, StringComparison.Ordinal)))
            { status = "请先选择本场景的测试动线。"; Repaint(); return; }
            if (createKind != 2)
                foreach (Transform point in points)
                {
                    if (point == null) continue;
                    SpawnAnchor spawn = point.GetComponent<SpawnAnchor>();
                    QuestLocation location = point.GetComponent<QuestLocation>();
                    if ((createKind == 0 && spawn != null && spawn.SpawnId == key) || (createKind == 1 && location != null && location.Key == key))
                    { status = "ID 已存在，请选择已有点位或换一个 ID。"; Repaint(); return; }
                }
            Transform owner = selectedGroup != null && selectedGroup.gameObject.scene == SceneManager.GetActiveScene() ? selectedGroup.transform : null;
            GameObject created = CreateObject(createKind == 0 ? "Spawn_" + key : key, hit.point, createKind == 2 ? route : owner);
            if (createKind != 2)
            {
                Component component = createKind == 0 ? (Component)Undo.AddComponent<SpawnAnchor>(created) : Undo.AddComponent<QuestLocation>(created);
                var data = new SerializedObject(component);
                data.FindProperty(createKind == 0 ? "spawnId" : "locationKey").stringValue = key;
                data.ApplyModifiedProperties();
            }
            Selection.activeGameObject = created;
            placeOnClick = false; status = "已创建；调整后保存当前场景。"; e.Use(); Refresh();
        }

        private static GameObject CreateObject(string name, Vector3 position, Transform parent)
        {
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, SceneManager.GetActiveScene());
            Undo.RegisterCreatedObjectUndo(go, "创建关卡点位");
            if (parent != null) Undo.SetTransformParent(go.transform, parent, "加入测试动线");
            go.transform.position = position;
            EditorSceneManager.MarkSceneDirty(go.scene);
            return go;
        }

        public static float RouteLength(Transform root)
        {
            if (root == null) return 0;
            float length = 0;
            for (int i = 1; i < root.childCount; i++) length += Vector3.Distance(root.GetChild(i - 1).position, root.GetChild(i).position);
            return length;
        }
    }
}
