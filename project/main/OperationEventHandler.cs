using CAMAPI.DotnetHelper;
using CAMAPI.EventHandler;
using CAMAPI.ModelFormerTypes;
using CAMAPI.ResultStatus;
using CAMAPI.TechOperation;

namespace PunchingOperationExtension;

public class OperationEventHandler : ICamApiEventHandler,
    ICamApiHandlerTechOperationInitModelFormers
{
    /// <summary>
    /// Create a class, which implements ICamApiModelFormerMakeSupportedItems - it's object
    /// will be passed to the method MakeSupportedItems
    /// </summary>
    private class ModelFormerMakeSupportedItems : ICamApiModelFormerMakeSupportedItems
    {
        public void MakeSupportedItems(ICamApiModelFormerSupportedItems itemsObj)
        {
            using var itemsCom = ComWrapper.Create(itemsObj);
            itemsCom.Invoke(items => 
            {
                items.AddItem("Curve", 
                    InterfaceInfo.IID<ICamApiCurvesArrayModelItem>(),
                    "", "Curve", "", "",
                    false, null);
            });
        }
    }
    
    /// <summary>
    /// We always return false, because only one event is supported. And this method should be synchronous
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
        using var modelFormersCom = ComWrapper.Create(modelFormersObj);
        var modelFormers = modelFormersCom.Instance
            ?? throw new Exception("Failed to get model formers");
        
        if (modelFormers.SupportedItems != null)
            return;
        modelFormers.MakeSupportedItems(new ModelFormerMakeSupportedItems(), out var resultStatus);
        if (resultStatus.Code == TResultStatusCode.rsError)
            throw new Exception(resultStatus.Description);
    }
}