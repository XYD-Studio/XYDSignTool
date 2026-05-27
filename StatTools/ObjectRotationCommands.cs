using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace XYDSignTool
{
    public class ObjectRotationCommands
    {
        [CommandMethod("XYD_MRO", CommandFlags.UsePickSet)]
        [CommandMethod("MRO", CommandFlags.UsePickSet)]
        public void RotateObjectsAroundOwnCenter()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;

            PromptSelectionOptions pso = new PromptSelectionOptions
            {
                MessageForAdding = "\n请选择需要各自原地旋转的对象: "
            };

            PromptSelectionResult psr = GetSelectionOrPrompt(ed, pso);
            if (psr.Status != PromptStatus.OK)
            {
                ed.WriteMessage("\n未选择任何对象，命令取消。");
                return;
            }

            PromptDoubleOptions pdo = new PromptDoubleOptions("\n请输入旋转角度(输入度数即可，逆时针为正，顺时针为负): ")
            {
                AllowNone = false
            };
            PromptDoubleResult angleResult = ed.GetDouble(pdo);
            if (angleResult.Status != PromptStatus.OK) return;

            double angleRadians = angleResult.Value * Math.PI / 180.0;
            int rotated = 0;
            int skipped = 0;

            using (DocumentLock docLock = doc.LockDocument())
            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                foreach (SelectedObject selected in psr.Value)
                {
                    if (selected == null) continue;

                    try
                    {
                        Entity entity = tr.GetObject(selected.ObjectId, OpenMode.ForWrite, false) as Entity;
                        if (entity == null) continue;

                        Extents3d extents = entity.GeometricExtents;
                        Point3d center = new Point3d(
                            (extents.MinPoint.X + extents.MaxPoint.X) / 2.0,
                            (extents.MinPoint.Y + extents.MaxPoint.Y) / 2.0,
                            0.0);

                        entity.TransformBy(Matrix3d.Rotation(angleRadians, Vector3d.ZAxis, center));
                        rotated++;
                    }
                    catch
                    {
                        skipped++;
                    }
                }

                tr.Commit();
            }

            ed.WriteMessage($"\n>>> 已成功将 {rotated} 个对象绕各自中心旋转。");
            if (skipped > 0) ed.WriteMessage($"\n已跳过 {skipped} 个无法获取边界或无法旋转的对象。");
        }

        private static PromptSelectionResult GetSelectionOrPrompt(Editor ed, PromptSelectionOptions options)
        {
            PromptSelectionResult implied = ed.SelectImplied();
            if (implied.Status == PromptStatus.OK && implied.Value != null && implied.Value.Count > 0)
            {
                ed.WriteMessage("\n已使用当前预选对象进行批量旋转。");
                return implied;
            }

            return ed.GetSelection(options);
        }
    }
}
