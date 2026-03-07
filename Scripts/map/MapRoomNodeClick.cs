using UnityEngine;
using UnityEngine.EventSystems; // 必须引用

public class MapRoomNodeClick : MonoBehaviour, IPointerClickHandler
{
    public MapRoomNode owner;

    // UI 点击事件的标准接口
    public void OnPointerClick(PointerEventData eventData)
    {
        if (owner != null)
        {
            // 调用 MapManager 处理点击
            MapManager.Instance.OnRoomClicked(owner);
        }
    }
}