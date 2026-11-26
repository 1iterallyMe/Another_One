using System;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Загрузка сцен по имени с запасным путём через Build Settings.
/// В редакторе Application.CanStreamedLevelBeLoaded(name) может возвращать false,
/// хотя сцена добавлена в Build Settings — поэтому имя резолвится в индекс.
/// </summary>
public static class SceneLoadUtility
{
    /// <summary>Индекс сцены в Build Settings по её имени (без пути и расширения). -1 — не найдена.</summary>
    public static int ResolveIndex(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName)) return -1;

        int count = SceneManager.sceneCountInBuildSettings;
        for (int i = 0; i < count; i++)
        {
            string path = SceneUtility.GetScenePathByBuildIndex(i);
            if (string.IsNullOrEmpty(path)) continue;

            string name = Path.GetFileNameWithoutExtension(path);
            if (string.Equals(name, sceneName, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    /// <summary>Есть ли сцена в Build Settings (работает и в редакторе, и в билде).</summary>
    public static bool Exists(string sceneName) => ResolveIndex(sceneName) >= 0;

    public static AsyncOperation LoadAsync(string sceneName)
    {
        int index = ResolveIndex(sceneName);
        if (index < 0)
        {
            Debug.LogError($"[SceneLoad] Сцена «{sceneName}» не найдена в Build Settings.");
            return null;
        }

        return SceneManager.LoadSceneAsync(index);
    }

    public static void Load(string sceneName)
    {
        int index = ResolveIndex(sceneName);
        if (index < 0)
        {
            Debug.LogError($"[SceneLoad] Сцена «{sceneName}» не найдена в Build Settings.");
            return;
        }

        SceneManager.LoadScene(index);
    }
}
