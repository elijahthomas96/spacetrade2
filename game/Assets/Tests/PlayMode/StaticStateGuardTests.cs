using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using SpaceTrade.Game.Map;
using UnityEngine;

namespace SpaceTrade.Game.Tests
{
    /// <summary>
    /// The project enters Play mode without reloading code, so static state in SpaceTrade.Game survives from one Play
    /// session to the next (AGENTS.md). Any static field that can change, or that holds a Unity object, must belong to a
    /// type that resets it at the start of every Play session.
    /// </summary>
    public class StaticStateGuardTests
    {
        /// <summary>Fixed data, built once and never changed, that is safe to keep between sessions.</summary>
        private static readonly string[] FixedData =
        {
            "SpaceTrade.Game.Art.ArtManifest.All",
        };

        private static bool IsImmutable(Type t) =>
            t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal) || t == typeof(CultureInfo) ||
            (t.IsValueType && t.Namespace == "UnityEngine");

        private static bool ResetsAtPlayStart(Type t) =>
            t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Any(m => m.GetCustomAttributes<RuntimeInitializeOnLoadMethodAttribute>()
                    .Any(a => a.loadType == RuntimeInitializeLoadType.SubsystemRegistration));

        [Test]
        public void EveryChangeableStaticField_InTheGameCode_IsResetAtTheStartOfEveryPlaySession()
        {
            var assembly = typeof(MapScreen).Assembly;
            var kept = new List<string>();
            var problems = new List<string>();
            foreach (var type in assembly.GetTypes())
            {
                if (type.GetCustomAttribute<CompilerGeneratedAttribute>() != null) continue;
                foreach (var f in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (f.IsLiteral) continue;
                    string name = $"{type.FullName}.{f.Name}";
                    if (f.IsInitOnly && IsImmutable(f.FieldType)) continue;
                    if (FixedData.Contains(name)) continue;
                    kept.Add(name);
                    if (!ResetsAtPlayStart(type)) problems.Add(name + " (" + f.FieldType.Name + ")");
                }
            }
            TestContext.WriteLine("Static state reset at every Play start: " + string.Join(", ", kept));
            CollectionAssert.IsEmpty(problems, "static state with no SubsystemRegistration reset in its type");
        }
    }
}
