using NUnit.Framework;

namespace SpaceTrade.Sim.Tests
{
    public class SeededRandomTests
    {
        [Test]
        public void SameSeed_SameSequence()
        {
            var a = new SeededRandom(99);
            var b = new SeededRandom(99);
            for (int i = 0; i < 1000; i++) Assert.AreEqual(a.NextULong(), b.NextULong());
        }

        [Test]
        public void DifferentSeeds_DifferentSequences()
        {
            Assert.AreNotEqual(new SeededRandom(1).NextULong(), new SeededRandom(2).NextULong());
        }

        [Test]
        public void RestoredState_ContinuesTheSameSequence()
        {
            var a = new SeededRandom(7);
            for (int i = 0; i < 10; i++) a.NextULong();
            var b = new SeededRandom(a.S0, a.S1, a.S2, a.S3);
            for (int i = 0; i < 100; i++) Assert.AreEqual(a.NextULong(), b.NextULong());
        }

        [Test]
        public void Range_StaysInsideAndHitsBothEnds()
        {
            var r = new SeededRandom(3);
            bool sawMin = false, sawMax = false;
            for (int i = 0; i < 10_000; i++)
            {
                int v = r.Range(-3, 4);
                Assert.That(v, Is.InRange(-3, 4));
                sawMin |= v == -3;
                sawMax |= v == 4;
            }
            Assert.IsTrue(sawMin && sawMax);
        }

        [Test]
        public void Range_SingleValue()
        {
            var r = new SeededRandom(3);
            Assert.AreEqual(5, r.Range(5, 5));
        }
    }
}
