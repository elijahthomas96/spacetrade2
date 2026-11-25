using System.Collections.Generic;
using SpaceTrade.Sim.Json;

namespace SpaceTrade.Sim.Tuning
{
    /// <summary>
    /// Reads the fields of one JSON object, recording an error that names the field for anything
    /// missing, mistyped, out of range, or unknown. Reading never throws.
    /// </summary>
    internal sealed class ObjectReader
    {
        private readonly JsonValue _obj;
        private readonly List<string> _errors;
        private readonly HashSet<string> _known = new HashSet<string>();

        public string Path { get; }

        private ObjectReader(JsonValue obj, string path, List<string> errors)
        {
            _obj = obj;
            Path = path;
            _errors = errors;
        }

        public static ObjectReader Root(JsonValue value, List<string> errors)
        {
            if (value.Kind != JsonKind.Object)
            {
                errors.Add($"The file must be one group of fields in {{ }}, got {value.Describe()} (line {value.Line})");
                return new ObjectReader(null, "", errors);
            }
            return new ObjectReader(value, "", errors);
        }

        public string FieldPath(string name) => Path.Length == 0 ? name : Path + "." + name;

        public void Error(string path, string message, JsonValue at = null)
        {
            _errors.Add(at == null ? $"{path}: {message}" : $"{path}: {message} (line {at.Line})");
        }

        private JsonValue Take(string name)
        {
            _known.Add(name);
            if (_obj == null) return null;
            var v = _obj.Get(name);
            if (v == null) Error(FieldPath(name), "is missing", _obj);
            return v;
        }

        public long Long(string name, long min, long max = long.MaxValue)
        {
            var v = Take(name);
            if (v == null) return 0;
            if (!v.TryGetLong(out long value))
            {
                if (v.Kind != JsonKind.Number) Error(FieldPath(name), $"must be a whole number, got {v.Describe()}", v);
                else if (v.NumberText.IndexOfAny(new[] { '.', 'e', 'E' }) >= 0) Error(FieldPath(name), $"must be a whole number with no decimal point, got {v.NumberText}", v);
                else Error(FieldPath(name), $"is far too large, got {v.NumberText}", v);
                return 0;
            }
            if (value < min) Error(FieldPath(name), $"must be at least {min}, got {value}", v);
            else if (value > max) Error(FieldPath(name), $"must be at most {max}, got {value}", v);
            return value;
        }

        public int Int(string name, int min, int max = int.MaxValue) => (int)Clamp(Long(name, min, max), int.MinValue, int.MaxValue);

        private static long Clamp(long v, long lo, long hi) => v < lo ? lo : v > hi ? hi : v;

        public string Text(string name)
        {
            var v = Take(name);
            if (v == null) return "";
            if (v.Kind != JsonKind.String)
            {
                Error(FieldPath(name), $"must be text in double quotes, got {v.Describe()}", v);
                return "";
            }
            if (v.StringValue.Trim().Length == 0)
            {
                Error(FieldPath(name), "cannot be empty", v);
                return "";
            }
            return v.StringValue;
        }

        public bool Bool(string name)
        {
            var v = Take(name);
            if (v == null) return false;
            if (v.Kind != JsonKind.Bool)
            {
                Error(FieldPath(name), $"must be true or false, got {v.Describe()}", v);
                return false;
            }
            return v.BoolValue;
        }

        public List<string> TextList(string name)
        {
            var result = new List<string>();
            var v = Take(name);
            if (v == null) return result;
            if (v.Kind != JsonKind.Array)
            {
                Error(FieldPath(name), $"must be a list in [ ], got {v.Describe()}", v);
                return result;
            }
            for (int i = 0; i < v.Items.Count; i++)
            {
                var item = v.Items[i];
                if (item.Kind != JsonKind.String || item.StringValue.Trim().Length == 0)
                    Error($"{FieldPath(name)}[#{i + 1}]", $"must be non-empty text in double quotes, got {item.Describe()}", item);
                else
                    result.Add(item.StringValue);
            }
            return result;
        }

        public ObjectReader Child(string name)
        {
            var v = Take(name);
            if (v == null) return new ObjectReader(null, FieldPath(name), _errors);
            if (v.Kind != JsonKind.Object)
            {
                Error(FieldPath(name), $"must be a group of fields in {{ }}, got {v.Describe()}", v);
                return new ObjectReader(null, FieldPath(name), _errors);
            }
            return new ObjectReader(v, FieldPath(name), _errors);
        }

        /// <summary>
        /// Reads a list of objects. Each item's path uses its id field when present, for example
        /// <c>ships[courier]</c>, otherwise its position counting from 1, for example <c>ships[#2]</c>.
        /// </summary>
        public List<ObjectReader> ObjectList(string name, string labelField)
        {
            var result = new List<ObjectReader>();
            var v = Take(name);
            if (v == null) return result;
            if (v.Kind != JsonKind.Array)
            {
                Error(FieldPath(name), $"must be a list in [ ], got {v.Describe()}", v);
                return result;
            }
            for (int i = 0; i < v.Items.Count; i++)
            {
                var item = v.Items[i];
                var label = item.Kind == JsonKind.Object ? item.Get(labelField) : null;
                string path = label != null && label.Kind == JsonKind.String && label.StringValue.Trim().Length > 0
                    ? $"{FieldPath(name)}[{label.StringValue}]"
                    : $"{FieldPath(name)}[#{i + 1}]";
                if (item.Kind != JsonKind.Object)
                {
                    Error(path, $"must be a group of fields in {{ }}, got {item.Describe()}", item);
                    continue;
                }
                result.Add(new ObjectReader(item, path, _errors));
            }
            return result;
        }

        /// <summary>Reports every field present in the file that this reader never asked for.</summary>
        public void RejectUnknownFields()
        {
            if (_obj == null) return;
            foreach (var m in _obj.Members)
                if (!_known.Contains(m.Key))
                    Error(FieldPath(m.Key), "is not a recognised field (check the spelling)", m.Value);
        }
    }
}
