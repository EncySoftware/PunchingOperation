using System.Reflection;
using System.Xml;
using PunchingOperationExtension;

// ReSharper disable once CheckNamespace
namespace CAMAPI;

using Extensions;
using ResultStatus;

/// <summary>
/// Factory for creating extensions. Namespace and class name always should be CAMAPI.ExtensionFactory,
/// so CAMAPI will find it
/// </summary>
public class ExtensionFactory : IExtensionFactory
{
    /// <summary>
    /// Create new instance of our extension
    /// </summary>
    /// <param name="extensionIdent">
    /// Unique identifier, if out library has more than one extension. Should accord with
    /// value in settings json, describing this library
    /// </param>
    /// <param name="ret">Error to return it, because throw exception will not work</param>
    /// <returns>Instance of out extension</returns>
    public IExtension? Create(string extensionIdent, out TResultStatus ret)
    {
        try
        {
            ret = default;
            if (extensionIdent == "PunchingOperationExtension.ToolPathCalculation")
                return new ExtensionToolPathCalculation();
            throw new Exception("Unknown extension identifier: " + extensionIdent);
        }
        catch (Exception e)
        {
            ret.Code = TResultStatusCode.rsError;
            ret.Description = e.Message;
        }
        return null;
    }
    
    private XmlNode GetOperationsNode(XmlNode rootNode)
    {
        if (rootNode!=null)
        {
            foreach (XmlNode childNode in rootNode.ChildNodes)
            {
                if (childNode.Attributes != null && childNode.Attributes["ID"] != null && childNode.Attributes["ID"].Value == "Operations")
                {
                    return childNode;
                }
            }
        }  
        return null;
    } 
    private void DeleteOperationNode(XmlNode operationsNode, string OperationXMlName)
    {
        if (operationsNode!=null)
        {
            var childNode = operationsNode.FirstChild;
            while (childNode!=null)
            {
                var siblingNode = childNode.NextSibling;
                if (childNode.InnerText.ToLower().EndsWith(OperationXMlName.ToLower())) 
                {
                    operationsNode.RemoveChild(childNode);
                }
                childNode = siblingNode;
            }
        }
    } 
    public void OnLibraryRegistered(IExtensionFactoryContext Context, out TResultStatus ret)
    {
        string assemblyLocation = Assembly.GetExecutingAssembly().Location;
        string OperationXMlName = "SimpleToolPathGenerator_ExtOp.xml";
        string pathToOperationXML = Path.GetDirectoryName(assemblyLocation) + "\\" + OperationXMlName;
        string pathToUserOperationsList = Context.Paths.TryUnfoldPath(@"$(OPERATIONS_FOLDER)\UserOperationsList.xml");
        if (File.Exists(pathToUserOperationsList))
        {
            try
            {
                XmlDocument UserOperationsListDoc = new XmlDocument();
                UserOperationsListDoc.Load(pathToUserOperationsList);
                XmlNode rootNode = UserOperationsListDoc.DocumentElement;     
                if (rootNode!=null)
                {
                    XmlNode operationsNode = GetOperationsNode(rootNode);
                    if (operationsNode!=null)
                    {
                        DeleteOperationNode(operationsNode, OperationXMlName);
                        XmlElement newOperationElement = UserOperationsListDoc.CreateElement("SCInclude");
                        newOperationElement.InnerText = pathToOperationXML;
                        XmlAttribute optionalAttr = UserOperationsListDoc.CreateAttribute("Optional");
                        optionalAttr.Value = "true";
                        newOperationElement.Attributes.Append(optionalAttr);
                        operationsNode.AppendChild(newOperationElement);
                        UserOperationsListDoc.Save(pathToUserOperationsList);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }
        else
        {
             try
            {
                XmlDocument UserOperationsListDoc = new XmlDocument();
                XmlDeclaration xmlDeclaration = UserOperationsListDoc.CreateXmlDeclaration("1.0", null, null);
                UserOperationsListDoc.InsertBefore(xmlDeclaration, UserOperationsListDoc.DocumentElement);

                XmlElement rootElement = UserOperationsListDoc.CreateElement("SCCollection");
                XmlAttribute rootIDAttr = UserOperationsListDoc.CreateAttribute("ID");
                rootIDAttr.Value = @"$(OPERATIONS_FOLDER)\UserOperationsList.xml";
                rootElement.Attributes.Append(rootIDAttr);
                UserOperationsListDoc.AppendChild(rootElement);

                XmlElement nameSpaceElement = UserOperationsListDoc.CreateElement("SCNameSpace");
                XmlAttribute nameSpaceIDAttr = UserOperationsListDoc.CreateAttribute("ID");
                nameSpaceIDAttr.Value = "Operations";
                nameSpaceElement.Attributes.Append(nameSpaceIDAttr);
                rootElement.AppendChild(nameSpaceElement);

                XmlElement newOperationElement = UserOperationsListDoc.CreateElement("SCInclude");
                newOperationElement.InnerText = pathToOperationXML;
                XmlAttribute optionalAttr = UserOperationsListDoc.CreateAttribute("Optional");
                optionalAttr.Value = "true";
                newOperationElement.Attributes.Append(optionalAttr);
                nameSpaceElement.AppendChild(newOperationElement);
                UserOperationsListDoc.Save(pathToUserOperationsList);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }
        ret = default;
    }

    public void OnLibraryUnRegistered(IExtensionFactoryContext Context, out TResultStatus ret)
    {
        string assemblyLocation = Assembly.GetExecutingAssembly().Location;
        string OperationXMlName = "CuraEngineToolpath_ExtOp.xml";
        string pathToOperationXML = Path.GetDirectoryName(assemblyLocation) + "\\" + OperationXMlName;
        string pathToUserOperationsList = Context.Paths.TryUnfoldPath(@"$(OPERATIONS_FOLDER)\UserOperationsList.xml");
        if (File.Exists(pathToUserOperationsList))
        {
            try
            {
                XmlDocument UserOperationsListDoc = new XmlDocument();
                UserOperationsListDoc.Load(pathToUserOperationsList);
                XmlNode rootNode = UserOperationsListDoc.DocumentElement;     
                if (rootNode!=null)
                {
                    XmlNode operationsNode = GetOperationsNode(rootNode);
                    if (operationsNode!=null)
                    {
                        DeleteOperationNode(operationsNode, OperationXMlName);
                        UserOperationsListDoc.Save(pathToUserOperationsList);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }
        ret = default;
    }
}
