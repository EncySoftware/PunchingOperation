using CAMAPI.DotnetHelper;
using CAMAPI.EventHandler;
using CAMAPI.ModelFormerTypes;
using CAMAPI.ResultStatus;
using CAMAPI.TechOperation;

namespace PunchingOperationExtension;

public class OperationEventHandler : ICamApiEventHandler,
    ICamApiHandlerTechOperationInitModelFormers
{
    private class ModelFormerMakeSupportedItems : ICamApiModelFormerMakeSupportedItems
    {
        public void MakeSupportedItems(ICamApiModelFormerSupportedItems itemsObj)
        {
            using var itemsCom = new ComWrapper<ICamApiModelFormerSupportedItems>(itemsObj);
            var items = itemsCom.Instance
                ?? throw new Exception("Failed to get supported items");
            items.AddItem("Curve", 
                InterfaceInfo.IID<ICamApiCurvesArrayModelItem>(),
                "", "Curve", "", "", false, null);
        }
    }
    
    /// <summary>
    /// We always return false, because only one event is supported
    /// </summary>
    public bool GetAsyncMode(string interfaceUid)
    {
        return false;
    }
    
    /// <summary>
    /// Initialize model items, which can be created in job assignment
    /// </summary>
    public void InitModelFormers(ICamApiModelFormer modelFormersObj)
    {
        using var modelFormersCom = new ComWrapper<ICamApiModelFormer>(modelFormersObj);
        var modelFormers = modelFormersCom.Instance
            ?? throw new Exception("Failed to get model formers");
        
        if (modelFormers.SupportedItems != null)
            return;
        modelFormers.MakeSupportedItems(new ModelFormerMakeSupportedItems(), out var resultStatus);
        if (resultStatus.Code == TResultStatusCode.rsError)
            throw new Exception(resultStatus.Description);
    }
}