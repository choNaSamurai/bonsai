#if UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

namespace PocketArbor.Editor.Build
{
    /// <summary>
    /// Patches Info.plist after the Xcode project is generated (ARCHITECTURE §6.5).
    /// </summary>
    public static class IosPostProcessBuild
    {
        [PostProcessBuild(100)]
        public static void OnPostProcessBuild(BuildTarget target, string pathToBuiltProject)
        {
            if (target != BuildTarget.iOS) return;

            string plistPath = Path.Combine(pathToBuiltProject, "Info.plist");
            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);

            // Unlocks 120 Hz on ProMotion iPhones; without it iOS caps CADisplayLink at 60 Hz.
            plist.root.SetBoolean("CADisableMinimumFrameDurationOnPhone", true);

            plist.WriteToFile(plistPath);
        }
    }
}
#endif
