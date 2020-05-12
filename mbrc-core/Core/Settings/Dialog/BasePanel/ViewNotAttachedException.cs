using System;
using System.Runtime.Serialization;

namespace MusicBeeRemote.Core.Settings.Dialog.BasePanel
{
    [Serializable]
    public class ViewNotAttachedException : Exception
    {
        public ViewNotAttachedException()
            : base(string.Empty)
        {
        }

        public ViewNotAttachedException(string message)
            : base(message)
        {
        }

        public ViewNotAttachedException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        protected ViewNotAttachedException(SerializationInfo serializationInfo, StreamingContext streamingContext)
        {
            throw new NotImplementedException();
        }
    }
}
