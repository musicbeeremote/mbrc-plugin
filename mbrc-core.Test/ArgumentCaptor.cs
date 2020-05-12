using Moq;

namespace MusicBeeRemote.Test
{
    public class ArgumentCaptor<T>
    {
        public T Value { get; private set; }

        public T Capture()
        {
            return It.Is<T>(t => SaveValue(t));
        }

        private bool SaveValue(T t)
        {
            Value = t;
            return true;
        }
    }
}
