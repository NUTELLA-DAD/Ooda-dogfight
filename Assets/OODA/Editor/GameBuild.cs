using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Ooda.EditorTools
{
    /// <summary>
    /// OODA > Build Windows Game: the scenes in the build settings into a Windows game. From the command line:
    /// Unity -batchmode -quit -projectPath . -executeMethod Ooda.EditorTools.GameBuild.Windows -buildPath C:\OODA\OODA.exe
    /// (without -buildPath it goes to Builds/Windows/OODA.exe in the project). Exits with 1 if the build fails.
    /// </summary>
    public static class GameBuild
    {
        [MenuItem("OODA/Build Windows Game")]
        public static void Windows()
        {
            ApplyIcon();
            BootSceneBuilder.Build();   // the game opens on the two planes spinning while the title loads
            MissileRacks.Fit();   // the wing hardpoints from the airframes as they are now (a game build can't read the meshes)
            var args = System.Environment.GetCommandLineArgs();
            int at = System.Array.IndexOf(args, "-buildPath");
            string path = at >= 0 && at + 1 < args.Length ? args[at + 1] : Path.Combine("Builds", "Windows", "OODA.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            var report = BuildPipeline.BuildPlayer(scenes, path, BuildTarget.StandaloneWindows64, BuildOptions.None);
            Debug.Log($"OODA: build {report.summary.result} to {path}: {report.summary.totalErrors} errors, {report.summary.totalSize / 1048576} MB");
            if (Application.isBatchMode && report.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        }

        const string IconPath = "Assets/OODA/Art/FurballIcon.png";   // Kenny's art, cropped square to 1024 px

        /// <summary>OODA > Set Game Icon: the red and blue turn circles as the exe's and the window's icon. It is kept in
        /// Project Settings, so builds from File > Build Profiles get it too; Build Windows Game sets it each time.</summary>
        [MenuItem("OODA/Set Game Icon")]
        public static void ApplyIcon()
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            if (tex == null) { Debug.LogWarning($"OODA: no game icon at {IconPath}; the build keeps Unity's."); return; }
            PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { tex }, IconKind.Any);
            AssetDatabase.SaveAssets();
            Debug.Log($"OODA: game icon set from {IconPath} ({tex.width}x{tex.height}).");
        }
    }
}
