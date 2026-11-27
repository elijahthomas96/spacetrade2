using SpaceTrade.Game.Boot;
using UnityEditor;
using UnityEngine;

namespace SpaceTrade.Game.Editor
{
    /// <summary>
    /// Debug skip from the Unity Editor's menu (spec 13), for playtests before the in-game button exists (session 17).
    /// This file is in an Editor-only assembly, so none of it is in any build, normal or development. The skip itself is
    /// the simulation's own command: it is refused unless the game was started with debug permission, which the first
    /// menu command below switches on for a new game. Both commands work only while the game is playing in the Editor.
    /// </summary>
    public static class DebugSkipMenu
    {
        private const string RestartPath = "spacetrade2/Debug/Start a new game with debug skip allowed";
        private const string SkipPath = "spacetrade2/Debug/Skip to next arrival";

        private static GameBootstrap Bootstrap => Application.isPlaying ? Object.FindAnyObjectByType<GameBootstrap>() : null;

        [MenuItem(RestartPath, true)]
        private static bool CanRestart() => Bootstrap != null;

        /// <summary>Throws away the game in play and starts a fresh one from tuning.json with debug permission granted.</summary>
        [MenuItem(RestartPath)]
        private static void Restart()
        {
            var bootstrap = Bootstrap;
            if (bootstrap == null) return;
            if (!EditorUtility.DisplayDialog("spacetrade2 debug",
                    "Start a new game with debug skip allowed?\n\nThe game on screen now is thrown away (nothing is saved yet in this version).",
                    "Start a new game", "Cancel")) return;
            bootstrap.DebugMode = true;
            bootstrap.DebugRestart(0);
        }

        [MenuItem(SkipPath, true)]
        private static bool CanSkip() => Bootstrap != null && Bootstrap.Runner != null && Bootstrap.Runner.Simulation != null;

        /// <summary>Jumps to the earliest pending arrival through the simulation's normal Advance path. Works while paused.</summary>
        [MenuItem(SkipPath)]
        private static void Skip()
        {
            var bootstrap = Bootstrap;
            if (bootstrap == null || bootstrap.Runner == null) return;
            var result = bootstrap.Runner.DebugSkipToNextArrival();
            if (!result.Succeeded)
                EditorUtility.DisplayDialog("spacetrade2 debug", result.Reason + "\n\nTo allow the skip, use: spacetrade2 > Debug > Start a new game with debug skip allowed.", "OK");
        }
    }
}
