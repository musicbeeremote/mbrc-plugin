using System;

namespace MusicBeePlugin.Services.Core
{
    public interface IStateMonitor : IDisposable
    {
        void StartMonitoring();
        void StopMonitoring();
    }
}
