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
            return ScaleXy(db, selection, factor, factor);
        }

        public static ObjectScaleResult ScaleXy(Database db, IEnumerable<ObjectId> selection, double x, double y)
        {
            CheckPositive(x);
            CheckPositive(y);
            if (x == 1 && y == 1) return new ObjectScaleResult();
            return Apply(db, selection, size => new Scale3d(x, y, x == y ? x : 1), x != y);
        }

        public static ObjectScaleResult ScaleToSize(Database db, IEnumerable<ObjectId> selection, double? width, double? height)
        {
            if (!width.HasValue && !height.HasValue) throw new ArgumentException("请输入目标宽或目标高。");
            if (width.HasValue) CheckPositive(width.Value);
            if (height.HasValue) CheckPositive(height.Value);
            return Apply(db, selection, size =>
            {
                double? x = width.HasValue ? DimensionFactor(width.Value, size.X) : (double?)null;
                double? y = height.HasValue ? DimensionFactor(height.Value, size.Y) : (double?)null;
                double xFactor = x ?? y.Value;
                double yFactor = y ?? x.Value;
                return new Scale3d(xFactor, yFactor, xFactor == yFactor ? xFactor : 1);
            }, true);
        }

        private static double DimensionFactor(double target, double original)
        {
            if (original <= 0) throw new InvalidOperationException("原对象在目标方向上的尺寸为零。");
            double factor = target / original;
            CheckPositive(factor);
            return factor;
        }

        private static void CheckPositive(double value)
        {
            if (!Finite(value) || value <= 0) throw new ArgumentException("缩放参数必须是有效正数。");
        }

        private static ObjectScaleResult Apply(Database db, IEnumerable<ObjectId> selection, Func<Vector3d, Scale3d> resolveFactors, bool verify)
        {
            var result = new ObjectScaleResult();
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
                            Extents3d extents = GetExtents(part, entity);
                            Point3d center = Center(extents);
                            Vector3d size = extents.MaxPoint - extents.MinPoint;
                            Scale3d factors = resolveFactors(size);
                            if (factors.X == 1 && factors.Y == 1) continue;
                            Matrix3d matrix = factors.X == factors.Y
                                ? Matrix3d.Scaling(factors.X, center)
                                : new Matrix3d(new[]
                                {
                                    factors.X, 0, 0, center.X * (1 - factors.X),
                                    0, factors.Y, 0, center.Y * (1 - factors.Y),
                                    0, 0, 1, 0,
                                    0, 0, 0, 1
                                });
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
                            entity.UpgradeOpen();
                            if (factors.X == factors.Y) entity.TransformBy(matrix);
                            else TransformXy(part, entity, matrix);
                            if (verify) VerifyExtents(GetExtents(part, entity), center, size, factors);
                            part.Commit(); result.Scaled++;
                        }
                        catch (System.Exception ex) { part.Abort(); result.Failures.Add("对象 " + (id.IsNull ? "<空>" : id.ToString()) + " 保留：" + ex.Message); }
                    }
                }
                whole.Commit();
            }
            return result;
        }

        private static Extents3d GetExtents(Transaction tr, Entity entity)
        {
            if (entity is BlockReference block && block.IsDynamicBlock &&
                AttributeMappingService.EffectiveName(block, tr).Equals(SignBlockSchema.Definition, StringComparison.OrdinalIgnoreCase))
            {
                // AutoCAD includes other visibility states in a dynamic reference's GeometricExtents.
                // The visible straight-line frame defines the size and center of an XYD sign.
                var definition = (BlockTableRecord)tr.GetObject(block.BlockTableRecord, OpenMode.ForRead);
                Matrix3d transform = block.BlockTransform;
                Extents3d? visible = null;
                foreach (ObjectId id in definition)
                {
                    if (!(tr.GetObject(id, OpenMode.ForRead) is Polyline frame) || !frame.Visible) continue;
                    for (int i = 0; i < frame.NumberOfVertices; i++)
                    {
                        Point3d point = frame.GetPoint3dAt(i).TransformBy(transform);
                        Extents3d bounds = visible ?? new Extents3d(point, point);
                        bounds.AddPoint(point);
                        visible = bounds;
                    }
                }
                if (visible.HasValue) return visible.Value;
            }
            return entity.GeometricExtents;
        }

        private static void TransformXy(Transaction tr, Entity entity, Matrix3d matrix)
        {
            if (entity is Line line && line.Thickness == 0)
            {
                line.StartPoint = line.StartPoint.TransformBy(matrix);
                line.EndPoint = line.EndPoint.TransformBy(matrix);
                line.XDataTransformBy(matrix);
                return;
            }
            if (entity is Polyline polyline)
            {
                int segments = polyline.NumberOfVertices - (polyline.Closed ? 0 : 1);
                if (!polyline.Normal.IsParallelTo(Vector3d.ZAxis) || polyline.Thickness != 0 ||
                    Enumerable.Range(0, Math.Max(0, segments)).Any(i => polyline.GetBulgeAt(i) != 0 ||
                        polyline.GetStartWidthAt(i) != 0 || polyline.GetEndWidthAt(i) != 0))
                    throw new InvalidOperationException("带圆弧、几何宽度、厚度或倾斜平面的 PL 无法保持原类型进行非等比缩放。");
                Matrix3d toPlane = Matrix3d.WorldToPlane(polyline.Normal);
                for (int i = 0; i < polyline.NumberOfVertices; i++)
                {
                    Point3d point = polyline.GetPoint3dAt(i).TransformBy(matrix).TransformBy(toPlane);
                    polyline.SetPointAt(i, new Point2d(point.X, point.Y));
                }
                polyline.XDataTransformBy(matrix);
                return;
            }
            if (entity is Polyline2d legacy && legacy.PolyType == Poly2dType.SimplePoly)
            {
                var vertices = legacy.Cast<ObjectId>().Select(id => (Vertex2d)tr.GetObject(id, OpenMode.ForRead)).ToList();
                int segments = vertices.Count - (legacy.Closed ? 0 : 1);
                if (!legacy.Normal.IsParallelTo(Vector3d.ZAxis) || legacy.Thickness != 0 ||
                    vertices.Take(segments).Any(v => v.Bulge != 0 || v.StartWidth != 0 || v.EndWidth != 0))
                    throw new InvalidOperationException("带圆弧、几何宽度、厚度或倾斜平面的 PL 无法保持原类型进行非等比缩放。");
                Matrix3d toWorld = Matrix3d.PlaneToWorld(legacy.Normal);
                Matrix3d inPlane = toWorld.Inverse() * matrix * toWorld;
                foreach (var vertex in vertices)
                {
                    vertex.UpgradeOpen();
                    vertex.Position = vertex.Position.TransformBy(inPlane);
                    vertex.XDataTransformBy(matrix);
                }
                legacy.XDataTransformBy(matrix);
                return;
            }
            entity.TransformBy(matrix);
        }

        private static void VerifyExtents(Extents3d extents, Point3d center, Vector3d size, Scale3d factors)
        {
            Vector3d actual = extents.MaxPoint - extents.MinPoint;
            Vector3d offset = Center(extents) - center;
            if (!Near(actual.X, size.X * factors.X) || !Near(actual.Y, size.Y * factors.Y) ||
                !Near(actual.Z, size.Z * factors.Z) || !Near(offset.X, 0) ||
                !Near(offset.Y, 0) || !Near(offset.Z, 0))
                throw new InvalidOperationException("对象无法达到目标尺寸或保持原中心。");
        }

        private static bool Near(double actual, double expected)
        {
            return Finite(actual) && Finite(expected) && Math.Abs(actual - expected) <= Math.Max(1e-6, Math.Abs(expected) * 1e-9);
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
            var options = PositiveInput("\n输入等比倍率，或选百分比(P)、目标尺寸(S) <1>");
            options.AllowNone = true;
            options.Keywords.Add("Percentage", "P", "百分比(P)");
            options.Keywords.Add("Size", "S", "目标尺寸(S)");
            options.AppendKeywordsToMessage = false;
            var number = ed.GetDouble(options);
            if (number.Status != PromptStatus.OK && number.Status != PromptStatus.None && number.Status != PromptStatus.Keyword) return;
            bool bySize = number.Status == PromptStatus.Keyword && number.StringResult == "Size";
            double? first = number.Status == PromptStatus.OK ? number.Value : 1;
            double? second = first;
            if (number.Status == PromptStatus.Keyword && !ReadOptions(ed, bySize, out first, out second)) return;
            if (!bySize && first == 1 && second == 1) { ed.WriteMessage("\n倍率为 1，未修改任何对象。"); return; }
            try
            {
                ObjectScaleResult result;
                using (doc.LockDocument())
                    result = bySize
                        ? ObjectScaleService.ScaleToSize(doc.Database, selected.Value.GetObjectIds(), first, second)
                        : ObjectScaleService.ScaleXy(doc.Database, selected.Value.GetObjectIds(), first.Value, second.Value);
                ed.WriteMessage("\n已绕各自中心缩放 " + result.Scaled + " 个对象，跳过 " + result.Failures.Count + " 个对象。可一次 U 撤销。" +
                    (result.Deduplicated == 0 ? "" : "已去除 " + result.Deduplicated + " 个随父图块联动的属性重复选择。") +
                    (result.Failures.Count == 0 ? "" : "\n" + string.Join("\n", result.Failures)));
            }
            catch (System.Exception ex) { ed.WriteMessage("\n批量缩放失败：" + ex.Message); }
        }

        private static bool ReadOptions(Editor ed, bool bySize, out double? first, out double? second)
        {
            first = second = null;
            var aspect = new PromptKeywordOptions("\n宽高比");
            aspect.Keywords.Add("Lock", "L", "锁定(L)");
            aspect.Keywords.Add("Unlock", "U", "解锁(U)");
            aspect.Keywords.Default = "Lock";
            aspect.AllowNone = true;
            var mode = ed.GetKeywords(aspect);
            if (mode.Status != PromptStatus.OK && mode.Status != PromptStatus.None) return false;
            bool unlocked = mode.Status == PromptStatus.OK && mode.StringResult == "Unlock";
            if (bySize && !unlocked)
            {
                var axis = new PromptKeywordOptions("\n选择要匹配的目标方向");
                axis.Keywords.Add("Width", "W", "宽(W)");
                axis.Keywords.Add("Height", "H", "高(H)");
                var direction = ed.GetKeywords(axis);
                if (direction.Status != PromptStatus.OK) return false;
                bool width = direction.StringResult == "Width";
                var size = ed.GetDouble(PositiveInput(width ? "\n输入目标宽度" : "\n输入目标高度"));
                if (size.Status != PromptStatus.OK) return false;
                if (width) first = size.Value;
                else second = size.Value;
                return true;
            }

            string prompt = bySize ? "\n输入目标宽度" : unlocked ? "\n输入 X 方向百分比" : "\n输入等比百分比（100 表示原尺寸）";
            var x = ed.GetDouble(PositiveInput(prompt));
            if (x.Status != PromptStatus.OK) return false;
            first = bySize ? x.Value : x.Value / 100;
            second = first;
            if (!unlocked) return true;
            var y = ed.GetDouble(PositiveInput(bySize ? "\n输入目标高度" : "\n输入 Y 方向百分比"));
            if (y.Status != PromptStatus.OK) return false;
            second = bySize ? y.Value : y.Value / 100;
            return true;
        }

        private static PromptDoubleOptions PositiveInput(string prompt)
        {
            return new PromptDoubleOptions(prompt) { AllowNegative = false, AllowZero = false };
        }
    }
}
