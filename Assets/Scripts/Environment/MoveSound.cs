using UnityEngine;

/// <summary>
/// Общий звук движения для стен и дверей: выбор случайного клипа из массива
/// и запасной сгенерированный «скрежет + гул лебёдки», если свои клипы не назначены.
/// </summary>
public static class MoveSound
{
    private static AudioClip generatedMoveClip;

    /// <summary>Зацикленный сгенерированный звук движения (1 с, бесшовный цикл).</summary>
    public static AudioClip GeneratedMoveClip
    {
        get
        {
            if (generatedMoveClip == null)
                generatedMoveClip = CreateGeneratedMoveClip();

            return generatedMoveClip;
        }
    }

    /// <summary>Случайный клип из массива. Если массив пуст — сгенерированный звук движения.</summary>
    public static AudioClip GetRandomClip(AudioClip[] clips)
    {
        if (clips != null && clips.Length > 0)
        {
            int start = Random.Range(0, clips.Length);

            for (int i = 0; i < clips.Length; i++)
            {
                AudioClip clip = clips[(start + i) % clips.Length];
                if (clip != null)
                    return clip;
            }
        }

        return GeneratedMoveClip;
    }

    // Все синусоиды имеют целые частоты, поэтому клип длиной ровно 1 с
    // зацикливается без щелчка на стыке.
    private static AudioClip CreateGeneratedMoveClip()
    {
        const int sampleRate = 22050;
        const float duration = 1f;
        int sampleCount = Mathf.CeilToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];

        const int partialCount = 18;
        float[] frequencies = new float[partialCount];
        float[] amplitudes = new float[partialCount];
        float[] phases = new float[partialCount];

        System.Random random = new System.Random(20211);

        for (int p = 0; p < partialCount; p++)
        {
            // «Шорох» трущегося металла: набор частот от низких до высоких.
            float frequency = Mathf.Lerp(70f, 2200f, p / (float)(partialCount - 1));
            frequencies[p] = Mathf.Round(frequency);
            amplitudes[p] = 1f / Mathf.Pow(frequencies[p], 0.6f);
            phases[p] = (float)random.NextDouble() * Mathf.PI * 2f;
        }

        for (int i = 0; i < sampleCount; i++)
        {
            float time = i / (float)sampleRate;
            float sample = 0f;

            for (int p = 0; p < partialCount; p++)
                sample += Mathf.Sin(2f * Mathf.PI * frequencies[p] * time + phases[p]) * amplitudes[p];

            // Низкий гул мотора/лебёдки и медленное «дрожание» громкости.
            sample += Mathf.Sin(2f * Mathf.PI * 50f * time) * 0.35f;
            sample *= 0.55f + 0.45f * Mathf.Sin(2f * Mathf.PI * 3f * time);

            samples[i] = sample;
        }

        // Нормируем громкость клипа по пику, чтобы звук был слышен уверенно.
        float peak = 0f;
        for (int i = 0; i < sampleCount; i++)
            peak = Mathf.Max(peak, Mathf.Abs(samples[i]));

        float scale = peak > 0.0001f ? 0.9f / peak : 1f;
        for (int i = 0; i < sampleCount; i++)
            samples[i] = Mathf.Clamp(samples[i] * scale, -1f, 1f);

        AudioClip clip = AudioClip.Create("GeneratedMoveSound", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }
}
