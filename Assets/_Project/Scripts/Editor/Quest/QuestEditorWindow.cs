// 职责：菜单「21Days/策划/任务编辑器」——策划编排主线 / 支线、配置任务与目标、一键校验 / 保存 / 生成的 IMGUI 窗口。
//   窗口只做「画界面 + 把点击变成对草稿的修改」：读写 JSON 在 QuestTableFile，校验在 QuestTableValidator，
//   主线链推导与 ▲▼ 重写前置在 QuestMainChain，下拉选项在 QuestKeyOptions。窗口自己不判任何规则，
//   保证「编辑器里绿 = 校验菜单绿 = 生成闸口放行」是同一套逻辑。
//
// 为什么新建（project-root.md「加能力的顺序」）：
//   1. 复用不行：现有窗口 ReplayWindow（运行期回放控制）、AssetAuditWindow（只读资产体检）都不编辑数据表，
//      没有「选中一条记录 → 改字段 → 写回源文件」的形态。
//   2. 扩展不行：塞进 AssetAuditWindow / GenerateTablesMenu 名实不符（一个是体检、一个是起生成进程），
//      且任务编排的两栏布局与它们毫无共用之处。
//
// 已知限制：草稿不是 Unity 可序列化对象，脚本重编译（域重载）会丢掉未保存改动并从磁盘重新载入——
//   域重载不走 OnDestroy，也就没有保存提示。改完及时点「保存」。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Game.Quest;
using UnityEditor;
using UnityEngine;

namespace Game.Editor.Quest
{
    /// <summary>
    /// 任务编辑器窗口。左栏主线链（▲▼ 调序 = 重写主线前置）与按解锁时机分组的支线；右栏选中任务的详情；
    /// 底部最近一次校验结果（单击跳到对应任务）。播放模式下只读。
    /// </summary>
    public sealed class QuestEditorWindow : EditorWindow
    {
        private const string LogPrefix = "[任务编辑器]";
        private const string WindowTitle = "任务编辑器";
        private const float LeftWidth = 300f;
        private const float BottomHeight = 140f;
        private const float SmallButtonWidth = 22f;
        private const float DotWidth = 14f;
        private const float DescriptionMinHeight = 48f;
        private const int MaxDialogLines = 5;

        private const int MainIdMin = 1000;
        private const int MainIdMax = 1999;
        private const int SideIdMin = 2000;
        private const int SideIdMax = 2999;

        private static readonly QuestKind[] KindValues = { QuestKind.Main, QuestKind.Side };
        private static readonly string[] KindLabels = { "主线", "支线" };

        private static readonly QuestObjectiveKind[] ObjectiveKindValues =
        {
            QuestObjectiveKind.TalkTo, QuestObjectiveKind.ReachLocation, QuestObjectiveKind.Counter
        };

        private static readonly string[] ObjectiveKindLabels = { "与某人对话", "到达地点", "计数" };

        private static readonly Color ErrorColor = new Color(0.95f, 0.32f, 0.3f);
        private static readonly Color WarningColor = new Color(0.95f, 0.75f, 0.2f);
        private static readonly Color SelectedColor = new Color(0.24f, 0.49f, 0.9f, 0.35f);

        // ---------- 数据（工作副本） ----------

        /// <summary>从磁盘读出的草稿；所有编辑直接改它们，「重新载入」整份丢弃重读。</summary>
        private readonly List<QuestDraft> drafts = new List<QuestDraft>();

        /// <summary>已从列表删掉、等「保存」时再删文件的草稿。</summary>
        private readonly List<QuestDraft> pendingDeletes = new List<QuestDraft>();

        /// <summary>改过、保存时要写回的草稿（按引用）。</summary>
        private readonly HashSet<QuestDraft> dirtyDrafts = new HashSet<QuestDraft>();

        /// <summary>载入时读坏的文件（带文件名的原因）。这些任务不在 <see cref="drafts"/> 里。</summary>
        private readonly List<string> loadProblems = new List<string>();

        private QuestValidationContext context;
        private QuestKeyOptions options = QuestKeyOptions.Empty;

        // ---------- 最近一次校验 ----------

        private readonly List<QuestIssue> issues = new List<QuestIssue>();
        private readonly Dictionary<int, QuestIssueSeverity> worstSeverity = new Dictionary<int, QuestIssueSeverity>();
        private string[] issueLabels = Array.Empty<string>();
        private string[] loadProblemLabels = Array.Empty<string>();
        private bool validated;
        private int errorCount;
        private int warningCount;

        // ---------- 左栏结构缓存（改动后在下一个 Layout 事件重建，不每帧算） ----------

        private readonly List<QuestDraft> sortedDrafts = new List<QuestDraft>();
        private readonly List<QuestDraft> chain = new List<QuestDraft>();
        private readonly List<QuestDraft> unchainedMains = new List<QuestDraft>();
        private readonly List<SideGroup> sideGroups = new List<SideGroup>();
        private readonly Dictionary<QuestDraft, string> rowLabels = new Dictionary<QuestDraft, string>();
        private readonly Dictionary<QuestDraft, string> prerequisiteLabels = new Dictionary<QuestDraft, string>();
        private bool chainIsLinear = true;
        private string chainReason = string.Empty;
        private bool structureStale = true;
        private string statusText = string.Empty;

