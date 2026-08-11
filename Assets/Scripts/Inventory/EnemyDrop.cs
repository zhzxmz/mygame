using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EnemyDrop : MonoBehaviour
{
    public ItemData dropItem;

    public GameObject worldItemPrefab;

    public void Drop()
{
    if (worldItemPrefab == null)
    {
        Debug.LogWarning("EnemyDrop: worldItemPrefab 没赋值！");
        return;
    }

    GameObject obj = Instantiate(worldItemPrefab, transform.position, Quaternion.identity);

    WorldItem worldItem = obj.GetComponent<WorldItem>();
    if (worldItem == null)
    {
        Debug.LogWarning("EnemyDrop: Prefab 上没有 WorldItem 组件！");
        return;
    }

    if (dropItem == null)
    {
        Debug.LogWarning("EnemyDrop: dropItem 没赋值！");
        return;
    }

    worldItem.SetItem(dropItem);
}
}

