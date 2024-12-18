// This code is distributed under MIT license. 
// Copyright (c) 2010-2018 George Mamaladze
// See license.txt or https://mit-license.org/

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace Gma.System.MouseKeyHook.Implementation
{
    internal class Chord : IEnumerable<Keys>
    {
        private readonly Keys[] _keys;

        internal Chord(IEnumerable<Keys> additionalKeys)
        {
            _keys = additionalKeys.Select(k => k.Normalize()).OrderBy(k => k).ToArray();
        }

        public int Count
        {
            get { return _keys.Length; }
        }

        public IEnumerator<Keys> GetEnumerator()
        {
            return _keys.Cast<Keys>().GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public override string ToString()
        {
            return string.Join("+", _keys);
        }

        public static Chord FromString(string chord)
        {
            var parts = chord
                .Split('+')
                .Select(p => Enum.Parse(typeof(Keys), p))
                .Cast<Keys>();
            var stack = new Stack<Keys>(parts);
            return new Chord(stack);
        }

        protected bool Equals(Chord other)
        {
            if (_keys.Length != other._keys.Length) return false;
            return _keys.SequenceEqual(other._keys);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != GetType()) return false;
            return Equals((Chord) obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (_keys.Length + 13) ^
                       ((_keys.Length != 0 ? (int) _keys[0] ^ (int) _keys[_keys.Length - 1] : 0) * 397);
            }
        }
    }
}