using UnityEngine;

/// <summary>
/// Розетка на столбе: принимает штекер VacuumCablePlug по нажатию E (когда он в руках),
/// фиксирует его в подготовленной точке и включает статус подключения провода в FuelGenerator.
/// </summary>
[DisallowMultipleComponent]
public class CableSocket : InteractableBase
{
    [Header("Socket Setup")]
    [SerializeField] private FuelGenerator generator;
    [Tooltip("Точка на столбе, куда ровно встаёт штекер при подключении.")]
    [SerializeField] private Transform socketSnapPoint;
    [Tooltip("Разрешить нажатием E по уже подключённой вилке выдернуть её обратно.")]
    [SerializeField] private bool allowUnplug = false;

    [Header("Audio")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip plugInClip;
    [SerializeField, Range(0f, 1f)] private float plugInVolume = 0.9f;
    [SerializeField] private AudioClip unplugClip;
    [SerializeField, Range(0f, 1f)] private float unplugVolume = 0.75f;

    private VacuumCablePlug pluggedCable;

    public bool IsPlugged => pluggedCable != null && pluggedCable.IsConnected;

    private void Awake()
    {
        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();

        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();

        audioSource.spatialBlend = 1f;
        audioSource.playOnAwake = false;
    }

    public override bool CanInteract(PlayerInteractor interactor)
    {
        if (!base.CanInteract(interactor) || interactor == null)
            return false;

        if (IsPlugged)
            return allowUnplug;

        return GetHeldPlug(interactor) != null;
    }

    public override void Interact(PlayerInteractor interactor)
    {
        if (interactor == null)
            return;

        if (IsPlugged)
        {
            if (!allowUnplug)
                return;

            VacuumCablePlug current = pluggedCable;
            pluggedCable = null;
            current.DisconnectFromSocket();
            PlayClip(unplugClip, unplugVolume);
            return;
        }

        VacuumCablePlug heldPlug = GetHeldPlug(interactor);
        if (heldPlug == null)
            return;

        Transform snap = socketSnapPoint != null ? socketSnapPoint : transform;
        FuelGenerator targetGen = generator != null ? generator : heldPlug.Generator;

        pluggedCable = heldPlug;
        heldPlug.ConnectToSocket(snap, targetGen);
        PlayClip(plugInClip, plugInVolume);
    }

    private VacuumCablePlug GetHeldPlug(PlayerInteractor interactor)
    {
        PhysicsHoldController hold = interactor.PhysicsHold;
        if (hold == null || !hold.IsHolding)
            return null;

        VacuumCablePlug plug = hold.Held as VacuumCablePlug;
        if (plug == null && hold.Held is MonoBehaviour mb)
            plug = mb.GetComponentInParent<VacuumCablePlug>();

        if (plug == null || plug.IsConnected)
            return null;

        if (generator != null && plug.Generator != null && plug.Generator != generator)
            return null;

        return plug;
    }

    private void PlayClip(AudioClip clip, float volume)
    {
        if (audioSource == null || clip == null)
            return;

        audioSource.PlayOneShot(clip, volume);
    }

    private void OnDrawGizmosSelected()
    {
        Transform snap = socketSnapPoint != null ? socketSnapPoint : transform;
        Gizmos.color = Color.green;
        Gizmos.DrawSphere(snap.position, 0.04f);
        Gizmos.DrawRay(snap.position, snap.forward * 0.2f);
    }
}