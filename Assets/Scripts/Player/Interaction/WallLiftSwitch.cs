using System.Collections;
using UnityEngine;

/// <summary>
/// Интерактивный переключатель: опускает стену вниз, ждёт и возвращает её.
/// Игрок активирует объект через существующий PlayerInteractor кнопкой E.
/// </summary>
public class WallLiftSwitch : InteractableBase
{
    [Header("Wall movement")]
    [SerializeField] private Transform wall;
    [SerializeField, Min(0f)] private float dropDistance = 3f;
    [SerializeField, Min(0.01f)] private float movementSpeed = 2f;
    [SerializeField, Min(0f)] private float waitAtBottom = 2f;

    private Vector3 wallStartPosition;
    private bool isMoving;

    private void Awake()
    {
        if (wall != null)
            wallStartPosition = wall.position;
    }

    public override bool CanInteract(PlayerInteractor interactor)
    {
        return base.CanInteract(interactor) && wall != null && !isMoving;
    }

    public override void Interact(PlayerInteractor interactor)
    {
        if (wall == null || isMoving)
            return;

        StartCoroutine(DropAndRaiseWall());
    }

    private IEnumerator DropAndRaiseWall()
    {
        isMoving = true;

        Vector3 loweredPosition = wallStartPosition + Vector3.down * dropDistance;
        yield return MoveWallTo(loweredPosition);

        if (waitAtBottom > 0f)
            yield return new WaitForSeconds(waitAtBottom);

        yield return MoveWallTo(wallStartPosition);
        isMoving = false;
    }

    private IEnumerator MoveWallTo(Vector3 targetPosition)
    {
        while ((wall.position - targetPosition).sqrMagnitude > 0.0001f)
        {
            wall.position = Vector3.MoveTowards(
                wall.position,
                targetPosition,
                movementSpeed * Time.deltaTime);
            yield return null;
        }

        wall.position = targetPosition;
    }
}
