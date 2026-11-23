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

    [Transaction(TransactionMode.Manual)]
    public class NextCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Launcher.Next(commandData.Application);
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class PreviousCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Launcher.Previous(commandData.Application);
            return Result.Succeeded;
        }
    }
}
