using CAMAPI.TechSolvers;

namespace PunchingOperationExtension;

/// <summary>
/// Callback for getting optimal route
/// </summary>
public class RouteVoyagerGetOptimalRouteCallback : ICamApiRouteVoyagerGetOptimalRouteCallback
{
    /// <summary>
    /// Array to be filled with points when executing GetOptimalRoute
    /// </summary>
    private readonly Dictionary<int, PunchItem> _item = new();

    /// <summary>
    /// Result order of elements after callback method is called
    /// </summary>
    public readonly List<PunchItem> Result = [];
    
    /// <summary>
    /// Add item to the array
    /// </summary>
    /// <param name="index"></param>
    /// <param name="item"></param>
    public void AddItem(int index, PunchItem item)
    {
        _item.Add(index, item);
    }

    /// <summary>
    /// Some code for each point
    /// </summary>
    public void ExecuteAction(int index)
    {
        Result.Add(_item[index]);
    }
}