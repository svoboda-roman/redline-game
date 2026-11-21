using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Redline.BuildTools
{
    /// <summary>
    /// Builds a Windows 64-bit player into Build/Windows/Redline.exe.
    /// Batch: Unity -batchmode -quit -projectPath . -executeMethod Redline.BuildTools.WindowsBuild.Run -logFile build.log
    /// </summary>
    public static class WindowsBuild
    {
        [MenuItem("Redline/Build Windows")]
        public static void Run()
        {
            string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Build", "Windows"));
            Directory.CreateDirectory(outDir);

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Path.Combine(outDir, "Redline.exe"),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            Debug.Log($"[WindowsBuild] Result: {report.summary.result}, errors: {report.summary.totalErrors}, output: {options.locationPathName}");
            if (Application.isBatchMode)
                EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
