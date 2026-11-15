using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

[System.Serializable]
public class GrabUIBinding
{
    [Header("Item Info")]
    public string itemName;

    [Header("Binding")]
    public MonoBehaviour itemComponent;
    public GameObject uiIconPrefab;

    [Header("Stacking (for consumables)")]
    public bool stackable = false;
    public int maxStack = 99;
}

public class GrabManager : MonoBehaviour
{
    [Header("Hold Point")]
    public Transform lightPoint;

    [Header("UI")]
    public RectTransform uiContainer;
    public float iconSpacing = 60f;
    [Tooltip("Используется, если для типа предмета не задан uiBinding")]
    public GameObject defaultIconPrefab;
    public GrabUIBinding[] uiBindings;

    [Header("Animation")]
    public Animator animator;

    public bool IsInventoryHidden => isInventoryHidden;
    public bool HasEquipped => currentIndex >= 0 && currentIndex < inventory.Count;

    public event System.Action<MonoBehaviour> EquippedChanged;

    class InventoryEntry
    {
        public MonoBehaviour component;
        public IInventoryItem item;
        public int count;
        public GrabUIBinding binding;
    }

    List<InventoryEntry> inventory = new List<InventoryEntry>();
    List<GameObject> uiIcons = new List<GameObject>();
    List<TextMeshProUGUI> uiCounts = new List<TextMeshProUGUI>();

    int currentIndex = -1;
    int savedIndex = -1;
    bool isInventoryHidden = false;

    Coroutine holdCoroutine;

    void Update()
    {
        if (isInventoryHidden) return;
        HandleSwitch();
        HandleScroll();
    }

    // ================= PUBLIC API =================

    /// <summary>Пытается положить предмет в инвентарь (вызывается из PlayerInteractor).</summary>
    public bool TryAdd(IInventoryItem item)
    {
        if (item == null) return false;
        var component = item as MonoBehaviour;
        if (component == null) return false;

        RemoveNullEntries();

        GrabUIBinding binding = GetBinding(component);
        bool isStackable = binding != null && binding.stackable;
        int maxStack = binding != null ? Mathf.Max(1, binding.maxStack) : 1;
        int existingIndex = FindSameTypeIndex(component);

        if (isStackable && existingIndex != -1)
        {
            InventoryEntry ent = inventory[existingIndex];
            if (ent.count < maxStack)
            {
                ent.count++;
                UpdateCountForSlot(existingIndex, ent.count);
                Destroy(component.gameObject);
                if (animator != null && currentIndex == existingIndex)
                    animator.SetTrigger("GrabTrigger");
                return true;
            }
        }

        if (existingIndex != -1)
        {
            SwapIntoInventory(existingIndex, component, binding);
            return true;
        }

        InventoryEntry newEntry = new InventoryEntry
        {
            component = component,
            item = item,
            count = 1,
            binding = binding
        };
        inventory.Add(newEntry);
        CreateUIIconForSlot(newEntry);
        SendItemToStorage(component);
        SetEquipped(inventory.Count - 1);
        return true;
    }

    /// <summary>Использовать экипированный предмет (клавиша F).</summary>
    public void UseEquipped(PlayerInteractor interactor)
    {
        if (!HasEquipped) return;
        var entry = inventory[currentIndex];
        if (entry == null || entry.component == null) return;

        if (entry.component is IUsableItem usable)
            usable.Use(interactor);
        else if (animator != null)
            animator.SetTrigger("InteractTrigger");
    }

    public void SetEquipped(int index)
    {
        for (int i = 0; i < inventory.Count; i++)
        {
            var e = inventory[i];
            if (e == null || e.component == null) continue;
            SetGrabActive(e, false);
            SendItemToStorage(e.component);
        }

        if (index < 0 || index >= inventory.Count)
        {
            currentIndex = -1;
            UpdateUISelection();
            UpdateAnimationState();
            EquippedChanged?.Invoke(null);
            return;
        }

        var entry = inventory[index];
        if (entry == null || entry.component == null)
        {
            RemoveItem(null);
            return;
        }

        currentIndex = index;
        var component = entry.component;
        component.transform.SetParent(lightPoint, false);
        component.transform.localPosition = Vector3.zero;
        component.transform.localRotation = Quaternion.identity;

        SetItemVisible(component, true);
        SetItemAudio(component, true);
        SetItemCollider(component, false);

        SetGrabActive(entry, true);

        UpdateUISelection();
        UpdateAnimationState();

        if (animator != null) animator.SetTrigger("GrabTrigger");
        EquippedChanged?.Invoke(component);
    }
    public void UnequipAll()
    {
        for (int i = 0; i < inventory.Count; i++)
        {
            var e = inventory[i];
            if (e == null || e.component == null) continue;
            SetGrabActive(e, false);
            SendItemToStorage(e.component);
        }
        currentIndex = -1;
        UpdateUISelection();
        UpdateAnimationState();
        EquippedChanged?.Invoke(null);
    }

