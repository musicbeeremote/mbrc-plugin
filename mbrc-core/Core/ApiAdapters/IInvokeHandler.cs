using System.Windows.Forms;

namespace MusicBeeRemoteCore.Core.ApiAdapters
{
    public interface IInvokeHandler
    {
        void Invoke(MethodInvoker function);
    }
}