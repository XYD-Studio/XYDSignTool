using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace XYDSignTool
{
    // Each original curve is one graph edge: internal vertices are not junctions.
    public class PolylinePiece
    {
        public Point3d Start { get; set; }
        public Point3d End { get; set; }
        public double Bulge { get; set; }
        public Vector3d Normal { get; set; } = Vector3d.ZAxis;
        public double StartWidth { get; set; }
        public double EndWidth { get; set; }
        public PolylinePiece Reverse()
        {
            return new PolylinePiece { Start = End, End = Start, Bulge = -Bulge, Normal = Normal,
                StartWidth = EndWidth, EndWidth = StartWidth };
        }
        public Point3d At(double fraction)
        {
            if (Bulge == 0) return Start + (End - Start) * fraction;
            var chord = End - Start;
            var center = Start + chord * .5 + Normal.GetNormal().CrossProduct(chord.GetNormal()) *
                (chord.Length * (1 - Bulge * Bulge) / (4 * Bulge));
            return center + (Start - center).RotateBy(4 * Math.Atan(Bulge) * fraction, Normal.GetNormal());
        }
    }
    public class LineSegmentInput
    {
        public ObjectId Id { get; set; }
        public Point3d Start { get; set; }
        public Point3d End { get; set; }
        public string Properties { get; set; }
        public List<PolylinePiece> Pieces { get; } = new List<PolylinePiece>();
        public bool Closed { get; set; }
        public bool Plinegen { get; set; }
        public bool Approximated { get; set; }
        public Vector3d? Normal { get; set; }
        public double Thickness { get; set; }
        // Owned detached source for exploded outlines; disposed by the command.
        public Entity DisplaySource { get; set; }
    }
    public class PolylinePath
    {
        public List<Point3d> Points { get; } = new List<Point3d>();
        public List<PolylinePiece> Pieces { get; } = new List<PolylinePiece>();
        public List<ObjectId> Originals { get; } = new List<ObjectId>();
        public bool Closed { get; set; }
        public bool Plinegen { get; set; }
        public Vector3d? Normal { get; set; }
        public double Thickness { get; set; }
        public Entity DisplaySource { get; set; }
    }
    public static class LineToPolylineService
    {
        private const int MaxVertices = 100000;
        private class CurveSample { public double Parameter; public Point3d Point; }
        private class Node { public List<Point3d> Points = new List<Point3d>(); public List<int> Edges = new List<int>(); }
        private class Edge { public LineSegmentInput Input; public int A, B; public bool Used; }

        public static bool IsSupported(Entity entity)
        {
            // Accept finite Curve subclasses, including Helix and future curve types.
            return (entity is Curve && !(entity is Ray) && !(entity is Xline)) || entity is Mline || entity is Region;
        }
        public static bool NeedsApproximation(Curve curve)
        {
            if (curve is Polyline2d) return ((Polyline2d)curve).PolyType != Poly2dType.SimplePoly;
            if (curve is Polyline3d) return ((Polyline3d)curve).PolyType != Poly3dType.SimplePoly;
            return IsSupported(curve) && !(curve is Line || curve is Arc || curve is Circle || curve is Polyline);
        }
        public static List<LineSegmentInput> ReadAll(Entity entity, Transaction tr, double approximationError, bool preserveData = true, bool unifyZ = false)
        {
            var curve = entity as Curve;
            if (curve != null) return new List<LineSegmentInput> { Read(curve, tr, approximationError, preserveData, unifyZ) };
            if (!(entity is Mline || entity is Region)) throw new InvalidOperationException("此对象没有可转换的有限曲线轮廓。");
            var exploded = new DBObjectCollection(); var inputs = new List<LineSegmentInput>();
            try
            {
                entity.Explode(exploded);
                foreach (DBObject part in exploded)
                {
                    var child = part as Curve;
                    if (child == null || !IsSupported(child)) throw new InvalidOperationException("轮廓含无法转换的组成对象，保留整个原对象。");
                    var input = Read(child, tr, approximationError, preserveData, unifyZ); input.Id = entity.ObjectId;
                    // Keep outlines local to their original: holes and multiline branches
                    // must not accidentally join other selected entities.
                    input.Properties = entity.ObjectId + "|outline|" + input.Properties;
                    if (preserveData) input.Properties += CurveMetadataService.GroupKey(entity);
                    input.DisplaySource = (Entity)child.Clone(); inputs.Add(input);
                }
                if (inputs.Count == 0) throw new InvalidOperationException("对象没有可转换的轮廓。");
                return inputs;
            }
            catch { foreach (var input in inputs) input.DisplaySource.Dispose(); throw; }
            finally { foreach (DBObject part in exploded) part.Dispose(); }
        }
        public static LineSegmentInput Read(Curve curve, Transaction tr, double approximationError, bool preserveData = true, bool unifyZ = false)
        {
            var input = new LineSegmentInput { Id = curve.ObjectId, Closed = curve.Closed };
            Action<Point3d, Point3d, double, Vector3d, double, double> add = (a, b, bulge, normal, w1, w2) =>
                input.Pieces.Add(new PolylinePiece { Start = a, End = b, Bulge = bulge, Normal = normal, StartWidth = w1, EndWidth = w2 });
            var line = curve as Line; var arc = curve as Arc; var circle = curve as Circle;
            var pl = curve as Polyline; var pl2 = curve as Polyline2d; var pl3 = curve as Polyline3d;
            if (line != null)
            {
                input.Normal = line.Normal; input.Thickness = line.Thickness;
                add(line.StartPoint, line.EndPoint, 0, line.Normal, 0, 0);
            }
            else if (arc != null)
            {
                input.Normal = arc.Normal; input.Thickness = arc.Thickness;
                add(arc.StartPoint, arc.EndPoint, Math.Tan(arc.TotalAngle / 4), arc.Normal, 0, 0);
            }
            else if (circle != null)
            {
                input.Normal = circle.Normal; input.Thickness = circle.Thickness;
                var points = Enumerable.Range(0, 4).Select(i => circle.GetPointAtParameter(i * Math.PI / 2)).ToArray();
                for (int i = 0; i < 4; i++) add(points[i], points[(i + 1) % 4], Math.Tan(Math.PI / 8), circle.Normal, 0, 0);
            }
            else if (pl != null)
            {
                input.Normal = pl.Normal; input.Thickness = pl.Thickness; input.Plinegen = pl.Plinegen;
                int count = pl.NumberOfVertices;
                for (int i = 0; i < count - (pl.Closed ? 0 : 1); i++)
                    add(pl.GetPoint3dAt(i), pl.GetPoint3dAt((i + 1) % count), pl.GetBulgeAt(i), pl.Normal, pl.GetStartWidthAt(i), pl.GetEndWidthAt(i));
            }
            else if (pl2 != null && pl2.PolyType == Poly2dType.SimplePoly)
            {
                input.Normal = pl2.Normal; input.Thickness = pl2.Thickness; input.Plinegen = pl2.LinetypeGenerationOn;
                var vertices = pl2.Cast<ObjectId>().Select(id => (Vertex2d)tr.GetObject(id, OpenMode.ForRead)).ToList();
                Func<Vertex2d, Point3d> world = v => new Point3d(v.Position.X, v.Position.Y, pl2.Elevation).TransformBy(Matrix3d.PlaneToWorld(pl2.Normal));
                for (int i = 0; i < vertices.Count - (pl2.Closed ? 0 : 1); i++)
                {
                    var v = vertices[i]; add(world(v), world(vertices[(i + 1) % vertices.Count]), v.Bulge, pl2.Normal, v.StartWidth, v.EndWidth);
                }
            }
            else if (pl3 != null && pl3.PolyType == Poly3dType.SimplePoly)
            {
                var points = pl3.Cast<ObjectId>().Select(id => ((PolylineVertex3d)tr.GetObject(id, OpenMode.ForRead)).Position).ToList();
                for (int i = 0; i < points.Count - (pl3.Closed ? 0 : 1); i++) add(points[i], points[(i + 1) % points.Count], 0, Vector3d.ZAxis, 0, 0);
            }
            else if (NeedsApproximation(curve))
            {
                ValidateError(approximationError);
                List<Vertex2d> widthVertices = null;
                if (pl2 != null)
                {
                    input.Normal = pl2.Normal; input.Thickness = pl2.Thickness; input.Plinegen = pl2.LinetypeGenerationOn;
                    widthVertices = pl2.Cast<ObjectId>().Select(id => (Vertex2d)tr.GetObject(id, OpenMode.ForRead)).Where(v => v.VertexType != Vertex2dType.SplineControlVertex).ToList();
                    int spans = widthVertices.Count - (pl2.Closed ? 0 : 1);
                    if (curve.StartParam != 0 || Math.Abs(curve.EndParam - spans) > 1e-8)
                        throw new InvalidOperationException("旧式拟合多段线的顶点参数无效，保留原对象。");
                }
                if (pl2 != null || pl3 != null)
                {
                    // Legacy fitted polylines' geometric spline can differ from their
                    // displayed fit vertices. Evaluate the database curve itself.
                    var points = SampleLegacy(curve, approximationError);
                    for (int i = 1; i < points.Count; i++) if (points[i].Point.DistanceTo(points[i - 1].Point) > 0)
                    {
                        double w1 = 0, w2 = 0;
                        if (widthVertices != null)
                        {
                            int span = Math.Min(widthVertices.Count - 1, (int)Math.Floor(points[i - 1].Parameter));
                            var v = widthVertices[span]; double delta = v.EndWidth - v.StartWidth;
                            w1 = v.StartWidth + delta * (points[i - 1].Parameter - span);
                            w2 = v.StartWidth + delta * (points[i].Parameter - span);
                        }
                        add(points[i - 1].Point, points[i].Point, 0, Vector3d.ZAxis, w1, w2);
                    }
                }
                else using (var geometry = curve.GetGeCurve())
                {
                    var interval = geometry.GetInterval();
                    if (!interval.IsBounded) throw new InvalidOperationException("无界曲线没有有限的 PL 轮廓。");
                    var samples = geometry.GetSamplePoints(interval.LowerBound, interval.UpperBound, approximationError);
                    try
                    {
                        if (samples.Length > MaxVertices) throw new InvalidOperationException("拟合顶点超过 100000，请增大拟合误差。");
                        var points = samples.Select(p => p.Point).ToList();
                        if (points.Count < 2) throw new InvalidOperationException("曲线没有可转换的长度。");
                        points[0] = curve.StartPoint; points[points.Count - 1] = input.Closed ? points[0] : curve.EndPoint;
                        for (int i = 1; i < points.Count; i++) if (points[i].DistanceTo(points[i - 1]) > 0) add(points[i - 1], points[i], 0, Vector3d.ZAxis, 0, 0);
                    }
                    finally { foreach (var sample in samples) sample.Dispose(); }
                }
                input.Approximated = true;
            }
            else throw new InvalidOperationException("此对象不支持转换。");
            if (input.Pieces.Count == 0 || input.Pieces.All(p => p.Start.DistanceTo(p.End) < 1e-10))
                throw new InvalidOperationException("零长度曲线或退化多段线。");
            if (input.Pieces.Any(p => p.Bulge != 0 && p.Start.DistanceTo(p.End) < 1e-10))
                throw new InvalidOperationException("圆弧段端点重合，保留原对象。");
            if (unifyZ) Flatten(input, approximationError);
            input.Start = input.Pieces[0].Start; input.End = input.Pieces.Last().End;
            // Zero-thickness LINE normals must not prevent joins to tilted arcs.
            input.Properties = curve.OwnerId + "|" + curve.LayerId + "|" + curve.Color + "|" + curve.LinetypeId + "|" +
                curve.LinetypeScale.ToString("R", CultureInfo.InvariantCulture) + "|" + curve.LineWeight + "|" + curve.Transparency + "|" +
                input.Thickness.ToString("R", CultureInfo.InvariantCulture) + "|" + (input.Thickness == 0 ? "" : input.Normal.ToString()) + "|" +
                curve.MaterialId + "|" + curve.PlotStyleNameId + "|" + curve.Visible + "|" + input.Plinegen + (preserveData ? CurveMetadataService.GroupKey(curve) : "");
            return input;
        }
        private static int ArcSteps(PolylinePiece piece, double error)
        {
            if (piece.Bulge == 0) return 1;
            ValidateError(error);
            double radius = piece.Start.DistanceTo(piece.End) * (1 + piece.Bulge * piece.Bulge) / (4 * Math.Abs(piece.Bulge));
            double step = Math.Min(Math.PI / 2, 4 * Math.Asin(Math.Sqrt(Math.Min(1, error / (2 * radius)))));
            double required = Math.Ceiling(Math.Abs(4 * Math.Atan(piece.Bulge)) / step);
            if (required > MaxVertices || double.IsInfinity(required) || double.IsNaN(required))
                throw new InvalidOperationException("圆弧拟合顶点超过 100000，请增大拟合误差。");
            return Math.Max(1, (int)required);
        }
        private static void Flatten(LineSegmentInput input, double error)
        {
            Func<Point3d, Point3d> project = p => new Point3d(p.X, p.Y, 0);
            var pieces = new List<PolylinePiece>();
            foreach (var piece in input.Pieces)
            {
                bool tiltedArc = piece.Bulge != 0 && piece.Normal.GetNormal().CrossProduct(Vector3d.ZAxis).Length > 1e-12;
                int count = tiltedArc ? ArcSteps(piece, error) : 1;
                if (pieces.Count + count > MaxVertices) throw new InvalidOperationException("投影路径顶点超过 100000，请增大拟合误差。");
                var start = project(piece.Start);
                for (int i = 1; i <= count; i++)
                {
                    var end = project(i == count ? piece.End : piece.At((double)i / count));
                    // A vertical segment disappears under XY projection. Do not add
                    // a duplicate vertex, but keep the surrounding path connected.
                    if (start.DistanceTo(end) > 1e-10)
                        pieces.Add(new PolylinePiece { Start = start, End = end, Normal = Vector3d.ZAxis,
                            Bulge = tiltedArc ? 0 : piece.Bulge * (piece.Normal.Z < 0 ? -1 : 1),
                            StartWidth = piece.StartWidth + (piece.EndWidth - piece.StartWidth) * (i - 1) / count,
                            EndWidth = piece.StartWidth + (piece.EndWidth - piece.StartWidth) * i / count });
                    start = end;
                }
                if (tiltedArc) input.Approximated = true;
            }
            if (pieces.Count == 0) throw new InvalidOperationException("统一 Z 后投影为零长度，保留原对象。");
            input.Pieces.Clear(); input.Pieces.AddRange(pieces); input.Normal = Vector3d.ZAxis;
        }
        private static List<CurveSample> SampleLegacy(Curve curve, double error)
        {
            double first = curve.StartParam, last = curve.EndParam;
            if (last <= first || last - first > MaxVertices) throw new InvalidOperationException("旧式拟合多段线参数范围无效或顶点过多。");
            var result = new List<CurveSample> { new CurveSample { Parameter = first, Point = curve.StartPoint } };
            Func<Point3d, Point3d, Point3d, double> distance = (p, a, b) =>
            {
                var d = b - a; double t = d.LengthSqrd == 0 ? 0 : Math.Max(0, Math.Min(1, (p - a).DotProduct(d) / d.LengthSqrd));
                return p.DistanceTo(a + d * t);
            };
            Action<double, Point3d, double, Point3d, int> subdivide = null;
            subdivide = (a, pa, b, pb, depth) =>
            {
                var pm = curve.GetPointAtParameter((a + b) / 2); var q1 = curve.GetPointAtParameter(a + (b - a) / 4); var q3 = curve.GetPointAtParameter(a + 3 * (b - a) / 4);
                if (Math.Max(distance(pm, pa, pb), Math.Max(distance(q1, pa, pb), distance(q3, pa, pb))) > error * .5)
                {
                    if (depth >= 30) throw new InvalidOperationException("旧式拟合多段线无法达到指定误差。");
                    subdivide(a, pa, (a + b) / 2, pm, depth + 1); subdivide((a + b) / 2, pm, b, pb, depth + 1);
                }
                else { if (result.Count >= MaxVertices) throw new InvalidOperationException("拟合顶点超过 100000，请增大拟合误差。"); result.Add(new CurveSample { Parameter = b, Point = pb }); }
            };
            // Integer parameters delimit the individual legacy polyline spans.
            for (double a = first; a < last; )
            {
                double b = Math.Min(last, Math.Floor(a) + 1); subdivide(a, curve.GetPointAtParameter(a), b, curve.GetPointAtParameter(b), 0); a = b;
            }
            result[0].Point = curve.StartPoint; result[result.Count - 1].Point = curve.Closed ? result[0].Point : curve.EndPoint; return result;
        }
        private static void Append(PolylinePath path, PolylinePiece piece)
        {
            if (path.Points.Count == 0) path.Points.Add(piece.Start);
            else if (path.Points.Last().DistanceTo(piece.Start) > 0)
            {
                path.Pieces.Add(new PolylinePiece { Start = path.Points.Last(), End = piece.Start }); path.Points.Add(piece.Start);
            }
            path.Pieces.Add(piece); path.Points.Add(piece.End);
        }
        private static void Append(PolylinePath path, LineSegmentInput input, bool forward)
        {
            if (path.Originals.Count == 0) { path.Normal = input.Normal; path.Thickness = input.Thickness; path.Plinegen = input.Plinegen; path.DisplaySource = input.DisplaySource; }
            IEnumerable<PolylinePiece> pieces = input.Pieces.Count == 0 ? new[] { new PolylinePiece { Start = input.Start, End = input.End } } : (IEnumerable<PolylinePiece>)input.Pieces;
            foreach (var piece in forward ? pieces : pieces.Reverse().Select(p => p.Reverse())) Append(path, piece);
            path.Originals.Add(input.Id);
        }
        private static void Close(PolylinePath path)
        {
            if (path.Points.Last().DistanceTo(path.Points[0]) > 0) Append(path, new PolylinePiece { Start = path.Points.Last(), End = path.Points[0] });
            path.Points.RemoveAt(path.Points.Count - 1); path.Closed = true;
        }
        public static List<PolylinePath> Build(IEnumerable<LineSegmentInput> input, double tolerance)
        {
            if (double.IsNaN(tolerance) || double.IsInfinity(tolerance) || tolerance < 0) throw new ArgumentException("容差必须是有效非负数字。");
            var result = new List<PolylinePath>();
            foreach (var group in input.GroupBy(s => s.Properties))
            {
                var nodes = new List<Node>(); var edges = new List<Edge>();
                var buckets = new Dictionary<string, HashSet<int>>(); double cellSize = Math.Max(tolerance, 1e-8);
                Func<Point3d, int> nodeFor = point =>
                {
                    var candidates = new HashSet<int>();
                    long x = (long)Math.Floor(point.X / cellSize), y = (long)Math.Floor(point.Y / cellSize), z = (long)Math.Floor(point.Z / cellSize);
                    for (int a = -1; a <= 1; a++) for (int b = -1; b <= 1; b++) for (int c = -1; c <= 1; c++)
                    { HashSet<int> ids; if (buckets.TryGetValue((x + a) + ":" + (y + b) + ":" + (z + c), out ids)) candidates.UnionWith(ids); }
                    int index = candidates.Where(i => nodes[i].Points.All(p => p.DistanceTo(point) <= tolerance)).OrderBy(i => nodes[i].Points.Max(p => p.DistanceTo(point))).ThenBy(i => i).DefaultIfEmpty(-1).First();
                    if (index < 0) { index = nodes.Count; nodes.Add(new Node()); }
                    nodes[index].Points.Add(point);
                    string key = x + ":" + y + ":" + z; if (!buckets.ContainsKey(key)) buckets[key] = new HashSet<int>(); buckets[key].Add(index);
                    return index;
                };
                foreach (var segment in group)
                {
                    if (segment.Closed) { var path = new PolylinePath(); Append(path, segment, true); Close(path); result.Add(path); continue; }
                    int a = nodeFor(segment.Start), b = nodeFor(segment.End); int edge = edges.Count;
                    edges.Add(new Edge { Input = segment, A = a, B = b }); nodes[a].Edges.Add(edge); nodes[b].Edges.Add(edge);
                }
                Action<int, int> walk = (start, firstEdge) =>
                {
                    var path = new PolylinePath(); int node = start, nextEdge = firstEdge;
                    while (!edges[nextEdge].Used)
                    {
                        var e = edges[nextEdge]; e.Used = true; bool forward = e.A == node;
                        Append(path, e.Input, forward); node = forward ? e.B : e.A;
                        if (node == start && path.Originals.Count > 1) { Close(path); break; }
                        if (nodes[node].Edges.Count != 2) break;
                        int candidate = nodes[node].Edges.Where(i => !edges[i].Used).DefaultIfEmpty(-1).First();
                        if (candidate < 0) break; nextEdge = candidate;
                    }
                    result.Add(path);
                };
                for (int i = 0; i < nodes.Count; i++) if (nodes[i].Edges.Count != 2) foreach (int e in nodes[i].Edges) if (!edges[e].Used) walk(i, e);
                for (int i = 0; i < edges.Count; i++) if (!edges[i].Used) walk(edges[i].A, i);
            }
            return result;
        }
        private static List<PolylinePiece> Pieces(PolylinePath path)
        {
            if (path.Pieces.Count > 0) return path.Pieces;
            var pieces = new List<PolylinePiece>();
            for (int i = 0; i < path.Points.Count - (path.Closed ? 0 : 1); i++) pieces.Add(new PolylinePiece { Start = path.Points[i], End = path.Points[(i + 1) % path.Points.Count] });
            return pieces;
        }
        public static bool TryPlane(PolylinePath path, Vector3d? preferredNormal, out Vector3d normal)
        {
            var pieces = Pieces(path); var points = path.Points.Concat(pieces.Where(p => p.Bulge != 0).Select(p => p.At(.5))).ToList();
            var origin = points[0]; var axis = points.First(p => p.DistanceTo(origin) > 1e-10) - origin;
            normal = Vector3d.ZAxis; bool found = false;
            foreach (var point in points)
            {
                var cross = axis.CrossProduct(point - origin);
                if (cross.Length > 1e-9) { normal = cross.GetNormal(); found = true; break; }
            }
            if (!found && Math.Abs(axis.GetNormal().DotProduct(normal)) > 1e-8)
                normal = axis.CrossProduct(Vector3d.XAxis).Length > 1e-9 ? axis.CrossProduct(Vector3d.XAxis).GetNormal() : axis.CrossProduct(Vector3d.YAxis).GetNormal();
            Func<Vector3d, bool> fits = n => points.All(p => Math.Abs((p - origin).DotProduct(n)) <= 1e-8) &&
                pieces.All(p => p.Bulge == 0 || Math.Abs(p.Normal.GetNormal().DotProduct(n)) >= 1 - 1e-10);
            if (preferredNormal.HasValue && fits(preferredNormal.Value.GetNormal())) normal = preferredNormal.Value.GetNormal();
            return fits(normal);
        }
        private static void ValidateError(double error)
        {
            if (double.IsNaN(error) || double.IsInfinity(error) || error <= 0) throw new ArgumentException("曲线拟合误差必须是有效正数。");
        }
        public static Entity Create(PolylinePath path, Vector3d? preferredNormal = null, double approximationError = .01)
        {
            var pieces = Pieces(path); Vector3d normal;
            if (!TryPlane(path, preferredNormal ?? path.Normal, out normal))
            {
                if (path.Thickness != 0 || pieces.Any(p => p.StartWidth != 0 || p.EndWidth != 0))
                    throw new InvalidOperationException("非共面路径无法保留宽度或厚度，保留原对象。");
                var points = new List<Point3d> { pieces[0].Start };
                foreach (var piece in pieces)
                {
                    int count = ArcSteps(piece, approximationError);
                    if (points.Count + count > MaxVertices) throw new InvalidOperationException("三维路径顶点超过 100000。");
                    for (int i = 1; i <= count; i++) points.Add(i == count ? piece.End : piece.At((double)i / count));
                }
                if (path.Closed) points.RemoveAt(points.Count - 1);
                return new Polyline3d(Poly3dType.SimplePoly, new Point3dCollection(points.ToArray()), path.Closed);
            }
            if (path.Thickness != 0 && path.Normal.HasValue && Math.Abs(path.Normal.Value.GetNormal().DotProduct(normal)) < 1 - 1e-10)
                throw new InvalidOperationException("路径无法保留厚度的拉伸方向。");
            var pl = new Polyline { Normal = normal, Closed = path.Closed, Thickness = path.Thickness, Plinegen = path.Plinegen };
            var transform = Matrix3d.WorldToPlane(normal); pl.Elevation = path.Points[0].TransformBy(transform).Z;
            try
            {
                for (int i = 0; i < path.Points.Count; i++)
                {
                    var p = path.Points[i].TransformBy(transform); var piece = i < pieces.Count ? pieces[i] : null;
                    double bulge = piece == null ? 0 : piece.Bulge * (piece.Normal.DotProduct(normal) < 0 ? -1 : 1);
                    pl.AddVertexAt(i, new Point2d(p.X, p.Y), bulge, piece == null ? 0 : piece.StartWidth, piece == null ? 0 : piece.EndWidth);
                }
                return pl;
            }
            catch { pl.Dispose(); throw; }
        }
    }
    public static class CurveMetadataService
    {
        public static bool HasData(Entity entity)
        {
            if (!entity.ExtensionDictionary.IsNull || entity.GetPersistentReactorIds().Count != 0) return true;
            using (var data = entity.XData) return data != null;
        }
        public static string GroupKey(Entity entity)
        {
            // Independent dictionaries, handles and reactors cannot be collapsed into
            // one owner. They still convert, but retain separate object identities.
            if (!entity.ExtensionDictionary.IsNull || entity.GetPersistentReactorIds().Count != 0) return "|metadata-owner:" + entity.ObjectId;
            using (var data = entity.XData)
            {
                if (data == null) return "";
                var values = data.AsArray();
                if (values.Any(v => v.TypeCode == 1005 || v.Value is ObjectId)) return "|metadata-owner:" + entity.ObjectId;
                using (var stream = new MemoryStream()) using (var writer = new BinaryWriter(stream))
                {
                    foreach (var value in values)
                    {
                        writer.Write(value.TypeCode); object v = value.Value;
                        writer.Write(v == null ? "null" : v.GetType().FullName);
                        if (v is byte[]) { var bytes = (byte[])v; writer.Write(bytes.Length); writer.Write(bytes); }
                        else if (v is double) writer.Write((double)v);
                        else if (v is Point3d) { var p = (Point3d)v; writer.Write(p.X); writer.Write(p.Y); writer.Write(p.Z); }
                        else if (v is Vector3d) { var p = (Vector3d)v; writer.Write(p.X); writer.Write(p.Y); writer.Write(p.Z); }
                        else writer.Write(Convert.ToString(v, CultureInfo.InvariantCulture) ?? "");
                    }
                    writer.Flush(); using (var hash = SHA256.Create()) return "|metadata:" + Convert.ToBase64String(hash.ComputeHash(stream.ToArray()));
                }
            }
        }
        private static void CopyXData(Entity source, Entity target)
        {
            using (var data = source.XData) if (data != null) target.XData = data;
        }
        public static void ReplaceBatch(Database db, List<PolylinePath> paths, double fitError, bool preserveData = true)
        {
            var originals = paths.SelectMany(p => p.Originals).Distinct().ToList();
            bool attachedData;
            using (var read = db.TransactionManager.StartTransaction())
                attachedData = preserveData && originals.Any(id => HasData((Entity)read.GetObject(id, OpenMode.ForRead)));
            if (attachedData) { ReplaceAssociated(db, paths, originals, fitError); return; }
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var outputs = new List<Entity>();
                try
                {
                    foreach (var path in paths)
                    {
                        var source = (Entity)tr.GetObject(path.Originals[0], OpenMode.ForRead);
                        var output = LineToPolylineService.Create(path, path.Normal, fitError); outputs.Add(output);
                        output.SetPropertiesFrom(path.DisplaySource ?? source);
                        if (preserveData) CopyXData(source, output);
                    }
                    // Validate every boundary before changing the database. In
                    // particular, an unsupported later boundary must not leave
                    // an already appended result from the same original.
                    for (int i = 0; i < paths.Count; i++)
                    {
                        var source = (Entity)tr.GetObject(paths[i].Originals[0], OpenMode.ForRead);
                        ((BlockTableRecord)tr.GetObject(source.OwnerId, OpenMode.ForWrite)).AppendEntity(outputs[i]); tr.AddNewlyCreatedDBObject(outputs[i], true);
                    }
                    var anchorId = paths[0].Originals[0]; var anchor = (Entity)tr.GetObject(anchorId, OpenMode.ForWrite);
                    anchor.Erase();
                    foreach (var id in originals.Where(id => id != anchorId)) tr.GetObject(id, OpenMode.ForWrite).Erase();
                    tr.Commit();
                }
                catch { tr.Abort(); throw; }
                finally { foreach (var output in outputs) output.Dispose(); }
            }
        }
        // Takes ownership of all detached outputs, including on failure. This is
        // also used by exact native splitting without approximating the geometry.
        public static void ReplacePrepared(Database db, ObjectId original, List<Entity> outputs)
        {
            ReplaceAssociated(db, null, new List<ObjectId> { original }, 0, outputs);
        }
        private static void ReplaceAssociated(Database db, List<PolylinePath> paths, List<ObjectId> originals, double fitError, List<Entity> prepared = null)
        {
            if (db.TransactionManager.TopTransaction != null)
            {
                if (prepared != null) foreach (var output in prepared) PolylineBreakService.DisposeDetached(output);
                throw new InvalidOperationException("关联对象替换需要独立的对象访问环境。");
            }
            var outputs = prepared ?? new List<Entity>(); var extraIds = new List<ObjectId>();
            try
            {
                // HandOverTo retains ObjectId, handle, XData, dictionary AND reactors.
                // Autodesk forbids it on transaction-resident objects, so open directly.
                // ObjectId.Open is deliberately used here: GetObject would enlist the
                // object in a transaction, which HandOverTo explicitly prohibits.
#pragma warning disable 0618
                using (var source = (Entity)originals[0].Open(OpenMode.ForRead))
                {
                    if (prepared != null)
                    {
                        if (outputs.Count == 0) throw new InvalidOperationException("没有可替换的分段。");
                        foreach (var output in outputs)
                        {
                            if (!output.ObjectId.IsNull || !output.ExtensionDictionary.IsNull)
                                throw new InvalidOperationException("分段结果不是独立的几何对象，保留原 PL。");
                            output.SetPropertiesFrom(source); output.XData = null;
                        }
                        foreach (var output in outputs.Skip(1)) CopyXData(source, output);
                    }
                    else foreach (var path in paths)
                        {
                            var output = LineToPolylineService.Create(path, path.Normal, fitError); outputs.Add(output);
                            output.SetPropertiesFrom(path.DisplaySource ?? source);
                            // Primary XData is handed over with the identity. Pre-populating
                            // it would interfere with AutoCAD's replacement undo record.
                            if (outputs.Count > 1) CopyXData(source, output);
                        }
                }
                if (outputs.Count > 1 || originals.Count > 1)
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        try
                        {
                            var source = (Entity)tr.GetObject(originals[0], OpenMode.ForRead);
                            var space = (BlockTableRecord)tr.GetObject(source.OwnerId, OpenMode.ForWrite);
                            for (int i = 1; i < outputs.Count; i++) { extraIds.Add(space.AppendEntity(outputs[i])); tr.AddNewlyCreatedDBObject(outputs[i], true); }
                            foreach (ObjectId reactorId in source.GetPersistentReactorIds())
                            {
                                var group = tr.GetObject(reactorId, OpenMode.ForRead) as Group;
                                if (group != null && extraIds.Count > 0) { group.UpgradeOpen(); group.Append(new ObjectIdCollection(extraIds.ToArray())); }
                            }
                            // Erase identical-data secondary sources before handover;
                            // they can be unerased if the replacement is rejected.
                            foreach (var id in originals.Skip(1)) tr.GetObject(id, OpenMode.ForWrite).Erase();
                            tr.Commit();
                        }
                        catch { tr.Abort(); throw; }
                    }
                using (var source = (Entity)originals[0].Open(OpenMode.ForWrite)) source.HandOverTo(outputs[0], true, true);
#pragma warning restore 0618
                // Close the replacement before any transaction accesses the identity.
                outputs[0].Dispose();
            }
            catch
            {
                // Any additional boundaries are reverted if the atomic replacement fails.
                if (extraIds.Count > 0 || originals.Count > 1) using (var tr = db.TransactionManager.StartTransaction())
                {
                    foreach (var id in extraIds) if (id.IsValid && !id.IsErased) tr.GetObject(id, OpenMode.ForWrite).Erase();
                    foreach (var id in originals.Skip(1)) if (id.IsValid && id.IsErased) tr.GetObject(id, OpenMode.ForWrite, true).Erase(false);
                    tr.Commit();
                }
                throw;
            }
            finally { foreach (var output in outputs) { if (prepared != null) PolylineBreakService.DisposeDetached(output); else output.Dispose(); } }
        }
    }
    public class CurveConversionOptions
    {
        public double MergeTolerance { get; set; } = .01;
        public double FitError { get; set; } = .01;
        public bool PreserveData { get; set; }
        public bool UnifyZ { get; set; } = true;
    }
    public class LineToPolylineCommands
    {
        private static string SettingsFile(string name) { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XYDSignTool", name); }
        private static double ReadSetting(string name, bool allowZero)
        {
            try
            {
                double value; string file = SettingsFile(name);
                if (File.Exists(file) && double.TryParse(File.ReadAllText(file), NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
                    !double.IsNaN(value) && !double.IsInfinity(value) && (allowZero ? value >= 0 : value > 0)) return value;
            }
            catch (System.Exception) { }
            return .01;
        }
        private static void SaveSetting(Editor ed, string name, double value)
        {
            try { string file = SettingsFile(name); Directory.CreateDirectory(Path.GetDirectoryName(file)); File.WriteAllText(file, value.ToString("R", CultureInfo.InvariantCulture)); }
            catch (System.Exception ex) { ed.WriteMessage("\n已完成转换，但无法保存容差设置：" + ex.Message); }
        }
        private static bool Configure(Editor ed, CurveConversionOptions settings)
        {
            while (true)
            {
                ed.WriteMessage("\n当前设置：容差=" + settings.MergeTolerance.ToString("G", CultureInfo.CurrentCulture) + "，保留数据=" + (settings.PreserveData ? "是" : "否") +
                    "，统一 Z=" + (settings.UnifyZ ? "是（世界坐标 Z=0）" : "否（保留原高程）") +
                    "，拟合误差=" + settings.FitError.ToString("G", CultureInfo.CurrentCulture) + "。输入容差或回车开始；点击选项可修改。");
                // Format the default explicitly: drawings with LUPREC=0 otherwise
                // display a misleading <0> for a valid 0.01 default.
                var prompt = new PromptDoubleOptions("\n设置端点合并容差（回车使用 " + settings.MergeTolerance.ToString("G", CultureInfo.CurrentCulture) + "）或")
                    { UseDefaultValue = false, AllowNone = true, AllowNegative = false, AllowZero = true };
                prompt.Keywords.Add("Data", "D", "是否保留数据(D)");
                prompt.Keywords.Add("Z", "Z", "是否统一Z坐标(Z)");
                prompt.Keywords.Add("Fit", "F", "拟合误差(F)");
                var answer = ed.GetDouble(prompt);
                if (answer.Status == PromptStatus.OK || answer.Status == PromptStatus.None)
                {
                    if (answer.Status == PromptStatus.OK) settings.MergeTolerance = answer.Value;
                    return true;
                }
                if (answer.Status != PromptStatus.Keyword) return false;
                switch (answer.StringResult)
                {
                    case "Data": settings.PreserveData = !settings.PreserveData; break;
                    case "Z": settings.UnifyZ = !settings.UnifyZ; break;
                    case "Fit":
                        var fit = ed.GetDouble(new PromptDoubleOptions("\n设置曲线拟合误差（图纸单位，必须大于零） <" + settings.FitError.ToString("G", CultureInfo.CurrentCulture) + ">")
                            { UseDefaultValue = false, AllowNone = true, AllowNegative = false, AllowZero = false });
                        if (fit.Status == PromptStatus.OK) settings.FitError = fit.Value;
                        else if (fit.Status != PromptStatus.None) return false;
                        break;
                }
            }
        }
        private static List<List<PolylinePath>> AtomicBatches(List<PolylinePath> paths)
        {
            var parents = Enumerable.Range(0, paths.Count).ToArray(); var owners = new Dictionary<ObjectId, int>();
            Func<int, int> root = null; root = i => parents[i] == i ? i : (parents[i] = root(parents[i]));
            for (int i = 0; i < paths.Count; i++) foreach (var id in paths[i].Originals)
            {
                int owner; if (owners.TryGetValue(id, out owner)) parents[root(i)] = root(owner); else owners[id] = i;
            }
            return Enumerable.Range(0, paths.Count).GroupBy(root).Select(g => g.Select(i => paths[i]).ToList()).ToList();
        }
        [CommandMethod("XYD_LINE2PL", CommandFlags.UsePickSet)]
        public void ConvertLines()
        {
            var doc = Application.DocumentManager.MdiActiveDocument; if (doc == null) return; var ed = doc.Editor;
            var implied = ed.SelectImplied();
            var settings = new CurveConversionOptions { MergeTolerance = ReadSetting("LineTolerance.txt", true), FitError = ReadSetting("CurveTolerance.txt", false) };
            if (!Configure(ed, settings)) return;
            // Do not hard-code DXF names: every supported finite Curve can be selected.
            var selected = implied.Status == PromptStatus.OK ? implied : ed.GetSelection(
                new PromptSelectionOptions { MessageForAdding = "\n选择要转为 PL 的曲线（点选/框选，回车完成）：" });
            if (selected.Status != PromptStatus.OK) return;
            var ids = selected.Value.GetObjectIds().Distinct().OrderBy(id => id.Handle.Value).ToList();
            var inputs = new List<LineSegmentInput>(); var failures = new List<string>();
            try
            {
                bool onlyPolylines;
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    onlyPolylines = ids.All(id => { var e = tr.GetObject(id, OpenMode.ForRead); return e is Polyline || e is Polyline2d || e is Polyline3d; });
                    if (onlyPolylines) ed.WriteMessage("\n所选对象全为 PL 线，按端点容差合并开放路径；闭合轮廓保持独立。");
                    foreach (var id in ids)
                    {
                        try
                        {
                            var entity = tr.GetObject(id, OpenMode.ForRead) as Entity;
                            if (!LineToPolylineService.IsSupported(entity)) throw new InvalidOperationException("此对象不是可转换的有限曲线或轮廓。");
                            if (((LayerTableRecord)tr.GetObject(entity.LayerId, OpenMode.ForRead)).IsLocked) throw new InvalidOperationException("图层已锁定。");
                            inputs.AddRange(LineToPolylineService.ReadAll(entity, tr, settings.FitError, settings.PreserveData, settings.UnifyZ));
                        }
                        catch (System.Exception ex) { failures.Add("对象 " + id.Handle + " 保留：" + ex.Message); }
                    }
                }
                var paths = LineToPolylineService.Build(inputs, settings.MergeTolerance);
                int converted = 0, convertedCurves = 0;
                using (var loc = doc.LockDocument())
                {
                    foreach (var batch in AtomicBatches(paths))
                    {
                        try
                        {
                            CurveMetadataService.ReplaceBatch(doc.Database, batch, settings.FitError, settings.PreserveData);
                            converted += batch.Count; convertedCurves += batch.SelectMany(p => p.Originals).Distinct().Count();
                        }
                        catch (System.Exception ex) { failures.Add("路径 " + batch[0].Originals[0].Handle + " 保留：" + ex.Message); }
                    }
                }
                SaveSetting(ed, "LineTolerance.txt", settings.MergeTolerance); SaveSetting(ed, "CurveTolerance.txt", settings.FitError);
                ed.WriteMessage("\n已将 " + convertedCurves + (onlyPolylines ? " 条 PL 线合并整理为 " : " 个曲线对象转换为 ") + converted + " 条 PL 线。" +
                    (settings.PreserveData ? "附加数据保留；不同数据或独立关联分别处理。" : "生成 PL 不保留 XData、扩展字典及原关联数据。") +
                    (settings.UnifyZ ? "顶点统一到世界坐标 Z=0。" : "保留原高程。") + "可一次撤销恢复。" +
                    (failures.Count == 0 ? "" : "\n" + string.Join("\n", failures)));
            }
            finally { foreach (var input in inputs) if (input.DisplaySource != null) input.DisplaySource.Dispose(); }
        }
    }
}
