using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Xsolla.SDK
{
    /// <summary>
    /// Creates <c>Assets/Plugins/Android</c> when it is missing and the active build target is Android.
    /// </summary>
    /// <remarks>
    /// In a new project the Android Resolver of the External Dependency Manager for Unity (EDM4U 1.2.190) throws a
    /// <see cref="DirectoryNotFoundException"/> until this folder exists, and its Gradle fallback then fails as well.
    /// The folder is created on disk at once, so that it exists before a <c>-quit</c> exit or an <c>-executeMethod</c>
    /// resolve in batch mode. Asset operations are not safe while <see cref="InitializeOnLoadAttribute"/> runs, so plain
    /// file IO is used, and Unity writes the <c>.meta</c> file on its next asset refresh. An existing folder is never
    /// touched, and other build targets leave the project as it is.
    /// </remarks>
    [InitializeOnLoad]
    internal sealed class XsollaAndroidPluginsFolder : IActiveBuildTargetChanged
    {
        private const string ParentFolder = "Assets/Plugins";
        private const string FolderName = "Android";
        private const string FolderPath = ParentFolder + "/" + FolderName;

        /// <summary>
        /// Checks the folder on every editor load and domain reload.
        /// </summary>
        static XsollaAndroidPluginsFolder()
        {
            EnsureFolder(EditorUserBuildSettings.activeBuildTarget);
        }

        /// <inheritdoc />
        public int callbackOrder => 0;

        /// <summary>
        /// Creates the folder when the user switches the active build target to Android.
        /// </summary>
        public void OnActiveBuildTargetChanged(BuildTarget previousTarget, BuildTarget newTarget)
        {
            EnsureFolder(newTarget);
        }

        /// <summary>
        /// Creates the folder when <paramref name="activeTarget"/> is Android and the folder does not exist yet.
        /// </summary>
        private static void EnsureFolder(BuildTarget activeTarget)
        {
            if (activeTarget == BuildTarget.Android && !Directory.Exists(FolderPath))
            {
                Directory.CreateDirectory(FolderPath);
                Debug.Log($"[XsollaSDK] Created {FolderPath}, which the Android Resolver of the External Dependency Manager needs to resolve the SDK's native dependencies.");
            }
        }
    }
}
