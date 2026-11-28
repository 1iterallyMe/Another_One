using UnityEngine;

/// <summary>
/// Единые тайминги игры: стабильная частота кадров и шаг физики.
/// Без этого билд рендерит «как получится» (VSync выключен, лимита FPS нет):
/// время кадра скачет, и движение с камерой выглядят дёргано, хотя в редакторе
/// Game View ограничен и всё плавно.
/// </summary>
public static class GameBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ConfigureTiming()
    {
        // Кадры привязаны к монитору: ровный шаг времени без рывков и разрывов.
        QualitySettings.vSyncCount = 1;
        Application.targetFrameRate = -1; // при включённом VSync игнорируется

        // Физика 60 Гц — совпадает с типичной частотой кадров, не «двоит» шаги.
        Time.fixedDeltaTime = 1f / 60f;

        // После долгого кадра (загрузка, фриз) не телепортировать игрока.
        Time.maximumDeltaTime = 0.1f;
    }
}
