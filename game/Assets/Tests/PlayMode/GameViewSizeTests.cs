using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace SpaceTrade.Game.Tests
{
    /// <summary>
    /// PlayMode tests always run with the Game view at 1920 x 1080 (AGENTS.md), set by the shared setup in
    /// <see cref="TestGameView"/>, so no result depends on the size of the owner's window.
    /// </summary>
    public sealed class GameViewSizeTests
    {
        private readonly MapTestRig _rig = new MapTestRig();

        [TearDown]
        public void TearDown() => _rig.Close();

        [UnityTest]
        public IEnumerator ThePlayModeRun_HasTheGameViewAt1920x1080_WhateverSizeTheOwnersWindowIs()
        {
            yield return TestGameView.WaitUntilApplied();
            Assert.AreEqual(1920, Screen.width, "screen width");
            Assert.AreEqual(1080, Screen.height, "screen height");

            yield return _rig.Open(GameTestTuning.NewGameWithSystems(12));
            Assert.AreEqual(1920, _rig.Camera.pixelWidth, "the map camera's width");
            Assert.AreEqual(1080, _rig.Camera.pixelHeight, "the map camera's height");
            Assert.AreEqual(new Vector2(1920f, 1080f), _rig.Map.Hud.PanelSize, "the interface layer's size");
        }
    }
}
