using System;
using System.Collections.Generic;
using SpaceTrade.Game.Audio;
using UnityEngine;
using UnityEngine.UIElements;

namespace SpaceTrade.Game.Hud
{
    /// <summary>A work panel that opens over the selection's panel: the voyage planner or a receipt.</summary>
    public interface IWorkPanel
    {
        /// <summary>The panel's own element. The manager shows and hides it.</summary>
        VisualElement Panel { get; }

        /// <summary>A short name for sound log lines and tests.</summary>
        string Title { get; }
    }

    /// <summary>
    /// The one-panel rule (AGENTS.md), in two tiers.
    ///
    /// Work panels (the system view and ship panel, which belong to the selection, and the planner and receipt opened
    /// from them): only one shows at a time. Opening another replaces the one showing, and closing it brings back the
    /// one it replaced.
    ///
    /// Utility panels (the notification list and settings): only one is open at a time, and it takes the work panel's
    /// place while it is open. They replace each other and never bring each other back; closing one returns to the work
    /// panel underneath.
    ///
    /// This class keeps the work panels' order. The bar owns the two utility panels and tells this class when one is
    /// open, so the work panel underneath is hidden rather than showing through beside it.
    /// </summary>
    public sealed class WorkPanels
    {
        private readonly List<IWorkPanel> _open = new List<IWorkPanel>();
        private readonly List<IWorkPanel> _known = new List<IWorkPanel>();

        /// <summary>True while the notification list or settings is open. Set by the bar.</summary>
        public Func<bool> UtilityOpen { get; set; }

        /// <summary>Called whenever what should be showing changes: (a work panel is over the selection's panel, a utility is open).</summary>
        public Action<bool, bool> SelectionPanelCovered { get; set; }

        /// <summary>The planner or receipt on top, or null when the selection's own panel is the top work panel.</summary>
        public IWorkPanel Top => _open.Count > 0 ? _open[_open.Count - 1] : null;

        /// <summary>The opened work panels, the one on top last.</summary>
        public IReadOnlyList<IWorkPanel> Open => _open;

        public bool IsOpen(IWorkPanel panel) => _open.Contains(panel);

        /// <summary>True when this panel is the one on screen: on top, with no utility panel open over it.</summary>
        public bool IsShowing(IWorkPanel panel) => Top == panel && !Utility;

        private bool Utility => UtilityOpen != null && UtilityOpen();

        public void Register(IWorkPanel panel)
        {
            if (!_known.Contains(panel)) _known.Add(panel);
            Apply();
        }

        /// <summary>Opens a work panel over whatever is showing. Opening the one already on top does nothing.</summary>
        public void Show(IWorkPanel panel)
        {
            if (Top == panel) return;
            _open.Remove(panel);
            _open.Add(panel);
            Apply();
            GameAudio.Play(SoundId.PanelOpen, panel.Title + " opened");
        }

        /// <summary>Closes a work panel; the one it replaced comes back.</summary>
        public void Close(IWorkPanel panel)
        {
            if (!_open.Remove(panel)) return;
            Apply();
            GameAudio.Play(SoundId.PanelClose, panel.Title + " closed");
        }

        /// <summary>Takes a work panel away without a sound, because what it was about has gone (another ship was selected).</summary>
        public void Drop(IWorkPanel panel)
        {
            if (_open.Remove(panel)) Apply();
        }

        /// <summary>Esc: closes the planner or receipt on top. False when the selection's own panel is the top one.</summary>
        public bool CloseTop()
        {
            var top = Top;
            if (top == null) return false;
            Close(top);
            return true;
        }

        /// <summary>Puts every panel's visibility in line with the rule. Cheap; safe to call every frame.</summary>
        public void Apply()
        {
            bool utility = Utility;
            var top = Top;
            foreach (var p in _known)
            {
                var show = p == top ? DisplayStyle.Flex : DisplayStyle.None;
                if (p.Panel.style.display != show) p.Panel.style.display = show;
                var visible = utility ? Visibility.Hidden : Visibility.Visible;
                if (p.Panel.style.visibility != visible) p.Panel.style.visibility = visible;
            }
            SelectionPanelCovered?.Invoke(top != null, utility);
        }

        /// <summary>The rectangle of the planner or receipt on screen, if one is.</summary>
        public bool TryGetShownRect(out Rect rect)
        {
            rect = default;
            var top = Top;
            if (top == null || Utility) return false;
            rect = top.Panel.layout;
            return rect.width > 0f;
        }
    }
}
