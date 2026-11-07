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
    public static class PolylineBreakService
    {
        public const double PointTolerance = 1e-8;
        public static bool IsPolyline(Entity entity) { return entity is Polyline || entity is Polyline2d || entity is Polyline3d; }
        public static double Length(Curve curve) { return curve.GetDistanceAtParameter(curve.EndParam) - curve.GetDistanceAtParameter(curve.StartParam); }
        public static List<double> Normalize(Curve curve, IEnumerable<double> parameters)
        {
            if (!IsPolyline(curve)) throw new InvalidOperationException("请选择 PL 线。");
            double length = Length(curve), startDistance = curve.GetDistanceAtParameter(curve.StartParam);
            if (length <= PointTolerance) throw new InvalidOperationException("PL 为零长度，无法打断。");
            var result = new List<double>(); var distances = new List<double>();
            foreach (double raw in parameters.OrderBy(p => p))
            {
                if (double.IsNaN(raw) || double.IsInfinity(raw) || raw < curve.StartParam || raw > curve.EndParam)
                    throw new ArgumentException("断点不在 PL 的参数范围内。");
                double parameter = raw, distance = curve.GetDistanceAtParameter(raw) - startDistance;
                if (curve.Closed && length - distance <= PointTolerance) { parameter = curve.StartParam; distance = 0; }
                if (!curve.Closed && (distance <= PointTolerance || length - distance <= PointTolerance)) continue;
                if (distances.Any(d => Math.Abs(d - distance) <= PointTolerance)) continue;
                result.Add(parameter); distances.Add(distance);
            }
            return result.OrderBy(p => p).ToList();
        }
        public static bool AddPoint(Curve curve, Point3d worldPoint, List<double> points)
        {
            var nearest = curve.GetClosestPointTo(worldPoint, false);
            double parameter = curve.GetParameterAtPoint(nearest);
            var valid = Normalize(curve, new[] { parameter });
            if (valid.Count == 0) return false;
            double distance = curve.GetDistanceAtParameter(valid[0]), length = Length(curve);
            if (points.Any(p => {
                double delta = Math.Abs(curve.GetDistanceAtParameter(p) - distance);
                return delta <= PointTolerance || (curve.Closed && length - delta <= PointTolerance);
            })) return false;
            points.Add(valid[0]); return true;
        }
        // Results are detached and owned by the caller until ReplacePrepared.
        public static List<Entity> Split(Curve curve, IEnumerable<double> parameters)
        {
            var cuts = Normalize(curve, parameters);
            if (cuts.Count == 0) throw new InvalidOperationException("没有有效断点，原 PL 保留。");
            if (curve.Closed && cuts.Count == 1) return OpenLoop(curve, cuts[0]);
            var objects = curve.GetSplitCurves(new DoubleCollection(cuts.ToArray()));
            Entity wrap = null;
            try
            {
                var results = objects.Cast<DBObject>().OfType<Entity>().ToList();
                int expected = curve.Closed ? cuts.Count : cuts.Count + 1;
                // Legacy polylines also split at the old seam. Join only those
                // two end pieces so requested points remain the only breaks.
                if (curve.Closed && results.Count == expected + 1)
                {
                    wrap = JoinParts(curve, new[] { (Curve)results.Last(), (Curve)results.First() });
                    results = results.Skip(1).Take(results.Count - 2).Concat(new[] { wrap }).ToList();
                }
                if (results.Count != expected || results.Any(e => !IsPolyline(e) || ((Curve)e).Closed || Length((Curve)e) <= PointTolerance))
                    throw new InvalidOperationException("原生打断未生成完整的开放 PL 分段，保留原对象。");
                double originalLength = Length(curve), total = results.Sum(e => Length((Curve)e));
                if (Math.Abs(total - originalLength) > Math.Max(1e-7, originalLength * 1e-10))
                    throw new InvalidOperationException("分段长度与原 PL 不一致，保留原对象。");
                foreach (Entity unused in objects.Cast<Entity>().Where(e => !results.Contains(e))) DisposeDetached(unused);
                return results;
            }
            catch { if (wrap != null) DisposeDetached(wrap); foreach (Entity part in objects) DisposeDetached(part); throw; }
        }
        private static List<Entity> OpenLoop(Curve source, double cut)
        {
            // A single native cut can also split at the old seam. Two native cuts
            // give exact subcurves; concatenate them from the requested point,
            // leaving coincident endpoints and keeping the entire closing edge.
            double startDistance = source.GetDistanceAtParameter(source.StartParam), length = Length(source);
            double auxiliaryDistance = (source.GetDistanceAtParameter(cut) - startDistance + length / 2) % length;
            double auxiliary = source.GetParameterAtDistance(startDistance + auxiliaryDistance);
            var raw = source.GetSplitCurves(new DoubleCollection(new[] { cut, auxiliary }.OrderBy(p => p).ToArray()));
            Entity output = null;
            try
            {
                var parts = raw.Cast<Curve>().ToList(); var requested = source.GetPointAtParameter(cut);
                if (parts.Count < 2 || parts.Any(p => p.Closed)) throw new InvalidOperationException("闭合 PL 无法生成完整的环形分段。");
                int first = parts.FindIndex(p => p.StartPoint.DistanceTo(requested) <= PointTolerance);
                if (first < 0) throw new InvalidOperationException("无法定位闭合 PL 的单点断口。");
                output = JoinParts(source, Enumerable.Range(0, parts.Count).Select(i => parts[(first + i) % parts.Count]));
                if (Math.Abs(Length((Curve)output) - length) > Math.Max(1e-7, length * 1e-10) ||
                    ((Curve)output).StartPoint.DistanceTo(requested) > PointTolerance || ((Curve)output).EndPoint.DistanceTo(requested) > PointTolerance)
                    throw new InvalidOperationException("打开后的 PL 与原轮廓不一致，保留原对象。");
                return new List<Entity> { output };
            }
            catch { if (output != null) DisposeDetached(output); throw; }
            finally { foreach (Entity part in raw) DisposeDetached(part); }
        }
        private static Entity JoinParts(Curve source, IEnumerable<Curve> parts)
        {
            Entity output = null;
            try
            {
                var pieces = parts.SelectMany(DetachedPieces).ToList();
                if (pieces.Count == 0) throw new InvalidOperationException("闭合 PL 没有有效长度。");
                var path = new PolylinePath(); path.Pieces.AddRange(pieces); path.Points.Add(pieces[0].Start);
                foreach (var piece in pieces)
                {
                    if (piece.Start.DistanceTo(path.Points.Last()) > PointTolerance) throw new InvalidOperationException("环形分段不连续。");
                    path.Points.Add(piece.End);
                }
                var pl = source as Polyline; var legacy = source as Polyline2d;
                if (pl != null) { path.Normal = pl.Normal; path.Thickness = pl.Thickness; path.Plinegen = pl.Plinegen; }
                if (legacy != null) { path.Normal = legacy.Normal; path.Thickness = legacy.Thickness; path.Plinegen = legacy.LinetypeGenerationOn; }
                if (source is Polyline3d) output = new Polyline3d(Poly3dType.SimplePoly, new Point3dCollection(path.Points.ToArray()), false);
                else if (legacy != null)
                {
                    var transform = Matrix3d.WorldToPlane(legacy.Normal);
                    var vertices = path.Points.Select(p => p.TransformBy(transform)).ToList();
                    var p2 = new Polyline2d(Poly2dType.SimplePoly, new Point3dCollection(vertices.Select(p => new Point3d(p.X, p.Y, 0)).ToArray()), 0, false, 0, 0,
                        new DoubleCollection(pieces.Select(p => p.Bulge).Concat(new[] { 0.0 }).ToArray())); output = p2;
                    p2.Normal = legacy.Normal; p2.Elevation = vertices[0].Z; p2.Thickness = legacy.Thickness; p2.LinetypeGenerationOn = legacy.LinetypeGenerationOn;
                    int index = 0;
                    foreach (Vertex2d vertex in p2)
                    {
                        if (index < pieces.Count) { vertex.StartWidth = pieces[index].StartWidth; vertex.EndWidth = pieces[index].EndWidth; }
                        index++;
                    }
                }
                else output = LineToPolylineService.Create(path, path.Normal);
                return output;
            }
            catch { if (output != null) DisposeDetached(output); throw; }
        }
        private static List<PolylinePiece> DetachedPieces(Curve source)
        {
            var pieces = new List<PolylinePiece>(); var p = source as Polyline; var p2 = source as Polyline2d; var p3 = source as Polyline3d;
            if (p != null)
                for (int i = 0; i < p.NumberOfVertices - 1; i++) pieces.Add(new PolylinePiece { Start = p.GetPoint3dAt(i), End = p.GetPoint3dAt(i + 1),
                    Bulge = p.GetBulgeAt(i), Normal = p.Normal, StartWidth = p.GetStartWidthAt(i), EndWidth = p.GetEndWidthAt(i) });
            else if (p2 != null && p2.PolyType == Poly2dType.SimplePoly)
            {
                var vertices = p2.Cast<Vertex2d>().ToList();
                for (int i = 0; i < vertices.Count - 1; i++) pieces.Add(new PolylinePiece { Start = p2.VertexPosition(vertices[i]), End = p2.VertexPosition(vertices[i + 1]),
                    Bulge = vertices[i].Bulge, Normal = p2.Normal, StartWidth = vertices[i].StartWidth, EndWidth = vertices[i].EndWidth });
            }
            else if (p3 != null && p3.PolyType == Poly3dType.SimplePoly)
            {
                var vertices = p3.Cast<PolylineVertex3d>().ToList();
                for (int i = 0; i < vertices.Count - 1; i++) pieces.Add(new PolylinePiece { Start = vertices[i].Position, End = vertices[i + 1].Position });
            }
            else throw new InvalidOperationException("原生分段未返回可无损重组的 PL。");
            return pieces;
        }
        public static void DisposeDetached(Entity entity)
        {
            if (entity.IsDisposed) return;
            // Nonresident heavy polylines do not own disposal of their vertices.
            // Resident ones are owned by AutoCAD and must only close their wrapper.
            if (entity.ObjectId.IsNull)
            {
                var enumerable = entity as System.Collections.IEnumerable;
                if (entity is Polyline2d || entity is Polyline3d)
                    foreach (DBObject vertex in enumerable) vertex.Dispose();
            }
            entity.Dispose();
        }
    }

    public class PolylineBreakCommands
    {
        [CommandMethod("XYD_PLBREAK", CommandFlags.UsePickSet)]
        public void BreakPolyline()
        {
            var doc = Application.DocumentManager.MdiActiveDocument; if (doc == null) return;
            var ed = doc.Editor; var id = ObjectId.Null;
            var implied = ed.SelectImplied();
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                if (implied.Status == PromptStatus.OK)
                {
                    var ids = implied.Value.GetObjectIds();
                    if (ids.Length == 1 && PolylineBreakService.IsPolyline(tr.GetObject(ids[0], OpenMode.ForRead) as Entity)) id = ids[0];
                }
            }
            if (id.IsNull)
            {
                var prompt = new PromptEntityOptions("\n选择要无缝打断的 PL 线");
                prompt.SetRejectMessage("\n请选择轻量、二维或三维 PL 线。");
                prompt.AddAllowedClass(typeof(Polyline), true); prompt.AddAllowedClass(typeof(Polyline2d), true); prompt.AddAllowedClass(typeof(Polyline3d), true);
                var selected = ed.GetEntity(prompt); if (selected.Status != PromptStatus.OK) return; id = selected.ObjectId;
            }
            Curve snapshot = null; var points = new List<double>(); bool multiple = false;
            try
            {
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    var source = (Curve)tr.GetObject(id, OpenMode.ForRead);
                    if (((LayerTableRecord)tr.GetObject(source.LayerId, OpenMode.ForRead)).IsLocked) throw new InvalidOperationException("图层已锁定，原 PL 保留。");
                    if (((BlockTableRecord)tr.GetObject(source.OwnerId, OpenMode.ForRead)).IsFromExternalReference) throw new InvalidOperationException("外参对象不能直接编辑。");
                    if (PolylineBreakService.Length(source) <= PolylineBreakService.PointTolerance) throw new InvalidOperationException("零长度 PL 无法打断。");
                    snapshot = (Curve)source.Clone();
                }
                if (snapshot.Closed) ed.WriteMessage("\n闭合 PL：单点打开为一条开放 PL；多个不同断点按环分段，全部轮廓保留。");
                while (true)
                {
                    var prompt = new PromptPointOptions(multiple ? "\n指定下一个断点（回车完成）或" : "\n指定打断点或") { AllowNone = true };
                    if (multiple)
                    {
                        prompt.Keywords.Add("Single", "S", "单点(S)"); prompt.Keywords.Add("Undo", "U", "撤销上点(U)");
                    }
                    else prompt.Keywords.Add("Multiple", "M", "多点(M)");
                    var picked = ed.GetPoint(prompt);
                    if (picked.Status == PromptStatus.Keyword)
                    {
                        if (picked.StringResult == "Multiple") { multiple = true; ed.WriteMessage("\n多点模式：连续点选断点，回车确认，Esc 取消整次操作。"); }
                        else if (picked.StringResult == "Single") { multiple = false; points.Clear(); ed.WriteMessage("\n已切换为单点模式，清除暂存断点，请重新指定一个点。"); }
                        else if (picked.StringResult == "Undo")
                        {
                            if (points.Count > 0) points.RemoveAt(points.Count - 1);
                            ed.WriteMessage("\n暂存断点 " + points.Count + " 个。");
                        }
                        continue;
                    }
                    if (picked.Status == PromptStatus.None) break;
                    if (picked.Status != PromptStatus.OK) { ed.WriteMessage("\n已取消，原 PL 保留。"); return; }
                    var worldPoint = picked.Value.TransformBy(ed.CurrentUserCoordinateSystem);
                    if (!PolylineBreakService.AddPoint(snapshot, worldPoint, points)) { ed.WriteMessage("\n重复断点或开放线端点已跳过，请选择有效位置。"); continue; }
                    if (!multiple) break;
                    ed.WriteMessage("\n已暂存 " + points.Count + " 个断点，预计生成 " + (snapshot.Closed ? points.Count : points.Count + 1) + " 条 PL。原线尚未修改。");
                }
                if (points.Count == 0) { ed.WriteMessage("\n没有有效断点，原 PL 保留。"); return; }
                // Split the resident curve after input is complete: legacy polyline
                // fit vertices are accessed by AutoCAD itself, without chord fitting.
                List<Entity> outputs;
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    var source = (Curve)tr.GetObject(id, OpenMode.ForRead);
                    if (((LayerTableRecord)tr.GetObject(source.LayerId, OpenMode.ForRead)).IsLocked) throw new InvalidOperationException("图层已锁定，原 PL 保留。");
                    outputs = PolylineBreakService.Split(source, points);
                }
                int count = outputs.Count;
                try { using (doc.LockDocument()) CurveMetadataService.ReplacePrepared(doc.Database, id, outputs); }
                finally { foreach (var output in outputs) PolylineBreakService.DisposeDetached(output); }
                ed.WriteMessage("\n已按 " + points.Count + " 个断点生成 " + count + " 条 PL，全部线段保留，无缺口；圆弧、宽度和原高程保留。一次 U 可撤销整次打断。");
            }
            catch (System.Exception ex) { ed.WriteMessage("\nPL 打断未完成：" + ex.Message); }
            finally { if (snapshot != null) PolylineBreakService.DisposeDetached(snapshot); }
        }
    }
}
