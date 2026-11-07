using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace XYDSignTool
{
    public sealed class ObjectScaleResult
    {
        public int Scaled { get; set; }
        public int Deduplicated { get; set; }
        public List<string> Failures { get; } = new List<string>();
    }
    public static class ObjectScaleService
    {
        public static Point3d Center(Extents3d box)
        {
            var p = box.MinPoint + (box.MaxPoint - box.MinPoint) * .5;
            if (!Finite(p.X) || !Finite(p.Y) || !Finite(p.Z)) throw new InvalidOperationException("对象边界包含无效坐标。");
            return p;
        }
        private static bool Finite(double n) { return !double.IsNaN(n) && !double.IsInfinity(n); }
        public static ObjectScaleResult Scale(Database db, IEnumerable<ObjectId> selection, double factor)
        {
            if (!Finite(factor) || factor <= 0) throw new ArgumentException("缩放倍率必须是有效正数。");
            var result = new ObjectScaleResult(); if (factor == 1) return result;
            var ids = new HashSet<ObjectId>(selection); var parentBlocks = new HashSet<ObjectId>();
            using (var read = db.TransactionManager.StartTransaction())
                foreach (var id in ids)
                    try { if (!id.IsNull && id.IsValid && !id.IsErased && read.GetObject(id, OpenMode.ForRead) is BlockReference) parentBlocks.Add(id); }
                    catch (System.Exception) { /* Report unreadable objects in the per-object transaction below. */ }
            using (var whole = db.TransactionManager.StartTransaction())
            {
                foreach (var id in ids)
                {
                    using (var part = db.TransactionManager.StartTransaction())
                    {
                        try
                        {
                            var entity = part.GetObject(id, OpenMode.ForRead) as Entity;
                            if (entity == null) throw new InvalidOperationException("不是可缩放的图形对象。");
                            if (entity is AttributeReference && parentBlocks.Contains(entity.OwnerId)) { result.Deduplicated++; continue; }
                            CheckWritable(part, entity);
                            var matrix = Matrix3d.Scaling(factor, Center(entity.GeometricExtents));
                            var attributes = new List<AttributeReference>();
                            var block = entity as BlockReference;
                            if (block != null) foreach (ObjectId attrId in block.AttributeCollection)
                            {
                                var attribute = (AttributeReference)part.GetObject(attrId, OpenMode.ForRead);
                                CheckWritable(part, attribute); attributes.Add(attribute);
                            }
                            foreach (var attribute in attributes) attribute.UpgradeOpen();
                            // BlockReference.TransformBy already transforms its
                            // owned attributes. Applying the matrix again doubles
                            // both their displacement and their text size.
                            entity.UpgradeOpen(); entity.TransformBy(matrix);
                            part.Commit(); result.Scaled++;
                        }
                        catch (System.Exception ex) { part.Abort(); result.Failures.Add("对象 " + (id.IsNull ? "<空>" : id.ToString()) + " 保留：" + ex.Message); }
                    }
                }
                whole.Commit();
            }
            return result;
        }
        private static void CheckWritable(Transaction tr, Entity entity)
        {
            if (((LayerTableRecord)tr.GetObject(entity.LayerId, OpenMode.ForRead)).IsLocked) throw new InvalidOperationException("图层已锁定。");
            if (entity.IsAProxy) throw new InvalidOperationException("代理对象不支持安全缩放。");
        }
    }
    public class ObjectScaleCommands
    {
        [CommandMethod("XYD_MSCALE", CommandFlags.UsePickSet)]
        public void ScaleObjects()
        {
            var doc = Application.DocumentManager.MdiActiveDocument; if (doc == null) return;
            var ed = doc.Editor; var selected = ed.SelectImplied();
            if (selected.Status != PromptStatus.OK || selected.Value.Count == 0)
                selected = ed.GetSelection(new PromptSelectionOptions { MessageForAdding = "\n选择需要各自原地缩放的对象（回车完成）" });
            if (selected.Status != PromptStatus.OK) return;
            var number = ed.GetDouble(new PromptDoubleOptions("\n输入等比例缩放倍率（2 放大两倍，0.5 缩小一半） <1>")
                { AllowNone = true, AllowNegative = false, AllowZero = false, UseDefaultValue = false });
            if (number.Status != PromptStatus.OK && number.Status != PromptStatus.None) return;
            double factor = number.Status == PromptStatus.None ? 1 : number.Value;
            if (factor == 1) { ed.WriteMessage("\n倍率为 1，未修改任何对象。"); return; }
            try
            {
                ObjectScaleResult result;
                using (doc.LockDocument()) result = ObjectScaleService.Scale(doc.Database, selected.Value.GetObjectIds(), factor);
                ed.WriteMessage("\n已绕各自中心缩放 " + result.Scaled + " 个对象，跳过 " + result.Failures.Count + " 个对象。可一次 U 撤销。" +
                    (result.Deduplicated == 0 ? "" : "已去除 " + result.Deduplicated + " 个随父图块联动的属性重复选择。") +
                    (result.Failures.Count == 0 ? "" : "\n" + string.Join("\n", result.Failures)));
            }
            catch (System.Exception ex) { ed.WriteMessage("\n批量缩放失败：" + ex.Message); }
        }
    }
}
