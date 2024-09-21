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
    private string _logFileName = "";
    private string _tempDir = "";

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

    private delegate void TMakeOneLayer(double currentZ);

    private static readonly TST3DMatrix UnitMatrix = new()
    {
        vX = new TST3DPoint
        {
            X = 1, Y = 0, Z = 0
        },
        vY = new TST3DPoint
        {
            X = 0, Y = 1, Z = 0
        },
        vZ = new TST3DPoint
        {
            X = 0, Y = 0, Z = 1
        },
        vT = new TST3DPoint
        {
            X = 0, Y = 0, Z = 0
        },
        A = 0,
        B = 0,
        C = 0,
        D = 1
    };

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
        var p1 = box.Min;
        var p2 = box.Max;
        var center = new TST3DPoint
        {
            X = (p1.X + p2.X) / 2,
            Y = (p1.Y + p2.Y) / 2,
            Z = (p1.Z + p2.Z) / 2
        };

        var sectorsCount = 5;
        
        // through all points in curve search 5 points, which are the farthest from the center
        var points = new List<TST3DPoint>();
        var distances = new Dictionary<int, double>();
        for (var i = 0; i < curve.QntP; i++)
        {
            var point = curve.KnotPoint[i];
            var distance = Math.Sqrt(
                Math.Pow(point.X - center.X, 2) +
                Math.Pow(point.Y - center.Y, 2) +
                Math.Pow(point.Z - center.Z, 2)
            );
            distances.Add(i, distance);
        }

        var sortedDistances = distances.OrderBy(pair => pair.Value).ToList();
        for (var i = sortedDistances.Count - 1; i >= sortedDistances.Count - sectorsCount; i--)
        {
            var index = sortedDistances[i].Key;
            points.Add(curve.KnotPoint[index]);
        }
        
        // find the center point relative to the most far points
        var centerPoint = new TST3DPoint
        {
            X = 0,
            Y = 0,
            Z = 0
        };
        foreach (var point in points)
        {
            centerPoint.X += point.X;
            centerPoint.Y += point.Y;
            centerPoint.Z += point.Z;
        }
        centerPoint.X /= sectorsCount;
        centerPoint.Y /= sectorsCount;
        centerPoint.Z /= sectorsCount;
        
        // create punch item
        var result = new PunchItem();
        for (var i = 0; i < sectorsCount; i++)
        {
            var point = points[i];
            
            var valueVX = new T3DPoint(point.X - centerPoint.X, point.Y - centerPoint.Y, point.Z - centerPoint.Z);
            T3DPoint.TryNorm(ref valueVX); // TODO: if not return error throw new Exception("Error normalizing vector");
            var valueVZ = new T3DPoint(0, 0, 1);
            var valueVY = T3DPoint.VxV(valueVZ, valueVX);
            
            var punchPoint = new PunchPoint
            {
                LCS = new TST3DMatrix
                {
                    vX = new TST3DPoint
                    {
                        X = valueVX.X,
                        Y = valueVX.Y,
                        Z = valueVX.Z
                    },
                    vY = new TST3DPoint
                    {
                        X = valueVY.X,
                        Y = valueVY.Y,
                        Z = valueVY.Z
                    },
                    vZ = new TST3DPoint
                    {
                        X = valueVZ.X,
                        Y = valueVZ.Y,
                        Z = valueVZ.Z
                    },
                    
                    vT = new TST3DPoint
                    {
                        X = centerPoint.X,
                        Y = centerPoint.Y,
                        Z = centerPoint.Z
                    },
                    A = 0,
                    B = 0,
                    C = 0,
                    D = 1
                }
            };
            result.Points.Add(punchPoint);
        }

        return result;
    }

    private List<PunchItem> OptimizePath(PunchItems punchItems)
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
        var getOptimalRouteCallback = new RouteVoyagerGetOptimalRouteCallback();
        
        // fill points
        foreach (var punchItem in punchItems.Items)
        {
            var punchPointFirst = punchItem.Points.First();
            var point5d = new TST5DPoint
            {
                P = new TST3DPoint
                {
                    X = punchPointFirst.LCS.vT.X,
                    Y = punchPointFirst.LCS.vT.Y,
                    Z = punchPointFirst.LCS.vT.Z
                },
                n = punchPointFirst.LCS.vZ
            };
            var pointIndex = routeFinder.AddPoint5D(point5d);
            getOptimalRouteCallback.AddItem(pointIndex, punchItem);
        }
        
        // calc and return result
        routeFinder.GetOptimalRoute(getOptimalRouteCallback, out resultStatus);
        if (resultStatus.Code == TResultStatusCode.rsError)
            throw new Exception("Error getting optimal route: " + resultStatus.Description);
        return getOptimalRouteCallback.Result;
    }

    private static TST3DMatrix FromVML(T3DMatrix value)
    {
        var result = new TST3DMatrix();
        result.vX.X = value.vX.X;
        result.vX.Y = value.vX.Y;
        result.vX.Z = value.vX.Z;
        result.A = value.A;
        result.vY.X = value.vY.X;
        result.vY.Y = value.vY.Y;
        result.vY.Z = value.vY.Z;
        result.B = value.B;
        result.vZ.X = value.vZ.X;
        result.vZ.Y = value.vZ.Y;
        result.vZ.Z = value.vZ.Z;
        result.C = value.C;
        result.vT.X = value.vT.X;
        result.vT.Y = value.vT.Y;
        result.vT.Z = value.vT.Z;
        result.D = value.D;
        return result;
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

            var operationLcs = opContainer.LCS.ToVML();
            foreach (var punchItem in punchItems)
            {
                // find best rotation matrix
                var punchPointFirst = punchItem.Points.First();
                var punchPointLcs = punchPointFirst.LCS.ToVML();
                var currentPoint = operationLcs.TransformMatrix(punchPointLcs);
                var point = FromVML(currentPoint);
                var point5d = new TST5DPoint
                {
                    P = new TST3DPoint
                    {
                        X = point.vT.X,
                        Y = point.vT.Y,
                        Z = point.vT.Z
                    },
                    n = point.vZ
                };
                if (!machineEvaluator.CalcNextPos(point5d, false, false, false))
                    continue;
                
                result.Add(punchPointFirst);
                
                var rotationMatrix = machineEvaluator.GetAbsoluteMatrix();
                var bestRotationMatrix = operationLcs.GetLocalMatrix(rotationMatrix.ToVML());

                // find the rotation variant, we can reach, with the smallest distance to the current rotation
                PunchPoint? bestPoint = null;
                var smallestAngle = double.MaxValue;
                foreach (var punchPoint in punchItem.Points)
                {
                    // check we can reach
                    punchPointLcs = punchPoint.LCS.ToVML();
                    currentPoint = operationLcs.TransformMatrix(punchPointLcs);
                    point = FromVML(currentPoint);
                    if (!machineEvaluator.CalcNextPos6d(point, false, false))
                        continue;
                    
                    // save if the angle is the smallest
                    var curAngle = VML.CalcVecsAngle(bestRotationMatrix.vX, punchPoint.LCS.ToVML().vX);
                    if (curAngle >= smallestAngle)
                        continue;
                    smallestAngle = curAngle;
                    bestPoint = punchPoint;
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
        
        return result;
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
        
        // сделать траекторию
        foreach (var punchPoint in optimizedPunchPoints)
        {
            // подойти к точке на высоте
            var topPoint = punchPoint.LCS;
            topPoint.vT.Z = 10;
            
            clf.OutStandardFeed((int)TSTFeedTypeFlag.ffRapid);
            clf.CutTo6d(topPoint);
            
            // опуститься
            clf.OutStandardFeed((int)TSTFeedTypeFlag.ffWorking);
            clf.CutTo6d(punchPoint.LCS);
            
            // пробить
            clf.AddComment("punch");
            
            // подняться
            clf.OutStandardFeed((int)TSTFeedTypeFlag.ffReturn);
            clf.CutTo6d(topPoint);
        }
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
