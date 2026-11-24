using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace BIMRadar.ClashNavigator.Standalone
{
    [Transaction(TransactionMode.Manual)]
    public class OpenCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Launcher.Open(commandData.Application);
            return Result.Succeeded;
        }
    }
}
