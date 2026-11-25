using System;
using System.Numerics;
using SpaceTrade.Sim.Timing;
using SpaceTrade.Sim.Tuning;

namespace SpaceTrade.Sim.Ships
{
    /// <summary>What one voyage needs and how full it makes the ship (spec 6.3). Masses in kilograms.</summary>
    public sealed class VoyageNeeds
    {
        public long DistanceMilliLy { get; }
        public long TransitTicks { get; }
        public long FreightTonnes { get; }
        public int Passengers { get; }

        /// <summary>Crew plus passengers.</summary>
        public int People { get; }

        public long FoodRequiredKg { get; }
        public long WaterRequiredKg { get; }

        /// <summary>Food and water aboard after topping up: what is needed, or more if more is already aboard.</summary>
        public long FoodAboardAfterKg { get; }
        public long WaterAboardAfterKg { get; }

        /// <summary>Freight plus food and water aboard after topping up. Must fit in the hold.</summary>
        public long HoldUsedKg { get; }

        /// <summary>Dry mass, freight, food and water aboard after topping up, and people. Fuel's own mass is left out.</summary>
        public long LoadedMassKg { get; }

        public long FuelRequiredKg { get; }

        internal VoyageNeeds(long distanceMilliLy, long transitTicks, long freightTonnes, int passengers, int people,
            long foodRequiredKg, long waterRequiredKg, long foodAboardAfterKg, long waterAboardAfterKg,
            long holdUsedKg, long loadedMassKg, long fuelRequiredKg)
        {
            DistanceMilliLy = distanceMilliLy;
            TransitTicks = transitTicks;
            FreightTonnes = freightTonnes;
            Passengers = passengers;
            People = people;
            FoodRequiredKg = foodRequiredKg;
            WaterRequiredKg = waterRequiredKg;
            FoodAboardAfterKg = foodAboardAfterKg;
            WaterAboardAfterKg = waterAboardAfterKg;
            HoldUsedKg = holdUsedKg;
            LoadedMassKg = loadedMassKg;
            FuelRequiredKg = fuelRequiredKg;
        }
    }

    /// <summary>
    /// The provisioning formulas (spec 6.3), shared by the voyage quote and the tuning starter-range
    /// check. Supplies needed round up to the whole kilogram so a ship is never short.
    /// </summary>
    public static class ProvisioningMath
    {
        /// <summary>Food or water for a voyage: people x voyage days x grams per person per day, in kilograms, rounded up.</summary>
        public static long ConsumableKg(int people, int gramsPerPersonPerDay, long transitTicks)
        {
            if (people < 0) throw new ArgumentOutOfRangeException(nameof(people), people, "People cannot be negative.");
            if (transitTicks < 0) throw new ArgumentOutOfRangeException(nameof(transitTicks), transitTicks, "Transit time cannot be negative.");
            return IntMath.DivideCeiling((BigInteger)people * gramsPerPersonPerDay * transitTicks, (BigInteger)GameTime.TicksPerDay * 1000);
        }

        /// <summary>Fuel for a voyage: light-years x loaded tonnes x fuel kg per tonne-ly, in kilograms, rounded up.</summary>
        public static long FuelKg(long distanceMilliLy, long loadedMassKg, int fuelKgPerTonneLy)
        {
            if (distanceMilliLy < 0) throw new ArgumentOutOfRangeException(nameof(distanceMilliLy), distanceMilliLy, "Distance cannot be negative.");
            if (loadedMassKg < 0) throw new ArgumentOutOfRangeException(nameof(loadedMassKg), loadedMassKg, "Mass cannot be negative.");
            return IntMath.DivideCeiling((BigInteger)distanceMilliLy * loadedMassKg * fuelKgPerTonneLy, 1000 * 1000);
        }

        /// <summary>
        /// Everything a voyage of <paramref name="distanceMilliLy"/> needs for this class and drive, with
        /// this freight and these passengers, given the food and water already aboard.
        /// </summary>
        public static VoyageNeeds Compute(TuningData tuning, ShipClassTuning shipClass, DriveTuning drive, long distanceMilliLy,
            long freightTonnes, int passengers, long foodAboardKg, long waterAboardKg)
        {
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));
            if (shipClass == null) throw new ArgumentNullException(nameof(shipClass));
            if (drive == null) throw new ArgumentNullException(nameof(drive));
            if (freightTonnes < 0) throw new ArgumentOutOfRangeException(nameof(freightTonnes), freightTonnes, "Freight cannot be negative.");
            if (passengers < 0) throw new ArgumentOutOfRangeException(nameof(passengers), passengers, "Passengers cannot be negative.");

            var p = tuning.Provisions;
            long ticks = TransitMath.TransitTicks(distanceMilliLy, drive.SpeedMilliC);
            int people = checked(shipClass.Crew + passengers);
            long food = ConsumableKg(people, p.FoodGramsPerPersonPerDay, ticks);
            long water = ConsumableKg(people, p.WaterGramsPerPersonPerDay, ticks);
            long foodAfter = Math.Max(food, foodAboardKg);
            long waterAfter = Math.Max(water, waterAboardKg);
            long freightKg = checked(freightTonnes * 1000);
            long hold = checked(freightKg + foodAfter + waterAfter);
            long loaded = checked((long)shipClass.DryMassTonnes * 1000 + hold + (long)people * p.PersonMassKg);
            long fuel = FuelKg(distanceMilliLy, loaded, drive.FuelKgPerTonneLy);
            return new VoyageNeeds(distanceMilliLy, ticks, freightTonnes, passengers, people, food, water, foodAfter, waterAfter, hold, loaded, fuel);
        }

        /// <summary>
        /// Cost of buying <paramref name="kg"/> at a price per tonne: kg x price / 1000, rounded once
        /// with the one rounding rule.
        /// </summary>
        public static long PurchaseCost(long kg, long pricePerTonne)
        {
            if (kg < 0) throw new ArgumentOutOfRangeException(nameof(kg), kg, "Cannot buy a negative amount.");
            return IntMath.DivideRounded((BigInteger)kg * pricePerTonne, 1000);
        }
    }
}
