using System.Collections;
using NUnit.Framework;
using PocketArbor.Application.Lifecycle;
using PocketArbor.Bootstrap;
using PocketArbor.Domain.Time;
using UnityEngine;
using UnityEngine.TestTools;

namespace PocketArbor.PlayMode.Tests
{
    public sealed class RootLifetimeScopeTests
    {
        [UnityTest]
        public IEnumerator ComposesCoreServicesAndAppliesFrameRate()
        {
            var go = new GameObject("RootLifetimeScope (test)");
            var scope = go.AddComponent<RootLifetimeScope>();
            yield return null;

            Assert.IsNotNull(scope.Container, "container was not built");
            Assert.IsNotNull(scope.Container.Resolve(typeof(IClock)));
            Assert.IsNotNull(scope.Container.Resolve(typeof(AppPauseState)));
            Assert.AreEqual(AppBootstrapper.InteractiveFrameRate, UnityEngine.Application.targetFrameRate);

            Object.Destroy(go);
        }
    }
}
