using UnityEngine;

public class AnimationEventBridge : MonoBehaviour
{
    // Ссылка на компонент шагов (если пусто — найдётся в родителе).
    public PlayerFootsteps footsteps;

    void Awake()
    {
        if (!footsteps) footsteps = GetComponentInParent<PlayerFootsteps>();
    }

    // Этот метод вызывается анимацией (Walk / Crouch клипы).
    public void PlayFootstep()
    {
        if (footsteps != null) footsteps.PlayFootstep();
    }
}