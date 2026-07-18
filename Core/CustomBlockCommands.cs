using Autodesk.AutoCAD.Runtime;
using Application = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace XYDSignTool
{
    public class CustomBlockCommands
    {
        [CommandMethod("XYD_CUSTOMBLOCKS", CommandFlags.Session)]
        public void ShowCustomBlockLibrary()
        {
            CustomBlockLibraryWindow window = new CustomBlockLibraryWindow();
            Application.ShowModalWindow(window);

            CustomBlockDescriptor selected = window.SelectedBlock;
            if (selected == null) return;
            BlockManager.InsertBlockFromLibrary(selected.BlockName, selected.SourcePath, true);
        }
    }
}
