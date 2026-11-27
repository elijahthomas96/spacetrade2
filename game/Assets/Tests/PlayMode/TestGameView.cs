using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEditor;
#endif

namespace SpaceTrade.Game.Tests
{
    /// <summary>
    /// Keeps the PlayMode tests independent of how big the owner's Game view happens to be. Before a PlayMode run the Game
    /// view is set to a fixed 1920 x 1080; after the run it is put back to whatever size was selected before.
    /// The Game view's size list has no public API in Unity, so it is reached by reflection, in the Editor only.
    /// </summary>
    internal static class TestGameView
    {
        public const int Width = 1920;
        public const int Height = 1080;

        /// <summary>How many frames the size change may take to reach the screen before the run is failed.</summary>
        private const int MaxFramesToApply = 30;

        public static bool IsApplied => Screen.width == Width && Screen.height == Height;

#if UNITY_EDITOR
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly Dictionary<EditorWindow, int> Previous = new Dictionary<EditorWindow, int>();

        private static Type GameViewType => typeof(Editor).Assembly.GetType("UnityEditor.GameView");

        /// <summary>Selects the fixed 1920 x 1080 size in every Game view, remembering what each had selected.</summary>
        public static void Apply()
        {
            int index = FixedSizeIndex();
            foreach (EditorWindow view in Resources.FindObjectsOfTypeAll(GameViewType))
            {
                int now = SelectedIndex(view);
                if (!Previous.ContainsKey(view)) Previous[view] = now;
                if (now != index) Select(view, index);
                // A Game view hidden behind another tab keeps its old size until it is shown.
                view.Focus();
                view.Repaint();
            }
        }

        /// <summary>Puts every Game view back to the size it had before <see cref="Apply"/>.</summary>
        public static void Restore()
        {
            foreach (var entry in Previous)
            {
                if (entry.Key == null) continue;
                if (SelectedIndex(entry.Key) != entry.Value) Select(entry.Key, entry.Value);
                entry.Key.Repaint();
            }
            Previous.Clear();
        }

        private static int SelectedIndex(EditorWindow view) => (int)GameViewType.GetProperty("selectedSizeIndex", Any).GetValue(view, null);

        private static void Select(EditorWindow view, int index) =>
            GameViewType.GetMethod("SizeSelectionCallback", Any).Invoke(view, new object[] { index, null });

        /// <summary>The place of "fixed resolution 1920 x 1080" in the Game view's size list, added to the list if it is not there.</summary>
        private static int FixedSizeIndex()
        {
            var editor = typeof(Editor).Assembly;
            var sizesType = editor.GetType("UnityEditor.GameViewSizes");
            object sizes = typeof(ScriptableSingleton<>).MakeGenericType(sizesType).GetProperty("instance").GetValue(null, null);
            object groupType = sizesType.GetProperty("currentGroupType").GetValue(sizes, null);
            object group = sizesType.GetMethod("GetGroup").Invoke(sizes, new[] { groupType });
            var g = group.GetType();
            int total = (int)g.GetMethod("GetTotalCount").Invoke(group, null);
            var sizeType = editor.GetType("UnityEditor.GameViewSize");
            var kindType = editor.GetType("UnityEditor.GameViewSizeType");
            object fixedKind = Enum.Parse(kindType, "FixedResolution");
            for (int i = 0; i < total; i++)
            {
                object size = g.GetMethod("GetGameViewSize").Invoke(group, new object[] { i });
                if ((int)sizeType.GetProperty("width").GetValue(size, null) == Width
                    && (int)sizeType.GetProperty("height").GetValue(size, null) == Height
                    && Equals(sizeType.GetProperty("sizeType").GetValue(size, null), fixedKind)) return i;
            }
            object added = Activator.CreateInstance(sizeType, fixedKind, Width, Height, "spacetrade2 tests");
            g.GetMethod("AddCustomSize").Invoke(group, new[] { added });
            return total;
        }
#else
        public static void Apply() { }
        public static void Restore() { }
#endif

        /// <summary>
        /// Waits until the screen really is 1920 x 1080 (the change reaches the screen a frame or two after it is asked
        /// for), and fails the test's setup if it never does. Outside the Editor there is no Game view, so nothing waits.
        /// </summary>
        public static IEnumerator WaitUntilApplied()
        {
#if UNITY_EDITOR
            if (!IsApplied) Apply();
            for (int i = 0; i < MaxFramesToApply && !IsApplied; i++) yield return null;
            Assert.IsTrue(IsApplied, $"Test setup: the Game view should be {Width} x {Height} for PlayMode tests, but the screen is {Screen.width} x {Screen.height}.");
#else
            yield break;
#endif
        }
    }
}

/// <summary>
/// Runs once around the whole PlayMode run (it is outside any namespace, so it covers every test in the assembly):
/// sets the Game view to 1920 x 1080 before the first test and restores the previous size after the last.
/// </summary>
[SetUpFixture]
public sealed class GameViewSizeForPlayModeTests
{
    [OneTimeSetUp]
    public void SetFixedSize() => SpaceTrade.Game.Tests.TestGameView.Apply();

    [OneTimeTearDown]
    public void RestoreSize() => SpaceTrade.Game.Tests.TestGameView.Restore();
}
