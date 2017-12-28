using System.Windows.Forms;

namespace MusicBeeRemote.Core.ApiAdapters
{
    public interface IInvokeHandler
    {
        void Invoke(MethodInvoker function);
    }
}