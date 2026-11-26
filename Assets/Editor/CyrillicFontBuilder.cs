using TMPro;
using UnityEditor;
using UnityEngine;

namespace AnotherOne.EditorTools
{
    /// <summary>
    /// Сборка TMP-шрифта с кириллицей.
    ///
    /// Зачем: шрифт по умолчанию (LiberationSans SDF) содержит только латиницу,
    /// а список fallback-шрифтов в TMP Settings пуст. Из-за этого русские надписи
    /// в игре («ВЫ УМЕРЛИ», «ДОСТУП РАЗРЕШЁН», подсказки) не отрисовывались.
    ///
    /// Важные детали, из-за которых шрифт «терялся»:
    ///  * исходный TTF уже является ассетом проекта — вкладывать его в шрифт
    ///    нельзя (AssetDatabase.AddObjectToAsset бросает исключение, и если оно
    ///    прервёт скрипт до SaveAssets(), ссылка на текстуру атласа теряется);
    ///  * текстура атласа обязана быть sub-asset шрифта — иначе в рантайме
    ///    TMP падает с «The variable m_AtlasTextures of TMP_FontAsset doesn't
    ///    exist anymore»;
    ///  * динамический режим требует Read/Write у текстуры, поэтому для
    ///    статического ассета символы добавляются до сохранения.
    /// </summary>
    public static class CyrillicFontBuilder
    {
        private const string SourceFontPath = "Assets/TextMesh Pro/Fonts/LiberationSans.ttf";
        private const string AssetPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSansCyrillic SDF.asset";
        private const string FontAssetName = "LiberationSansCyrillic SDF";

        /// <summary>Русский алфавит + латиница + цифры + знаки препинания.</summary>
        private const string CyrillicRange =
            "АБВГДЕЁЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯ" +
            "абвгдеёжзийклмнопрстуфхцчшщъыьэюя" +
            "ЁёЄєІіЇїЎўҐґ" +
            "ABCDEFGHIJKLMNOPQRSTUVWXYZ" +
            "abcdefghijklmnopqrstuvwxyz" +
            "0123456789" +
            " !\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~" +
            "×÷±°…—–«»„“”№";

        [MenuItem("Another One/Собрать TMP-шрифт с кириллицей")]
        public static void Build()
        {
            Font source = AssetDatabase.LoadAssetAtPath<Font>(SourceFontPath);
            if (source == null)
            {
                Debug.LogError($"[CyrillicFontBuilder] Не найден исходный шрифт {SourceFontPath}");
                return;
            }

            // Шрифт по умолчанию не должен ссылаться на удаляемый ассет.
            TMP_Settings settings = TMP_Settings.instance;
            TMP_FontAsset safeDefault = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
            if (settings != null && safeDefault != null)
            {
                SerializedObject tmpSo = new SerializedObject(settings);
                tmpSo.FindProperty("m_defaultFontAsset").objectReferenceValue = safeDefault;
                tmpSo.ApplyModifiedPropertiesWithoutUndo();
            }

            TMP_FontAsset old = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetPath);
            if (old != null) AssetDatabase.DeleteAsset(AssetPath);

            // Динамический атлас: TMP добирает глифы из TTF в рантайме.
            // ВАЖНО: в TMP Settings выключено «Clear Dynamic Data On Build»
            // (m_ClearDynamicDataOnBuild: 0) — иначе Unity стирает нарисованные
            // глифы при сборке, и кириллица пропадает из билда.
            TMP_FontAsset asset = TMP_FontAsset.CreateFontAsset(source);
            asset.name = FontAssetName;
            asset.atlasPopulationMode = AtlasPopulationMode.Dynamic;

            // Засеваем атлас символами: только так глифы попадут в шрифт.
            asset.TryAddCharacters(CyrillicRange, true);

            AssetDatabase.CreateAsset(asset, AssetPath);

            // Текстура атласа — sub-asset шрифта. Без этого ссылка теряется
            // при перезагрузке ассета, и TMP падает в рантайме.
            SaveSubAssets(asset);

            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            ApplyToSettings(asset);

            int cyr = 0;
            foreach (var c in asset.characterTable)
            {
                if (c == null) continue;
                uint u = c.unicode;
                if (u >= 0x0400u && u <= 0x04FFu) cyr++;
            }

            Debug.Log($"[CyrillicFontBuilder] Создан {AssetPath}: символов {asset.characterTable.Count}, кириллических {cyr}");
        }

        /// <summary>Сохраняет текстуры атласа и материал шрифта как sub-assets.</summary>
        private static void SaveSubAssets(TMP_FontAsset asset)
        {
            var textures = asset.atlasTextures;
            if (textures != null)
            {
                for (int i = 0; i < textures.Length; i++)
                {
                    if (textures[i] == null) continue;
                    textures[i].name = FontAssetName + " Atlas";
                    AssetDatabase.AddObjectToAsset(textures[i], asset);
                }
            }

            // Материал доступен только через сериализованное свойство:
            // публичного atlasMaterial в этой версии TMP нет.
            var so = new SerializedObject(asset);
            var materialProp = so.FindProperty("m_Material");
            if (materialProp != null && materialProp.objectReferenceValue != null)
            {
                var material = (Material)materialProp.objectReferenceValue;
                material.name = FontAssetName + " Material";
                AssetDatabase.AddObjectToAsset(material, asset);
            }
        }

        /// <summary>Назначить шрифт по умолчанию и в fallback (кириллица в любом TMP-тексте).</summary>
        private static void ApplyToSettings(TMP_FontAsset asset)
        {
            TMP_Settings settings = TMP_Settings.instance;
            if (settings == null)
            {
                Debug.LogWarning("[CyrillicFontBuilder] TMP Settings не найдены — назначьте шрифт вручную.");
                return;
            }

            SerializedObject so = new SerializedObject(settings);
            so.FindProperty("m_defaultFontAsset").objectReferenceValue = asset;

            SerializedProperty fallback = so.FindProperty("m_fallbackFontAssets");
            fallback.ClearArray();
            fallback.InsertArrayElementAtIndex(0);
            fallback.GetArrayElementAtIndex(0).objectReferenceValue = asset;

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();

            Debug.Log("[CyrillicFontBuilder] Шрифт по умолчанию и fallback обновлены.");
        }
    }
}
