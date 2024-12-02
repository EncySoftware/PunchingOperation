using System.Runtime.InteropServices;
using CAMAPI.CurveTypes;
using CAMAPI.DotnetHelper;
using CAMAPI.EventHandler;
using CAMAPI.Extensions;
using CAMAPI.Machine;
using CAMAPI.MCDFormerTypes;
using CAMAPI.ModelFormerTypes;
using CAMAPI.ResultStatus;
using CAMAPI.TechOperation;
using CAMAPI.TechSolvers;
using Geometry.VecMatrLib;
using STCustomPropTypes;

namespace PunchingOperationExtension;

/// <summary>
/// Extension for exampling - how to calculate tool path
/// </summary>
public class ExtensionToolPathCalculation :
    IExtension,
    ICamApiTechOperationSolver
{
    /// <summary>
    /// Additional information about extension, provided in json file. It initializes in main CAM application
    /// </summary>
    public IExtensionInfo? Info { get; set; }
    
    private ICamApiEventHandler? _operationEventHandler;

    public void InitSolver(ICamApiTechOperationSolverInitializeContext context, out TResultStatus resultStatus)
    {
        resultStatus = default;
        try
        {
            using var operationCom = new ComWrapper<ICamApiTechOperation>(context.TechOperation);
            var operation = operationCom.Instance
                ?? throw new Exception("TechOperation container is not initialized");
            _operationEventHandler ??= new OperationEventHandler();
            operation.RegisterHandler("OperationSolverExtension", _operationEventHandler, new ListString(), out resultStatus);
        }
        catch (Exception e)
        {
            resultStatus.Code = TResultStatusCode.rsError;
            resultStatus.Description = e.Message;
        }
    }

    public void FinalizeSolver()
    {
        _operationEventHandler = null;
    }

    public bool GetPropIterator(string pageId, out IST_CustomPropIterator? iterator,
        out TResultStatus resultStatus)
    {
        resultStatus = default;
        iterator = null;
        return false;
    }

    public void OnPropFilterChanged(string parameterName, string value)
    {
        //
    }

    public void MakeWorkPath(ICamApiCLDReceiver? cldReceiver, ICamApiTechOperation? techOperation, out TResultStatus ret)
    {
        ret = default;
        try
        {
            if (techOperation == null || cldReceiver == null)
                return;
            using var cldFormerCom = new ComWrapper<ICamApiCLDReceiver>(cldReceiver);
            var cldFormer = cldFormerCom.Instance
                ?? throw new Exception("CLDReceiver container is not initialized");
            using var operationCom = new ComWrapper<ICamApiTechOperation>(techOperation);
            var operation = operationCom.Instance;
            if (operation == null)
                return;
        
            // get all points, we have to punch
            var punchItems = CalcPunchItems(operation);
        
            // sort points, so that the tool moves in the optimal order
            OptimizeOrder(operation, punchItems);
        
            // in each point choose the most optimal rotation
            OptimizeRotation(operation, punchItems);
        
            // Output toolpath
            OutToolpath(punchItems, cldFormer, operation);
        }
        catch (Exception e)
        {
            ret.Code = TResultStatusCode.rsError;
            ret.Description = e.Message;
        }
    }

    private PunchItems CalcPunchItems(ICamApiTechOperation techOperation)
    {
        if (techOperation == null)
            throw new Exception("OperationSolver container is not initialized");

        var punchItems = new PunchItems();
        var props = techOperation.XMLProp;
        var jobAssignment = techOperation.ModelFormerJobAssignment;
        try
        {
            punchItems.Pattern = PunchPattern.LoadFromXMLProp(props.Ptr["Punching"]);
            
            for (var i = 0; i < jobAssignment.Count; i++)
            {
                var modelItem = jobAssignment.Item[i];
                if (modelItem is not ICamApiCurvesArrayModelItem curvesItem)
                    continue;
                
                var curveList = curvesItem.GetCurveList(techOperation.LCS);
                for (var j = 0; j < curveList.Count; j++)
                {
                    var abstractCurve = curveList.Curve[j];
                    if (abstractCurve is not ICamApiCurve curve)
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
            // VML.GeWatch.
        }
        finally
        {
            Marshal.ReleaseComObject(props);
            Marshal.ReleaseComObject(jobAssignment);
        }

        return punchItems;
    }

    private PunchItem? RecognizePattern(ICamApiCurve curve, PunchPattern pattern)
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

    private PunchItem RecognizeRound(ICamApiCurve curve, PunchPattern pattern) {
        var box = curve.Box;
        var center = 0.5 * ((T3DPoint)box.Min + box.Max);
        var point = new PunchPoint
        {
            LCS = new T3DMatrix(center)
        };
        var result = new PunchItem();
        result.Points.Add(point);
        return result;
    }

    private PunchItem RecognizeRectangle(ICamApiCurve curve, PunchPattern pattern) {
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

        var vX = T3DPoint.Norm(points[0] - center);
        var startAngleRotMatrix = T3DMatrix.MakeRotMatrix(pattern.StartAngleOffset, 3, T3DPoint.Zero);
        vX = startAngleRotMatrix.TransformVector(vX);
        var rotMatrix90 = T3DMatrix.MakeRotMatrix(Math.PI/2, 3, T3DPoint.Zero);

        // create punch item
        var result = new PunchItem();
        for (var i = 0; i < sectorsCount; i++)
        {
            var punchPoint = new PunchPoint
            {
                LCS = new T3DMatrix(
                    center, 
                    T3DPoint.UnitZ, 
                    vX
                )
            };
            result.Points.Add(punchPoint);
            vX = rotMatrix90.TransformVector(vX);
        }

        return result;
    }

    private PunchItem RecognizeStar(ICamApiCurve curve, PunchPattern pattern)
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

        var vX = T3DPoint.Norm(points[0] - center);
        var startAngleRotMatrix = T3DMatrix.MakeRotMatrix(pattern.StartAngleOffset, 3, T3DPoint.Zero);
        vX = startAngleRotMatrix.TransformVector(vX);
        var sectorRotMatrix = T3DMatrix.MakeRotMatrix(2*Math.PI/sectorsCount, 3, T3DPoint.Zero);
        
        // create punch item
        var result = new PunchItem();
        for (var i = 0; i < sectorsCount; i++)
        {
            var punchPoint = new PunchPoint
            {
                LCS = new T3DMatrix(
                    center, 
                    T3DPoint.UnitZ, 
                    vX
                )
            };
            result.Points.Add(punchPoint);
            vX = sectorRotMatrix.TransformVector(vX);
        }

        return result;
    }

    private PunchItem RecognizeCustom(ICamApiCurve curve, PunchPattern pattern) {
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

        var vX = T3DPoint.Norm(farPoint - center);
        var startAngleRotMatrix = T3DMatrix.MakeRotMatrix(pattern.StartAngleOffset, 3, T3DPoint.Zero);
        vX = startAngleRotMatrix.TransformVector(vX);
        
        // create punch item
        var result = new PunchItem();
        for (var i = 0; i < pattern.SymmetriesCount; i++)
        {
            var sectorRotMatrix = T3DMatrix.MakeRotMatrix(pattern.SymmetryAngles[i], 3, T3DPoint.Zero);
            var vXi = sectorRotMatrix.TransformVector(vX);
            var punchPoint = new PunchPoint
            {
                LCS = new T3DMatrix(
                    center, 
                    T3DPoint.UnitZ, 
                    vXi
                )
            };
            result.Points.Add(punchPoint);
        }

        return result;
    }

    private void OptimizeOrder(ICamApiTechOperation techOperation, PunchItems punchItems)
    {
        punchItems.InitOrder();

        var shouldOptimize = punchItems.Items.Count > 1 && 
                             techOperation.XMLProp.Bol["OptimizeOrder"];
        if (!shouldOptimize)
            return;
        
        // get optimal route finder
        using var routeFinderCom = SystemExtensionFactory.CreateExtension<ICamApiRouteVoyager>("Extension.Helper.RouteVoyager", Info);
        var routeFinder = routeFinderCom.Instance
            ?? throw new Exception("RouteVoyager container is not initialized");
        routeFinder.GroupByPlanes = true;

        // fill points
        foreach (var punchItem in punchItems.Items)
        {
            var firstP = punchItem.Points.First().LCS;
            routeFinder.AddPoint5D(new T5DPoint(firstP.vT, firstP.vZ));
        }

        // calc and return result
        var getOptimalRouteCallback = new RouteVoyagerGetOptimalRouteCallback(punchItems);
        routeFinder.GetOptimalRoute(getOptimalRouteCallback, out var ret);
        if (ret.Code == TResultStatusCode.rsError)
            throw new Exception("Error getting optimal route: " + ret.Description);
    }

    private void OptimizeRotation(ICamApiTechOperation techOperation, PunchItems punchItems)
    {
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

            T3DMatrix operationLcs = techOperation.LCS;

            for (var i = 0; i < punchItems.Items.Count; i++)
            {
                var punchItem = punchItems.GetOrderedItem(i);
                // find best rotation matrix
                punchItem.OptimalPoint = null;

                var firstPoint = punchItem.Points.First();
                var globalPoint = operationLcs.TransformMatrix(firstPoint.LCS);
                var point5d = new T5DPoint(globalPoint.vT, globalPoint.vZ);
                if (!machineEvaluator.CalcNextPos5D(point5d, false, false, false))
                    continue;
                machineEvaluator.SetNextPos(false);

                if (punchItems.Pattern.Is5D) {
                    punchItem.OptimalPoint = firstPoint;
                    punchItems.SetOrderedItem(i, punchItem);
                } else
                {
                    // find the rotation variant, we can reach, with the smallest distance to the current rotation
                    // var bestRotationMatrix = operationLcs.GetLocalMatrix(machineEvaluator.GetAbsoluteMatrix());
                    var bestRotationMatrix = machineEvaluator.GetAbsoluteMatrix();
                    var smallestAngle = double.MaxValue;
                    foreach (var punchPoint in punchItem.Points)
                    {
                        // check we can reach
                        globalPoint = operationLcs.TransformMatrix(punchPoint.LCS);
                        if (!machineEvaluator.CalcNextPos6D(globalPoint, false, false))
                            continue;
                        machineEvaluator.SetNextPos(false);
                        var rotationMatrix = machineEvaluator.GetAbsoluteMatrix();

                        // save if the angle is the smallest
                        // var curAngle = VML.CalcVecsAngle(bestRotationMatrix.vX, punchPoint.LCS.vX);
                        var curAngle = VML.CalcVecsAngle(bestRotationMatrix.vX, rotationMatrix.vX);
                        if (curAngle < smallestAngle) {
                            smallestAngle = curAngle;
                            punchItem.OptimalPoint = punchPoint;
                        }

                        // return initial state for the next iteration
                        machineEvaluator.CalcNextPos5D(point5d, false, false, false);
                        machineEvaluator.SetNextPos(false);
                    }

                    if (punchItem.OptimalPoint == null)
                        continue;
                    globalPoint = operationLcs.TransformMatrix(punchItem.OptimalPoint.Value.LCS);
                    machineEvaluator.CalcNextPos6D(globalPoint, false, false);
                    machineEvaluator.SetNextPos(false);
                    punchItems.SetOrderedItem(i, punchItem);
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

    double GetToolDiameter(ICamApiTechOperation techOperation)
    {
        // if (techOperation.Tool is ICamApiMillTool mt)
        // {
        //     return mt.Diameter;
        //     Marshal.ReleaseComObject(mt);
        // }
        return 1; //techOperation.XMLProp.Flt["TechOperation.TechTool.Diameter"];
    }

    private void OutToolpath(PunchItems punchItems, ICamApiCLDReceiver cldFormer, ICamApiTechOperation techOperation)
    {
        // Make toolpath movements from punch points
        if (punchItems.Items.Count<1)
            return;

        var firstPoint = punchItems.GetFirstOptimalPoint();
        if (firstPoint==null)
            return;

        var props = techOperation.XMLProp;
        try
        {
            var safeLevel = firstPoint.Value.LCS.vT.Z + props.Flt["SafeLevel.RelValue"];
            if (props.Int["SafeLevel.ReferenceType"]==0)
                safeLevel = props.Flt["SafeLevel.AbsValue"];

            var feedLevel = firstPoint.Value.LCS.vT.Z + props.Flt["FeedSwitchLevel.RelValue"];
            if (props.Int["FeedSwitchLevel.ReferenceType"]==0)
                feedLevel = props.Flt["FeedSwitchLevel.AbsValue"];
            if (props.Int["FeedSwitchLevel.ReferenceType"]==3)
                feedLevel = 0.01*GetToolDiameter(techOperation)*props.Flt["FeedSwitchLevel.PercentValue"];
            if (safeLevel<feedLevel)
                safeLevel = feedLevel;

            for (var i = 0; i < punchItems.Items.Count; i++)
            {
                var punchItem = punchItems.GetOrderedItem(i);
                if (punchItem.OptimalPoint==null)
                    continue;
                var punchPoint = punchItem.OptimalPoint.Value;

                cldFormer.BeginItem(TCLDItemType.aitGroup, "Point", $"Point {punchItems.OrderIndex[i]}");
                // point above punch point on safe plane
                var safePoint = punchPoint.LCS;
                safePoint.vT.Z = safeLevel;

                // point above punch point on feed switch plane
                var feedPoint = punchPoint.LCS;
                feedPoint.vT.Z = feedLevel;

                cldFormer.OutStandardFeed((int)TFeedTypeFlag.affRapid5D);
                if (punchItems.Pattern.Is5D)
                    cldFormer.CutTo5d(safePoint.vT, safePoint.vZ);
                else
                    cldFormer.CutTo6d(safePoint);

                cldFormer.OutStandardFeed((int)TFeedTypeFlag.affPlunge);
                if (punchItems.Pattern.Is5D)
                    cldFormer.CutTo5d(feedPoint.vT, feedPoint.vZ);
                else
                    cldFormer.CutTo6d(feedPoint);

                cldFormer.OutStandardFeed((int)TFeedTypeFlag.affWorking);
                if (punchItems.Pattern.Is5D)
                    cldFormer.CutTo5d(punchPoint.LCS.vT, punchPoint.LCS.vZ);
                else
                    cldFormer.CutTo6d(punchPoint.LCS);

                // punch
                cldFormer.AddComment("Punch");

                // go up
                cldFormer.OutStandardFeed((int)TFeedTypeFlag.affReturn);
                if (punchItems.Pattern.Is5D)
                    cldFormer.CutTo5d(safePoint.vT, safePoint.vZ);
                else
                    cldFormer.CutTo6d(safePoint);

                cldFormer.EndItem();
            }
        }
        finally
        {
            Marshal.ReleaseComObject(props);
        }
    }
}
