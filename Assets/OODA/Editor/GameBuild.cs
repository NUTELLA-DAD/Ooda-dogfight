using System.IO;
using System.Linq;
using UnityEditor;
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
    }
}
