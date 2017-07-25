using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace MusicBeeRemote.PartyMode.Core.Helper
{
    public class CycledList<T> : BindingList<T>
    {
        private readonly long _limit;

        public CycledList(long maxElements)
        {
            _limit = maxElements;
        }

        public void AddRange(IEnumerable<T> elements)
        {
            var allElements = elements.ToList();
            var count = allElements.Count;
            if (count < _limit)
            {
                allElements.ForEach(Add);
            }
            else
            {
                foreach (var element in allElements.Skip((int) (count - _limit)))
                {
                    Add(element);
                }
            }
        }
        
        public new void Add(T item)
        {
            if (Count >= _limit)
            {
                RemoveAt(0);
                base.Add(item);
            }
            else
            {
                base.Add(item);
            }
        }
    }
}