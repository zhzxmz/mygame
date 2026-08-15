using UnityEngine;

[RequireComponent(typeof(WorldItem))]
public class WorldItemPickup : MonoBehaviour
{
    public InventoryManager inventory;
    public KeyCode pickupKey = KeyCode.E;
    public bool destroyOnPickup = true;

    private WorldItem worldItem;
    private bool playerInRange;

    void Awake()
    {
        worldItem = GetComponent<WorldItem>();

        if (inventory == null)
        {
            inventory = FindObjectOfType<InventoryManager>();
        }
    }

    void Update()
    {
        if (!playerInRange) return;
        if (!Input.GetKeyDown(pickupKey)) return;

        TryPickup();
    }

    void OnTriggerEnter(Collider other)
    {
        if (IsPlayer(other))
        {
            playerInRange = true;
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (IsPlayer(other))
        {
            playerInRange = false;
        }
    }

    private bool IsPlayer(Collider other)
    {
        return other.GetComponentInParent<MovementController>() != null;
    }

    private void TryPickup()
    {
        if (inventory == null)
        {
            Debug.LogWarning("WorldItemPickup: 没有找到 InventoryManager，无法拾取物品");
            return;
        }

        if (worldItem == null || worldItem.itemData == null)
        {
            Debug.LogWarning("WorldItemPickup: WorldItem 或 itemData 为空，无法拾取物品");
            return;
        }

        bool added = inventory.AddItem(worldItem.itemData);
        if (!added)
        {
            return;
        }

        if (destroyOnPickup)
        {
            Destroy(gameObject);
        }
    }
}