        // ---------- 界面状态 ----------

        private QuestDraft selected;
        private readonly HashSet<QuestObjectiveDraft> manualKeys = new HashSet<QuestObjectiveDraft>();
        private readonly HashSet<QuestObjectiveDraft> manualLocations = new HashSet<QuestObjectiveDraft>();
        private bool prerequisitesFoldout = true;
        private bool discardRequested;
        private Vector2 leftScroll;
        private Vector2 rightScroll;
        private Vector2 bottomScroll;

        /// <summary>
        /// 改变列表结构的操作（选中、调序、增删）推迟到 OnGUI 末尾执行：
        /// 同一事件里前半截按旧结构画、后半截按新结构画会让 IMGUI 的布局对不上。
        /// </summary>
        private Action pendingAction;

        private GUIStyle rowStyle;
        private GUIStyle descriptionStyle;
        private GUIStyle chainWarningStyle;

        private bool IsDirty => dirtyDrafts.Count > 0 || pendingDeletes.Count > 0;

        [MenuItem("21Days/策划/任务编辑器", false, 400)]
        public static void Open()
        {
            var window = GetWindow<QuestEditorWindow>();
            window.titleContent = new GUIContent(WindowTitle);
            window.minSize = new Vector2(900f, 560f);
            window.Show();
        }

        private void OnEnable()
        {
            titleContent = new GUIContent(WindowTitle);
            saveChangesMessage = "任务表有未保存的改动。";
            EditorApplication.playModeStateChanged += HandlePlayModeChanged;
            Reload();
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= HandlePlayModeChanged;
        }

        /// <summary>
        /// 正常关窗由 <see cref="EditorWindow.hasUnsavedChanges"/> 弹「保存 / 取消 / 放弃」三选（能取消关闭）。
        /// 这里是兜底：别的途径（如布局切换）关掉窗口时仍有改动，退而弹「保存 / 放弃」两选。
        /// </summary>
        private void OnDestroy()
        {
            if (!IsDirty || discardRequested) return;

            if (EditorUtility.DisplayDialog(WindowTitle, "任务表有未保存的改动，关窗前要保存吗？", "保存", "放弃"))
            {
                SaveAll();
            }
        }

        public override void SaveChanges()
        {
            if (SaveAll())
            {
                base.SaveChanges();
            }
        }

        public override void DiscardChanges()
        {
            discardRequested = true;
            base.DiscardChanges();
        }

        private void HandlePlayModeChanged(PlayModeStateChange change)
        {
            Repaint();
        }

        // ================= 绘制 =================

        private void OnGUI()
        {
            EnsureStyles();
            if (structureStale && Event.current.type == EventType.Layout)
            {
                RebuildStructure();
            }

            bool readOnly = EditorApplication.isPlayingOrWillChangePlaymode;
            DrawToolbar(readOnly);
            if (readOnly)
            {
                EditorGUILayout.HelpBox(
                    "播放中，窗口只读：任务表改动要退出播放、保存并生成后，下次进 Play 才生效。可以浏览与校验。",
                    MessageType.Info);
            }

            EditorGUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));
            DrawLeftPanel(readOnly);
            DrawRightPanel(readOnly);
            EditorGUILayout.EndHorizontal();

            DrawBottomPanel();

