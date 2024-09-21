using STTypes;

namespace PunchingOperationExtension;

/// <summary>
/// Point to be punched in tool path
/// </summary>
public struct PunchPoint
{
    /// <summary>
    /// Local coordinate system
    /// </summary>
    public TST3DMatrix LCS;
}