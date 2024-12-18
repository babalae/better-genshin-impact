// This code is distributed under MIT license. 
// Copyright (c) 2010-2018 George Mamaladze
// See license.txt or https://mit-license.org/

using System.Linq;

namespace Gma.System.MouseKeyHook
{
    /// <summary>
    ///     Describes key or key combination sequences. e.g. Control+Z,Z
    /// </summary>
    public class Sequence : SequenceBase<Combination>
    {
        private Sequence(Combination[] combinations) : base(combinations)
        {
        }

        /// <summary>
        ///     Creates an instance of sequence object from parameters representing keys or key combinations.
        /// </summary>
        /// <param name="combinations"></param>
        /// <returns></returns>
        public static Sequence Of(params Combination[] combinations)
        {
            return new Sequence(combinations);
        }

        /// <summary>
        ///     Creates an instance of sequnce object from string.
        ///     The string must contain comma ',' delimited list of strings describing keys or key combinations.
        ///     Examples: 'A,B,C' 'Alt+R,S', 'Shift+R,Alt+K'
        /// </summary>
        /// <param name="text"></param>
        /// <returns></returns>
        public static Sequence FromString(string text)
        {
            var parts = text.Split(',');
            var combinations = parts.Select(Combination.FromString).ToArray();
            return new Sequence(combinations);
        }
    }
}