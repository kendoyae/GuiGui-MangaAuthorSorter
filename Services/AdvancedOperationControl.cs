using System;
using System.Threading;

namespace MangaAuthorSorter
{
    /// <summary>Pause/resume gate for streaming download and filtering workers.</summary>
    internal sealed class AdvancedOperationControl : IDisposable
    {
        private readonly ManualResetEventSlim _gate = new ManualResetEventSlim(true);
        private volatile bool _paused;
        public bool IsPaused { get { return _paused; } }
        public void Toggle()
        {
            if (_paused) { _paused=false; _gate.Set(); }
            else { _paused=true; _gate.Reset(); }
        }
        public void Resume() { _paused=false; _gate.Set(); }
        public void Checkpoint(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            _gate.Wait(token);
            token.ThrowIfCancellationRequested();
        }
        public void Dispose() { _gate.Set();_gate.Dispose(); }
    }
}
