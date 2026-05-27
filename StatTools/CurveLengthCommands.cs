using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace XYDSignTool
{
    public class CurveLengthCommands
    {
        [CommandMethod("XYD_LINELEN", CommandFlags.UsePickSet)]
        [CommandMethod("ZZ", CommandFlags.UsePickSet)]
        public void SumCurveLength()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;

            SelectionFilter filter = new SelectionFilter(new[]
            {
                new TypedValue((int)DxfCode.Start, "CIRCLE,ELLIPSE,LINE,LWPOLYLINE,POLYLINE,SPLINE,ARC")
            });

            PromptSelectionOptions pso = new PromptSelectionOptions
            {
                MessageForAdding = "\n请选择要统计长度的线段/曲线: "
            };

            PromptSelectionResult psr = GetSelectionOrPrompt(ed, pso, filter, "\n已使用当前预选对象统计线段总长。");
            if (psr.Status != PromptStatus.OK)
            {
                ed.WriteMessage("\n未选择任何对象，命令取消。");
                return;
            }

            int count = 0;
            int skipped = 0;
            double sumLength = 0.0;

            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                foreach (SelectedObject selected in psr.Value)
                {
                    if (selected == null) continue;
                    Curve curve = tr.GetObject(selected.ObjectId, OpenMode.ForRead) as Curve;
                    if (curve == null) continue;

                    try
                    {
                        double length = curve.GetDistanceAtParameter(curve.EndParam) - curve.GetDistanceAtParameter(curve.StartParam);
                        sumLength += Math.Abs(length);
                        count++;
                    }
                    catch
                    {
                        skipped++;
                    }
                }

                tr.Commit();
            }

            ed.WriteMessage($"\n共选择 {count} 条线段，线段总长: {sumLength:0.###}.");
            if (skipped > 0) ed.WriteMessage($"\n已跳过 {skipped} 个无法计算长度的对象。");
        }

        private static PromptSelectionResult GetSelectionOrPrompt(Editor ed, PromptSelectionOptions options, SelectionFilter filter, string impliedMessage)
        {
            PromptSelectionResult implied = ed.SelectImplied();
            if (implied.Status == PromptStatus.OK && implied.Value != null && implied.Value.Count > 0)
            {
                ed.WriteMessage(impliedMessage);
                return implied;
            }

            return ed.GetSelection(options, filter);
        }
    }
}
