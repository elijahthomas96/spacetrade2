using System.Collections.Generic;

namespace SpaceTrade.Sim.Json
{
    public enum JsonKind
    {
        Object,
        Array,
        String,
        Number,
        Bool,
        Null,
    }

    /// <summary>
    /// One parsed JSON value. Numbers keep their source text; only whole numbers convert to an
    /// integer, so no floating point ever enters the simulation.
    /// </summary>
    public sealed class JsonValue
    {
        public JsonKind Kind { get; }
        public int Line { get; }

        public string StringValue { get; }
        public bool BoolValue { get; }

        /// <summary>The number exactly as written in the file.</summary>
        public string NumberText { get; }

        /// <summary>Object members in file order.</summary>
        public IReadOnlyList<KeyValuePair<string, JsonValue>> Members { get; }
        public IReadOnlyList<JsonValue> Items { get; }

        private JsonValue(JsonKind kind, int line, string str = null, bool b = false, string number = null,
            IReadOnlyList<KeyValuePair<string, JsonValue>> members = null, IReadOnlyList<JsonValue> items = null)
        {
            Kind = kind;
            Line = line;
            StringValue = str;
            BoolValue = b;
            NumberText = number;
            Members = members;
            Items = items;
        }

        public static JsonValue Object(int line, IReadOnlyList<KeyValuePair<string, JsonValue>> members) => new JsonValue(JsonKind.Object, line, members: members);
        public static JsonValue Array(int line, IReadOnlyList<JsonValue> items) => new JsonValue(JsonKind.Array, line, items: items);
        public static JsonValue String(int line, string value) => new JsonValue(JsonKind.String, line, str: value);
        public static JsonValue Number(int line, string text) => new JsonValue(JsonKind.Number, line, number: text);
        public static JsonValue Bool(int line, bool value) => new JsonValue(JsonKind.Bool, line, b: value);
        public static JsonValue Null(int line) => new JsonValue(JsonKind.Null, line);

        /// <summary>True if this is a number written as a whole number that fits in a long.</summary>
        public bool TryGetLong(out long value)
        {
            value = 0;
            if (Kind != JsonKind.Number) return false;
            return long.TryParse(NumberText, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out value);
        }

        public JsonValue Get(string name)
        {
            if (Members == null) return null;
            foreach (var m in Members)
                if (m.Key == name) return m.Value;
            return null;
        }

        /// <summary>How the value reads in an error message.</summary>
        public string Describe()
        {
            switch (Kind)
            {
                case JsonKind.String: return "\"" + StringValue + "\"";
                case JsonKind.Number: return NumberText;
                case JsonKind.Bool: return BoolValue ? "true" : "false";
                case JsonKind.Null: return "null";
                case JsonKind.Array: return "a list";
                default: return "a group of fields";
            }
        }
    }
}
