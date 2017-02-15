using System.Collections;
using System.Collections.Generic;

namespace mbrcPartyMode.Helper
{

   public class CycledList<T> : IList<T>
    {
        private readonly IList<T> list = new List<T>();
        private readonly long limit;

        public CycledList(long maxElements)
        {
            limit = maxElements;
        }

        #region Implementation of IEnumerable

        public IEnumerator<T> GetEnumerator()
        {
            return list.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        #endregion

        #region Implementation of ICollection<T>

        public void Add(T item)
        {
            if (list.Count >= this.limit)
            {
                list.RemoveAt(0);
                list.Add(item);
               
            }
            else
            {
                list.Add(item);
            }
           
        }

        public void Clear()
        {
            list.Clear();
        }

        public bool Contains(T item)
        {
            return list.Contains(item);
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            list.CopyTo(array, arrayIndex);
        }

        public bool Remove(T item)
        {
            return list.Remove(item);
        }

        public int Count
        {
            get { return list.Count; }
        }

        public bool IsReadOnly
        {
            get { return list.IsReadOnly; }
        }

        #endregion

        #region Implementation of IList<T>

        public int IndexOf(T item)
        {
            return list.IndexOf(item);
        }

        public void Insert(int index, T item)
        {
            list.Insert(index, item);
        }

        public void RemoveAt(int index)
        {
            list.RemoveAt(index);
        }

        public T this[int index]
        {
            get { return list[index]; }
            set { list[index] = value; }
        }

        #endregion



      
    }

}