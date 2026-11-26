using UnityEditor;
using UnityEngine;

namespace AnotherOne.EditorTools
{
    /// <summary>
    /// Навешивает мышиное управление на префаб игрока.
    ///
    /// MouseGameplayController и MouseHud добавляются на существующий Player.prefab.
    /// Так сцена и префаб остаются согласованными, а сами компоненты
    /// подключаются без ручной правки сериализованного YAML.
    /// </summary>
    public static class MouseControlSetup
    {
        private const string PlayerPrefabPath = "Assets/Prefabs/Player/Player.prefab";

        [MenuItem("Another One/Настроить мышиное управление")]
        public static void Setup()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            if (prefab == null)
            {
                Debug.LogError($"[MouseControlSetup] Не найден префаб {PlayerPrefabPath}");
                return;
            }

            var root = prefab.transform;
            var camera = root.GetComponentInChildren<Camera>(true);
            if (camera == null)
            {
                Debug.LogError("[MouseControlSetup] В префабе игрока нет камеры.");
                return;
            }

            // --- MouseGameplayController ---
            var gameplay = root.GetComponent<MouseGameplayController>();
            if (gameplay == null) gameplay = root.gameObject.AddComponent<MouseGameplayController>();

            var so = new SerializedObject(gameplay);
            so.FindProperty("controller").objectReferenceValue =
                root.GetComponent<FPSController>();
            so.FindProperty("interactor").objectReferenceValue =
                root.GetComponent<PlayerInteractor>();
            so.FindProperty("viewCamera").objectReferenceValue = camera;
            so.ApplyModifiedPropertiesWithoutUndo();

            // --- MouseHud: на канвас игрока ---
            var canvas = root.GetComponentInChildren<Canvas>(true);
            if (canvas == null)
            {
                Debug.LogError("[MouseControlSetup] В префабе игрока нет Canvas.");
                return;
            }

            var hud = canvas.gameObject.GetComponent<MouseHud>();
            if (hud == null) hud = canvas.gameObject.AddComponent<MouseHud>();

            var hudSo = new SerializedObject(hud);
            hudSo.FindProperty("gameplay").objectReferenceValue = gameplay;
            hudSo.FindProperty("canvas").objectReferenceValue = canvas;
            hudSo.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(gameplay);
            EditorUtility.SetDirty(hud);
            EditorUtility.SetDirty(prefab);
            AssetDatabase.SaveAssets();

            Debug.Log("[MouseControlSetup] Мышиное управление настроено на Player.prefab.");
        }
    }
}
