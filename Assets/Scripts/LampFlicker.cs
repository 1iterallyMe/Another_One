using System.Collections;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class LampFlicker : MonoBehaviour
{
    public enum LampMode { Working, Broken }
    
    [Header("Настройки")]
    public LampMode mode = LampMode.Working;
    public Light lampLight;

    [Header("Визуал (Две модели)")]
    [Tooltip("Объект, который выглядит как ГОРЯЩАЯ лампа")]
    public GameObject visualOn;
    [Tooltip("Объект, который выглядит как СЛОМАННАЯ/выключенная лампа")]
    public GameObject visualOff;

    [Header("Зона срабатывания (для сломанной)")]
    public Collider triggerZone;

    [Header("Звуки мерцания")]
    public AudioSource audioSource;
    public AudioClip[] flickerSounds;

    [Header("Звук работы (только для Working)")]
    public AudioSource humAudioSource;
    public AudioClip humSound;

    [Header("Таймеры мерцания (только для Working)")]
    public float minTimeBetweenFlickers = 10f;
    public float maxTimeBetweenFlickers = 30f;
    [Range(0f, 1f)] public float flickerChance = 0.5f;

    [Header("Шанс для сломанной лампы")]
    [Range(0f, 1f)] public float chanceToBeHalfAlive = 0.5f;

    [Header("Общие настройки мерцания")]
    public int flickerCount = 4;
    public float minFlickerDelay = 0.05f;
    public float maxFlickerDelay = 0.2f;

    private float timer;
    private bool isFlickering = false;
    private bool hasDied = false;
    private bool isHalfAlive = false;
    private Transform playerTransform;

    void Start()
    {
        if (lampLight == null) lampLight = GetComponentInChildren<Light>();
        if (audioSource == null) audioSource = GetComponent<AudioSource>();
        if (triggerZone == null) triggerZone = GetComponent<Collider>();

        FPSController player = FindFirstObjectByType<FPSController>();
        if (player != null) playerTransform = player.transform;

        if (mode == LampMode.Working)
        {
            SetVisualState(true);
            if (humAudioSource != null && humSound != null)
            {
                humAudioSource.clip = humSound;
                humAudioSource.loop = true;
                humAudioSource.Play();
            }
            ResetTimer();
        }
        else if (mode == LampMode.Broken)
        {
            isHalfAlive = Random.value < chanceToBeHalfAlive;
            if (!isHalfAlive)
            {
                SetVisualState(false); // Полностью мертвая
            }
            else
            {
                SetVisualState(true); // Ещё чуть живая
            }
        }
    }

    void Update()
    {
        if (mode == LampMode.Working && !isFlickering && !hasDied)
        {
            timer -= Time.deltaTime;
            if (timer <= 0f)
            {
                if (Random.value < flickerChance)
                    StartCoroutine(FlickerRoutine(false));
                ResetTimer();
            }
        }

        if (mode == LampMode.Broken && isHalfAlive && !hasDied && triggerZone != null && playerTransform != null)
        {
            if (triggerZone.bounds.Contains(playerTransform.position))
            {
                hasDied = true;
                StartCoroutine(FlickerRoutine(true));
            }
        }
    }

    void ResetTimer()
    {
        timer = Random.Range(minTimeBetweenFlickers, maxTimeBetweenFlickers);
    }

    // ГЛАВНЫЙ МЕТОД: Синхронно переключает свет и визуальные модели
    void SetVisualState(bool isOn)
    {
        if (lampLight != null) lampLight.enabled = isOn;
        if (visualOn != null) visualOn.SetActive(isOn);
        if (visualOff != null) visualOff.SetActive(!isOn);
    }

    IEnumerator FlickerRoutine(bool dieAtEnd)
    {
        isFlickering = true;

        if (mode == LampMode.Working && humAudioSource != null && humAudioSource.isPlaying)
            humAudioSource.Pause();

        if (audioSource != null && flickerSounds != null && flickerSounds.Length > 0)
        {
            AudioClip clip = flickerSounds[Random.Range(0, flickerSounds.Length)];
            if (clip != null) audioSource.PlayOneShot(clip);
        }

        for (int i = 0; i < flickerCount; i++)
        {
            // Переключаем состояние (если было вкл, станет выкл)
            SetVisualState(!lampLight.enabled); 
            yield return new WaitForSeconds(Random.Range(minFlickerDelay, maxFlickerDelay));
        }

        if (dieAtEnd)
        {
            SetVisualState(false); // Принудительно выключаем в конце
            if (humAudioSource != null) humAudioSource.Stop();
        }
        else
        {
            SetVisualState(true); // Возвращаем в рабочее состояние
            if (humAudioSource != null && !humAudioSource.isPlaying) humAudioSource.UnPause();
        }

        isFlickering = false;
    }
}

#if UNITY_EDITOR
[CustomEditor(typeof(LampFlicker))]
public class LampFlickerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        LampFlicker script = (LampFlicker)target;
        serializedObject.Update();

        EditorGUILayout.PropertyField(serializedObject.FindProperty("mode"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("lampLight"));

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Визуал", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("visualOn"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("visualOff"));

        if (script.mode == LampFlicker.LampMode.Broken)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Сломанная лампа", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("triggerZone"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("chanceToBeHalfAlive"));
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Звуки мерцания", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("audioSource"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("flickerSounds"), true); 

        if (script.mode == LampFlicker.LampMode.Working)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Звук работы (Гул)", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("humAudioSource"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("humSound"));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Таймеры мерцания", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("minTimeBetweenFlickers"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("maxTimeBetweenFlickers"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("flickerChance"));
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Общие настройки", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("flickerCount"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("minFlickerDelay"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("maxFlickerDelay"));

        serializedObject.ApplyModifiedProperties();
    }
}
#endif