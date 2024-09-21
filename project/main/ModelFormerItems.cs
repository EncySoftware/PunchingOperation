using STModelFormerTypes;
using STXMLPropTypes;

namespace PunchingOperationExtension;

public class ModelFormerItems : IST_ModelFormerSupportedItems
{
    private struct ModelItemRec
    {
        public ModelItemRec(Guid _ItemType, string _ItemID, string _ItemTypeName, string _ItemCaption)
        {
            ItemType= _ItemType;
            ItemID = _ItemID;
            ItemTypeName = _ItemTypeName;
            ItemCaption = _ItemCaption;
        }
        public Guid ItemType;
        public string ItemID;
        public string ItemTypeName;
        public string ItemCaption;
    }
    
    List<ModelItemRec> fRecs;
    public ModelFormerItems()
    {
        fRecs = new List<ModelItemRec>();
    }
    public void Clear()
    {
        fRecs.Clear();
    }

    public int AddItem(string ItemID, Guid ItemType, string ItemTypeName, string ItemCaption, string ItemHint, string ItemIconFile, bool AllowDoubleItems, IST_XMLPropPointer ItemXMLProp)
    {
        var rec = new ModelItemRec(ItemType, ItemID, ItemTypeName, ItemCaption);
        fRecs.Add(rec);
        return fRecs.Count-1;
    }

    public int IndexOfItem(string AnItemID)
    {
        for (var i=0; i<fRecs.Count; i++)
        {
            if (fRecs[i].ItemID==AnItemID)
                return i;
        }
        return -1;
    }

    public int Count => fRecs.Count;

    public Guid get_ItemType(int i)
    {
        return fRecs[i].ItemType;
    }

    public string get_ItemTypeName(int i)
    {
        return fRecs[i].ItemTypeName;
    }

    public string get_ItemID(int i)
    {
        return fRecs[i].ItemID;
    }

    public string get_ItemCaption(int i)
    {
        return fRecs[i].ItemCaption;
    }

    public string get_ItemHint(int i)
    {
        return "";
    }

    public string get_ItemIconFile(int i)
    {
        return "";
    }

    public bool get_AllowDoubleItems(int i)
    {
        return false;
    }

    public bool get_ItemVisible(int i)
    {
        return true;
    }

    public IST_XMLPropPointer get_ItemXMLProp(int i)
    {
        return null;
    }
}