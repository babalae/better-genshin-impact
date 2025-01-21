using System;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace BetterGenshinImpact.CombatScript;

public sealed class SymbolParser
{
    public ScriptUnit Parse(ReadOnlySpan<char> raw)
    {
        ImmutableArray<ISymbol>.Builder symbols = ImmutableArray.CreateBuilder<ISymbol>();
        ParseLines(raw, symbols);
        return new(symbols.ToImmutable());
    }

    private void ParseLines(ReadOnlySpan<char> raw, ImmutableArray<ISymbol>.Builder symbols)
    {
        bool skipNextRange = false;
        ref readonly char end = ref raw[^1];

        foreach(Range range in raw.SplitAny(['\r', '\n', ';']))
        {
            if (skipNextRange)
            {
                skipNextRange = false;
                continue;
            }

            int offset = range.End.GetOffset(raw.Length);
            if (offset >= raw.Length)
            {
                break;
            }

            ref readonly char peek = ref raw[offset];

            LineBreakTriviaSymbol lineBreakTrivia;
            if (peek is '\r')
            {
                if (Unsafe.IsAddressLessThan(in peek, in end) && Unsafe.Add(ref Unsafe.AsRef(in peek), 1) is '\n')
                {
                    // It's a CRLF
                    lineBreakTrivia = new("\r\n");
                    skipNextRange = true;
                }
                else
                {
                    // It's a CR
                    lineBreakTrivia = new("\r");
                }
            }
            else if (peek is '\n')
            {
                // It's a LF
                lineBreakTrivia = new("\n");
            }
            else if (peek is ';')
            {
                lineBreakTrivia = new(";");
            }
            else
            {
                throw new InvalidOperationException($"Failed to parse line break trivia at {range}.");
            }

            ReadOnlySpan<char> currentSpan = raw[range];

            symbols.Add(lineBreakTrivia);
        }
    }
}