// This code is distributed under MIT license. 
// Copyright (c) 2010-2018 George Mamaladze
// See license.txt or https://mit-license.org/

using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Gma.System.MouseKeyHook
{
    /// <summary>
    ///     Describes a sequence of generic objects.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public abstract class SequenceBase<T> : IEnumerable<T>
    {
        private readonly T[] _elements;

        /// <summary>
        ///     Creates an instance of sequnce from sequnce elements.
        /// </summary>
        /// <param name="elements"></param>
        protected SequenceBase(params T[] elements)
        {
            _elements = elements;
        }

        /// <summary>
        ///     Number of elements in the sequnce.
        /// </summary>
        public int Length
        {
            get { return _elements.Length; }
        }

        /// <inheritdoc />
        public IEnumerator<T> GetEnumerator()
        {
            return _elements.Cast<T>().GetEnumerator();
        }

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return string.Join(",", _elements);
        }

        /// <inheritdoc />
        protected bool Equals(SequenceBase<T> other)
        {
            if (_elements.Length != other._elements.Length) return false;
            return _elements.SequenceEqual(other._elements);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != GetType()) return false;
            return Equals((SequenceBase<T>) obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            unchecked
            {
                return (_elements.Length + 13) ^
                       ((_elements.Length != 0
                            ? _elements[0].GetHashCode() ^ _elements[_elements.Length - 1].GetHashCode()
                            : 0) * 397);
            }
        }
    }
}