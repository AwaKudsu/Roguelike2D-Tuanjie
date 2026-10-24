using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 背包界面里的一个格子。它只负责「把鼠标事件转给 InventoryUI」，
/// 自己不判断能不能穿 —— 规则全在 InventoryUI / Equipment 里，
/// 免得同一套判断散落在十二个格子上。
/// </summary>
public class ItemSlotWidget : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    public InventoryUI owner;

    public Image background;
    public Image icon;
    public Text  nameLabel;

    /// <summary>这个格子当前放的物品，空格子为 null</summary>
    public ItemData item;

    /// <summary>装备槽的槽位；背包格子用不到</summary>
    public EquipSlot slot;

    /// <summary>true = 这是左边的装备槽（点一下是卸下），false = 背包格子（点一下是穿上）</summary>
    public bool isEquipSlot;

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (owner != null) owner.HandleHover(this, eventData.position);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (owner != null) owner.HandleHoverEnd();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;
        if (owner != null) owner.HandleClick(this);
    }
}
