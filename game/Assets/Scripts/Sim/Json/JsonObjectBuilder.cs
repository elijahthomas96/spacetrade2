using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SpaceTrade.Sim.Json
{
    /// <summary>Builds readable, indented JSON text with fields in the order they were added.</summary>
    public sealed class JsonObjectBuilder
    {
        private sealed class StringList
        {
            public IReadOnlyList<string> Items;
            public bool OnePerLine;
        }

        private readonly List<KeyValuePair<string, object>> _fields = new List<KeyValuePair<string, object>>();

        public JsonObjectBuilder Add(string key, long value) { _fields.Add(new KeyValuePair<string, object>(key, value)); return this; }
        public JsonObjectBuilder Add(string key, string value) { _fields.Add(new KeyValuePair<string, object>(key, value)); return this; }
        public JsonObjectBuilder Add(string key, bool value) { _fields.Add(new KeyValuePair<string, object>(key, value)); return this; }
        public JsonObjectBuilder Add(string key, JsonObjectBuilder value) { _fields.Add(new KeyValuePair<string, object>(key, value)); return this; }
        public JsonObjectBuilder Add(string key, List<JsonObjectBuilder> value) { _fields.Add(new KeyValuePair<string, object>(key, value)); return this; }

        public JsonObjectBuilder Add(string key, IReadOnlyList<string> value, bool onePerLine = false)
        {
            _fields.Add(new KeyValuePair<string, object>(key, new StringList { Items = value, OnePerLine = onePerLine }));
            return this;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            Render(sb, 0);
            sb.Append('\n');
            return sb.ToString();
        }

        private void Render(StringBuilder sb, int depth)
        {
            if (_fields.Count == 0)
            {
                sb.Append("{}");
                return;
            }
            sb.Append("{\n");
            for (int i = 0; i < _fields.Count; i++)
            {
                Indent(sb, depth + 1);
                Quote(sb, _fields[i].Key);
                sb.Append(": ");
                RenderValue(sb, _fields[i].Value, depth + 1);
                if (i < _fields.Count - 1) sb.Append(',');
                sb.Append('\n');
            }
            Indent(sb, depth);
            sb.Append('}');
        }

        private static void RenderValue(StringBuilder sb, object value, int depth)
        {
            switch (value)
            {
                case long l: sb.Append(l.ToString(CultureInfo.InvariantCulture)); break;
                case string s: Quote(sb, s); break;
                case bool b: sb.Append(b ? "true" : "false"); break;
                case JsonObjectBuilder o: o.Render(sb, depth); break;
                case List<JsonObjectBuilder> list:
                    if (list.Count == 0) { sb.Append("[]"); break; }
                    sb.Append("[\n");
                    for (int i = 0; i < list.Count; i++)
                    {
                        Indent(sb, depth + 1);
                        list[i].Render(sb, depth + 1);
                        if (i < list.Count - 1) sb.Append(',');
                        sb.Append('\n');
                    }
                    Indent(sb, depth);
                    sb.Append(']');
                    break;
                case StringList sl:
                    if (sl.Items.Count == 0) { sb.Append("[]"); break; }
                    if (sl.OnePerLine)
                    {
                        sb.Append("[\n");
                        for (int i = 0; i < sl.Items.Count; i++)
                        {
                            Indent(sb, depth + 1);
                            Quote(sb, sl.Items[i]);
                            if (i < sl.Items.Count - 1) sb.Append(',');
                            sb.Append('\n');
                        }
                        Indent(sb, depth);
                        sb.Append(']');
                    }
                    else
                    {
                        sb.Append('[');
                        for (int i = 0; i < sl.Items.Count; i++)
                        {
                            if (i > 0) sb.Append(", ");
                            Quote(sb, sl.Items[i]);
                        }
                        sb.Append(']');
                    }
                    break;
            }
        }

        private static void Indent(StringBuilder sb, int depth) => sb.Append(' ', depth * 2);

        private static void Quote(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
