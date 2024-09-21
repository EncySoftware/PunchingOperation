namespace PunchingOperationExtension;

/// <summary>
/// Array of <see cref="PunchItem"/>
/// </summary>
public class PunchItems
{
    /// <summary>
    /// Pattern of punch
    /// </summary>
    public PunchPattern Pattern;

    /// <summary>
    /// Array of <see cref="PunchItem"/>
    /// </summary>
    public readonly List<PunchItem> Items = [];

    /// <summary>
    /// Punch point indexes in final order
    /// </summary>
    public int[] OrderIndex = [];

    /// <summary>
    /// Array of <see cref="PunchItem"/>
    /// </summary>
    public PunchItems()
    {
    }

    public void InitOrder()
    {
        if (Items.Count > 0)
        {
            OrderIndex = new int[Items.Count];
            for (int i = 0; i < Items.Count; i++)
                OrderIndex[i] = i;
        } else
            OrderIndex = [];
    }

    public PunchItem GetOrderedItem(int index)
    {
        if ((index>=0) && (index<OrderIndex.Length))
            return Items[OrderIndex[index]];
        else
            return default;
    }

    public void SetOrderedItem(int index, PunchItem value)
    {
        if ((index>=0) && (index<OrderIndex.Length))
            Items[OrderIndex[index]] = value;
    }

    public PunchPoint? GetFirstOptimalPoint()
    {
        for (int i = 0; i < Items.Count; i++) {
            var p = GetOrderedItem(i);
            if (p.OptimalPoint.HasValue)
                return p.OptimalPoint;
        }
        return null;
    }
}