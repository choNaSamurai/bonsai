using System;
using R3;

namespace PocketArbor.Application.Lifecycle
{
    /// <summary>
    /// Application-level pause state. Presentation/Bootstrap feeds OS lifecycle events in;
    /// use-cases (save, catch-up, audio pause) observe it. Engine-free by design (ADR-0002).
    /// </summary>
    public sealed class AppPauseState : IDisposable
    {
        readonly ReactiveProperty<bool> _isPaused = new ReactiveProperty<bool>(false);

        public ReadOnlyReactiveProperty<bool> IsPaused => _isPaused;

        public void SetPaused(bool paused) => _isPaused.Value = paused;

        public void Dispose() => _isPaused.Dispose();
    }
}
