using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Сборка из командной строки (batchmode) для macOS и Windows.
/// Вызов: Unity -batchmode -nographics -quit -projectPath <proj> -executeMethod CliBuild.BuildOSX
/// </summary>
public static class CliBuild
{
    private const string OutDir = "/Users/amirusupov/Desktop/NTO_Project/build";

    public static void BuildOSX()
    {
        var opts = new BuildPlayerOptions
        {
            scenes = EnabledScenes(),
            locationPathName = OutDir + "/Another_One.app",
            target = BuildTarget.StandaloneOSX,
            options = BuildOptions.CleanBuildCache
        };

        var report = BuildPipeline.BuildPlayer(opts);
        Debug.Log("CLI_BUILD_RESULT=" + report.summary.result + " ERRORS=" + report.summary.totalErrors + " SIZE_MB=" + (report.summary.totalSize / 1048576));
        EditorApplication.Exit(report.summary.totalErrors == 0 ? 0 : 1);
    }

    public static void BuildWindows()
    {
        var opts = new BuildPlayerOptions
        {
            scenes = EnabledScenes(),
            locationPathName = OutDir + "/Windows/Another_One.exe",
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.CleanBuildCache
        };

        var report = BuildPipeline.BuildPlayer(opts);
        Debug.Log("CLI_BUILD_RESULT=" + report.summary.result + " ERRORS=" + report.summary.totalErrors + " SIZE_MB=" + (report.summary.totalSize / 1048576));
        EditorApplication.Exit(report.summary.totalErrors == 0 ? 0 : 1);
    }

    private static string[] EnabledScenes()
    {
        var list = new List<string>();
        foreach (var s in EditorBuildSettings.scenes)
            if (s.enabled) list.Add(s.path);
        return list.ToArray();
    }
}
