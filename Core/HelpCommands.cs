using Autodesk.AutoCAD.Runtime;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace XYDSignTool
{
    public class HelpCommands
    {
        [CommandMethod("XYD_HELP")]
        public void ShowHelp()
        {
            HelpWindow window = new HelpWindow();
            Application.ShowModalWindow(window);
        }
    }
}
