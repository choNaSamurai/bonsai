using System;
using System.Reflection;
using NUnit.Framework;

namespace PocketArbor.Domain.Tests
{
    /// <summary>
    /// Guards ARCHITECTURE §3 on the compiled assemblies. asmdef references already block most violations,
    /// but Unity silently adds platform extension DLLs (UnityEditor.iOS.Extensions.Xcode, Unity.Android.Gradle)
    /// even to noEngineReferences assemblies, so we verify what was actually referenced.
    /// </summary>
    public sealed class LayerDependencyTests
    {
        [TestCase("PocketArbor.Domain")]
        [TestCase("PocketArbor.Application")]
        public void PureLayers_DoNotReferenceUnity(string assemblyName)
        {
            foreach (var reference in Load(assemblyName).GetReferencedAssemblies())
            {
                Assert.IsFalse(reference.Name.StartsWith("Unity", StringComparison.Ordinal),
                    assemblyName + " must stay engine-free but references " + reference.Name);
            }
        }

        [TestCase("PocketArbor.Domain", "PocketArbor.Application")]
        [TestCase("PocketArbor.Domain", "PocketArbor.Infrastructure")]
        [TestCase("PocketArbor.Domain", "PocketArbor.Presentation")]
        [TestCase("PocketArbor.Domain", "PocketArbor.Bootstrap")]
        [TestCase("PocketArbor.Application", "PocketArbor.Infrastructure")]
        [TestCase("PocketArbor.Application", "PocketArbor.Presentation")]
        [TestCase("PocketArbor.Application", "PocketArbor.Bootstrap")]
        [TestCase("PocketArbor.Infrastructure", "PocketArbor.Presentation")]
        [TestCase("PocketArbor.Infrastructure", "PocketArbor.Bootstrap")]
        [TestCase("PocketArbor.Presentation", "PocketArbor.Infrastructure")]
        [TestCase("PocketArbor.Presentation", "PocketArbor.Bootstrap")]
        // PA-102V: editor-only dev tools must never be referenced by shipping assemblies.
        [TestCase("PocketArbor.Domain", "PocketArbor.DevTools")]
        [TestCase("PocketArbor.Application", "PocketArbor.DevTools")]
        [TestCase("PocketArbor.Infrastructure", "PocketArbor.DevTools")]
        [TestCase("PocketArbor.Presentation", "PocketArbor.DevTools")]
        [TestCase("PocketArbor.Bootstrap", "PocketArbor.DevTools")]
        public void Layer_DoesNotReference(string assemblyName, string forbidden)
        {
            foreach (var reference in Load(assemblyName).GetReferencedAssemblies())
            {
                Assert.AreNotEqual(forbidden, reference.Name, assemblyName + " must not depend on " + forbidden);
            }
        }

        static Assembly Load(string name)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetName().Name == name) return assembly;
            }
            return Assembly.Load(name);
        }
    }
}
