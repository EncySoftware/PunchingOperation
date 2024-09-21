using System.Diagnostics.CodeAnalysis;
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

namespace PunchingOperationExtension;

/// <summary>
/// Extension for exampling - how to calculate tool path
/// </summary>
public class ExtensionToolPathCalculation : IST_Operation,
    IST_OperationSolver,
    IExtension,
    IExtensionOperationSolver
{
    /// <summary>
    /// Additional information about extension, provided in json file. It initializes in main CAM application
    /// </summary>
    public IExtensionInfo? Info { get; set; }
    
    
    IST_OpContainer? opContainer;
    // IST_UpdateHandler? updateHandler;
    IST_CLDReceiver? clf;

    public void Create(IST_OpContainer Container) {
        opContainer = Container;
    }

    public void ClearReferences() {
        if (opContainer != null) {
            Marshal.ReleaseComObject(opContainer);
            opContainer = null;
        }
        if (clf != null) {
            Marshal.ReleaseComObject(clf);
            clf = null;
        }
        // if (updateHandler != null) {
        //     Marshal.ReleaseComObject(updateHandler);
        //     updateHandler = null;
        // }
    }

    public void InitModelFormers() {
        var supportedItems = new ModelFormerItems();
        var interfaceType = typeof(IST_CurvesArrayModelItem);
        var guidAttr = (GuidAttribute)Attribute.GetCustomAttribute(interfaceType, typeof(GuidAttribute));
        supportedItems.AddItem("Curve", new Guid(guidAttr.Value), "", "Curve", "", "", false, null);
        
        var jobAssignment = opContainer.MFJobAssignment;
        try
        {
            jobAssignment.SupportedItems = supportedItems;
            jobAssignment.FillItemsBySupportedItems();
        }
        finally
        {
            Marshal.ReleaseComObject(jobAssignment);    
        }
    }

    public void SaveToStream(STXMLPropTypes.IStream Stream) {

    }

    public void LoadFromStream(STXMLPropTypes.IStream Stream) {

    }

    public void SaveToXML(STXMLPropTypes.IST_XMLPropPointer XMLProp) {

    }

    public void LoadFromXML(STXMLPropTypes.IST_XMLPropPointer XMLProp) {

    }

    public void SetDefaultParameters(IST_OpContainer CopyFrom) {

    }

    public bool IsToolTypeSupported(TSTMillToolType tt) {
        return true;
    }

    public bool IsCorrectParameters() {
        return true;
    }

    public void ResetAll() {

    }

    public void ResetFillOnly() {

    }

    public void ResetTransitionOnly() {

    }

    public void ResetTechInfOnly() {

    }

    public void SaveDebugFiles(string FileNameWithoutExt) {

    }

    public bool GetPropIterator(string PageID, [UnscopedRef] out IST_CustomPropIterator PropIterator)
    {
        PropIterator = null;
        return false;
    }

    public void DoChangeParameter(string ParameterName, string Value)
    {
        // nothing to do
    }

    public Guid ID => Guid.Empty;

    public IST_OpContainer Container => opContainer;

    public IST_OperationSolver Solver => this;

    public IST_OpParametersUI ParametersUI => null;

    // ------------ IST_OperationSolver --------------------------

    public bool IsCorrectParameters(IST_Operation Operation) {
        return true;
    }

    public void InitializeRun(IST_CLDReceiver CLDFormer, IST_UpdateHandler UpdateHandler) {
        clf = CLDFormer;
        // updateHandler = UpdateHandler;
    }

    public void Prepare() {

    }

    private PunchItems calcPunchItems(int numberMirroredParts)
    {
        if (opContainer == null)
            throw new Exception("Operation container is not initialized");
        var punchItems = new PunchItems();
        
        var jobAssignment = opContainer.MFJobAssignment
            ?? throw new Exception("MFJobAssignment is not initialized");
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
                var addingPunchItem = recognizeStar(curve);
                
                // добавить в то, что станет траекторию
                punchItems.Items.Add(addingPunchItem);
            }
        }

        return punchItems;
    }

    private PunchItem recognizeStar(IST_Curve curve)
    {
        // find center of curve
        var box = curve.Box;
        var center = 0.5 * ((T3DPoint)box.Min + box.Max);

        var sectorsCount = 5;
        
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
        var centerPoint = T3DPoint.Zero;
        foreach (var point in points)
            centerPoint += point;
        centerPoint /= sectorsCount;
        
        // create punch item
        var result = new PunchItem();
        for (var i = 0; i < sectorsCount; i++)
        {
            var point = points[i];            
            var punchPoint = new PunchPoint();
            {
                punchPoint.LCS = new T3DMatrix(
                    centerPoint, 
                    T3DPoint.UnitZ, 
                    T3DPoint.Norm(point - centerPoint)
                );
            };
            result.Points.Add(punchPoint);
        }

        return result;
    }

    private List<PunchItem> OptimizePath(PunchItems punchItems)
    {
        if (punchItems.Items.Count<1)
            return new List<PunchItem>();
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
        var getOptimalRouteCallback = new RouteVoyagerGetOptimalRouteCallback();
        
        // fill points
        foreach (var punchItem in punchItems.Items)
        {
            var firstP = punchItem.Points.First().LCS;
            var pointIndex = routeFinder.AddPoint5D(new T5DPoint(firstP.vT, firstP.vZ));
            getOptimalRouteCallback.AddItem(pointIndex, punchItem);
        }
        
        // calc and return result
        routeFinder.GetOptimalRoute(getOptimalRouteCallback, out resultStatus);
        if (resultStatus.Code == TResultStatusCode.rsError)
            throw new Exception("Error getting optimal route: " + resultStatus.Description);
        return getOptimalRouteCallback.Result;
    }

    private List<PunchPoint> OptimizeRotation(List<PunchItem> punchItems)
    {
        var result = new List<PunchPoint>();
        
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
            foreach (var punchItem in punchItems)
            {
                // find best rotation matrix
                var currentPoint = operationLcs.TransformMatrix(punchItem.Points.First().LCS);
                var point5d = new T5DPoint(currentPoint.vT, currentPoint.vZ);
                if (!machineEvaluator.CalcNextPos(point5d, false, false, false))
                    continue;
                
                var bestRotationMatrix = operationLcs.GetLocalMatrix(machineEvaluator.GetAbsoluteMatrix());

                // find the rotation variant, we can reach, with the smallest distance to the current rotation
                PunchPoint? bestPoint = null;
                var smallestAngle = double.MaxValue;
                foreach (var punchPoint in punchItem.Points)
                {
                    // check we can reach
                    currentPoint = operationLcs.TransformMatrix(punchPoint.LCS);
                    if (!machineEvaluator.CalcNextPos6d(currentPoint, false, false))
                        continue;
                    
                    // save if the angle is the smallest
                    var curAngle = VML.CalcVecsAngle(bestRotationMatrix.vX, punchPoint.LCS.vX);
                    if (curAngle >= smallestAngle)
                        continue;
                    smallestAngle = curAngle;
                    bestPoint = punchPoint;
                }
                if (bestPoint != null)
                    result.Add((PunchPoint)bestPoint);
            }
        }
        finally
        {
            if (machineEvaluator != null)
                Marshal.ReleaseComObject(machineEvaluator);
            if (machine != null)
                Marshal.ReleaseComObject(machine);
        }
        
        return result;
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

    void OutToolpath(List<PunchPoint> punchPoints)
    {
        // Make toolpath movements from punch points
        if (punchPoints.Count<1)
            return;

        var props = opContainer.XMLProp;

        var safeLevel = punchPoints[0].LCS.vT.Z + props.Flt["SafeLevel.RelValue"];
        if (props.Int["SafeLevel.ReferenceType"]==0)
            safeLevel = props.Flt["SafeLevel.AbsValue"];

        var feedLevel = punchPoints[0].LCS.vT.Z + props.Flt["FeedSwitchLevel.RelValue"];
        if (props.Int["FeedSwitchLevel.ReferenceType"]==0)
            feedLevel = props.Flt["FeedSwitchLevel.AbsValue"];
        if (props.Int["FeedSwitchLevel.ReferenceType"]==3)
            feedLevel = 0.01*GetToolDiameter()*props.Flt["FeedSwitchLevel.PercentValue"];
        if (safeLevel<feedLevel)
            safeLevel = feedLevel;

        try
        {


            int pointIndex = 1;

            foreach (var punchPoint in punchPoints)
            {
                clf.BeginItem(TST_CLDItemType.itGroup, "Point", $"Point {pointIndex++}");
                // point above punch point on safe plane
                var safePoint = punchPoint.LCS;
                safePoint.vT.Z = safeLevel;

                // point above punch point on feed switch plane
                var feedPoint = punchPoint.LCS;
                feedPoint.vT.Z = feedLevel;

                clf.OutStandardFeed((int)TSTFeedTypeFlag.ffRapid);
                clf.CutTo6d(safePoint);
                
                clf.OutStandardFeed((int)TSTFeedTypeFlag.ffPlunge);
                clf.CutTo6d(feedPoint);
                
                clf.OutStandardFeed((int)TSTFeedTypeFlag.ffWorking);
                clf.CutTo6d(punchPoint.LCS);
                
                // punch
                clf.AddComment("punch");
                
                // go up
                clf.OutStandardFeed((int)TSTFeedTypeFlag.ffReturn);
                clf.CutTo6d(safePoint);

                clf.EndItem();
            }
        }
        finally
        {
            Marshal.ReleaseComObject(props);
        }
    }

    public void MakeWorkPath() {
        if (opContainer == null || clf == null)
            return;
        
        // read params
        //var xmlPropPointer = opContainer.XMLProp;
        var numberMirroredParts = 5;
        try
        {
            //numberMirroredParts = xmlPropPointer.Int["NumberMirroredParts"];
        }
        finally
        {
            //Marshal.ReleaseComObject(xmlPropPointer);
        }
        
        // get all points, we have to punch
        var allPunchItems = calcPunchItems(numberMirroredParts);
        
        // sort points, so that the tool moves in the optimal order
        var optimizedPunchItems = OptimizePath(allPunchItems);
        
        // in each point choose the most optimal rotation
        var optimizedPunchPoints = OptimizeRotation(optimizedPunchItems);

        // Output toolpath
        OutToolpath(optimizedPunchPoints);
    }

    public void MakeFill() {

    }

    public void MakeTransition() {

    }

    public void MakeTechInf() {

    }

    public void FinalizeRun() {
        if (clf != null)
        {
            Marshal.ReleaseComObject(clf);
            clf = null;
        }
    }

    public void InitLngRes(int LngID) {

    }
}
