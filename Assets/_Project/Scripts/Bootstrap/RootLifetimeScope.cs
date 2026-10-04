using PocketArbor.Application.Lifecycle;
using PocketArbor.Domain.Time;
using PocketArbor.Infrastructure.Time;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace PocketArbor.Bootstrap
{
    /// <summary>
    /// Composition root (ADR-0002). The only place that knows concrete implementations.
    /// Lives on a single GameObject in Main.unity.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RootLifetimeScope : LifetimeScope
    {
        AppPauseState _pauseState;

        protected override void Configure(IContainerBuilder builder)
        {
            builder.Register<SystemClock>(Lifetime.Singleton).As<IClock>();
            builder.Register<AppPauseState>(Lifetime.Singleton);
            builder.RegisterEntryPoint<AppBootstrapper>();

            // Startup-only callback: resolving here keeps OnApplicationPause allocation-free.
            builder.RegisterBuildCallback(container => _pauseState = container.Resolve<AppPauseState>());
        }

        void OnApplicationPause(bool paused)
        {
            if (_pauseState != null) _pauseState.SetPaused(paused);
        }
    }
}
