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
public partial class ExtensionToolPathCalculation : IST_Operation,
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

    public void MakeWorkPath() {
        CalculateToolPath();
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
