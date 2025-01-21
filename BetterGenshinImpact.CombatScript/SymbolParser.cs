using System;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace BetterGenshinImpact.CombatScript;

public static class SymbolParser
{
    public static ScriptUnit Parse(ReadOnlySpan<char> raw)
    {
        ImmutableArray<ISymbol>.Builder symbols = ImmutableArray.CreateBuilder<ISymbol>();
        ParseLines(raw, symbols);
        return new(symbols.ToImmutable());
    }

    private static void ParseLines(ReadOnlySpan<char> raw, ImmutableArray<ISymbol>.Builder symbols)
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

            if (ParseLine(raw[range]) is { } symbol)
            {
                symbols.Add(symbol);
            }

            symbols.Add(lineBreakTrivia);
        }
    }

    private static ISymbol? ParseLine(ReadOnlySpan<char> raw)
    {
        if (raw.IsEmpty)
        {
            return default;
        }

        if (raw.StartsWith("//"))
        {
            return new CommentSymbol(raw[2..].ToString());
        }
        
        int indexOfSpace = raw.IndexOf(' ');
        ReadOnlySpan<char> avatarIdentifier = raw[..indexOfSpace];
        AvatarSymbol avatarSymbol = new(avatarIdentifier.ToString());
        ImmutableArray<TriviaSymbol> triviaList = ParseTriviaList(raw[indexOfSpace..], out int advanced);
        InstructionListSymbol listSymbol = ParseInstructionList(raw[(indexOfSpace + advanced)..]);
        return new AvatarInstructionListSymbol(avatarSymbol, triviaList, listSymbol);
    }

    private static InstructionListSymbol ParseInstructionList(ReadOnlySpan<char> raw)
    {
        ImmutableArray<InstructionSymbol>.Builder builder = ImmutableArray.CreateBuilder<InstructionSymbol>();
        
        foreach (Range range in raw.Split(','))
        {
            ReadOnlySpan<char> current = raw[range];
            ImmutableArray<TriviaSymbol> leadingTriviaList = ParseTriviaList(current, out int advanced);
            
            if (ParseInstruction(current[advanced..], leadingTriviaList) is { } symbol)
            {
                builder.Add(symbol);
            }
        }
        
        return new(builder.ToImmutable());
    }

    private static InstructionSymbol? ParseInstruction(ReadOnlySpan<char> raw, ImmutableArray<TriviaSymbol> leadingTriviaList, TriviaSymbol? tailingTrivia)
    {
        if (raw.IsEmpty)
        {
            return default;
        }
        
        switch (raw[0])
        {
            // attack|a
            case 'a':
                break;
            // burst
            case 'b':
                return new BurstSymbol(false, [], leadingTriviaList, tailingTrivia);
                break;
            // charge
            case 'c':
                break;
            // dash|d
            case 'd':
                break;
            // jump|j
            case 'j':
                break;
            // q
            case 'q':
                return new BurstSymbol(true, [], leadingTriviaList, tailingTrivia);
                break;
            // skill|s
            case 's':
                break;
            // wait|walk|w
            case 'w':
                break;
        }

        return default;
    }
    
    // Handle ','&' ' 
    private static ImmutableArray<TriviaSymbol> ParseTriviaList(ReadOnlySpan<char> raw, out int advanced)
    {
        advanced = 0;
        if (raw.IsEmpty)
        {
            return ImmutableArray<TriviaSymbol>.Empty;
        }

        ImmutableArray<TriviaSymbol>.Builder builder = ImmutableArray.CreateBuilder<TriviaSymbol>();
        
        int spaceCount = 0;
        foreach (ref readonly char value in raw)
        {
            advanced++;
            if (value is ' ')
            {
                spaceCount++;
            }
            else
            {
                if (spaceCount is not 0)
                {
                    builder.Add(new SpaceTriviaSymbol(spaceCount));
                    spaceCount = 0;
                }
                
                if (value is ',')
                {
                    builder.Add(new CommaTriviaSymbol());
                }
                else
                {
                    break;
                }
            }
        }

        return builder.ToImmutable();
    }
}