/// <summary>
/// 记录当前在背包中选中的 ItemStack。
/// 仅保存选择状态，不负责装备、使用或丢弃。
/// </summary>
public static class InventorySelection
{
    public static ItemStack SelectedStack { get; private set; }

    public static void Select(ItemStack stack)
    {
        SelectedStack = stack;
    }

    public static void Clear()
    {
        SelectedStack = null;
    }
}
