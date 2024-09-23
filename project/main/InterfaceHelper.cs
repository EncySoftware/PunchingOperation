namespace PunchingOperationExtension;

using System.Runtime.InteropServices;

public static class InterfaceHelper<T> 
{
    public static Guid IID { 
        get {
            var interfaceType = typeof(T);
            var guidAttr = (GuidAttribute)Attribute.GetCustomAttribute(interfaceType, typeof(GuidAttribute));
            return new Guid(guidAttr.Value);
        } 
    }
}