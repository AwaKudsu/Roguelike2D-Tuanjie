using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 背包界面右边那排分类标签（全部 / 武器 / 护甲 / 饰品）。
///
/// 它和 ItemSlotWidget 是同一类东西：只把鼠标事件转给 InventoryUI，
/// 自己不记「现在选中哪个分类」—— 那个状态只有一份，在 InventoryUI 里。
/// </summary>
public class TabButtonWidget : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    public InventoryUI owner;

    /// <summary>分类下标：0 = 全部，1..3 = 依次对应 EquipSlot</summary>
    public int index;

    public Image background;
    public Text  label;

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (owner != null) owner.HandleTabHover(this, true);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (owner != null) owner.HandleTabHover(this, false);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;
        if (owner != null) owner.SelectCategory(index);
    }
}
