using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using SpaceTrade.Sim.Routes;
using SpaceTrade.Sim.Saves;
using SpaceTrade.Sim.Ships;
using SpaceTrade.Sim.Voyages;
using SpaceTrade.Sim.Yards;

namespace SpaceTrade.Sim.Tests
{
    /// <summary>
    /// State-building helpers are internal, so the compiler refuses any call to them from outside
    /// SpaceTrade.Sim. These tests pin the conditions that guarantee rests on.
    /// </summary>
    public class SetupAccessGuardTests
    {
        private static readonly Assembly Sim = typeof(SimAssemblyMarker).Assembly;
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        [Test]
        public void Internals_AreVisibleOnlyToTheEditModeTestAssembly()
        {
            var friends = Sim.GetCustomAttributes<InternalsVisibleToAttribute>().Select(a => a.AssemblyName).ToArray();
            CollectionAssert.AreEqual(new[] { "SpaceTrade.Sim.Tests" }, friends,
                "Only SpaceTrade.Sim.Tests may see SpaceTrade.Sim internals. SpaceTrade.Game must never be added.");
        }

        [TestCase(typeof(Simulation), "AddShip")]
        [TestCase(typeof(Simulation), "SetCredits")]
        [TestCase(typeof(SupplyStock), "Add")]
        [TestCase(typeof(SupplyStock), "Use")]
        public void SetupMethods_AreInternal(System.Type type, string name)
        {
            var methods = type.GetMethods(Any).Where(m => m.Name == name).ToArray();
            Assert.IsNotEmpty(methods, $"{type.Name}.{name} not found");
            foreach (var m in methods) Assert.IsTrue(m.IsAssembly, $"{type.Name}.{name} must be internal");
        }

        [Test]
        public void ShipStockVoyageAndReceiptConstructors_AreInternal()
        {
            foreach (var type in new[] { typeof(Ship), typeof(SupplyStock), typeof(Voyage), typeof(LockedFreight), typeof(VoyageReceipt), typeof(ConsumedSupply),
                         typeof(NewGameResult), typeof(OpeningJob), typeof(OpeningOfferCheck),
                         typeof(Shipyard), typeof(YardStock), typeof(DriveUpgradePreview), typeof(PreviewLeg),
                         typeof(ShipRoute), typeof(Notification), typeof(CircuitEstimate), typeof(CircuitLeg), typeof(CircuitFreight),
                         typeof(ReturnSummary), typeof(ShipAttention), typeof(SaveLoadResult), typeof(SaveWriteResult), typeof(GameSessionOpenResult),
                         typeof(DeliveryTotals), typeof(CatchUpRun), typeof(ShipPosition), typeof(VoyageEta) })
            foreach (var c in type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                Assert.IsTrue(c.IsAssembly, $"{type.Name} constructor must be internal");
        }

        /// <summary>Debug permission can only come from DebugCapability.Grant, which exists only in the editor and development builds.</summary>
        [Test]
        public void DebugCapability_HasOnlyAPrivateConstructor()
        {
            var ctors = typeof(DebugCapability).GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.AreEqual(1, ctors.Length);
            Assert.IsTrue(ctors[0].IsPrivate);
            Assert.IsTrue(typeof(DebugCapability).IsSealed);
        }

        [Test]
        public void GameSession_IsCreatedOnlyByOpenOrStart()
        {
            Assert.IsEmpty(typeof(GameSession).GetConstructors(BindingFlags.Instance | BindingFlags.Public));
        }

        [Test]
        public void StateProperties_HaveNoPublicSetters()
        {
            foreach (var type in new[] { typeof(Simulation), typeof(Ship), typeof(SupplyStock), typeof(Voyage), typeof(LockedFreight), typeof(VoyageReceipt), typeof(ConsumedSupply),
                         typeof(NewGameResult), typeof(OpeningJob), typeof(OpeningOfferCheck),
                         typeof(Shipyard), typeof(YardStock), typeof(DriveUpgradePreview), typeof(PreviewLeg),
                         typeof(RouteStop), typeof(ShipRoute), typeof(Notification), typeof(CircuitEstimate), typeof(CircuitLeg), typeof(CircuitFreight),
                         typeof(ReturnSummary), typeof(ShipAttention), typeof(SaveLoadResult), typeof(SaveWriteResult), typeof(GameSessionOpenResult), typeof(GameSession),
                         typeof(DeliveryTotals), typeof(CatchUpRun), typeof(ShipPosition), typeof(VoyageEta) })
            foreach (var p in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
                Assert.IsNull(p.GetSetMethod(false), $"{type.Name}.{p.Name} has a public setter");
        }

        /// <summary>
        /// The simulation's public surface: player commands plus read-only queries. A new public method
        /// fails this test until it is added here on purpose, so no setup helper becomes a command unseen.
        /// </summary>
        [Test]
        public void Simulation_PublicMethods_AreExactlyTheKnownCommandsAndQueries()
        {
            var publicMethods = typeof(Simulation).GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName)
                .Select(m => m.Name)
                .OrderBy(n => n)
                .ToArray();
            var expected = new[] { "Advance", "AttachMarket", "BuyDrive", "BuyShip", "ClearRoute", "DebugSkipToNextArrival", "DescribeState", "Dispatch", "EstimateCircuit", "EtaOf", "FindShip", "Pause", "PauseRoute", "Play",
                "PositionOf", "PreviewDriveUpgrade", "Provision", "Quote", "RemainingSupplyKg", "ResumeRoute", "Schedule", "SetInSystemTransport", "SetRoute", "SetSpeed" };
            CollectionAssert.AreEqual(expected, publicMethods);
        }
    }
}
