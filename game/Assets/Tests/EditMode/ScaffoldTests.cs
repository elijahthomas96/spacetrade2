using System.Linq;
using NUnit.Framework;

namespace SpaceTrade.Sim.Tests
{
    public class ScaffoldTests
    {
        [Test]
        public void SimAssembly_Loads()
        {
            Assert.AreEqual("SpaceTrade.Sim", typeof(SimAssemblyMarker).Assembly.GetName().Name);
        }

        [Test]
        public void SimAssembly_DoesNotReferenceUnityEngine()
        {
            var unityRefs = typeof(SimAssemblyMarker).Assembly
                .GetReferencedAssemblies()
                .Select(a => a.Name)
                .Where(n => n.StartsWith("UnityEngine") || n.StartsWith("UnityEditor"))
                .ToArray();

            Assert.IsEmpty(unityRefs, "SpaceTrade.Sim must not reference Unity: " + string.Join(", ", unityRefs));
        }
    }
}
