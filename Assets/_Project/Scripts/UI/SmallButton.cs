using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 背包里那张小菜单上的一个按钮（装备 / 服用 / 出售 / 取消）。
///
/// 和 ItemSlotWidget 一样，它只负责「把点击和悬停转给 InventoryUI」，
/// 自己不知道按钮上写的是什么 —— 标签和能做什么都由 InventoryUI 决定。
/// </summary>
public class SmallButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    public InventoryUI owner;
    public int id;

    public Image background;
    public Text label;

    /// <summary>这个按钮的底色。悬停时以它为基色往白里提，移开再还原</summary>
    public Color baseColor = Color.white;

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (owner != null) owner.HandleMenuHover(this, true);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (owner != null) owner.HandleMenuHover(this, false);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;
        if (owner != null) owner.HandleMenuClick(this);
    }
}