    public bool HasItemByType<T>() where T : MonoBehaviour
    {
        for (int i = 0; i < inventory.Count; i++)
        {
            if (inventory[i] == null || inventory[i].component == null) continue;
            if (inventory[i].component is T) return true;
        }
        return false;
    }

    public bool ConsumeItemByType<T>(int amount) where T : MonoBehaviour
    {
        if (amount <= 0) return false;
        for (int i = 0; i < inventory.Count; i++)
        {
            if (inventory[i] == null || inventory[i].component == null) continue;
            if (inventory[i].component is T)
            {
                ConsumeAtIndex(i, amount);
                return true;
            }
        }
        return false;
    }

    public void ConsumeItem(MonoBehaviour component, int amount)
    {
        if (component == null || amount <= 0) return;
        int idx = -1;
        for (int i = 0; i < inventory.Count; i++)
        {
            if (inventory[i] != null && inventory[i].component == component) { idx = i; break; }
        }
        if (idx == -1) return;
        ConsumeAtIndex(idx, amount);
    }

    public void RemoveItem(MonoBehaviour component)
    {
        if (component == null)
        {
            for (int i = inventory.Count - 1; i >= 0; i--)
            {
                if (inventory[i] == null || inventory[i].component == null)
                {
                    if (i < uiIcons.Count) { if (uiIcons[i] != null) Destroy(uiIcons[i]); uiIcons.RemoveAt(i); }
                    if (i < uiCounts.Count) uiCounts.RemoveAt(i);
                    inventory.RemoveAt(i);
                    if (currentIndex >= i) currentIndex--;
                }
            }
            UpdateUIPositions();
            UpdateUISelection();
            return;
        }

        int index = inventory.FindIndex(x => x.component == component);
        if (index == -1) return;

        if (index < uiIcons.Count) { if (uiIcons[index] != null) Destroy(uiIcons[index]); uiIcons.RemoveAt(index); }
        if (index < uiCounts.Count) uiCounts.RemoveAt(index);

        if (inventory[index].component != null && inventory[index].component.transform.parent == lightPoint)
            inventory[index].component.transform.SetParent(null, true);

        inventory.RemoveAt(index);

        if (inventory.Count == 0) currentIndex = -1;
        else SetEquipped(Mathf.Clamp(currentIndex, 0, inventory.Count - 1));

        UpdateUIPositions();
        UpdateUISelection();
        UpdateAnimationState();
    }

    public void HideInventory()
    {
        isInventoryHidden = true;
        savedIndex = currentIndex;

        for (int i = 0; i < inventory.Count; i++)
        {
            var e = inventory[i];
            if (e == null || e.component == null) continue;
            SetGrabActive(e, false);
            SendItemToStorage(e.component);
        }
        currentIndex = -1;

        for (int i = 0; i < uiIcons.Count; i++)
            if (uiIcons[i] != null) uiIcons[i].SetActive(false);

        if (animator != null)
        {
            animator.SetInteger("HoldState", 0);
            if (holdCoroutine != null) { StopCoroutine(holdCoroutine); holdCoroutine = null; }
        }
    }

    public void RestoreInventory()
    {
        isInventoryHidden = false;
        for (int i = 0; i < uiIcons.Count; i++)
            if (uiIcons[i] != null) uiIcons[i].SetActive(true);

        if (savedIndex >= 0 && savedIndex < inventory.Count &&
            inventory[savedIndex] != null && inventory[savedIndex].component != null)
        {
            SetEquipped(savedIndex);
        }
        else if (inventory.Count > 0)
        {
            SetEquipped(0);
        }
        savedIndex = -1;
    }

    // ================= PRIVATE =================

    void RemoveNullEntries()
    {
        for (int i = inventory.Count - 1; i >= 0; i--)
        {
            if (inventory[i] == null || inventory[i].component == null)
            {
                if (i < uiIcons.Count) { if (uiIcons[i] != null) Destroy(uiIcons[i]); uiIcons.RemoveAt(i); }
                if (i < uiCounts.Count) uiCounts.RemoveAt(i);
                inventory.RemoveAt(i);
                if (currentIndex >= i) currentIndex--;
            }
        }
    }

