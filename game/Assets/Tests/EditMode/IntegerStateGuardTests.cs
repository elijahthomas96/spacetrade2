using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace SpaceTrade.Sim.Tests
{
    public class IntegerStateGuardTests
    {
        [Test]
        public void SimAssembly_StoresNoFloatingPoint()
        {
            const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            var offenders = typeof(SimAssemblyMarker).Assembly.GetTypes()
                .SelectMany(t => t.GetFields(all).Select(f => (t, f)))
                .Where(x => x.f.FieldType == typeof(float) || x.f.FieldType == typeof(double) || x.f.FieldType == typeof(decimal))
                .Select(x => x.t.FullName + "." + x.f.Name)
                .ToArray();

            Assert.IsEmpty(offenders, "Simulation state must be integers only: " + string.Join(", ", offenders));
        }
    }
}
