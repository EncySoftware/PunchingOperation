namespace PunchingOperationExtension;

using System.Runtime.InteropServices;
using CAMAPI.Extensions;
using CAMAPI.Machine;
using CAMAPI.ResultStatus;
using CAMAPI.Technologist;
using CAMAPI.TechSolvers;
using Geometry.VecMatrLib;
using STCurveTypes;
using STCustomPropTypes;
using STCuttingToolTypes;
using STMCDFormerTypes;
using STModelFormerTypes;
using STOperationTypes;
using STTypes;

public partial class ExtensionToolPathCalculation : IST_Operation,
    IST_OperationSolver,
    IExtension,
    IExtensionOperationSolver
{

    private void CalculateToolPath() 
    {
        if (opContainer == null || clf == null)
            return;
        
        // get all points, we have to punch
        var punchItems = CalcPunchItems();
        
        // sort points, so that the tool moves in the optimal order
        OptimizeOrder(punchItems);
        
        // in each point choose the most optimal rotation
        OptimizeRotation(punchItems);

        // Output toolpath
        OutToolpath(punchItems);
    }

    private PunchItems CalcPunchItems()
    {
        if (opContainer == null)
            throw new Exception("Operation container is not initialized");

        var punchItems = new PunchItems();
        var props = opContainer.XMLProp;
        var jobAssignment = opContainer.MFJobAssignment;
        try
        {
            punchItems.Pattern = PunchPattern.LoadFromXMLProp(props.Ptr["Punching"]);
            
            for (var i = 0; i < jobAssignment.Count; i++)
            {
                var modelItem = jobAssignment.Item[i];
                if (modelItem is not IST_CurvesArrayModelItem curvesItem)
                    continue;
                
                var curveList = curvesItem.GetCurveList(opContainer.LCS);
                for (var j = 0; j < curveList.Count; j++)
                {
                    var abstractCurve = curveList.Curve[j];
                    if (abstractCurve is not IST_Curve curve)
                        continue;
                    
                    var box = curve.Box;
                    if (box.IsEmpty != 0)
                        continue;

                    // calc available punch points
                    var addingPunchItem = RecognizePattern(curve, punchItems.Pattern);
                    
                    // Add to recognized items
                    if (addingPunchItem.HasValue)
                        punchItems.Items.Add(addingPunchItem.Value);
                }
            }
        }
        finally
        {
            Marshal.ReleaseComObject(props);
            Marshal.ReleaseComObject(jobAssignment);
        }

        return punchItems;
    }

    private PunchItem? RecognizePattern(IST_Curve curve, PunchPattern pattern)
    {
        switch (pattern.PatternType)
        {
            case PunchPatternType.Round:
                return RecognizeRound(curve, pattern);
            case PunchPatternType.Star:
                return RecognizeStar(curve, pattern);
            case PunchPatternType.Rectangle:
                return RecognizeRectangle(curve, pattern);
            case PunchPatternType.Custom:
                return RecognizeCustom(curve, pattern);
            default:
                return null;
        }
    }

    private PunchItem RecognizeRound(IST_Curve curve, PunchPattern pattern) {
        var box = curve.Box;
        var center = 0.5 * ((T3DPoint)box.Min + box.Max);
        var point = new PunchPoint();
        point.LCS = new T3DMatrix(center);
        var result = new PunchItem();
        result.Points.Add(point);
        return result;
    }

    private PunchItem RecognizeRectangle(IST_Curve curve, PunchPattern pattern) {
        // find center of curve
        var box = curve.Box;
        var center = 0.5 * ((T3DPoint)box.Min + box.Max);

        var sectorsCount = 4;
        
        // through all points in curve search 4 points, which are the farthest from the center
        var points = new List<T3DPoint>();
        var distances = new Dictionary<int, double>();
        for (var i = 0; i < curve.QntP; i++)
        {
            var point = curve.KnotPoint[i];
            var distance = T3DPoint.Distance(center, point);
            distances.Add(i, distance);
        }

        var sortedDistances = distances.OrderBy(pair => pair.Value).ToList();
        for (var i = sortedDistances.Count - 1; i >= sortedDistances.Count - sectorsCount; i--)
        {
            var index = sortedDistances[i].Key;
            points.Add(curve.KnotPoint[index]);
        }

        // find the center point relative to the most far points
        center = T3DPoint.Zero;
        foreach (var point in points)
            center += point;
        center /= sectorsCount;

        T3DPoint vX = T3DPoint.Norm(points[0] - center);
        T3DMatrix startAngleRotMatrix = T3DMatrix.MakeRotMatrix(pattern.StartAngleOffset, 3, T3DPoint.Zero);
        vX = startAngleRotMatrix.TransformVector(vX);
        T3DMatrix rotMatrix90 = T3DMatrix.MakeRotMatrix(Math.PI/2, 3, T3DPoint.Zero);

        // create punch item
        var result = new PunchItem();
        for (var i = 0; i < sectorsCount; i++)
        {
            var point = points[i];            
            var punchPoint = new PunchPoint();
            punchPoint.LCS = new T3DMatrix(
                center, 
                T3DPoint.UnitZ, 
                vX
            );
            result.Points.Add(punchPoint);
            vX = rotMatrix90.TransformVector(vX);
        }

        return result;
    }

    private PunchItem RecognizeStar(IST_Curve curve, PunchPattern pattern)
    {
        // find center of curve
        var box = curve.Box;
        var center = 0.5 * ((T3DPoint)box.Min + box.Max);

        var sectorsCount = pattern.SymmetriesCount;
        
        // through all points in curve search 5 points, which are the farthest from the center
        var points = new List<T3DPoint>();
        var distances = new Dictionary<int, double>();
        for (var i = 0; i < curve.QntP; i++)
        {
            var point = curve.KnotPoint[i];
            var distance = T3DPoint.Distance(center, point);
            distances.Add(i, distance);
        }

        var sortedDistances = distances.OrderBy(pair => pair.Value).ToList();
        for (var i = sortedDistances.Count - 1; i >= sortedDistances.Count - sectorsCount; i--)
        {
            var index = sortedDistances[i].Key;
            points.Add(curve.KnotPoint[index]);
        }

        // find the center point relative to the most far points
        center = T3DPoint.Zero;
        foreach (var point in points)
            center += point;
        center /= sectorsCount;

        T3DPoint vX = T3DPoint.Norm(points[0] - center);
        T3DMatrix startAngleRotMatrix = T3DMatrix.MakeRotMatrix(pattern.StartAngleOffset, 3, T3DPoint.Zero);
        vX = startAngleRotMatrix.TransformVector(vX);
        T3DMatrix sectorRotMatrix = T3DMatrix.MakeRotMatrix(2*Math.PI/sectorsCount, 3, T3DPoint.Zero);
        
        // create punch item
        var result = new PunchItem();
        for (var i = 0; i < sectorsCount; i++)
        {
            var punchPoint = new PunchPoint();
            punchPoint.LCS = new T3DMatrix(
                center, 
                T3DPoint.UnitZ, 
                vX
            );
            result.Points.Add(punchPoint);
            vX = sectorRotMatrix.TransformVector(vX);
        }

        return result;
    }

    private PunchItem RecognizeCustom(IST_Curve curve, PunchPattern pattern) {
        // find center of curve
        var center = T3DPoint.Zero;
        for (var i = 0; i < curve.QntP; i++)
            center += curve.KnotPoint[i];
        center /= curve.QntP;

        // find the most far point of curve from the center
        var farPoint = curve.KnotPoint[0];
        var farDistance = T3DPoint.Distance(center, farPoint);
        for (var i = 1; i < curve.QntP; i++)
        {
            var p = curve.KnotPoint[i];
            var distance = T3DPoint.Distance(center, p);
            if (distance > farDistance)
            {
                farPoint = p;
                farDistance = distance;
            }
        }

        T3DPoint vX = T3DPoint.Norm(farPoint - center);
        T3DMatrix startAngleRotMatrix = T3DMatrix.MakeRotMatrix(pattern.StartAngleOffset, 3, T3DPoint.Zero);
        vX = startAngleRotMatrix.TransformVector(vX);
        
        // create punch item
        var result = new PunchItem();
        for (var i = 0; i < pattern.SymmetriesCount; i++)
        {
            var sectorRotMatrix = T3DMatrix.MakeRotMatrix(pattern.SymmetryAngles[i], 3, T3DPoint.Zero);
            var vXi = sectorRotMatrix.TransformVector(vX);
            var punchPoint = new PunchPoint();
            punchPoint.LCS = new T3DMatrix(
                center, 
                T3DPoint.UnitZ, 
                vXi
            );
            result.Points.Add(punchPoint);
        }

        return result;
    }

    private void OptimizeOrder(PunchItems punchItems)
    {
        punchItems.InitOrder();

        bool shouldOptimize = (
            (punchItems.Items.Count > 1) && 
            opContainer.XMLProp.Bol["OptimizeOrder"]);
        if (shouldOptimize)
        {
           // get optimal route finder
            var info = Info
                    ?? throw new Exception("Extension info is not initialized");
            var instanceInfo = info.InstanceInfo
                ?? throw new Exception("Instance info is not initialized");
            var extensionManager = instanceInfo.ExtensionManager
                ?? throw new Exception("Extension manager is not initialized");
            var extension = extensionManager.CreateExtension("Extension.Helper.RouteVoyager", out var resultStatus)
                ?? throw new Exception("ToolPathOptimization extension is not initialized");
            if (resultStatus.Code == TResultStatusCode.rsError)
                throw new Exception("Error getting ToolPathOptimization extension: " + resultStatus.Description);
            var routeFinder = (ICamApiRouteVoyager)extension;
            routeFinder.GroupByPlanes = true;

            // fill points
            foreach (var punchItem in punchItems.Items)
            {
                var firstP = punchItem.Points.First().LCS;
                var pointIndex = routeFinder.AddPoint5D(new T5DPoint(firstP.vT, firstP.vZ));
            }

            var getOptimalRouteCallback = new RouteVoyagerGetOptimalRouteCallback(punchItems);

            // calc and return result
            routeFinder.GetOptimalRoute(getOptimalRouteCallback, out resultStatus);
            if (resultStatus.Code == TResultStatusCode.rsError)
                throw new Exception("Error getting optimal route: " + resultStatus.Description);
        }
    }

    private void OptimizeRotation(PunchItems punchItems)
    {
        // here we should cast to public interface
        if (opContainer is not ICamApiTechOperation techOperation)
            throw new Exception("Tech operation is not initialized"); // TODO: freezes here
        
        ICamApiMachine? machine = null;
        ICamApiMachineEvaluator? machineEvaluator = null;
        
        try
        {
            // get machine evaluator for current operation
            machine = techOperation.Machine;
            machineEvaluator = machine.CreateEvaluator();
            techOperation.InitMachineEvaluator(machineEvaluator, out var resultStatus);
            if (resultStatus.Code == TResultStatusCode.rsError)
                throw new Exception("Error initializing machine evaluator: " + resultStatus.Description);

            T3DMatrix operationLcs = opContainer.LCS;

            for (int i = 0; i < punchItems.Items.Count; i++)
            {
                var punchItem = punchItems.GetOrderedItem(i);
                // find best rotation matrix
                punchItem.OptimalPoint = null;

                var firstPoint = punchItem.Points.First();
                var globalPoint = operationLcs.TransformMatrix(firstPoint.LCS);
                var point5d = new T5DPoint(globalPoint.vT, globalPoint.vZ);
                if (!machineEvaluator.CalcNextPos(point5d, false, false, false))
                    continue;

                if (punchItems.Pattern.Is5D) {
                    punchItem.OptimalPoint = firstPoint;
                    machineEvaluator.SetNextPos(false);
                    punchItems.SetOrderedItem(i, punchItem);
                } else
                {
                    // find the rotation variant, we can reach, with the smallest distance to the current rotation
                    var bestRotationMatrix = operationLcs.GetLocalMatrix(machineEvaluator.GetAbsoluteMatrix());
                    var smallestAngle = double.MaxValue;
                    foreach (var punchPoint in punchItem.Points)
                    {
                        // check we can reach
                        globalPoint = operationLcs.TransformMatrix(punchPoint.LCS);
                        if (!machineEvaluator.CalcNextPos6d(globalPoint, false, false))
                            continue;

                        // save if the angle is the smallest
                        var curAngle = VML.CalcVecsAngle(bestRotationMatrix.vX, punchPoint.LCS.vX);
                        if (curAngle >= smallestAngle)
                            continue;
                        smallestAngle = curAngle;
                        punchItem.OptimalPoint = punchPoint;
                    }
                    if (punchItem.OptimalPoint != null)
                    {
                        globalPoint = operationLcs.TransformMatrix(punchItem.OptimalPoint.Value.LCS);
                        machineEvaluator.CalcNextPos6d(globalPoint, false, false);
                        machineEvaluator.SetNextPos(false);
                        punchItems.SetOrderedItem(i, punchItem);
                    }
                }
            }
        }
        finally
        {
            if (machineEvaluator != null)
                Marshal.ReleaseComObject(machineEvaluator);
            if (machine != null)
                Marshal.ReleaseComObject(machine);
        }
    }

    double GetToolDiameter()
    {
        if (opContainer.Tool is IST_MillTool mt)
        {
            return mt.Diameter;
            Marshal.ReleaseComObject(mt);
        }
        return 1;
    }

    private void OutToolpath(PunchItems punchItems)
    {
        // Make toolpath movements from punch points
        if (punchItems.Items.Count<1)
            return;

        var firstPoint = punchItems.GetFirstOptimalPoint();
        if (firstPoint==null)
            return;

        var props = opContainer.XMLProp;
        try
        {
            var safeLevel = firstPoint.Value.LCS.vT.Z + props.Flt["SafeLevel.RelValue"];
            if (props.Int["SafeLevel.ReferenceType"]==0)
                safeLevel = props.Flt["SafeLevel.AbsValue"];

            var feedLevel = firstPoint.Value.LCS.vT.Z + props.Flt["FeedSwitchLevel.RelValue"];
            if (props.Int["FeedSwitchLevel.ReferenceType"]==0)
                feedLevel = props.Flt["FeedSwitchLevel.AbsValue"];
            if (props.Int["FeedSwitchLevel.ReferenceType"]==3)
                feedLevel = 0.01*GetToolDiameter()*props.Flt["FeedSwitchLevel.PercentValue"];
            if (safeLevel<feedLevel)
                safeLevel = feedLevel;

            for (int i = 0; i < punchItems.Items.Count; i++)
            {
                var punchItem = punchItems.GetOrderedItem(i);
                if (punchItem.OptimalPoint==null)
                    continue;
                var punchPoint = punchItem.OptimalPoint.Value;

                clf.BeginItem(TST_CLDItemType.itGroup, "Point", $"Point {punchItems.OrderIndex[i]}");
                // point above punch point on safe plane
                var safePoint = punchPoint.LCS;
                safePoint.vT.Z = safeLevel;

                // point above punch point on feed switch plane
                var feedPoint = punchPoint.LCS;
                feedPoint.vT.Z = feedLevel;

                clf.OutStandardFeed((int)TSTFeedTypeFlag.ffRapid5D);
                if (punchItems.Pattern.Is5D)
                    clf.CutTo5d(safePoint.vT, safePoint.vZ);
                else
                    clf.CutTo6d(safePoint);

                clf.OutStandardFeed((int)TSTFeedTypeFlag.ffPlunge);
                if (punchItems.Pattern.Is5D)
                    clf.CutTo5d(feedPoint.vT, feedPoint.vZ);
                else
                    clf.CutTo6d(feedPoint);

                clf.OutStandardFeed((int)TSTFeedTypeFlag.ffWorking);
                if (punchItems.Pattern.Is5D)
                    clf.CutTo5d(punchPoint.LCS.vT, punchPoint.LCS.vZ);
                else
                    clf.CutTo6d(punchPoint.LCS);

                // punch
                clf.AddComment("punch");

                // go up
                clf.OutStandardFeed((int)TSTFeedTypeFlag.ffReturn);
                if (punchItems.Pattern.Is5D)
                    clf.CutTo5d(safePoint.vT, safePoint.vZ);
                else
                    clf.CutTo6d(safePoint);

                clf.EndItem();
            }
        }
        finally
        {
            Marshal.ReleaseComObject(props);
        }
    }


}
