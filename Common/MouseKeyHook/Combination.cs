// This code is distributed under MIT license. 
// Copyright (c) 2010-2018 George Mamaladze
// See license.txt or https://mit-license.org/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Gma.System.MouseKeyHook.Implementation;

namespace Gma.System.MouseKeyHook
{
    /// <summary>
    ///     Used to represent a key combination as frequently used in application as shortcuts.
    ///     e.g. Alt+Shift+R. This combination is triggered when 'R' is pressed after 'Alt' and 'Shift' are already down.
    /// </summary>
    public class Combination
    {
        private readonly Chord _chord;

        private Combination(Keys triggerKey, IEnumerable<Keys> chordKeys)
            : this(triggerKey, new Chord(chordKeys))
        {
        }

        private Combination(Keys triggerKey, Chord chord)
        {
            TriggerKey = triggerKey.Normalize();
            _chord = chord;
        }

        /// <summary>
        ///     Last key which triggers the combination.
        /// </summary>
        public Keys TriggerKey { get; }

        /// <summary>
        ///     Keys which all must be alredy down when trigger key is pressed.
        /// </summary>
        public IEnumerable<Keys> Chord
        {
            get { return _chord; }
        }

        /// <summary>
        ///     Number of chord (modifier) keys which must be already down when the trigger key is pressed.
        /// </summary>
        public int ChordLength
        {
            get { return _chord.Count; }
        }

        /// <summary>
        ///     A chainable builder method to simplify chord creation. Used along with <see cref="TriggeredBy" />,
        ///     <see cref="With" />, <see cref="Control" />, <see cref="Shift" />, <see cref="Alt" />.
        /// </summary>
        /// <param name="key"></param>
        public static Combination TriggeredBy(Keys key)
        {
            return new Combination(key, (IEnumerable<Keys>) new Chord(Enumerable.Empty<Keys>()));
        }

        /// <summary>
        ///     A chainable builder method to simplify chord creation. Used along with <see cref="TriggeredBy" />,
        ///     <see cref="With" />, <see cref="Control" />, <see cref="Shift" />, <see cref="Alt" />.
        /// </summary>
        /// <param name="key"></param>
        public Combination With(Keys key)
        {
            return new Combination(TriggerKey, Chord.Concat(Enumerable.Repeat(key, 1)));
        }

        /// <summary>
        ///     A chainable builder method to simplify chord creation. Used along with <see cref="TriggeredBy" />,
        ///     <see cref="With" />, <see cref="Control" />, <see cref="Shift" />, <see cref="Alt" />.
        /// </summary>
        public Combination Control()
        {
            return With(Keys.Control);
        }

        /// <summary>
        ///     A chainable builder method to simplify chord creation. Used along with <see cref="TriggeredBy" />,
        ///     <see cref="With" />, <see cref="Control" />, <see cref="Shift" />, <see cref="Alt" />.
        /// </summary>
        public Combination Alt()
        {
            return With(Keys.Alt);
        }

        /// <summary>
        ///     A chainable builder method to simplify chord creation. Used along with <see cref="TriggeredBy" />,
        ///     <see cref="With" />, <see cref="Control" />, <see cref="Shift" />, <see cref="Alt" />.
        /// </summary>
        public Combination Shift()
        {
            return With(Keys.Shift);
        }


        /// <inheritdoc />
        public override string ToString()
        {
            return string.Join("+", Chord.Concat(Enumerable.Repeat(TriggerKey, 1)));
        }

        /// <summary>
        ///     TriggeredBy a chord from any string like this 'Alt+Shift+R'.
        ///     Nothe that the trigger key must be the last one.
        /// </summary>
        public static Combination FromString(string trigger)
        {
            var parts = trigger
                .Split('+')
                .Select(p => Enum.Parse(typeof(Keys), p))
                .Cast<Keys>();
            var stack = new Stack<Keys>(parts);
            var triggerKey = stack.Pop();
            return new Combination(triggerKey, stack);
        }

        /// <inheritdoc />
        protected bool Equals(Combination other)
        {
            return
                TriggerKey == other.TriggerKey
                && Chord.Equals(other.Chord);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != GetType()) return false;
            return Equals((Combination) obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return Chord.GetHashCode() ^
                   (int) TriggerKey;
        }
    }
}