using System;
using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace XYDSignTool
{
    public class BatchOleImageEmbedCommands
    {
        private const string LispFileName = "XYD_BATCHOLE.lsp";
        private const string LispEntryPoint = "c:XYD_BATCHOLE_LSP";

        [CommandMethod("XYD_BATCHOLE")]
        [CommandMethod("BatchOLE")]
        public void RunBatchOleLisp()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;

            RestoreDialogSystemVariables();

            string lispPath = ResolveLispPath();
            if (string.IsNullOrWhiteSpace(lispPath) || !File.Exists(lispPath))
            {
                ed.WriteMessage($"\n[批量嵌图] 未找到外挂 LSP: {LispFileName}");
                ed.WriteMessage("\n[批量嵌图] 请确认 Lisp 文件已复制到插件目录的 Lisp 子文件夹。");
                return;
            }

            string escapedPath = lispPath.Replace("\\", "/").Replace("\"", "\\\"");
            string command = $"(load \"{escapedPath}\" \"\") ({LispEntryPoint})\n";
            doc.SendStringToExecute(command, true, false, false);
        }

        private static string ResolveLispPath()
        {
            string assemblyDir = Path.GetDirectoryName(typeof(BatchOleImageEmbedCommands).Assembly.Location);
            if (string.IsNullOrWhiteSpace(assemblyDir)) return null;

            string lispPath = Path.Combine(assemblyDir, "Lisp", LispFileName);
            if (File.Exists(lispPath)) return lispPath;

            string devPath = Path.GetFullPath(Path.Combine(assemblyDir, @"..\..\..\Lisp", LispFileName));
            return File.Exists(devPath) ? devPath : lispPath;
        }

        private static void RestoreDialogSystemVariables()
        {
            TrySetSystemVariable("FILEDIA", 1);
            TrySetSystemVariable("CMDDIA", 1);
            TrySetSystemVariable("CMDECHO", 1);
        }

        private static void TrySetSystemVariable(string name, object value)
        {
            try { Application.SetSystemVariable(name, value); } catch { }
        }
    }
}
