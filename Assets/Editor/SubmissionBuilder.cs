using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace AnotherOne.EditorTools
{
    /// <summary>
    /// Пакетная сборка для сдачи (запускается в batch mode через Unity CLI).
    ///
    /// Шаги:
    ///  1) пересобирает TMP-шрифт с кириллицей (статический атлас — иначе
    ///     глифы стираются при пересохранении проекта и кириллица пропадает
    ///     из сборки);
    ///  2) собирает Windows x64 (этого требует регламент);
    ///  3) собирает macOS (нужен для проверки и демонстрации на этой машине).
    ///
    /// Запуск:
    ///   unity run &lt;project&gt; -- -batchmode -quit -executeMethod
    ///   AnotherOne.EditorTools.SubmissionBuilder.BuildAll
    /// </summary>
    public static class SubmissionBuilder
    {
        private static readonly string[] Scenes =
        {
            "Assets/Scenes/Amir.unity",
            "Assets/Scenes/Menu_Scene.unity",
            "Assets/Scenes/Credits_Scene.unity"
        };

        private const string BuildRoot = "/Users/amirusupov/Desktop/NTO_Project/build";

        [MenuItem("Another One/Собрать всё для сдачи (Windows + macOS)")]
        public static void BuildAll()
        {
            Debug.Log("[SubmissionBuilder] === Начало сборки ===");

            CyrillicFontBuilder.Build();

            bool windows = Build(BuildTarget.StandaloneWindows64,
                Path.Combine(BuildRoot, "Windows", "Another_One.exe"));

            bool mac = Build(BuildTarget.StandaloneOSX,
                Path.Combine(BuildRoot, "Another_One.app"));

            Debug.Log($"[SubmissionBuilder] ИТОГ: windows={windows} macos={mac}");

            if (!windows || !mac)
            {
                Debug.LogError("[SubmissionBuilder] Сборка завершилась с ошибками.");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }

            Debug.Log("[SubmissionBuilder] === Сборка завершена успешно ===");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        [MenuItem("Another One/Собрать только Windows x64")]
        public static void BuildWindowsOnly()
        {
            CyrillicFontBuilder.Build();
            bool ok = Build(BuildTarget.StandaloneWindows64,
                Path.Combine(BuildRoot, "Windows", "Another_One.exe"));
            Debug.Log($"[SubmissionBuilder] Windows: {ok}");
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        private static bool Build(BuildTarget target, string outputPath)
        {
            string dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var options = new BuildPlayerOptions
            {
                scenes = Scenes,
                locationPathName = outputPath,
                target = target,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            Debug.Log($"[SubmissionBuilder] {target}: result={summary.result}, " +
                      $"size={summary.totalSize / (1024 * 1024)} MB, " +
                      $"errors={summary.totalErrors}, warnings={summary.totalWarnings}");

            foreach (var step in report.steps)
            {
                if (step.messages == null) continue;
                foreach (var message in step.messages)
                {
                    if (message.type == LogType.Error || message.type == LogType.Exception)
                        Debug.LogError($"[{step.name}] {message.content}");
                    else if (message.type == LogType.Warning)
                        Debug.LogWarning($"[{step.name}] {message.content}");
                }
            }

            return summary.result == BuildResult.Succeeded && summary.totalErrors == 0;
        }
    }
}
