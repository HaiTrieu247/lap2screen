using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MacExtend.Core
{
    /// <summary>
    /// Reads the JSON pymobiledevice3's print_json writes, and tunneld's GET /, without a JSON library: objects become
    /// Dictionary&lt;string, object&gt; (ordinal keys), arrays List&lt;object&gt;, numbers double, plus string, bool
    /// and null. Strict: one value, nothing after it, nesting at most 64 deep; anything else is a FormatException.
    /// </summary>
    public static class MiniJson
    {
        private const int MaxDepth = 64;

        public static object Parse(string text)
        {
            if (text == null) throw new FormatException("no JSON text");
            var reader = new Reader(text);
            reader.SkipSpace();
            var value = reader.ReadValue(0);
            reader.SkipSpace();
            if (!reader.AtEnd) throw new FormatException("unexpected text after the JSON value at " + reader.Position);
            return value;
        }

        public static bool TryParse(string text, out object value)
        {
            try
            {
                value = Parse(text);
                return true;
            }
            catch (FormatException)
            {
                value = null;
                return false;
            }
        }

        /// <summary>
        /// Writes the request bodies WebDriverAgent takes: null, bool, string, the integer types, double (finite only),
        /// IEnumerable&lt;KeyValuePair&lt;string, object&gt;&gt; as an object in its own order, and any other IEnumerable as
        /// an array. Compact, invariant culture, every non-ASCII and control character escaped.
        /// </summary>
        /// <exception cref="ArgumentException">A value of another type, or NaN or infinity.</exception>
        public static string Write(object value)
        {
            var sb = new StringBuilder();
            WriteValue(sb, value, 0);
            return sb.ToString();
        }

        private static void WriteValue(StringBuilder sb, object value, int depth)
        {
            if (depth > MaxDepth) throw new ArgumentException("JSON nested too deeply");
            switch (value)
            {
                case null:
                    sb.Append("null");
                    return;
                case bool b:
                    sb.Append(b ? "true" : "false");
                    return;
                case string s:
                    WriteString(sb, s);
                    return;
                case int i:
                    sb.Append(i.ToString(CultureInfo.InvariantCulture));
                    return;
                case long l:
                    sb.Append(l.ToString(CultureInfo.InvariantCulture));
                    return;
                case double d:
                    if (double.IsNaN(d) || double.IsInfinity(d)) throw new ArgumentException("JSON has no NaN or infinity");
                    sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                    return;
                case IEnumerable<KeyValuePair<string, object>> pairs:
                    sb.Append('{');
                    var first = true;
                    foreach (var pair in pairs)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        WriteString(sb, pair.Key ?? throw new ArgumentException("JSON object key is null"));
                        sb.Append(':');
                        WriteValue(sb, pair.Value, depth + 1);
                    }
                    sb.Append('}');
                    return;
                case System.Collections.IEnumerable items:
                    sb.Append('[');
                    var firstItem = true;
                    foreach (var item in items)
                    {
                        if (!firstItem) sb.Append(',');
                        firstItem = false;
                        WriteValue(sb, item, depth + 1);
                    }
                    sb.Append(']');
                    return;
                default:
                    throw new ArgumentException("no JSON form for " + value.GetType().Name);
            }
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20 || c > 0x7E) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        private sealed class Reader
        {
            private readonly string _s;
            private int _i;

            public Reader(string s) => _s = s;

            public bool AtEnd => _i >= _s.Length;
            public int Position => _i;

            public void SkipSpace()
            {
                while (_i < _s.Length && (_s[_i] == ' ' || _s[_i] == '\t' || _s[_i] == '\r' || _s[_i] == '\n')) _i++;
            }

            public object ReadValue(int depth)
            {
                if (depth > MaxDepth) throw new FormatException("JSON nested too deeply");
                if (AtEnd) throw new FormatException("unexpected end of JSON");
                var c = _s[_i];
                switch (c)
                {
                    case '{': return ReadObject(depth);
                    case '[': return ReadArray(depth);
                    case '"': return ReadString();
                    case 't': ReadWord("true"); return true;
                    case 'f': ReadWord("false"); return false;
                    case 'n': ReadWord("null"); return null;
                }
                if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber();
                throw new FormatException("unexpected '" + c + "' at " + _i);
            }

            private Dictionary<string, object> ReadObject(int depth)
            {
                var result = new Dictionary<string, object>(StringComparer.Ordinal);
                _i++;
                SkipSpace();
                if (Peek('}'))
                {
                    _i++;
                    return result;
                }
                while (true)
                {
                    SkipSpace();
                    if (!Peek('"')) throw new FormatException("expected a property name at " + _i);
                    var key = ReadString();
                    SkipSpace();
                    Expect(':');
                    SkipSpace();
                    result[key] = ReadValue(depth + 1);
                    SkipSpace();
                    if (!Peek(',')) break;
                    _i++;
                }
                Expect('}');
                return result;
            }

            private List<object> ReadArray(int depth)
            {
                var result = new List<object>();
                _i++;
                SkipSpace();
                if (Peek(']'))
                {
                    _i++;
                    return result;
                }
                while (true)
                {
                    SkipSpace();
                    result.Add(ReadValue(depth + 1));
                    SkipSpace();
                    if (!Peek(',')) break;
                    _i++;
                }
                Expect(']');
                return result;
            }

            private string ReadString()
            {
                Expect('"');
                var sb = new StringBuilder();
                while (true)
                {
                    if (AtEnd) throw new FormatException("unterminated string");
                    var c = _s[_i++];
                    if (c == '"') return sb.ToString();
                    if (c < ' ') throw new FormatException("control character in a string at " + (_i - 1));
                    if (c != '\\')
                    {
                        sb.Append(c);
                        continue;
                    }
                    if (AtEnd) throw new FormatException("unterminated escape");
                    var e = _s[_i++];
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
                            if (_i + 4 > _s.Length || !int.TryParse(_s.Substring(_i, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var code))
                            {
                                throw new FormatException("bad \\u escape at " + _i);
                            }
                            sb.Append((char)code);
                            _i += 4;
                            break;
                        default:
                            throw new FormatException("bad escape \\" + e);
                    }
                }
            }

            private double ReadNumber()
            {
                var start = _i;
                _i++;
                while (_i < _s.Length && "0123456789.eE+-".IndexOf(_s[_i]) >= 0) _i++;
                var text = _s.Substring(start, _i - start);
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) throw new FormatException("bad number '" + text + "'");
                return value;
            }

            private void ReadWord(string word)
            {
                if (_i + word.Length > _s.Length || string.CompareOrdinal(_s, _i, word, 0, word.Length) != 0) throw new FormatException("expected " + word + " at " + _i);
                _i += word.Length;
            }

            private bool Peek(char c) => _i < _s.Length && _s[_i] == c;

            private void Expect(char c)
            {
                if (!Peek(c)) throw new FormatException("expected '" + c + "' at " + _i);
                _i++;
            }
        }
    }
}
