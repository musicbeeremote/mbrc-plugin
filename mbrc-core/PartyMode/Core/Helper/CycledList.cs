using System.ComponentModel;

namespace MusicBeeRemote.PartyMode.Core.Helper
{
    public class CycledList<T> : BindingList<T>
    {
        private readonly long _limit;

        public CycledList(long maxElements)
        {
            _limit = maxElements;
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