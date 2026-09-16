// 职责：把自定义按钮塞进 Unity 主工具栏（Play 按钮那一条），供本目录下的工具栏按钮共用。
// 为什么新建（project-root.md「加能力的顺序」）：
//   1. 复用不行：现有编辑器工具没有一个碰过主工具栏。
//   2. 扩展不行：这套反射注入原本长在 MainSceneShortcut 里，第二个按钮（刷新游戏流程）
//      要用同一套逻辑，留在那儿就得复制一遍四十行反射。抽出来两边都只管自己的按钮。

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Editor
{
    /// <summary>
    /// 主工具栏注入器。
    /// <para>
    /// <b>为什么用反射</b>：Unity 2022.3 没有扩展主工具栏的公开 API（<c>[MainToolbarElement]</c> 是
    /// Unity 6 才有的），只能拿 <c>UnityEditor.Toolbar</c> 这个 internal 类型的根元素往里塞。
    /// 已在 2022.3.62f2 上实测：<c>Toolbar.get</c>、<c>m_Root</c> 与下面三个区域名都在。
    /// </para>
    /// <para>
    /// <b>升级 Unity 时</b>：内部名字变了按钮会直接消失，但不会报错、不会挡住任何事——
    /// 每个按钮都另有菜单项和快捷键。要修就改 <see cref="ToolbarTypeName"/> 与 <see cref="ZoneNames"/>。
    /// </para>
    /// <para>
    /// 自动运行的锚定（harness-authoring.md）：
    /// 执行载体 <see cref="EditorApplication.update"/>；没有人注册按钮时第一行就返回，
    /// 有注册时每帧只做引用比较，工具栏没重建就立刻返回。
    /// 状态锚点＝工具栏上有没有那些按钮。退场条件＝删掉本文件与调用方，主工具栏恢复原样。
    /// </para>
    /// </summary>
    [InitializeOnLoad]
    internal static class MainToolbarInjector
    {
        /// <summary>工具栏的三片区域。左区在 Play 按钮左边，PlayMode 区就是 Play/Pause/Step 那一组。</summary>
        internal enum Zone
        {
            Left,
            PlayMode,
            Right,
        }

        private const string ToolbarTypeName = "UnityEditor.Toolbar";

        private static readonly string[] ZoneNames =
        {
            "ToolbarZoneLeftAlign",
            "ToolbarZonePlayMode",
            "ToolbarZoneRightAlign",
        };

        private static readonly Type ToolbarType = typeof(UnityEditor.Editor).Assembly.GetType(ToolbarTypeName);

        private static readonly List<Entry> Entries = new List<Entry>();

        /// <summary>上一次见到的工具栏实例。域重载、切 Layout 都会让工具栏重建，实例随之变化。</summary>
        private static object lastToolbar;

        static MainToolbarInjector()
        {
            EditorApplication.update += EnsureInjected;
        }

        /// <summary>
        /// 注册一个按钮。<paramref name="factory"/> 只会被调用一次，之后工具栏重建都复用同一个元素。
        /// 调用方在自己的 <c>[InitializeOnLoad]</c> 静态构造里调用本方法即可，顺序无所谓。
        /// </summary>
        internal static void Register(Zone zone, Func<VisualElement> factory, int index = -1)
        {
            if (factory == null)
            {
                return;
            }

            Entries.Add(new Entry { Zone = zone, Factory = factory, Index = index });
        }

        /// <summary>
        /// 建一个工具栏文字按钮。
        /// <para>
        /// 什么时候用文字、什么时候用图标：只有含义**不依赖上下文**的通用符号才配当图标
        /// （环形箭头＝刷新，谁都认得）。像「编译」「主场景」这种，没有对应的通用符号，
        /// 硬套一个 C# 文件图标或层级窗口图标反而让人猜，那就老实写字。
        /// </para>
        /// </summary>
        internal static EditorToolbarButton CreateTextButton(string text, string tooltip, Action onClick)
        {
            EditorToolbarButton button = new EditorToolbarButton(text, onClick);
            button.tooltip = tooltip;
            return button;
        }

        /// <summary>
        /// 建一个工具栏图标按钮。
        /// <para>
        /// 用 <see cref="EditorToolbarButton"/> 而不是普通 <c>Button</c>：普通 Button 在主工具栏里
        /// 继承不到编辑器的深色皮肤，会渲染成一个白底白字的方块（实测如此）。
        /// </para>
        /// <para>
        /// 图标名不带 d_ 前缀：<see cref="EditorGUIUtility.IconContent(string)"/> 会按当前皮肤
        /// 自己挑深色版。取不到图标时退回文字按钮——宁可难看，也不能让按钮凭空消失。
        /// </para>
        /// </summary>
        internal static EditorToolbarButton CreateIconButton(string iconName, string fallbackText, string tooltip, Action onClick)
        {
            Texture2D icon = null;
            GUIContent content = EditorGUIUtility.IconContent(iconName);
            if (content != null)
            {
                icon = content.image as Texture2D;
            }

            EditorToolbarButton button = icon != null
                ? new EditorToolbarButton(icon, onClick)
                : new EditorToolbarButton(fallbackText, onClick);

            button.tooltip = tooltip;
            return button;
        }

        private static void EnsureInjected()
        {
            if (Entries.Count == 0)
            {
                return;
            }

            object toolbar = GetToolbarInstance();
            if (toolbar == null)
            {
                // 工具栏还没建好（编辑器刚启动）或正在重建，下一帧再看。
                lastToolbar = null;
                return;
            }

            bool toolbarChanged = !ReferenceEquals(toolbar, lastToolbar);
            VisualElement root = null;

            for (int i = 0; i < Entries.Count; i++)
            {
                Entry entry = Entries[i];

                // 元素还挂在当前面板上就什么都不用做。工具栏重建后旧元素会脱离面板，panel 变 null。
                if (!toolbarChanged && entry.Element != null && entry.Element.panel != null)
                {
                    continue;
                }

                if (root == null)
                {
                    root = GetRoot(toolbar);
                    if (root == null)
                    {
                        return;
                    }
                }

                VisualElement zone = root.Q(ZoneNames[(int)entry.Zone]);
                if (zone == null)
                {
                    continue;
                }

                if (entry.Element == null)
                {
                    entry.Element = entry.Factory();
                    if (entry.Element == null)
                    {
                        continue;
                    }
                }

                // Index < 0 放末尾，否则插到指定位置。
                // 注意：左区是 Row、**右区是 RowReverse**，右区的列表顺序和视觉顺序相反——
                // 想让元素显示在右区最左边（History 左侧）要放列表末尾，放 0 会贴到窗口最右边缘。
                if (entry.Index >= 0 && entry.Index <= zone.childCount)
                {
                    zone.Insert(entry.Index, entry.Element);
                }
                else
                {
                    zone.Add(entry.Element);
                }
            }

            lastToolbar = toolbar;
        }

        private static object GetToolbarInstance()
        {
            if (ToolbarType == null)
            {
                return null;
            }

            FieldInfo field = ToolbarType.GetField("get", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            return field == null ? null : field.GetValue(null);
        }

        /// <summary>取工具栏的根元素。任何一环对不上都返回 null——按钮不出现，但不影响别的入口。</summary>
        private static VisualElement GetRoot(object toolbar)
        {
            FieldInfo rootField = ToolbarType.GetField("m_Root", BindingFlags.Instance | BindingFlags.NonPublic);
            return rootField == null ? null : rootField.GetValue(toolbar) as VisualElement;
        }

        /// <summary>一个注册项。Element 建好之后一直复用，不随工具栏重建而重建。</summary>
        private sealed class Entry
        {
            public Zone Zone;
            public Func<VisualElement> Factory;
            public VisualElement Element;

            /// <summary>
            /// 插入位置。负数表示放在该区列表末尾。
            /// 右区是 RowReverse，列表末尾在视觉上是最左边，别按视觉顺序想当然填数字。
            /// </summary>
            public int Index;
        }
    }
}