    void UpdateAnimationState()
    {
        if (animator == null) return;

        if (!HasEquipped || inventory[currentIndex] == null || inventory[currentIndex].component == null)
        {
            animator.SetInteger("HoldState", 0);
            if (holdCoroutine != null) { StopCoroutine(holdCoroutine); holdCoroutine = null; }
        }
        else
        {
            animator.SetInteger("HoldState", 2);
            if (holdCoroutine != null) StopCoroutine(holdCoroutine);
            holdCoroutine = StartCoroutine(ActiveHoldRoutine());
        }
    }

    IEnumerator ActiveHoldRoutine()
    {
        yield return new WaitForSeconds(20f);
        if (animator != null) animator.SetInteger("HoldState", 1);
    }

    void SwapIntoInventory(int slotIndex, MonoBehaviour newComponent, GrabUIBinding binding)
    {
        InventoryEntry slot = inventory[slotIndex];
        if (slot == null) return;

        MonoBehaviour oldComponent = slot.component;
        if (oldComponent == null)
        {
            slot.component = newComponent;
            slot.item = newComponent as IInventoryItem;
            slot.count = Mathf.Max(1, slot.count);
            UpdateCountForSlot(slotIndex, slot.count);
            SendItemToStorage(newComponent);
            SetEquipped(slotIndex);
            return;
        }

        Transform newParent = newComponent.transform.parent;
        Vector3 newWorldPos = newComponent.transform.position;
        Quaternion newWorldRot = newComponent.transform.rotation;

        SetGrabActive(slot, false);

        oldComponent.transform.SetParent(newParent, true);
        oldComponent.transform.position = newWorldPos;
        oldComponent.transform.rotation = newWorldRot;

        SetItemVisible(oldComponent, true);
        SetItemAudio(oldComponent, true);
        SetItemCollider(oldComponent, true);

        StartCoroutine(TemporarilyDisableColliders(oldComponent.gameObject, 0.25f));

        slot.component = newComponent;
        slot.item = newComponent as IInventoryItem;

        if (slotIndex >= 0 && slotIndex < uiIcons.Count)
            BindUIRefsToItem(uiIcons[slotIndex], newComponent);

        if (slot.binding == null || !slot.binding.stackable)
            slot.count = 1;

        SendItemToStorage(newComponent);
        SetEquipped(slotIndex);
        UpdateCountForSlot(slotIndex, slot.count);
    }

    IEnumerator TemporarilyDisableColliders(GameObject go, float delay)
    {
        if (go == null) yield break;
        Collider[] cols = go.GetComponentsInChildren<Collider>(true);
        if (cols == null || cols.Length == 0) yield break;

        bool[] wasEnabled = new bool[cols.Length];
        for (int i = 0; i < cols.Length; i++)
        {
            wasEnabled[i] = cols[i].enabled;
            cols[i].enabled = false;
        }

        yield return new WaitForSeconds(delay);

        for (int i = 0; i < cols.Length; i++)
            if (cols[i] != null) cols[i].enabled = wasEnabled[i];
    }

    int FindSameTypeIndex(MonoBehaviour component)
    {
        if (component == null) return -1;
        for (int i = 0; i < inventory.Count; i++)
        {
            var e = inventory[i];
            if (e == null || e.component == null) continue;
            if (e.component.GetType() == component.GetType()) return i;
        }
        return -1;
    }

    void CreateUIIconForSlot(InventoryEntry entry)
    {
        uiIcons.Add(null);
        uiCounts.Add(null);

        GameObject prefab = entry.binding != null ? entry.binding.uiIconPrefab : defaultIconPrefab;
        if (prefab == null)
        {
            UpdateUIPositions();
            return;
        }

        GameObject icon = Instantiate(prefab, uiContainer);
        uiIcons[uiIcons.Count - 1] = icon;

        TextMeshProUGUI count = icon.GetComponentInChildren<TextMeshProUGUI>(true);
        uiCounts[uiCounts.Count - 1] = count;

        BindUIRefsToItem(icon, entry.component);

        UpdateUIPositions();
        int slot = uiIcons.Count - 1;
        int cnt = entry.count;
        UpdateCountForSlot(slot, cnt);
        UpdateUISelection();
    }

