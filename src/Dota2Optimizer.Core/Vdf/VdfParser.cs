using System.Text;

namespace Dota2Optimizer.Core.Vdf;

public sealed class VdfParseException : Exception
{
    public VdfParseException(string message, int line)
        : base($"VDF: {message} (строка {line})")
    {
        Line = line;
    }

    public int Line { get; }
}

/// <summary>
/// Parser for the text KeyValues format used by Steam (libraryfolders.vdf, appmanifest_*.acf,
/// localconfig.vdf, loginusers.vdf) and Source configs. Supports quoted/unquoted tokens,
/// escape sequences, // comments and [$PLATFORM] conditionals (which are ignored).
/// </summary>
public static class VdfParser
{
    /// <summary>Parses a document. The returned node is a synthetic root whose children are the top-level keys.</summary>
    public static VdfNode ParseDocument(string text)
    {
        var lexer = new Lexer(text);
        var root = new VdfNode("<root>");
        ParseBody(lexer, root, topLevel: true);
        return root;
    }

    /// <summary>Parses and returns the first top-level object (e.g. "libraryfolders").</summary>
    public static VdfNode Parse(string text)
    {
        var doc = ParseDocument(text);
        return doc.Children.FirstOrDefault() ?? throw new VdfParseException("пустой документ", 1);
    }

    private static void ParseBody(Lexer lexer, VdfNode parent, bool topLevel)
    {
        while (true)
        {
            var token = lexer.Next();
            if (token is null)
            {
                if (!topLevel)
                {
                    throw new VdfParseException("неожиданный конец файла, не хватает '}'", lexer.Line);
                }

                return;
            }

            if (token.Kind == TokenKind.CloseBrace)
            {
                if (topLevel)
                {
                    throw new VdfParseException("лишняя '}'", lexer.Line);
                }

                return;
            }

            if (token.Kind != TokenKind.String)
            {
                throw new VdfParseException("ожидался ключ", lexer.Line);
            }

            var key = token.Text;
            var next = lexer.Next() ?? throw new VdfParseException($"нет значения для ключа \"{key}\"", lexer.Line);
            if (next.Kind == TokenKind.OpenBrace)
            {
                var obj = parent.Add(new VdfNode(key));
                ParseBody(lexer, obj, topLevel: false);
            }
            else if (next.Kind == TokenKind.String)
            {
                parent.Add(new VdfNode(key, next.Text));
            }
            else
            {
                throw new VdfParseException($"неожиданная '}}' после ключа \"{key}\"", lexer.Line);
            }
        }
    }

    private enum TokenKind
    {
        String,
        OpenBrace,
        CloseBrace,
    }

    private sealed record Token(TokenKind Kind, string Text);

    private sealed class Lexer
    {
        private readonly string _s;
        private int _i;

        public Lexer(string s)
        {
            // Strip UTF-8 BOM if present.
            _s = s.Length > 0 && s[0] == '\uFEFF' ? s[1..] : s;
        }

        public int Line { get; private set; } = 1;

        public Token? Next()
        {
            while (true)
            {
                SkipWhitespace();
                if (_i >= _s.Length)
                {
                    return null;
                }

                var c = _s[_i];
                if (c == '/' && _i + 1 < _s.Length && _s[_i + 1] == '/')
                {
                    while (_i < _s.Length && _s[_i] != '\n')
                    {
                        _i++;
                    }

                    continue;
                }

                // Platform conditionals like [$WIN32] follow a value; they don't change the structure.
                if (c == '[')
                {
                    var end = _s.IndexOf(']', _i);
                    _i = end < 0 ? _s.Length : end + 1;
                    continue;
                }

                if (c == '{')
                {
                    _i++;
                    return new Token(TokenKind.OpenBrace, "{");
                }

                if (c == '}')
                {
                    _i++;
                    return new Token(TokenKind.CloseBrace, "}");
                }

                return c == '"' ? ReadQuoted() : ReadUnquoted();
            }
        }

        private void SkipWhitespace()
        {
            while (_i < _s.Length && char.IsWhiteSpace(_s[_i]))
            {
                if (_s[_i] == '\n')
                {
                    Line++;
                }

                _i++;
            }
        }

        private Token ReadQuoted()
        {
            var startLine = Line;
            _i++; // opening quote
            var sb = new StringBuilder();
            while (_i < _s.Length)
            {
                var c = _s[_i++];
                if (c == '"')
                {
                    return new Token(TokenKind.String, sb.ToString());
                }

                if (c == '\\' && _i < _s.Length)
                {
                    var e = _s[_i++];
                    sb.Append(e switch
                    {
                        'n' => '\n',
                        't' => '\t',
                        '\\' => '\\',
                        '"' => '"',
                        _ => e,
                    });
                    if (e is not ('n' or 't' or '\\' or '"'))
                    {
                        // Unknown escape (e.g. Windows path "C:\Games" written unescaped): keep the backslash.
                        sb.Insert(sb.Length - 1, '\\');
                    }

                    continue;
                }

                if (c == '\n')
                {
                    Line++;
                }

                sb.Append(c);
            }

            throw new VdfParseException("незакрытая кавычка", startLine);
        }

        private Token ReadUnquoted()
        {
            var start = _i;
            while (_i < _s.Length && !char.IsWhiteSpace(_s[_i]) && _s[_i] is not ('{' or '}' or '"'))
            {
                _i++;
            }

            return new Token(TokenKind.String, _s[start.._i]);
        }
    }
}
