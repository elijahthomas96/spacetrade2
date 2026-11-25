using System;
using System.Collections.Generic;
using System.Text;

namespace SpaceTrade.Sim.Json
{
    public sealed class JsonSyntaxException : Exception
    {
        public int Line { get; }
        public int Column { get; }

        public JsonSyntaxException(string message, int line, int column)
            : base($"{message} (line {line}, column {column})")
        {
            Line = line;
            Column = column;
        }
    }

    /// <summary>Strict JSON parser (RFC 8259). Duplicate field names are a syntax error.</summary>
    public sealed class JsonParser
    {
        private readonly string _text;
        private int _pos;
        private int _line = 1;
        private int _lineStart;

        private JsonParser(string text)
        {
            _text = text ?? throw new ArgumentNullException(nameof(text));
        }

        public static JsonValue Parse(string text)
        {
            var p = new JsonParser(text);
            if (p._text.Length > 0 && p._text[0] == '\uFEFF') p._pos = 1;
            p.SkipWhitespace();
            var value = p.ParseValue();
            p.SkipWhitespace();
            if (p._pos < p._text.Length) throw p.Error("Unexpected text after the end of the data");
            return value;
        }

        private JsonSyntaxException Error(string message) => new JsonSyntaxException(message, _line, _pos - _lineStart + 1);

        private void SkipWhitespace()
        {
            while (_pos < _text.Length)
            {
                char c = _text[_pos];
                if (c == '\n')
                {
                    _pos++;
                    _line++;
                    _lineStart = _pos;
                }
                else if (c == ' ' || c == '\t' || c == '\r') _pos++;
                else break;
            }
        }

        private JsonValue ParseValue()
        {
            if (_pos >= _text.Length) throw Error("Unexpected end of file");
            char c = _text[_pos];
            int line = _line;
            switch (c)
            {
                case '{': return ParseObject();
                case '[': return ParseArray();
                case '"': return JsonValue.String(line, ParseString());
                case 't': ExpectWord("true"); return JsonValue.Bool(line, true);
                case 'f': ExpectWord("false"); return JsonValue.Bool(line, false);
                case 'n': ExpectWord("null"); return JsonValue.Null(line);
                default:
                    if (c == '-' || (c >= '0' && c <= '9')) return JsonValue.Number(line, ParseNumber());
                    throw Error($"Unexpected character '{c}'");
            }
        }

        private void ExpectWord(string word)
        {
            if (string.CompareOrdinal(_text, _pos, word, 0, word.Length) != 0) throw Error($"Expected '{word}'");
            _pos += word.Length;
        }

        private JsonValue ParseObject()
        {
            int line = _line;
            _pos++;
            var members = new List<KeyValuePair<string, JsonValue>>();
            var seen = new HashSet<string>();
            SkipWhitespace();
            if (_pos < _text.Length && _text[_pos] == '}')
            {
                _pos++;
                return JsonValue.Object(line, members);
            }
            while (true)
            {
                SkipWhitespace();
                if (_pos >= _text.Length || _text[_pos] != '"') throw Error("Expected a field name in double quotes");
                string name = ParseString();
                if (!seen.Add(name)) throw Error($"Field \"{name}\" appears twice");
                SkipWhitespace();
                if (_pos >= _text.Length || _text[_pos] != ':') throw Error($"Expected ':' after \"{name}\"");
                _pos++;
                SkipWhitespace();
                members.Add(new KeyValuePair<string, JsonValue>(name, ParseValue()));
                SkipWhitespace();
                if (_pos >= _text.Length) throw Error("Unexpected end of file, missing '}'");
                if (_text[_pos] == ',') { _pos++; continue; }
                if (_text[_pos] == '}') { _pos++; return JsonValue.Object(line, members); }
                throw Error("Expected ',' or '}'");
            }
        }

        private JsonValue ParseArray()
        {
            int line = _line;
            _pos++;
            var items = new List<JsonValue>();
            SkipWhitespace();
            if (_pos < _text.Length && _text[_pos] == ']')
            {
                _pos++;
                return JsonValue.Array(line, items);
            }
            while (true)
            {
                SkipWhitespace();
                items.Add(ParseValue());
                SkipWhitespace();
                if (_pos >= _text.Length) throw Error("Unexpected end of file, missing ']'");
                if (_text[_pos] == ',') { _pos++; continue; }
                if (_text[_pos] == ']') { _pos++; return JsonValue.Array(line, items); }
                throw Error("Expected ',' or ']'");
            }
        }

        private string ParseString()
        {
            _pos++;
            var sb = new StringBuilder();
            while (true)
            {
                if (_pos >= _text.Length) throw Error("Unexpected end of file inside text in quotes");
                char c = _text[_pos++];
                if (c == '"') return sb.ToString();
                if (c == '\n' || c == '\r') throw Error("Text in quotes cannot span lines");
                if (c != '\\')
                {
                    sb.Append(c);
                    continue;
                }
                if (_pos >= _text.Length) throw Error("Unexpected end of file inside text in quotes");
                char e = _text[_pos++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (_pos + 4 > _text.Length) throw Error("Incomplete \\u escape");
                        int code = 0;
                        for (int i = 0; i < 4; i++)
                        {
                            int h = HexValue(_text[_pos++]);
                            if (h < 0) throw Error("Invalid \\u escape");
                            code = code * 16 + h;
                        }
                        sb.Append((char)code);
                        break;
                    default: throw Error($"Invalid escape '\\{e}'");
                }
            }
        }

        private static int HexValue(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return -1;
        }

        private string ParseNumber()
        {
            int start = _pos;
            if (_text[_pos] == '-') _pos++;
            if (_pos >= _text.Length || !IsDigit(_text[_pos])) throw Error("Invalid number");
            if (_text[_pos] == '0') _pos++;
            else while (_pos < _text.Length && IsDigit(_text[_pos])) _pos++;
            if (_pos < _text.Length && _text[_pos] == '.')
            {
                _pos++;
                if (_pos >= _text.Length || !IsDigit(_text[_pos])) throw Error("Invalid number");
                while (_pos < _text.Length && IsDigit(_text[_pos])) _pos++;
            }
            if (_pos < _text.Length && (_text[_pos] == 'e' || _text[_pos] == 'E'))
            {
                _pos++;
                if (_pos < _text.Length && (_text[_pos] == '+' || _text[_pos] == '-')) _pos++;
                if (_pos >= _text.Length || !IsDigit(_text[_pos])) throw Error("Invalid number");
                while (_pos < _text.Length && IsDigit(_text[_pos])) _pos++;
            }
            return _text.Substring(start, _pos - start);
        }

        private static bool IsDigit(char c) => c >= '0' && c <= '9';
    }
}
