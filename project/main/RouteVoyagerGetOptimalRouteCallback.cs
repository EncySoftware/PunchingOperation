using CAMAPI.TechSolvers;

namespace PunchingOperationExtension;

/// <summary>
/// Callback for getting optimal route
/// </summary>
public class RouteVoyagerGetOptimalRouteCallback : ICamApiRouteVoyagerGetOptimalRouteCallback
{
    /// <summary>
    /// Internal counter of items
    /// </summary>
    private int currentItem;

    /// <summary>
    /// Punch items to which Order have to be defined
    /// </summary>
    private PunchItems punchItems;

    public RouteVoyagerGetOptimalRouteCallback(PunchItems punchItems)
    {
        currentItem = 0;
        this.punchItems = punchItems;
    }
    /// <summary>
    /// Some code for each point
    /// </summary>
    public void ExecuteAction(int index)
    {
        punchItems.OrderIndex[currentItem++] = index;
    }
}