﻿namespace PunchingOperationExtension;

/// <summary>
/// Point to be punched in tool path. It has many points. All points have the same center, but they are rotated
/// </summary>
public struct PunchItem
{
    /// <summary>
    /// Points with same center, but rotated
    /// </summary>
    public readonly HashSet<PunchPoint> Points = [];

    /// <summary>
    /// The most optimal point from Points
    /// </summary>
    public PunchPoint? OptimalPoint = null;

    /// <summary>
    /// Point to be punched in tool path. It has many points. All points have the same center, but they are rotated
    /// </summary>
    public PunchItem()
    {
    }
}