// 职责：UI 层栈的纯逻辑——谁压谁、压入全屏面板要隐藏谁、弹出后要恢复谁。不碰任何 Unity 对象。
// 为什么新建：这套规则是整个 UI 里最容易被后续改动破坏的部分（全屏剔除、弹窗叠加、Top 优先级），
//   写在 UIService 里就只能靠进 Play 模式点面板来验证；抽成纯 C# 类才能用 EditMode 测试钉住。
//   UIService 保留「粘合」职责（实例化、SetActive、生命周期回调），两边职责说得通。

using System.Collections.Generic;

namespace Game.Core.UI
{
    /// <summary>栈里一个条目需要暴露的两件事。<see cref="UIView"/> 实现它，测试用假实现。</summary>
    public interface IUIStackEntry
    {
        /// <summary>所在层。</summary>
        UILayer Layer { get; }

        /// <summary>是不是全屏（只对 <see cref="UILayer.Panel"/> 有意义）。</summary>
        bool IsFullScreen { get; }
    }

    /// <summary>
    /// UI 栈。两条独立的栈：
    /// <list type="bullet">
    /// <item>Panel 单栈：压入**全屏** Panel 时，它下面的 Panel 全部隐藏；它被弹出后，恢复到「最靠上的那个全屏面板及其之上」都可见。</item>
    /// <item>Popup 栈：可叠加，互不隐藏，也不影响 Panel。</item>
    /// </list>
    /// Hud 与 Top 两层不进栈——它们是常驻的，没有「盖住上一个」的语义。
    /// <para>
    /// 本类只做记账：<see cref="Push"/> 返回**需要隐藏**的条目，<see cref="Remove"/> 返回**需要恢复显示**的条目，
    /// 真正的 SetActive 由 UIService 做。
    /// </para>
    /// </summary>
    public sealed class UIStack
    {
        private static readonly IUIStackEntry[] Empty = new IUIStackEntry[0];

        private readonly List<IUIStackEntry> panels = new List<IUIStackEntry>();
        private readonly List<IUIStackEntry> popups = new List<IUIStackEntry>();
        private readonly HashSet<IUIStackEntry> hidden = new HashSet<IUIStackEntry>();

        /// <summary>Panel 栈，从下到上。</summary>
        public IReadOnlyList<IUIStackEntry> Panels => panels;

        /// <summary>Popup 栈，从下到上。</summary>
        public IReadOnlyList<IUIStackEntry> Popups => popups;

        /// <summary>
        /// 最上面的条目：先看 Popup 再看 Panel（弹窗永远盖在面板上，所以「关掉最上面那个」先关弹窗）。
        /// 两条栈都空时返回 null。
        /// </summary>
        public IUIStackEntry Top()
        {
            if (popups.Count > 0)
            {
                return popups[popups.Count - 1];
            }

            return panels.Count > 0 ? panels[panels.Count - 1] : null;
        }

        /// <summary>条目在不在栈里（Hud / Top 层的条目永远不在）。</summary>
        public bool Contains(IUIStackEntry entry)
        {
            return entry != null && (panels.Contains(entry) || popups.Contains(entry));
        }

        /// <summary>条目当前是不是被压在下面隐藏了。</summary>
        public bool IsHidden(IUIStackEntry entry)
        {
            return entry != null && hidden.Contains(entry);
        }

        /// <summary>
        /// 压栈。返回**因为这次压栈而需要隐藏**的条目（只有压入全屏 Panel 时才非空）。
        /// Hud / Top 层不进栈，返回空。
        /// </summary>
        public IReadOnlyList<IUIStackEntry> Push(IUIStackEntry entry)
        {
            if (entry == null || !IsStacked(entry.Layer))
            {
                return Empty;
            }

            if (entry.Layer == UILayer.Popup)
            {
                popups.Add(entry);
                return Empty;
            }

            panels.Add(entry);
            return Resync();
        }

        /// <summary>
        /// 出栈。返回**因为这次出栈而需要重新显示**的条目。
        /// 条目不在栈里（没压过、或是 Hud / Top 层）时返回空。
        /// </summary>
        public IReadOnlyList<IUIStackEntry> Remove(IUIStackEntry entry)
        {
            if (entry == null)
            {
                return Empty;
            }

            if (popups.Remove(entry))
            {
                hidden.Remove(entry);
                return Empty;
            }

            if (!panels.Remove(entry))
            {
                return Empty;
            }

            hidden.Remove(entry);
            return Resync();
        }

        /// <summary>清空两条栈（UIService.Dispose 用）。不返回任何需要改显隐的条目——对象都要销毁了。</summary>
        public void Clear()
        {
            panels.Clear();
            popups.Clear();
            hidden.Clear();
        }

        /// <summary>Hud 与 Top 是常驻层，不参与压栈。</summary>
        private static bool IsStacked(UILayer layer)
        {
            return layer == UILayer.Panel || layer == UILayer.Popup;
        }

        /// <summary>
        /// 按当前 Panel 栈重算一遍显隐，返回**状态发生翻转**的条目。
        /// 规则：从上往下找到第一个全屏 Panel，它和它之上的可见，它之下的隐藏；一个全屏的都没有就全可见。
        /// <para>
        /// 每次变更都整体重算而不是增量记「我隐藏了谁」：增量做法在
        /// 「全屏 A → 半透明 B → 全屏 C，然后先关 C 再关 A」这种顺序下会漏恢复，整体重算不会。
        /// </para>
        /// </summary>
        private IReadOnlyList<IUIStackEntry> Resync()
        {
            int firstVisible = 0;
            for (int i = panels.Count - 1; i >= 0; i--)
            {
                if (panels[i].IsFullScreen)
                {
                    firstVisible = i;
                    break;
                }
            }

            List<IUIStackEntry> flipped = null;
            for (int i = 0; i < panels.Count; i++)
            {
                IUIStackEntry panel = panels[i];
                bool shouldHide = i < firstVisible;
                bool isHidden = hidden.Contains(panel);
                if (shouldHide == isHidden)
                {
                    continue;
                }

                if (shouldHide)
                {
                    hidden.Add(panel);
                }
                else
                {
                    hidden.Remove(panel);
                }

                flipped ??= new List<IUIStackEntry>();
                flipped.Add(panel);
            }

            return (IReadOnlyList<IUIStackEntry>)flipped ?? Empty;
        }
    }
}
