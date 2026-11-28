using UnityEngine;

/// <summary>
/// Общий контур подсветки для интерактивных объектов, которые реализуют
/// <see cref="IInteractable"/> напрямую, а не через <see cref="InteractableBase"/>
/// (генератор, зиплайн). Раньше у них SetFocused был пустым, поэтому при
/// наведении вообще не было Outline.
/// </summary>
public static class OutlineHighlight
{
    private static readonly Color DefaultColor = new Color(1f, 0.85f, 0.2f, 1f);
    private const float DefaultWidth = 3f;

    /// <summary>Включить/выключить контур на объекте (компонент создаётся при первом наведении).</summary>
    public static void Set(MonoBehaviour owner, bool focused)
    {
        if (owner == null) return;

        Outline outline = owner.GetComponent<Outline>();

        if (outline == null)
        {
            // Не плодим компоненты на объектах, которые никогда не наводили.
            if (!focused) return;

            outline = owner.gameObject.AddComponent<Outline>();
            outline.OutlineMode = Outline.Mode.OutlineVisible;
            outline.OutlineColor = DefaultColor;
            outline.OutlineWidth = DefaultWidth;
        }

        if (focused)
        {
            outline.OutlineMode = Outline.Mode.OutlineVisible;
            outline.OutlineColor = DefaultColor;
            outline.OutlineWidth = DefaultWidth;
        }

        outline.enabled = focused;
    }
}
