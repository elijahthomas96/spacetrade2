using System;
using System.Collections.Generic;
using SpaceTrade.Sim.Json;

namespace SpaceTrade.Sim.Saves
{
    /// <summary>A save's contents do not fit the save format. The message names the field.</summary>
    internal sealed class SaveDataException : Exception
    {
        public SaveDataException(string message) : base(message)
        {
        }
    }

    /// <summary>
    /// Strict reading of one object in a save: every field must be present with the right type and range, and a
    /// field the format does not have is an error. Errors name the field by its path, for example game.ships[2].fuel.kg.
    /// </summary>
    internal sealed class SaveNode
    {
        private readonly JsonValue _value;
        private readonly string _path;
        private readonly HashSet<string> _read = new HashSet<string>();

        private SaveNode(JsonValue value, string path)
        {
            if (value.Kind != JsonKind.Object) throw new SaveDataException($"{path} must be a group of fields, got {value.Describe()}.");
            _value = value;
            _path = path;
        }

        public static SaveNode Root(JsonValue value) => new SaveNode(value, "save");

        public string Path => _path;

        private string PathOf(string name) => _path + "." + name;

        public bool Has(string name) => _value.Get(name) != null;

        private JsonValue Field(string name)
        {
            var v = _value.Get(name);
            if (v == null) throw new SaveDataException($"{PathOf(name)} is missing.");
            _read.Add(name);
            return v;
        }

        public long Long(string name, long min = long.MinValue, long max = long.MaxValue)
        {
            var v = Field(name);
            if (!v.TryGetLong(out long x)) throw new SaveDataException($"{PathOf(name)} must be a whole number, got {v.Describe()}.");
            if (x < min || x > max) throw new SaveDataException($"{PathOf(name)} must be from {min} to {max}, got {x}.");
            return x;
        }

        public int Int(string name, int min = int.MinValue, int max = int.MaxValue) => (int)Long(name, min, max);

        public long? OptLong(string name, long min = long.MinValue, long max = long.MaxValue) => Has(name) ? Long(name, min, max) : (long?)null;

        public int? OptInt(string name, int min = int.MinValue, int max = int.MaxValue) => Has(name) ? Int(name, min, max) : (int?)null;

        public string Text(string name)
        {
            var v = Field(name);
            if (v.Kind != JsonKind.String) throw new SaveDataException($"{PathOf(name)} must be text, got {v.Describe()}.");
            return v.StringValue;
        }

        public string OptText(string name) => Has(name) ? Text(name) : null;

        public bool Bool(string name)
        {
            var v = Field(name);
            if (v.Kind != JsonKind.Bool) throw new SaveDataException($"{PathOf(name)} must be true or false, got {v.Describe()}.");
            return v.BoolValue;
        }

        public SaveNode Child(string name) => new SaveNode(Field(name), PathOf(name));

        public SaveNode OptChild(string name) => Has(name) ? Child(name) : null;

        public List<SaveNode> List(string name)
        {
            var v = Field(name);
            if (v.Kind != JsonKind.Array) throw new SaveDataException($"{PathOf(name)} must be a list, got {v.Describe()}.");
            var list = new List<SaveNode>();
            for (int i = 0; i < v.Items.Count; i++) list.Add(new SaveNode(v.Items[i], $"{PathOf(name)}[{i}]"));
            return list;
        }

        public List<string> TextList(string name)
        {
            var v = Field(name);
            if (v.Kind != JsonKind.Array) throw new SaveDataException($"{PathOf(name)} must be a list, got {v.Describe()}.");
            var list = new List<string>();
            for (int i = 0; i < v.Items.Count; i++)
            {
                if (v.Items[i].Kind != JsonKind.String) throw new SaveDataException($"{PathOf(name)}[{i}] must be text, got {v.Items[i].Describe()}.");
                list.Add(v.Items[i].StringValue);
            }
            return list;
        }

        public T Enum<T>(string name) where T : struct
        {
            string text = Text(name);
            if (!System.Enum.TryParse(text, false, out T value) || !System.Enum.IsDefined(typeof(T), value) || int.TryParse(text, out _))
                throw new SaveDataException($"{PathOf(name)} must be one of {string.Join(", ", System.Enum.GetNames(typeof(T)))}, got \"{text}\".");
            return value;
        }

        /// <summary>Call after reading every field: any field left over is not part of the format.</summary>
        public void Done()
        {
            foreach (var m in _value.Members)
                if (!_read.Contains(m.Key)) throw new SaveDataException($"{PathOf(m.Key)} is not a field the save format has.");
        }
    }
}
