using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace XYDSignTool.Tests
{
    public class PolylineBreakTests
    {
        private static string Results { get { return Path.Combine(Environment.GetEnvironmentVariable("XYD_CURVE_TEST_ROOT"), "artifacts", "curve-regression", "results.txt"); } }
        private static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
        private static void Near(double a, double b, string message, double tolerance = 1e-7) { Check(Math.Abs(a - b) <= tolerance, message + ": " + a + " vs " + b); }
        private static void Record(string name, Action test)
        {
            try { test(); File.AppendAllText(Results, "PASS " + name + Environment.NewLine); }
            catch (System.Exception ex) { File.AppendAllText(Results, "FAIL " + name + " " + ex + Environment.NewLine); }
        }
        private static BlockTableRecord Model(Database db, Transaction tr, OpenMode mode)
        { return (BlockTableRecord)tr.GetObject(((BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead))[BlockTableRecord.ModelSpace], mode); }
        private static void Add(Database db, Transaction tr, Entity entity) { Model(db, tr, OpenMode.ForWrite).AppendEntity(entity); tr.AddNewlyCreatedDBObject(entity, true); }
        private static Polyline Open()
        {
            var p = new Polyline { Elevation = 9, Thickness = 2, Plinegen = true, ColorIndex = 3, LinetypeScale = 2 };
            p.AddVertexAt(0, new Point2d(0, 0), .5, 2, 6); p.AddVertexAt(1, new Point2d(10, 0), 0, 6, 3); p.AddVertexAt(2, new Point2d(20, 0), 0, 0, 0); return p;
        }
        private static Polyline Closed()
        {
            var p = new Polyline { Closed = true }; p.AddVertexAt(0, new Point2d(0, 0), 0, 0, 0); p.AddVertexAt(1, new Point2d(10, 0), 0, 0, 0);
            p.AddVertexAt(2, new Point2d(10, 10), 0, 0, 0); p.AddVertexAt(3, new Point2d(0, 10), 0, 0, 0); return p;
        }
        private static void Coverage(Curve source, List<Entity> parts)
        {
            Near(parts.Sum(p => PolylineBreakService.Length((Curve)p)), PolylineBreakService.Length(source), "full original length");
            for (int i = 0; i <= 1000; i++)
            {
                var point = source.GetPointAtParameter(source.StartParam + (source.EndParam - source.StartParam) * i / 1000);
                Check(parts.Cast<Curve>().Min(p => p.GetClosestPointTo(point, false).DistanceTo(point)) <= 1e-6, "original geometry covered exactly");
            }
        }
        private static void WithParts(Curve source, double[] cuts, Action<List<Entity>> test)
        {
            var parts = PolylineBreakService.Split(source, cuts);
            try { Coverage(source, parts); test(parts); }
            finally { foreach (var part in parts) PolylineBreakService.DisposeDetached(part); }
        }
        [CommandMethod("XYD_BREAK_REGRESSION")]
        public void Run()
        {
            using (var db = new Database(true, true)) using (var tr = db.TransactionManager.StartTransaction())
            {
                Record("PL split one point / exact arc / varying widths / plane / thickness", () =>
                {
                    var p = Open(); Add(db, tr, p);
                    WithParts(p, new[] { .25 }, parts => {
                        Check(parts.Count == 2 && parts.All(e => e is Polyline), "one point two PL"); var a = (Polyline)parts[0]; var b = (Polyline)parts[1];
                        Near(a.EndPoint.DistanceTo(b.StartPoint), 0, "shared cut point"); Near(a.GetBulgeAt(0), Math.Tan(Math.Atan(.5) * .25), "split arc bulge");
                        Near(a.GetEndWidthAt(0), 3, "interpolated width on first side"); Near(b.GetStartWidthAt(0), 3, "interpolated width on second side");
                        Check(parts.Cast<Polyline>().All(s => s.Elevation == 9 && s.Thickness == 2 && s.Plinegen), "geometry attributes preserved");
                    });
                    var tilted = Open(); tilted.Normal = new Vector3d(1, 2, 3).GetNormal(); Add(db, tr, tilted);
                    WithParts(tilted, new[] { .35 }, parts => Check(parts.Cast<Polyline>().All(s => s.Normal.IsEqualTo(tilted.Normal) && s.Elevation == 9), "tilted plane preserved"));
                });
                Record("PL split unsorted multiple points / repeated points / endpoints / existing vertex", () =>
                {
                    var p = Open(); Add(db, tr, p);
                    WithParts(p, new[] { 1.8, .25, .25, 0.0, 2.0 }, parts => Check(parts.Count == 3, "two unique interior points give three pieces"));
                    WithParts(p, new[] { 1.0 }, parts => Check(parts.Count == 2, "existing vertex cut"));
                    Check(PolylineBreakService.Normalize(p, new[] { 0.0, 2.0 }).Count == 0, "endpoint-only no modification");
                    var points = new List<double>(); Check(PolylineBreakService.AddPoint(p, new Point3d(15, 3, 9), points), "off-curve point projected");
                    Near(points.Single(), 1.5, "projection parameter"); Check(!PolylineBreakService.AddPoint(p, new Point3d(15, 0, 9), points), "duplicate skipped");
                });
                Record("closed PL one cut opens whole loop / multiple cuts wrap seam / seam duplicate", () =>
                {
                    var p = Closed(); Add(db, tr, p);
                    foreach (var cut in new[] { 0.0, .5, 2.0 }) WithParts(p, new[] { cut }, parts => { Check(parts.Count == 1 && !((Curve)parts[0]).Closed, "single cut opens loop"); Near(((Curve)parts[0]).StartPoint.DistanceTo(p.GetPointAtParameter(cut)), 0, "requested seam"); Near(((Curve)parts[0]).StartPoint.DistanceTo(((Curve)parts[0]).EndPoint), 0, "coincident open endpoints"); });
                    WithParts(p, new[] { 3.5, .5 }, parts => Check(parts.Count == 2, "two ring segments"));
                    WithParts(p, new[] { 4.0, 0.0, 1.5 }, parts => Check(parts.Count == 2, "start/end seam counted once"));
                    var arcLoop = Closed(); arcLoop.SetBulgeAt(3, .7); arcLoop.SetStartWidthAt(3, 2); arcLoop.SetEndWidthAt(3, 6); Add(db, tr, arcLoop);
                    WithParts(arcLoop, new[] { 3.5 }, parts => { var open = (Polyline)parts.Single(); Check(open.HasBulges, "closing arc retained"); Near(open.GetStartWidthAt(0), 4, "width at requested arc seam"); Near(open.GetEndWidthAt(open.NumberOfVertices - 2), 4, "width at end of opened loop"); });
                });
                Record("legacy 2D and 3D PL split without flattening or fitting", () =>
                {
                    var p2 = new Polyline2d(Poly2dType.SimplePoly, new Point3dCollection(new[] { new Point3d(0, 0, 0), new Point3d(10, 0, 0), new Point3d(10, 8, 0) }), 0, false, 2, 2, new DoubleCollection(new[] { .5, 0, 0 }));
                    Add(db, tr, p2); p2.Normal = new Vector3d(2, 3, 4).GetNormal(); p2.Elevation = 14;
                    WithParts(p2, new[] { .4, 1.6 }, parts => Check(parts.Count == 3 && parts.All(e => e is Polyline2d), "legacy 2D stays legacy"));
                    var p3 = new Polyline3d(Poly3dType.SimplePoly, new Point3dCollection(new[] { new Point3d(20, 0, 4), new Point3d(30, 0, 9), new Point3d(30, 10, 18) }), false); Add(db, tr, p3);
                    WithParts(p3, new[] { .4, 1.5 }, parts => Check(parts.Count == 3 && parts.All(e => e is Polyline3d), "3D stays 3D"));
                    p2.Closed = true; WithParts(p2, new[] { .4 }, parts => Check(parts.Count == 1 && parts[0] is Polyline2d, "legacy closed single opens without changing type"));
                    p3.Closed = true; WithParts(p3, new[] { .4 }, parts => Check(parts.Count == 1 && parts[0] is Polyline3d, "3D closed single opens without flattening"));
                    WithParts(p2, new[] { .4, 1.5 }, parts => Check(parts.Count == 2, "legacy two ring pieces"));
                    WithParts(p3, new[] { .4, 1.5 }, parts => Check(parts.Count == 2, "3D two ring pieces"));
                });
                Record("fitted legacy PL split preserves actual curve and length", () =>
                {
                    var p2 = new Polyline2d(Poly2dType.SimplePoly, new Point3dCollection(new[] { new Point3d(0, 0, 0), new Point3d(5, 8, 0), new Point3d(10, -5, 0), new Point3d(15, 0, 0) }), 0, false, 0, 0, new DoubleCollection(new[] { 0.0, 0, 0, 0 }));
                    Add(db, tr, p2); p2.SplineFit(Poly2dType.CubicSplinePoly, 12);
                    WithParts(p2, new[] { p2.EndParam * .25, p2.EndParam * .75 }, parts => Check(parts.Count == 3, "fitted PL gives three pieces"));
                    p2.Closed = true;
                    WithParts(p2, new[] { p2.EndParam * .25 }, parts => Check(parts.Count == 1 && parts[0] is Polyline2d, "closed fitted PL opens as one complete curve"));
                });
                tr.Commit();
            }
        }
        private static ObjectId Original, DictionaryRecord, GroupId;
        private static double OriginalLength;
        private static void Data(Database db, Transaction tr, Entity entity)
        {
            var apps = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForWrite);
            if (!apps.Has("XYD_BREAK_APP")) { var record = new RegAppTableRecord { Name = "XYD_BREAK_APP" }; apps.Add(record); tr.AddNewlyCreatedDBObject(record, true); }
            using (var values = new ResultBuffer(new TypedValue(1001, "XYD_BREAK_APP"), new TypedValue(1000, "打断保留"))) entity.XData = values;
            entity.CreateExtensionDictionary(); var dictionary = (DBDictionary)tr.GetObject(entity.ExtensionDictionary, OpenMode.ForWrite);
            var xrecord = new Xrecord(); dictionary.SetAt("BREAK_DATA", xrecord); tr.AddNewlyCreatedDBObject(xrecord, true); DictionaryRecord = xrecord.ObjectId;
            using (var values = new ResultBuffer(new TypedValue(1, "嵌套数据"), new TypedValue(330, entity.ObjectId))) xrecord.Data = values;
            var groups = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForWrite); var group = new Group("BREAK_GROUP", true);
            string name = "BREAK_GROUP_" + entity.Handle; groups.SetAt(name, group); tr.AddNewlyCreatedDBObject(group, true); group.Append(entity.ObjectId); GroupId = group.ObjectId;
        }
        private static void CheckData(Entity entity)
        { using (var data = entity.GetXDataForApplication("XYD_BREAK_APP")) Check(data != null && (string)data.AsArray()[1].Value == "打断保留", "XData retained"); }
        private static void CheckDictionary(Transaction tr, Entity entity)
        {
            var dictionary = (DBDictionary)tr.GetObject(entity.ExtensionDictionary, OpenMode.ForRead); Check(dictionary.GetAt("BREAK_DATA") == DictionaryRecord, "dictionary identity retained");
            using (var data = ((Xrecord)tr.GetObject(DictionaryRecord, OpenMode.ForRead)).Data) Check((ObjectId)data.AsArray()[1].Value == entity.ObjectId, "self reference retained");
        }
        [CommandMethod("XYD_BREAK_FIXTURE")]
        public void Fixture()
        {
            var db = Application.DocumentManager.MdiActiveDocument.Database;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in Model(db, tr, OpenMode.ForWrite)) { var entity = (Entity)tr.GetObject(id, OpenMode.ForRead); ((LayerTableRecord)tr.GetObject(entity.LayerId, OpenMode.ForWrite)).IsLocked = false; entity.UpgradeOpen(); entity.Erase(); }
                var p = new Polyline { Elevation = 0, ColorIndex = 4 }; p.AddVertexAt(0, new Point2d(0, 0), 0, 1, 2); p.AddVertexAt(1, new Point2d(10, 0), .5, 2, 4); p.AddVertexAt(2, new Point2d(20, 0), 0, 0, 0);
                Add(db, tr, p); Original = p.ObjectId; OriginalLength = p.Length; Data(db, tr, p); tr.Commit();
            }
        }
        private static void Verify(int count)
        {
            Record("native PL break yields " + count + " seamless parts and retains metadata / group / color / width", () => {
                var db = Application.DocumentManager.MdiActiveDocument.Database;
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var parts = Model(db, tr, OpenMode.ForRead).Cast<ObjectId>().Select(id => (Polyline)tr.GetObject(id, OpenMode.ForRead)).ToList();
                    Check(parts.Count == count, "output count"); Near(parts.Sum(p => p.Length), OriginalLength, "native command full length");
                    Check(parts.All(p => p.ColorIndex == 4 && !p.Closed), "display and openness"); foreach (var part in parts) CheckData(part);
                    CheckDictionary(tr, (Entity)tr.GetObject(Original, OpenMode.ForRead));
                    var members = ((Group)tr.GetObject(GroupId, OpenMode.ForRead)).GetAllEntityIds(); Check(members.Length == count && parts.All(p => members.Contains(p.ObjectId)), "all parts in original group");
                    for (int i = 0; i < count - 1; i++) Near(parts[i].EndPoint.DistanceTo(parts[i + 1].StartPoint), 0, "no gap at shared endpoint");
                }
            });
        }
        [CommandMethod("XYD_BREAK_VERIFY2", CommandFlags.NoUndoMarker)] public void VerifyTwo() { Verify(2); }
        [CommandMethod("XYD_BREAK_VERIFY3", CommandFlags.NoUndoMarker)] public void VerifyThree() { Verify(3); }
        [CommandMethod("XYD_BREAK_UNDO_VERIFY", CommandFlags.NoUndoMarker)]
        public void UndoVerify() { VerifyOriginal("one U restores original PL geometry / width / dictionary / group after break"); }
        private static void VerifyOriginal(string name)
        {
            Record(name, () => {
                var db = Application.DocumentManager.MdiActiveDocument.Database;
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    Check(Model(db, tr, OpenMode.ForRead).Cast<ObjectId>().SequenceEqual(new[] { Original }), "original sole identity");
                    var p = (Polyline)tr.GetObject(Original, OpenMode.ForRead); Near(p.Length, OriginalLength, "original length"); Check(p.NumberOfVertices == 3 && p.HasBulges, "original vertices and arc");
                    Near(p.GetStartWidthAt(0), 1, "original width"); CheckData(p); CheckDictionary(tr, p);
                    Check(((Group)tr.GetObject(GroupId, OpenMode.ForRead)).GetAllEntityIds().SequenceEqual(new[] { Original }), "original group");
                }
            });
        }
        [CommandMethod("XYD_BREAK_EMPTY_VERIFY", CommandFlags.NoUndoMarker)] public void EmptyVerify() { VerifyOriginal("empty / cancelled / locked break leaves original PL and metadata unchanged"); }
        [CommandMethod("XYD_BREAK_CLOSED_FIXTURE")]
        public void ClosedFixture()
        {
            var db = Application.DocumentManager.MdiActiveDocument.Database;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in Model(db, tr, OpenMode.ForWrite)) tr.GetObject(id, OpenMode.ForWrite).Erase();
                var p = Closed(); p.SetBulgeAt(3, .5); p.SetStartWidthAt(3, 2); p.SetEndWidthAt(3, 6); p.ColorIndex = 4;
                Add(db, tr, p); Original = p.ObjectId; OriginalLength = p.Length; Data(db, tr, p); tr.Commit();
            }
        }
        private static void ClosedVerify(int count)
        {
            Record("native closed PL yields " + count + " open ring parts, exact closing arc / width / metadata", () => {
                var db = Application.DocumentManager.MdiActiveDocument.Database;
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var parts = Model(db, tr, OpenMode.ForRead).Cast<ObjectId>().Select(id => (Polyline)tr.GetObject(id, OpenMode.ForRead)).ToList();
                    Check(parts.Count == count && parts.All(p => !p.Closed), "ring count and openness"); Near(parts.Sum(p => p.Length), OriginalLength, "whole ring length"); Check(parts.Any(p => p.HasBulges), "closing arc present");
                    foreach (var part in parts) CheckData(part); CheckDictionary(tr, (Entity)tr.GetObject(Original, OpenMode.ForRead));
                    Check(((Group)tr.GetObject(GroupId, OpenMode.ForRead)).GetAllEntityIds().Length == count, "ring group membership");
                    if (count == 1) { Near(parts[0].StartPoint.DistanceTo(new Point3d(5, 0, 0)), 0, "selected seam"); Near(parts[0].EndPoint.DistanceTo(parts[0].StartPoint), 0, "open coincident endpoints"); }
                    else { Near(parts[0].EndPoint.DistanceTo(parts[1].StartPoint), 0, "first ring break"); Near(parts[1].EndPoint.DistanceTo(parts[0].StartPoint), 0, "second ring break"); }
                }
            });
        }
        [CommandMethod("XYD_BREAK_CLOSED_VERIFY1", CommandFlags.NoUndoMarker)] public void ClosedOne() { ClosedVerify(1); }
        [CommandMethod("XYD_BREAK_CLOSED_VERIFY2", CommandFlags.NoUndoMarker)] public void ClosedTwo() { ClosedVerify(2); }
        [CommandMethod("XYD_BREAK_CLOSED_UNDO", CommandFlags.NoUndoMarker)]
        public void ClosedUndo()
        {
            Record("one U restores closed flag / closing bulge and widths / ring metadata", () => {
                var db = Application.DocumentManager.MdiActiveDocument.Database;
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    Check(Model(db, tr, OpenMode.ForRead).Cast<ObjectId>().SequenceEqual(new[] { Original }), "original ring identity"); var p = (Polyline)tr.GetObject(Original, OpenMode.ForRead);
                    Check(p.Closed && p.NumberOfVertices == 4, "original closed ring"); Near(p.Length, OriginalLength, "original ring length"); Near(p.GetBulgeAt(3), .5, "closing bulge"); Near(p.GetStartWidthAt(3), 2, "closing start width"); Near(p.GetEndWidthAt(3), 6, "closing end width"); CheckData(p); CheckDictionary(tr, p);
                    Check(((Group)tr.GetObject(GroupId, OpenMode.ForRead)).GetAllEntityIds().SequenceEqual(new[] { Original }), "original ring group");
                }
            });
        }
        [CommandMethod("XYD_BREAK_3D_FIXTURE")]
        public void SpatialFixture()
        {
            var db = Application.DocumentManager.MdiActiveDocument.Database;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in Model(db, tr, OpenMode.ForWrite)) tr.GetObject(id, OpenMode.ForWrite).Erase();
                var p = new Polyline3d(Poly3dType.SimplePoly, new Point3dCollection(new[] { new Point3d(0, 0, 3), new Point3d(10, 0, 8), new Point3d(20, 0, 13) }), false) { ColorIndex = 4 };
                Add(db, tr, p); Original = p.ObjectId; OriginalLength = PolylineBreakService.Length(p); Data(db, tr, p); tr.Commit();
            }
        }
        [CommandMethod("XYD_BREAK_3D_VERIFY", CommandFlags.NoUndoMarker)]
        public void SpatialVerify()
        {
            Record("native 3D PL break retains elevations / vertex owners / metadata and group", () => {
                var db = Application.DocumentManager.MdiActiveDocument.Database;
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var parts = Model(db, tr, OpenMode.ForRead).Cast<ObjectId>().Select(id => (Polyline3d)tr.GetObject(id, OpenMode.ForRead)).ToList(); Check(parts.Count == 2, "two 3D parts");
                    Near(parts.Sum(p => PolylineBreakService.Length(p)), OriginalLength, "3D length"); Near(parts[0].EndPoint.DistanceTo(new Point3d(5, 0, 5.5)), 0, "3D chosen break"); Near(parts[1].StartPoint.DistanceTo(parts[0].EndPoint), 0, "3D no gap");
                    foreach (var p in parts) { CheckData(p); foreach (ObjectId id in p) Check(tr.GetObject(id, OpenMode.ForRead).OwnerId == p.ObjectId, "native vertex owner"); }
                    CheckDictionary(tr, (Entity)tr.GetObject(Original, OpenMode.ForRead)); Check(((Group)tr.GetObject(GroupId, OpenMode.ForRead)).GetAllEntityIds().Length == 2, "3D group members");
                }
            });
        }
        [CommandMethod("XYD_BREAK_3D_UNDO", CommandFlags.NoUndoMarker)]
        public void SpatialUndo()
        {
            Record("one U restores 3D PL vertices / heights / data / group", () => {
                var db = Application.DocumentManager.MdiActiveDocument.Database;
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    Check(Model(db, tr, OpenMode.ForRead).Cast<ObjectId>().SequenceEqual(new[] { Original }), "original 3D identity"); var p = (Polyline3d)tr.GetObject(Original, OpenMode.ForRead);
                    Near(PolylineBreakService.Length(p), OriginalLength, "original 3D length"); Check(p.Cast<ObjectId>().Count() == 3, "original vertex count"); Near(p.StartPoint.Z, 3, "original start Z"); Near(p.EndPoint.Z, 13, "original end Z"); CheckData(p); CheckDictionary(tr, p);
                    Check(((Group)tr.GetObject(GroupId, OpenMode.ForRead)).GetAllEntityIds().SequenceEqual(new[] { Original }), "original 3D group");
                }
            });
        }
        private static Matrix3d PreviousUcs;
        [CommandMethod("XYD_BREAK_UCS")]
        public void SetUcs()
        {
            var editor = Application.DocumentManager.MdiActiveDocument.Editor; PreviousUcs = editor.CurrentUserCoordinateSystem;
            editor.CurrentUserCoordinateSystem = Matrix3d.AlignCoordinateSystem(Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis,
                new Point3d(3, -5, 0), Vector3d.YAxis, -Vector3d.XAxis, Vector3d.ZAxis);
        }
        [CommandMethod("XYD_BREAK_UCS_VERIFY", CommandFlags.NoUndoMarker)]
        public void VerifyUcs()
        {
            Verify(2);
            Record("native break interprets point in translated / rotated UCS", () => {
                var db = Application.DocumentManager.MdiActiveDocument.Database;
                using (var tr = db.TransactionManager.StartTransaction())
                    Near(((Polyline)tr.GetObject(Original, OpenMode.ForRead)).EndPoint.DistanceTo(new Point3d(5, 0, 0)), 0, "UCS point maps to correct WCS cut");
            });
        }
        [CommandMethod("XYD_BREAK_UCS_RESTORE", CommandFlags.NoUndoMarker)] public void RestoreUcs() { Application.DocumentManager.MdiActiveDocument.Editor.CurrentUserCoordinateSystem = PreviousUcs; }
        [CommandMethod("XYD_BREAK_LOCK")]
        public void LockFixture()
        {
            var db = Application.DocumentManager.MdiActiveDocument.Database;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var layers = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForWrite); ObjectId layerId;
                if (layers.Has("XYD_BREAK_LOCKED")) layerId = layers["XYD_BREAK_LOCKED"];
                else { var layer = new LayerTableRecord { Name = "XYD_BREAK_LOCKED" }; layerId = layers.Add(layer); tr.AddNewlyCreatedDBObject(layer, true); }
                ((Entity)tr.GetObject(Original, OpenMode.ForWrite)).LayerId = layerId; ((LayerTableRecord)tr.GetObject(layerId, OpenMode.ForWrite)).IsLocked = true; tr.Commit();
            }
        }
    }
}
