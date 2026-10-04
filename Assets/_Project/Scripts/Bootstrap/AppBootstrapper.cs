using VContainer.Unity;

namespace PocketArbor.Bootstrap
{
    /// <summary>
    /// Applies process-wide runtime settings once the container is built (ARCHITECTURE §6.5).
    /// </summary>
    public sealed class AppBootstrapper : IStartable
    {
        /// <summary>Interactive frame rate; idle throttling via OnDemandRendering comes later (PA-103+).</summary>
        public const int InteractiveFrameRate = 120;

        public void Start()
        {
            // Fully qualified: inside PocketArbor.* the name "Application" resolves to our namespace.
            UnityEngine.QualitySettings.vSyncCount = 0;
            UnityEngine.Application.targetFrameRate = InteractiveFrameRate;
        }
    }
}