    void BindUIRefsToItem(GameObject icon, MonoBehaviour component)
    {
        if (icon == null || component == null) return;
        if (component is IInventoryItem item) item.BindUI(icon);
    }

    void UpdateUIPositions()
    {
        if (uiContainer == null) return;
        int c = uiIcons.Count;
        if (c == 0) return;

        float spacing = iconSpacing;
        float totalSpan = (c - 1) * spacing;
        float startX = -totalSpan * 0.5f;

        for (int i = 0; i < c; i++)
        {
            var icon = uiIcons[i];
            if (icon == null) continue;
            var rt = icon.GetComponent<RectTransform>();
            if (rt != null) rt.anchoredPosition = new Vector2(startX + i * spacing, 0f);
        }
    }

    void UpdateUISelection()
    {
        for (int i = 0; i < uiIcons.Count; i++)
        {
            var icon = uiIcons[i];
            if (icon == null) continue;
            bool selected = (i == currentIndex);
            Transform frame = icon.transform.Find("Selection_Frame");
            if (frame == null) frame = icon.transform.Find("SelectionFrame");
            if (frame != null) frame.gameObject.SetActive(selected);
            icon.transform.localScale = selected ? Vector3.one * 1.05f : Vector3.one;
        }
    }

    void UpdateCountForSlot(int slotIndex, int count)
    {
        if (slotIndex < 0 || slotIndex >= uiCounts.Count) return;
        var t = uiCounts[slotIndex];
        if (t != null) t.text = (count <= 1) ? "" : $"x{count}";
        if (slotIndex < inventory.Count && inventory[slotIndex] != null && inventory[slotIndex].component != null)
            inventory[slotIndex].item?.OnCountChanged(count);
    }

    GrabUIBinding GetBinding(MonoBehaviour component)
    {
        if (component == null) return null;
        foreach (var b in uiBindings)
        {
            if (b.itemComponent == null) continue;
            if (component.GetType() == b.itemComponent.GetType()) return b;
        }
        return null;
    }

    void HandleSwitch()
    {
        if (Input.GetKeyDown(KeyCode.Alpha0)) { UnequipAll(); return; }
        for (int i = 0; i < inventory.Count && i < 9; i++)
            if (Input.GetKeyDown(KeyCode.Alpha1 + i)) SetEquipped(i);
    }

    void HandleScroll()
    {
        if (inventory.Count == 0) return;
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) < 0.0001f) return;

        int count = inventory.Count;
        if (scroll < 0f)
        {
            if (currentIndex == -1) currentIndex = 0;
            else if (currentIndex == count - 1) currentIndex = -1;
            else currentIndex++;
        }
        else
        {
            if (currentIndex == -1) currentIndex = count - 1;
            else if (currentIndex == 0) currentIndex = -1;
            else currentIndex--;
        }
        SetEquipped(currentIndex);
    }

    void SendItemToStorage(MonoBehaviour component)
    {
        if (component == null) return;
        component.transform.SetParent(null, true);
        component.transform.position = Vector3.zero;
        SetItemVisible(component, false);
        SetItemAudio(component, false);
        SetItemCollider(component, false);
    }

    void SetItemVisible(MonoBehaviour component, bool visible)
    {
        if (component == null) return;
        foreach (Renderer r in component.GetComponentsInChildren<Renderer>(true)) r.enabled = visible;
    }

    void SetItemAudio(MonoBehaviour component, bool enabled)
    {
        if (component == null) return;
        foreach (AudioSource a in component.GetComponentsInChildren<AudioSource>(true))
        {
            if (!enabled) a.Stop();
            a.enabled = enabled;
        }
    }

    void SetItemCollider(MonoBehaviour component, bool enabled)
    {
        if (component == null) return;
        foreach (Collider c in component.GetComponentsInChildren<Collider>(true)) c.enabled = enabled;
    }

    void SetGrabActive(InventoryEntry entry, bool state)
    {
        if (entry == null || entry.component == null || entry.item == null) return;
        if (state) entry.item.OnEquip(this); else entry.item.OnUnequip(this);
    }

    void ConsumeAtIndex(int idx, int amount)
    {
        inventory[idx].count -= amount;
        if (inventory[idx].count <= 0)
        {
            var rep = inventory[idx].component;
            if (rep != null) Destroy(rep.gameObject);
            RemoveItem(rep);
        }
        else
        {
            UpdateCountForSlot(idx, inventory[idx].count);
        }
    }
}

