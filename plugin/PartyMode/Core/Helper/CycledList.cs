using System.Collections;
using System.Collections.Generic;

namespace MusicBeePlugin.PartyMode.Core.Helper
{
    public class CycledList<T> : IList<T>
    {
        private readonly IList<T> _list = new List<T>();
        private readonly long _limit;

        public CycledList(long maxElements)
        {
            _limit = maxElements;
        }

        #region Implementation of IEnumerable

        public IEnumerator<T> GetEnumerator()
        {
            return _list.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        #endregion

        #region Implementation of ICollection<T>

        public void Add(T item)
        {
            if (_list.Count >= _limit)
            {
                _list.RemoveAt(0);
                _list.Add(item);
            }
            else
            {
                _list.Add(item);
            }
        }

        public void Clear()
        {
            _list.Clear();
        }

        public bool Contains(T item)
        {
            return _list.Contains(item);
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            _list.CopyTo(array, arrayIndex);
        }

        public bool Remove(T item)
        {
            return _list.Remove(item);
        }

        public int Count => _list.Count;

        public bool IsReadOnly => _list.IsReadOnly;

        #endregion

        #region Implementation of IList<T>

        public int IndexOf(T item)
        {
            return _list.IndexOf(item);
        }

        public void Insert(int index, T item)
        {
            _list.Insert(index, item);
        }

        public void RemoveAt(int index)
        {
            _list.RemoveAt(index);
        }

        public T this[int index]
        {
            get { return _list[index]; }
            set { _list[index] = value; }
        }

        #endregion
    }
}