using CAMAPI.TechSolvers;

namespace PunchingOperationExtension;

/// <summary>
/// Callback for getting optimal route
/// </summary>
public class RouteVoyagerGetOptimalRouteCallback(PunchItems punchItems) : ICamApiRouteVoyagerGetOptimalRouteCallback
{
    /// <summary>
    /// Internal counter of items
    /// </summary>
    private int _currentItem;

    /// <summary>
    /// Punch items to which Order have to be defined
    /// </summary>
    private readonly PunchItems _punchItems = punchItems;

    /// <summary>
    /// Some code for each point
    /// </summary>
    public void ExecuteAction(int index)
    {
        _punchItems.OrderIndex[_currentItem++] = index;
    }
}