            RunPendingAction();
        }

        private void EnsureStyles()
        {
            if (rowStyle != null) return;

            rowStyle = new GUIStyle(EditorStyles.label) { clipping = TextClipping.Clip };
            descriptionStyle = new GUIStyle(EditorStyles.textArea) { wordWrap = true };
            chainWarningStyle = new GUIStyle(EditorStyles.wordWrappedMiniLabel);
            chainWarningStyle.normal.textColor = WarningColor;
        }

        private void DrawToolbar(bool readOnly)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            using (new EditorGUI.DisabledScope(readOnly))
            {
                if (GUILayout.Button("重新载入", EditorStyles.toolbarButton)) Defer(ReloadWithConfirm);
                if (GUILayout.Button("新建主线", EditorStyles.toolbarButton)) Defer(() => CreateQuest(QuestKind.Main));
                if (GUILayout.Button("新建支线", EditorStyles.toolbarButton)) Defer(() => CreateQuest(QuestKind.Side));
                if (GUILayout.Button("保存", EditorStyles.toolbarButton)) Defer(() => SaveAll());
                if (GUILayout.Button("保存并生成", EditorStyles.toolbarButton)) Defer(SaveAndGenerate);
            }

            if (GUILayout.Button("校验", EditorStyles.toolbarButton)) Defer(RunValidation);

            GUILayout.FlexibleSpace();
            GUILayout.Label(statusText, EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawLeftPanel(bool readOnly)
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(LeftWidth));
            leftScroll = EditorGUILayout.BeginScrollView(leftScroll);

            EditorGUILayout.LabelField("主线（按顺序）", EditorStyles.boldLabel);
            if (chainIsLinear)
            {
                if (chain.Count == 0) EditorGUILayout.LabelField("（没有主线）", EditorStyles.miniLabel);
                for (int i = 0; i < chain.Count; i++)
                {
                    DrawRow(chain[i], i, readOnly);
                }
            }
            else
            {
                EditorGUILayout.LabelField("主线前置不是一条链，请在右侧手动整理前置", chainWarningStyle);
                EditorGUILayout.LabelField(chainReason, chainWarningStyle);
                for (int i = 0; i < unchainedMains.Count; i++)
                {
                    DrawRow(unchainedMains[i], -1, readOnly);
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("支线", EditorStyles.boldLabel);
            if (sideGroups.Count == 0) EditorGUILayout.LabelField("（没有支线）", EditorStyles.miniLabel);
            for (int g = 0; g < sideGroups.Count; g++)
            {
                SideGroup group = sideGroups[g];
                EditorGUILayout.LabelField(group.Label, EditorStyles.miniBoldLabel);
                for (int i = 0; i < group.Members.Count; i++)
                {
                    DrawRow(group.Members[i], -1, readOnly);
                }
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        /// <param name="chainIndex">主线链下标；-1 = 不画 ▲▼。</param>
        private void DrawRow(QuestDraft draft, int chainIndex, bool readOnly)
        {
            Rect row = EditorGUILayout.BeginHorizontal();
            if (Event.current.type == EventType.Repaint && ReferenceEquals(draft, selected))
            {
                EditorGUI.DrawRect(row, SelectedColor);
            }

            DrawDot(draft.Id);
            if (!rowLabels.TryGetValue(draft, out string label)) label = draft.Id.ToString(CultureInfo.InvariantCulture);
            if (GUILayout.Button(label, rowStyle, GUILayout.ExpandWidth(true)))
            {
                Defer(() => Select(draft));
            }

            if (chainIndex >= 0)
            {
                using (new EditorGUI.DisabledScope(readOnly || chainIndex == 0))
                {
                    if (GUILayout.Button("▲", EditorStyles.miniButtonLeft, GUILayout.Width(SmallButtonWidth)))
                    {
                        Defer(() => MoveMain(chainIndex, -1));
                    }
                }

                using (new EditorGUI.DisabledScope(readOnly || chainIndex == chain.Count - 1))
                {
                    if (GUILayout.Button("▼", EditorStyles.miniButtonRight, GUILayout.Width(SmallButtonWidth)))
                    {
                        Defer(() => MoveMain(chainIndex, 1));
                    }
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        /// <summary>行首圆点：最近一次校验里这条任务最严重的等级（红 = 错误，黄 = 警告，没有就留空位对齐）。</summary>
        private void DrawDot(int questId)
        {
            if (!worstSeverity.TryGetValue(questId, out QuestIssueSeverity severity))
            {
                GUILayout.Space(DotWidth + 4f);
                return;
            }

            Color previous = GUI.color;
            GUI.color = severity == QuestIssueSeverity.Error ? ErrorColor : WarningColor;
            GUILayout.Label("●", GUILayout.Width(DotWidth));
            GUI.color = previous;
        }

        private void DrawRightPanel(bool readOnly)
        {
            EditorGUILayout.BeginVertical();
            rightScroll = EditorGUILayout.BeginScrollView(rightScroll);

            if (selected == null)
            {
                EditorGUILayout.HelpBox("在左栏选一条任务，或点工具栏「新建主线」「新建支线」。", MessageType.None);
            }
            else
            {
                using (new EditorGUI.DisabledScope(readOnly))
                {
                    DrawDetail(selected);
                }
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawDetail(QuestDraft draft)
        {
            // 编号用 Delayed：回车 / 失焦才提交，避免敲「1003」途中把别的任务的前置改成 1、10、100。
            int newId = EditorGUILayout.DelayedIntField("编号", draft.Id);
            if (newId != draft.Id) ChangeId(draft, newId);

            int kindIndex = Array.IndexOf(KindValues, draft.Kind);
            int pickedKind = EditorGUILayout.Popup("类型", kindIndex, KindLabels);
            if (pickedKind != kindIndex && pickedKind >= 0)
            {
                draft.Kind = KindValues[pickedKind];
                MarkDirty(draft, true);
            }

            EditorGUI.BeginChangeCheck();
            string title = EditorGUILayout.TextField("标题", draft.Title ?? string.Empty);
            if (EditorGUI.EndChangeCheck())
            {
                draft.Title = title;
                MarkDirty(draft, true);
            }

            EditorGUILayout.LabelField("描述");
            EditorGUI.BeginChangeCheck();
            string description = EditorGUILayout.TextArea(draft.Description ?? string.Empty, descriptionStyle,
                GUILayout.MinHeight(DescriptionMinHeight));
            if (EditorGUI.EndChangeCheck())
            {
                draft.Description = description;
                MarkDirty(draft, false);
            }

            DrawIssues(draft.Id, -1);

            EditorGUILayout.Space();
            DrawPrerequisites(draft);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("目标", EditorStyles.boldLabel);
            if (draft.Objectives == null) draft.Objectives = new List<QuestObjectiveDraft>();
            for (int i = 0; i < draft.Objectives.Count; i++)
            {
                DrawObjectiveCard(draft, i);
            }

            if (GUILayout.Button("+ 添加目标")) Defer(() => AddObjective(draft));

            EditorGUILayout.Space();
            EditorGUILayout.Space();
            Color previous = GUI.backgroundColor;
            GUI.backgroundColor = ErrorColor;
            if (GUILayout.Button("删除这条任务")) Defer(() => DeleteQuest(draft));
            GUI.backgroundColor = previous;
        }

        private void DrawPrerequisites(QuestDraft draft)
        {
            prerequisitesFoldout = EditorGUILayout.Foldout(prerequisitesFoldout, "前置任务", true);
            if (!prerequisitesFoldout) return;

            if (draft.Prerequisites == null) draft.Prerequisites = new List<int>();
            EditorGUI.indentLevel++;
            if (draft.Kind == QuestKind.Main)
            {
                EditorGUILayout.LabelField("主线顺序建议用左栏 ▲▼ 调整", EditorStyles.miniLabel);
            }

            if (sortedDrafts.Count <= 1) EditorGUILayout.LabelField("（没有其它任务）", EditorStyles.miniLabel);
            for (int i = 0; i < sortedDrafts.Count; i++)
            {
                QuestDraft other = sortedDrafts[i];
                if (ReferenceEquals(other, draft)) continue;

                bool has = draft.Prerequisites.Contains(other.Id);
                if (!prerequisiteLabels.TryGetValue(other, out string label)) label = other.Id.ToString(CultureInfo.InvariantCulture);
                bool now = EditorGUILayout.ToggleLeft(label, has);
                if (now == has) continue;

                if (now) draft.Prerequisites.Add(other.Id);
                else draft.Prerequisites.RemoveAll(id => id == other.Id);
                MarkDirty(draft, true);
            }

            // 指向不存在的任务（或自己）的前置没有对应的勾选框，单独列出来给个「移除」。
            for (int p = 0; p < draft.Prerequisites.Count; p++)
            {
                int id = draft.Prerequisites[p];
                if (id != draft.Id && FindById(id) != null) continue;

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(id == draft.Id ? $"{id}（是自己）" : $"{id}（不存在）");
                if (GUILayout.Button("移除", GUILayout.Width(48f)))
                {
                    Defer(() => RemovePrerequisite(draft, id));
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUI.indentLevel--;
        }

        private void DrawObjectiveCard(QuestDraft draft, int index)
        {
            QuestObjectiveDraft objective = draft.Objectives[index];
            if (objective == null)
            {
                objective = new QuestObjectiveDraft();
                draft.Objectives[index] = objective;
                MarkDirty(draft, false);
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"第 {index + 1} 个目标", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(index == 0))
            {
                if (GUILayout.Button("▲", EditorStyles.miniButtonLeft, GUILayout.Width(SmallButtonWidth)))
                {
                    Defer(() => MoveObjective(draft, index, -1));
                }
            }

            using (new EditorGUI.DisabledScope(index == draft.Objectives.Count - 1))
            {
                if (GUILayout.Button("▼", EditorStyles.miniButtonMid, GUILayout.Width(SmallButtonWidth)))
                {
                    Defer(() => MoveObjective(draft, index, 1));
                }
            }

            if (GUILayout.Button("✕", EditorStyles.miniButtonRight, GUILayout.Width(SmallButtonWidth)))
            {
                Defer(() => RemoveObjective(draft, index));
            }

            EditorGUILayout.EndHorizontal();

            EditorGUI.BeginChangeCheck();
            string text = EditorGUILayout.TextField("文本", objective.Text ?? string.Empty);
            if (EditorGUI.EndChangeCheck())
            {
                objective.Text = text;
                MarkDirty(draft, false);
            }

            int kindIndex = Array.IndexOf(ObjectiveKindValues, objective.Kind);
            int pickedKind = EditorGUILayout.Popup("类型", kindIndex, ObjectiveKindLabels);
            if (pickedKind != kindIndex && pickedKind >= 0)
            {
                objective.Kind = ObjectiveKindValues[pickedKind];
                // 换了类型，旧键多半不再合法：手动输入状态交给下一帧按新列表重新判断。
                manualKeys.Remove(objective);
                MarkDirty(draft, false);
            }

            string key = DrawChoiceField(KeyLabel(objective.Kind), objective.Key, options.ForKind(objective.Kind), manualKeys, objective, out bool keyChanged);
            if (keyChanged)
            {
                objective.Key = key;
                MarkDirty(draft, false);
            }

            EditorGUI.BeginChangeCheck();
            int count = EditorGUILayout.IntField("次数", objective.Count);
            if (EditorGUI.EndChangeCheck())
            {
                objective.Count = Math.Max(1, count);
                MarkDirty(draft, false);
            }

            string location = DrawChoiceField("指引地点", objective.Location, options.GuideLocations, manualLocations, objective, out bool locationChanged);
            if (locationChanged)
            {
                objective.Location = location;
                MarkDirty(draft, false);
            }

            DrawIssues(draft.Id, index);
            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// 下拉 + 手动输入。当前值在列表里 → 选中那项；不在列表里（且非空）或选了「手动输入…」→ 下拉停在末项并多一个输入框，原值保留。
        /// </summary>
        private static string DrawChoiceField(string label, string value, QuestKeyOptions.Choices choices,
            HashSet<QuestObjectiveDraft> manualSet, QuestObjectiveDraft objective, out bool changed)
        {
            changed = false;
            value = value ?? string.Empty;
            int found = choices.IndexOf(value);
            if (found < 0 && value.Length > 0) manualSet.Add(objective);

            bool manual = manualSet.Contains(objective);
            int shown = manual ? choices.ManualIndex : found;
            int picked = EditorGUILayout.Popup(label, shown, choices.Labels);
            if (picked != shown)
            {
                if (picked == choices.ManualIndex)
                {
                    manualSet.Add(objective);
                    manual = true;
                }
                else if (picked >= 0)
                {
                    manualSet.Remove(objective);
                    changed = true;
                    return choices.ValueAt(picked) ?? string.Empty;
                }
            }

            if (!manual) return value;

            EditorGUI.indentLevel++;
            EditorGUI.BeginChangeCheck();
            string typed = EditorGUILayout.TextField("手动输入", value);
            if (EditorGUI.EndChangeCheck())
            {
                changed = true;
                value = typed;
            }

            EditorGUI.indentLevel--;
            return value;
        }

        private static string KeyLabel(QuestObjectiveKind kind)
        {
            switch (kind)
            {
                case QuestObjectiveKind.TalkTo: return "对话编号";
                case QuestObjectiveKind.ReachLocation: return "地点键";
                default: return "计数键";
            }
        }

        /// <summary>就地显示某任务（objectiveIndex = -1）或某目标的校验消息。</summary>
        private void DrawIssues(int questId, int objectiveIndex)
        {
            for (int i = 0; i < issues.Count; i++)
            {
                QuestIssue issue = issues[i];
                if (issue.QuestId != questId || issue.ObjectiveIndex != objectiveIndex) continue;

                EditorGUILayout.HelpBox(issue.Message,
                    issue.Severity == QuestIssueSeverity.Error ? MessageType.Error : MessageType.Warning);
            }
        }

        private void DrawBottomPanel()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Height(BottomHeight));
            EditorGUILayout.LabelField(validated ? "校验结果（单击跳到对应任务）" : "校验结果（还没校验）", EditorStyles.boldLabel);
            bottomScroll = EditorGUILayout.BeginScrollView(bottomScroll);

            Color previous = GUI.color;
            GUI.color = ErrorColor;
            for (int i = 0; i < loadProblemLabels.Length; i++)
            {
                GUILayout.Label(loadProblemLabels[i], rowStyle);
            }

            GUI.color = previous;

            if (validated && issues.Count == 0 && loadProblemLabels.Length == 0)
            {
                GUILayout.Label("没有问题。", rowStyle);
            }

            for (int i = 0; i < issues.Count; i++)
            {
                QuestIssue issue = issues[i];
                GUI.color = issue.Severity == QuestIssueSeverity.Error ? ErrorColor : WarningColor;
                if (issue.QuestId == 0)
                {
                    GUILayout.Label(issueLabels[i], rowStyle);
                }
                else if (GUILayout.Button(issueLabels[i], rowStyle))
                {
                    int questId = issue.QuestId;
                    Defer(() => Select(FindById(questId)));
                }

                GUI.color = previous;
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        // ================= 操作 =================

        private void Defer(Action action)
        {
            pendingAction = action;
        }

        private void RunPendingAction()
        {
            if (pendingAction == null) return;

            Action action = pendingAction;
            pendingAction = null;
            action();
            Repaint();
        }

        private void Select(QuestDraft draft)
        {
            // 清掉输入框焦点：否则正在编辑的框会把上一条任务的文字带到新选中的任务上。
            GUI.FocusControl(null);
            selected = draft;
        }

        private QuestDraft FindById(int id)
        {
            for (int i = 0; i < drafts.Count; i++)
            {
                if (drafts[i].Id == id) return drafts[i];
            }

            return null;
        }

        private void MarkDirty(QuestDraft draft, bool structureChanged)
        {
            dirtyDrafts.Add(draft);
            if (structureChanged) structureStale = true;
            RefreshStatus();
        }

        /// <summary>改编号：其它任务前置里的旧编号跟着改（旧编号没被别的任务占着时）。保存时由 QuestTableFile.Save 搬文件。</summary>
        private void ChangeId(QuestDraft draft, int newId)
        {
            int oldId = draft.Id;
            draft.Id = newId;
            MarkDirty(draft, true);
            if (FindById(oldId) != null) return;

            for (int i = 0; i < drafts.Count; i++)
            {
                QuestDraft other = drafts[i];
                if (ReferenceEquals(other, draft) || other.Prerequisites == null) continue;

                bool touched = false;
                for (int p = 0; p < other.Prerequisites.Count; p++)
                {
                    if (other.Prerequisites[p] != oldId) continue;
                    other.Prerequisites[p] = newId;
                    touched = true;
                }

                if (touched) MarkDirty(other, true);
            }
        }

        private void MoveMain(int index, int delta)
        {
            if (!QuestMainChain.Move(chain, index, delta)) return;

            for (int i = 0; i < chain.Count; i++)
            {
                dirtyDrafts.Add(chain[i]);
            }

            structureStale = true;
            RefreshStatus();
        }

        private void RemovePrerequisite(QuestDraft draft, int id)
        {
            draft.Prerequisites.RemoveAll(p => p == id);
            MarkDirty(draft, true);
        }

        private void AddObjective(QuestDraft draft)
        {
            QuestObjectiveKind kind = draft.Objectives.Count > 0 && draft.Objectives[draft.Objectives.Count - 1] != null
                ? draft.Objectives[draft.Objectives.Count - 1].Kind
                : QuestObjectiveKind.TalkTo;
            draft.Objectives.Add(new QuestObjectiveDraft { Kind = kind, Count = 1 });
            MarkDirty(draft, false);
        }

        private void MoveObjective(QuestDraft draft, int index, int delta)
        {
            int target = index + delta;
            if (index < 0 || target < 0 || index >= draft.Objectives.Count || target >= draft.Objectives.Count) return;

            GUI.FocusControl(null);
            QuestObjectiveDraft moving = draft.Objectives[index];
            draft.Objectives[index] = draft.Objectives[target];
            draft.Objectives[target] = moving;
            MarkDirty(draft, false);
        }

        private void RemoveObjective(QuestDraft draft, int index)
        {
            if (index < 0 || index >= draft.Objectives.Count) return;

            GUI.FocusControl(null);
            QuestObjectiveDraft removed = draft.Objectives[index];
            draft.Objectives.RemoveAt(index);
            if (removed != null)
            {
                manualKeys.Remove(removed);
                manualLocations.Remove(removed);
            }

            MarkDirty(draft, false);
        }

        /// <summary>新建：编号 = 该区间现有最大值 + 1（无则 1001 / 2001）；新主线默认接在当前主线链尾后面，新支线前置为空。</summary>
        private void CreateQuest(QuestKind kind)
        {
            bool isMain = kind == QuestKind.Main;
            int min = isMain ? MainIdMin : SideIdMin;
            int max = isMain ? MainIdMax : SideIdMax;
            int top = 0;
            for (int i = 0; i < drafts.Count; i++)
            {
                int id = drafts[i].Id;
                if (id >= min && id <= max && id > top) top = id;
            }

            var draft = new QuestDraft
            {
                Id = top == 0 ? min + 1 : top + 1,
                Kind = kind
            };

            if (isMain && chainIsLinear && chain.Count > 0)
            {
                draft.Prerequisites.Add(chain[chain.Count - 1].Id);
            }

            draft.Objectives.Add(new QuestObjectiveDraft());
            drafts.Add(draft);
            MarkDirty(draft, true);
            Select(draft);
        }

        /// <summary>确认后从列表删掉（文件等保存时删），并从其它任务的前置里移除它；删的是主线链中段时，下一条改接到上一条后面。</summary>
        private void DeleteQuest(QuestDraft draft)
        {
            string title = string.IsNullOrWhiteSpace(draft.Title) ? "（无标题）" : draft.Title;
            if (!EditorUtility.DisplayDialog("删除任务",
                    $"删除任务 {draft.Id}「{title}」？\n文件在点「保存」时才真正删除；保存前可以「重新载入」反悔。",
                    "删除", "取消"))
            {
                return;
            }

            string relinkNote = string.Empty;
            if (draft.Kind == QuestKind.Main && chainIsLinear)
            {
                int index = chain.IndexOf(draft);
                if (index > 0 && index < chain.Count - 1)
                {
                    QuestDraft previous = chain[index - 1];
                    QuestDraft next = chain[index + 1];
                    if (!next.Prerequisites.Contains(previous.Id)) next.Prerequisites.Add(previous.Id);
                    dirtyDrafts.Add(next);
                    relinkNote = $"\n主线 {next.Id} 已改接到 {previous.Id} 之后，主线链不断。";
                }
            }

            drafts.Remove(draft);
            dirtyDrafts.Remove(draft);
            if (!string.IsNullOrEmpty(draft.SourcePath)) pendingDeletes.Add(draft);

            var affected = new List<string>();
            if (FindById(draft.Id) == null)
            {
                for (int i = 0; i < drafts.Count; i++)
                {
                    QuestDraft other = drafts[i];
                    if (other.Prerequisites == null || other.Prerequisites.RemoveAll(id => id == draft.Id) == 0) continue;
                    dirtyDrafts.Add(other);
                    affected.Add(other.Id.ToString(CultureInfo.InvariantCulture));
                }
            }

            if (ReferenceEquals(selected, draft)) Select(null);
            structureStale = true;
            RefreshStatus();

            if (affected.Count > 0)
            {
                EditorUtility.DisplayDialog("已同步前置",
                    $"已从这些任务的前置里移除 {draft.Id}：{string.Join("、", affected)}。{relinkNote}", "好");
            }
        }

        private void ReloadWithConfirm()
        {
            if (IsDirty && !EditorUtility.DisplayDialog("重新载入",
                    "有未保存的改动，重新载入会丢掉它们。", "丢弃并重新载入", "取消"))
            {
                return;
            }

            GUI.FocusControl(null);
            Reload();
        }

        /// <summary>从磁盘整份重读 + 重新收集上下文 + 校验一次。选中项按编号保留。</summary>
        private void Reload()
        {
            int selectedId = selected == null ? 0 : selected.Id;

            loadProblems.Clear();
            List<QuestDraft> loaded = QuestTableFile.LoadAll(loadProblems);
            drafts.Clear();
            drafts.AddRange(loaded);
            pendingDeletes.Clear();
            dirtyDrafts.Clear();
            manualKeys.Clear();
            manualLocations.Clear();
            discardRequested = false;

            loadProblemLabels = new string[loadProblems.Count];
            for (int i = 0; i < loadProblems.Count; i++)
            {
                loadProblemLabels[i] = "[读文件失败] " + loadProblems[i];
            }

            selected = selectedId == 0 ? null : FindById(selectedId);
            CollectContext();
            RebuildStructure();
            RunValidation();
        }

        /// <summary>扫对话目录 / 场景 / LootConfig（有 IO），并据此重建下拉选项。只在载入、保存后调。</summary>
        private void CollectContext()
        {
            context = QuestTableValidator.CollectContext();
            options = QuestKeyOptions.Build(context);
        }

        private void RunValidation()
        {
            issues.Clear();
            issues.AddRange(QuestTableValidator.Validate(drafts, context));

            issueLabels = new string[issues.Count];
            worstSeverity.Clear();
            errorCount = 0;
            warningCount = 0;
            for (int i = 0; i < issues.Count; i++)
            {
                QuestIssue issue = issues[i];
                issueLabels[i] = issue.ToString();
                if (issue.Severity == QuestIssueSeverity.Error) errorCount++;
                else warningCount++;

                if (issue.QuestId == 0) continue;
                if (!worstSeverity.TryGetValue(issue.QuestId, out QuestIssueSeverity current) || issue.Severity < current)
                {
                    worstSeverity[issue.QuestId] = issue.Severity;
                }
            }

            validated = true;
            RefreshStatus();
        }

        /// <summary>写回全部改过的草稿、删掉待删文件，然后重新收集上下文并校验。全部成功返回 true。</summary>
        private bool SaveAll()
        {
            var failures = new List<string>();
            int deleted = 0;
            int saved = 0;

            // 先删后写：新任务可能正好用上被删任务空出来的编号。
            for (int i = pendingDeletes.Count - 1; i >= 0; i--)
            {
                QuestDraft draft = pendingDeletes[i];
                string error = TryFileOperation(() => QuestTableFile.Delete(draft));
                if (error == null)
                {
                    pendingDeletes.RemoveAt(i);
                    deleted++;
                }
                else
                {
                    failures.Add($"删除 {draft.Id}：{error}");
                }
            }

            for (int i = 0; i < drafts.Count; i++)
            {
                QuestDraft draft = drafts[i];
                if (!dirtyDrafts.Contains(draft)) continue;

                string error = TryFileOperation(() => QuestTableFile.Save(draft));
                if (error == null)
                {
                    dirtyDrafts.Remove(draft);
                    saved++;
                }
                else
                {
                    failures.Add($"保存 {draft.Id}：{error}");
                }
            }

            CollectContext();
            structureStale = true;
            RunValidation();

            if (failures.Count > 0)
            {
                for (int i = 0; i < failures.Count; i++)
                {
                    Debug.LogError($"{LogPrefix} {failures[i]}");
                }

                EditorUtility.DisplayDialog("有文件没保存上", string.Join("\n", failures), "好");
                return false;
            }

            Debug.Log($"{LogPrefix} 已保存 {saved} 条、删除 {deleted} 条。校验：{errorCount} 错 {warningCount} 警。");
            return true;
        }

        /// <returns>成功返回 null；失败返回原因。只接文件层会抛的几类异常，其它照常冒泡。</returns>
        private static string TryFileOperation(Action operation)
        {
            try
            {
                operation();
                return null;
            }
            catch (ArgumentException e)
            {
                return e.Message;
            }
            catch (InvalidOperationException e)
            {
                return e.Message;
            }
            catch (IOException e)
            {
                return e.Message;
            }
            catch (UnauthorizedAccessException e)
            {
                return e.Message;
            }
        }

        /// <summary>先按工作副本校验：有错误（含读坏的文件）拒绝；只有警告问一句。然后保存、调生成闸口。</summary>
        private void SaveAndGenerate()
        {
            RunValidation();

            int errors = errorCount + loadProblems.Count;
            if (errors > 0)
            {
                EditorUtility.DisplayDialog("不能生成",
                    $"任务表有 {errors} 处错误，先修正：\n\n{FirstLines(QuestIssueSeverity.Error, true)}", "好");
                return;
            }

            if (warningCount > 0 && !EditorUtility.DisplayDialog("有警告",
                    $"有 {warningCount} 条警告，仍然生成？\n\n{FirstLines(QuestIssueSeverity.Warning, false)}",
                    "仍然生成", "取消"))
            {
                return;
            }

            if (IsDirty && !SaveAll()) return;

            if (GenerateTablesMenu.TryGenerate(false))
            {
                Debug.Log($"{LogPrefix} 生成完成。进 Boot 场景 Play → 开始 即可验证（Alt+B 切主场景）");
            }
            else
            {
                EditorUtility.DisplayDialog("生成失败", "看 Console 里「[配置表]」开头的错误。", "好");
            }
        }

        /// <summary>对话框里列前 <see cref="MaxDialogLines"/> 条（错误时读坏的文件排最前）。</summary>
        private string FirstLines(QuestIssueSeverity severity, bool includeLoadProblems)
        {
            var sb = new StringBuilder();
            int shown = 0;
            int total = 0;
            if (includeLoadProblems)
            {
                for (int i = 0; i < loadProblemLabels.Length; i++)
                {
                    total++;
                    if (shown < MaxDialogLines)
                    {
                        sb.AppendLine(loadProblemLabels[i]);
                        shown++;
                    }
                }
            }

            for (int i = 0; i < issues.Count; i++)
            {
                if (issues[i].Severity != severity) continue;
                total++;
                if (shown < MaxDialogLines)
                {
                    sb.AppendLine(issueLabels[i]);
                    shown++;
                }
            }

            if (total > shown) sb.Append($"……另有 {total - shown} 条，见窗口底部。");
            return sb.ToString().TrimEnd();
        }

        // ================= 结构缓存 =================

        private void RebuildStructure()
        {
            structureStale = false;

            sortedDrafts.Clear();
            sortedDrafts.AddRange(drafts);
            sortedDrafts.Sort((a, b) => a.Id.CompareTo(b.Id));

            rowLabels.Clear();
            prerequisiteLabels.Clear();
            for (int i = 0; i < sortedDrafts.Count; i++)
            {
                QuestDraft draft = sortedDrafts[i];
                string id = draft.Id.ToString(CultureInfo.InvariantCulture);
                string title = string.IsNullOrWhiteSpace(draft.Title) ? "（无标题）" : draft.Title;
                rowLabels[draft] = $"{id} {title}";
                prerequisiteLabels[draft] = $"{id} {(draft.Kind == QuestKind.Main ? "主线" : "支线")} {title}";
            }

            chainIsLinear = QuestMainChain.TryBuildChain(drafts, chain, out chainReason);
            unchainedMains.Clear();
            if (!chainIsLinear)
            {
                for (int i = 0; i < sortedDrafts.Count; i++)
                {
                    if (sortedDrafts[i].Kind == QuestKind.Main) unchainedMains.Add(sortedDrafts[i]);
                }
            }

            // 支线按解锁时机分组：「开局」最前，其余按组名排序；组内按编号。
            sideGroups.Clear();
            var byLabel = new Dictionary<string, SideGroup>(StringComparer.Ordinal);
            for (int i = 0; i < sortedDrafts.Count; i++)
            {
                QuestDraft draft = sortedDrafts[i];
                if (draft.Kind != QuestKind.Side) continue;

                string label = QuestMainChain.SideUnlockLabel(draft, drafts);
                if (!byLabel.TryGetValue(label, out SideGroup group))
                {
                    group = new SideGroup(label, draft.Prerequisites == null || draft.Prerequisites.Count == 0);
                    byLabel.Add(label, group);
                    sideGroups.Add(group);
                }

                group.Members.Add(draft);
            }

            sideGroups.Sort((a, b) => a.IsStart != b.IsStart
                ? (a.IsStart ? -1 : 1)
                : string.CompareOrdinal(a.Label, b.Label));

            RefreshStatus();
        }

        private void RefreshStatus()
        {
            var sb = new StringBuilder();
            sb.Append("已载入 ").Append(drafts.Count).Append(" 条");
            if (IsDirty) sb.Append(" · 有未保存改动 *");
            if (validated) sb.Append(" · 最近校验：").Append(errorCount).Append(" 错 ").Append(warningCount).Append(" 警");
            statusText = sb.ToString();

            hasUnsavedChanges = IsDirty;
            titleContent = new GUIContent(IsDirty ? WindowTitle + " *" : WindowTitle);
        }

        /// <summary>左栏支线的一个分组。</summary>
        private sealed class SideGroup
        {
            public SideGroup(string label, bool isStart)
            {
                Label = label;
                IsStart = isStart;
            }

            public string Label { get; }

            /// <summary>「开局」组（没有前置），排在最前。</summary>
            public bool IsStart { get; }

            public List<QuestDraft> Members { get; } = new List<QuestDraft>();
        }
    }
}
