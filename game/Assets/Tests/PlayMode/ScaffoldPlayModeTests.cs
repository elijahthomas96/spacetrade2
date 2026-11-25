using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace SpaceTrade.Game.Tests
{
    public class ScaffoldPlayModeTests
    {
        [UnityTest]
        public IEnumerator GameAssembly_LoadsInPlayMode()
        {
            yield return null;

            Assert.IsTrue(Application.isPlaying);
            Assert.AreEqual("SpaceTrade.Game", typeof(GameAssemblyMarker).Assembly.GetName().Name);
        }
    }
}
