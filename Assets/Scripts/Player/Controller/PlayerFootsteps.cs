using UnityEngine;

public class PlayerFootsteps : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private FPSController controller;
    [SerializeField] private AudioSource audioSource;

    [Header("Clips")]
    [SerializeField] private AudioClip[] footstepClips;
    [SerializeField, Range(0f, 1f)] private float volume = 0.7f;

    [Header("Gating")]
    [Tooltip("Минимальная горизонтальная скорость, при которой звучит шаг.")]
    [SerializeField] private float minSpeed = 0.5f;

    void Awake()
    {
        if (!controller) controller = GetComponent<FPSController>();
        if (!controller) controller = GetComponentInParent<FPSController>();
        if (!audioSource) audioSource = GetComponent<AudioSource>();

        if (audioSource != null)
        {
            audioSource.playOnAwake = false;
            audioSource.loop = false;
        }
    }

    // Вызывается Animation Event'ом PlayFootstep (Walk / Crouch клипы).
    public void PlayFootstep()
    {
        if (audioSource == null || footstepClips == null || footstepClips.Length == 0) return;

        if (controller != null)
        {
            if (!controller.IsGrounded) return;
            if (controller.HorizontalSpeed < minSpeed) return;
        }

        AudioClip clip = footstepClips[Random.Range(0, footstepClips.Length)];
        if (clip == null) return;

        audioSource.pitch = Random.Range(0.95f, 1.05f);
        audioSource.PlayOneShot(clip, volume);
    }
}
