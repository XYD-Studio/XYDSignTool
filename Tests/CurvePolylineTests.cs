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
    public class CurvePolylineTests
    {
        private static string Results { get { return Path.Combine(Environment.GetEnvironmentVariable("XYD_CURVE_TEST_ROOT"), "artifacts", "curve-regression", "results.txt"); } }
        private static void Check(bool ok, string name) { if (!ok) throw new InvalidOperationException(name); }
        private static void Near(double actual, double expected, string name, double tolerance = 1e-8) { Check(Math.Abs(actual - expected) <= tolerance, name + ": " + actual + " vs " + expected); }
        private static void Record(string name, Action action)
        {
            try { action(); File.AppendAllText(Results, "PASS " + name + Environment.NewLine); }
            catch (System.Exception ex) { File.AppendAllText(Results, "FAIL " + name + " " + ex + Environment.NewLine); }
        }
        private static void Add(Database db, Transaction tr, Entity e)
        {
            var table = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            ((BlockTableRecord)tr.GetObject(table[BlockTableRecord.ModelSpace], OpenMode.ForWrite)).AppendEntity(e); tr.AddNewlyCreatedDBObject(e, true);
        }
        private static Curve Convert(Database db, Transaction tr, Curve original, double error = .001)
        {
            if (original.ObjectId.IsNull) Add(db, tr, original);
            var input = LineToPolylineService.Read(original, tr, error);
            var path = LineToPolylineService.Build(new[] { input }, 0).Single();
            var result = (Curve)LineToPolylineService.Create(path, input.Normal, error); Add(db, tr, result); return result;
        }
        private static void Fit(Curve source, Curve output, double error, bool checkEnds = true)
        {
            double worst = 0;
            for (int i = 0; i <= 2000; i++)
            {
                var point = source.GetPointAtParameter(source.StartParam + (source.EndParam - source.StartParam) * i / 2000);
                worst = Math.Max(worst, point.DistanceTo(output.GetClosestPointTo(point, false)));
            }
            Check(worst <= error * 1.01 + 1e-8, "maximum measured chord error " + worst + " exceeds " + error);
            if (checkEnds) { Near(output.StartPoint.DistanceTo(source.StartPoint), 0, "start preserved"); Near(output.EndPoint.DistanceTo(source.EndPoint), 0, "end preserved"); }
        }
        private static Polyline Rectangle(double x = 0)
        {
            var p = new Polyline { Closed = true }; p.AddVertexAt(0, new Point2d(x, 0), 0, 0, 0); p.AddVertexAt(1, new Point2d(x + 10, 0), 0, 0, 0);
            p.AddVertexAt(2, new Point2d(x + 10, 5), 0, 0, 0); p.AddVertexAt(3, new Point2d(x, 5), 0, 0, 0); return p;
        }
        private static Spline SampleSpline(bool spatial)
        {
            return new Spline(new Point3dCollection(new[] { new Point3d(0, 0, 0), new Point3d(5, 8, spatial ? 3 : 0),
                new Point3d(10, -5, spatial ? -2 : 0), new Point3d(15, 0, spatial ? 6 : 0) }), 3, 1e-6);
        }
        private static Polyline Project(Database db, Transaction tr, Curve source, double error = .001)
        {
            if (source.ObjectId.IsNull) Add(db, tr, source);
            var input = LineToPolylineService.Read(source, tr, error, false, true);
            var result = (Polyline)LineToPolylineService.Create(LineToPolylineService.Build(new[] { input }, 0).Single(), null, error);
            Add(db, tr, result); Check(result.Normal.IsEqualTo(Vector3d.ZAxis) && result.Elevation == 0, "WCS Z=0 plane");
            double worst = 0;
            for (int i = 0; i <= 2000; i++)
            {
                var p = source.GetPointAtParameter(source.StartParam + (source.EndParam - source.StartParam) * i / 2000);
                var xy = new Point3d(p.X, p.Y, 0); worst = Math.Max(worst, xy.DistanceTo(result.GetClosestPointTo(xy, false)));
            }
            Check(worst <= error * 1.01 + 1e-8, "projected curve chord error " + worst);
            Check(result.Closed == source.Closed, "projection keeps closure"); return result;
        }
        private static void Data(Database db, Transaction tr, Entity entity, string value)
        {
            var apps = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForWrite);
            foreach (string name in new[] { "XYD_CURVE_APP", "XYD_SECOND_APP" }) if (!apps.Has(name)) { var a = new RegAppTableRecord { Name = name }; apps.Add(a); tr.AddNewlyCreatedDBObject(a, true); }
            using (var data = new ResultBuffer(new TypedValue(1001, "XYD_CURVE_APP"), new TypedValue(1000, value), new TypedValue(1070, (short)23),
                new TypedValue(1040, 1.25), new TypedValue(1010, new Point3d(2, 3, 4)), new TypedValue(1004, new byte[] { 1, 7, 255 }),
                new TypedValue(1001, "XYD_SECOND_APP"), new TypedValue(1000, "第二应用"))) entity.XData = data;
        }
        private static ObjectId Dictionary(Transaction tr, Entity entity)
        {
            entity.CreateExtensionDictionary(); var dictionary = (DBDictionary)tr.GetObject(entity.ExtensionDictionary, OpenMode.ForWrite);
            var nested = new DBDictionary(); dictionary.SetAt("NESTED", nested); tr.AddNewlyCreatedDBObject(nested, true);
            var record = new Xrecord { XlateReferences = true }; nested.SetAt("VALUES", record); tr.AddNewlyCreatedDBObject(record, true);
            using (var data = new ResultBuffer(new TypedValue(1, "保留字典数据"), new TypedValue(40, 13.5), new TypedValue(330, entity.ObjectId))) record.Data = data;
            return record.ObjectId;
        }
        private static ObjectId Group(Database db, Transaction tr, Entity entity, string name)
        {
            var dictionary = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForWrite); var group = new Group(name, true);
            dictionary.SetAt(name, group); tr.AddNewlyCreatedDBObject(group, true); group.Append(entity.ObjectId); return group.ObjectId;
        }
        private static List<PolylinePath> Paths(Database db, params ObjectId[] ids)
        {
            using (var tr = db.TransactionManager.StartTransaction())
                return LineToPolylineService.Build(ids.Select(id => LineToPolylineService.Read((Curve)tr.GetObject(id, OpenMode.ForRead), tr, .001)).ToList(), .01);
        }
        private static void CheckData(Entity entity, string expected)
        {
            using (var data = entity.GetXDataForApplication("XYD_CURVE_APP"))
            {
                Check(data != null, "XData exists"); var values = data.AsArray(); Check((string)values[1].Value == expected, "XData payload");
                Check(((byte[])values[5].Value).SequenceEqual(new byte[] { 1, 7, 255 }), "XData binary payload");
            }
            using (var data = entity.GetXDataForApplication("XYD_SECOND_APP")) Check(data != null && (string)data.AsArray()[1].Value == "第二应用", "second registered application");
        }
        private static void CheckDictionary(Transaction tr, Entity entity, ObjectId recordId)
        {
            var dictionary = (DBDictionary)tr.GetObject(entity.ExtensionDictionary, OpenMode.ForRead); Check(dictionary.OwnerId == entity.ObjectId, "dictionary owner follows polyline identity");
            var nested = (DBDictionary)tr.GetObject(dictionary.GetAt("NESTED"), OpenMode.ForRead); Check(nested.GetAt("VALUES") == recordId, "nested object identity retained");
            using (var data = ((Xrecord)tr.GetObject(recordId, OpenMode.ForRead)).Data)
                Check((string)data.AsArray()[0].Value == "保留字典数据" && (ObjectId)data.AsArray()[2].Value == entity.ObjectId, "nested data and self reference retained");
        }
        private static void TestMetadata()
        {
            using (var db = new Database(true, true))
            {
                ObjectId one, two, three, dictionaryId, recordId;
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var a = new Line(Point3d.Origin, new Point3d(10, 0, 0)); var b = new Line(a.EndPoint, new Point3d(20, 0, 0)); var c = new Line(b.EndPoint, new Point3d(30, 0, 0));
                    Add(db, tr, a); Add(db, tr, b); Add(db, tr, c); one = a.ObjectId; two = b.ObjectId; three = c.ObjectId;
                    Data(db, tr, a, "SAME"); Data(db, tr, b, "SAME"); Data(db, tr, c, "OTHER"); tr.Commit();
                }
                var paths = Paths(db, one, two, three); Check(paths.Count == 2, "identical metadata merges; conflicts split, all convert");
                foreach (var path in paths) CurveMetadataService.ReplaceBatch(db, new List<PolylinePath> { path }, .001);
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var a = (Polyline)tr.GetObject(one, OpenMode.ForRead); Near(a.Length, 20, "two equal-data lines join"); Check(two.IsErased, "second joined original removed"); CheckData(a, "SAME");
                    var c = (Polyline)tr.GetObject(three, OpenMode.ForRead); Near(c.Length, 10, "conflicting line still converts"); CheckData(c, "OTHER");
                    var line = new Line(new Point3d(50, 0, 0), new Point3d(60, 0, 0)); Add(db, tr, line); dictionaryId = line.ObjectId; recordId = Dictionary(tr, line); Data(db, tr, line, "DICTIONARY"); tr.Commit();
                }
                CurveMetadataService.ReplaceBatch(db, Paths(db, dictionaryId), .001);
                using (var tr = db.TransactionManager.StartTransaction()) { var p = (Polyline)tr.GetObject(dictionaryId, OpenMode.ForRead); CheckData(p, "DICTIONARY"); CheckDictionary(tr, p, recordId); }
                string file = Path.Combine(Environment.GetEnvironmentVariable("XYD_CURVE_TEST_ROOT"), "artifacts", "curve-regression", "metadata-roundtrip.dwg"); db.SaveAs(file, DwgVersion.AC1032);
                using (var reopen = new Database(false, true))
                {
                    reopen.ReadDwgFile(file, FileShare.Read, true, "");
                    using (var tr = reopen.TransactionManager.StartTransaction())
                    { var p = (Polyline)tr.GetObject(reopen.GetObjectId(false, dictionaryId.Handle, 0), OpenMode.ForRead); CheckData(p, "DICTIONARY"); CheckDictionary(tr, p, reopen.GetObjectId(false, recordId.Handle, 0)); }
                }
            }
        }
        private static void TestReactors()
        {
            using (var db = new Database(true, true))
            {
                ObjectId id, groupId, record;
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var line = new Line(Point3d.Origin, new Point3d(10, 0, 0)); Add(db, tr, line); id = line.ObjectId;
                    Data(db, tr, line, "GROUP"); record = Dictionary(tr, line); groupId = Group(db, tr, line, "METADATA_GROUP"); Check(line.GetPersistentReactorIds().Contains(groupId), "fixture has group reactor"); tr.Commit();
                }
                CurveMetadataService.ReplaceBatch(db, Paths(db, id), .001);
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var p = (Polyline)tr.GetObject(id, OpenMode.ForRead); CheckData(p, "GROUP"); CheckDictionary(tr, p, record);
                    Check(p.GetPersistentReactorIds().Contains(groupId), "persistent reactor retained"); Check(((Group)tr.GetObject(groupId, OpenMode.ForRead)).GetAllEntityIds().SequenceEqual(new[] { id }), "group still refers to polyline");
                }
                // The associated replacement can also own newly resident 3D vertices.
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var p3 = new Polyline3d(Poly3dType.SimplePoly, new Point3dCollection(new[] { new Point3d(20, 0, 0), new Point3d(20, 10, 0), new Point3d(20, 10, 5), new Point3d(25, 10, 8) }), false);
                    Add(db, tr, p3); id = p3.ObjectId; Data(db, tr, p3, "3D"); groupId = Group(db, tr, p3, "METADATA_3D_GROUP"); tr.Commit();
                }
                CurveMetadataService.ReplaceBatch(db, Paths(db, id), .001);
                using (var tr = db.TransactionManager.StartTransaction())
                { var p = (Polyline3d)tr.GetObject(id, OpenMode.ForRead); CheckData(p, "3D"); Check(p.GetPersistentReactorIds().Contains(groupId), "3D reactor"); Check(p.Cast<ObjectId>().Count() == 4, "3D vertices survive replacement"); }
                List<LineSegmentInput> inputs;
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var ml = new Mline { Normal = Vector3d.ZAxis }; Add(db, tr, ml); ml.Style = db.CmlstyleID; ml.AppendSegment(new Point3d(40, 0, 0)); ml.AppendSegment(new Point3d(50, 0, 0)); ml.AppendSegment(new Point3d(50, 10, 0));
                    id = ml.ObjectId; Data(db, tr, ml, "MULTILINE"); record = Dictionary(tr, ml); groupId = Group(db, tr, ml, "METADATA_MULTILINE_GROUP");
                    inputs = LineToPolylineService.ReadAll(ml, tr, .001); tr.Commit();
                }
                try
                {
                    var paths = LineToPolylineService.Build(inputs, .01); Check(paths.Count >= 2, "multiple boundaries"); CurveMetadataService.ReplaceBatch(db, paths, .001);
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        var p = (Polyline)tr.GetObject(id, OpenMode.ForRead); CheckDictionary(tr, p, record); CheckData(p, "MULTILINE");
                        var members = ((Group)tr.GetObject(groupId, OpenMode.ForRead)).GetAllEntityIds(); Check(members.Length == paths.Count, "all multiline boundaries remain in group");
                        foreach (var member in members) { var entity = (Polyline)tr.GetObject(member, OpenMode.ForRead); CheckData(entity, "MULTILINE"); Check(entity.GetPersistentReactorIds().Contains(groupId), "all boundaries have group reactor"); }
                    }
                }
                finally { foreach (var input in inputs) input.DisplaySource.Dispose(); }
            }
        }
        private static void TestMetadataRollback()
        {
            using (var db = new Database(true, true))
            {
                ObjectId id, record;
                using (var tr = db.TransactionManager.StartTransaction())
                { var line = new Line(Point3d.Origin, new Point3d(10, 0, 0)); Add(db, tr, line); id = line.ObjectId; Data(db, tr, line, "ROLLBACK"); record = Dictionary(tr, line); tr.Commit(); }
                var paths = Paths(db, id); var bad = new PolylinePath { Thickness = 2 };
                bad.Points.AddRange(new[] { new Point3d(0, 0, 0), new Point3d(10, 0, 0), new Point3d(10, 10, 0), new Point3d(10, 10, 5) }); bad.Originals.Add(id); paths.Add(bad);
                foreach (bool preserve in new[] { true, false })
                {
                    bool failed = false; try { CurveMetadataService.ReplaceBatch(db, paths, .001, preserve); } catch (InvalidOperationException) { failed = true; } Check(failed, "bad second boundary aborts whole batch, preserve=" + preserve);
                    using (var read = db.TransactionManager.StartTransaction())
                    {
                        var original = (Line)read.GetObject(id, OpenMode.ForRead); CheckData(original, "ROLLBACK"); CheckDictionary(read, original, record);
                        var table = (BlockTable)read.GetObject(db.BlockTableId, OpenMode.ForRead);
                        Check(((BlockTableRecord)read.GetObject(table[BlockTableRecord.ModelSpace], OpenMode.ForRead)).Cast<ObjectId>().Count(objectId => objectId.IsValid && !objectId.IsErased) == 1, "no partial output, preserve=" + preserve);
                    }
                }
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var line = (Line)tr.GetObject(id, OpenMode.ForRead); CheckData(line, "ROLLBACK"); CheckDictionary(tr, line, record);
                    var table = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead); Check(((BlockTableRecord)tr.GetObject(table[BlockTableRecord.ModelSpace], OpenMode.ForRead)).Cast<ObjectId>().Count(objectId => objectId.IsValid && !objectId.IsErased) == 1, "no active partial output");
                    var a = new Line(new Point3d(20, 0, 0), new Point3d(30, 0, 0)); var b = new Line(a.EndPoint, new Point3d(40, 0, 0)); Add(db, tr, a); Add(db, tr, b); Data(db, tr, a, "PRECISION"); Data(db, tr, b, "PRECISION");
                    using (var data = b.XData) { var values = data.AsArray(); values[3] = new TypedValue(1040, 1.250000000000001); using (var replacement = new ResultBuffer(values)) b.XData = replacement; }
                    Check(CurveMetadataService.GroupKey(a) != CurveMetadataService.GroupKey(b), "full double precision prevents silent merge"); tr.Commit();
                }
            }
        }
        private static ObjectId[] MetadataIds;
        private static ObjectId MetadataGroup, MetadataRecord, PlainDictionaryRecord;
        [CommandMethod("XYD_METADATA_FIXTURE")]
        public void MetadataFixture()
        {
            var db = Application.DocumentManager.MdiActiveDocument.Database;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var table = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead); var space = (BlockTableRecord)tr.GetObject(table[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                foreach (ObjectId id in space) { var entity = (Entity)tr.GetObject(id, OpenMode.ForRead); var layer = (LayerTableRecord)tr.GetObject(entity.LayerId, OpenMode.ForWrite); layer.IsLocked = false; entity.UpgradeOpen(); entity.Erase(); }
                var ids = new List<ObjectId>();
                for (int i = 0; i < 5; i++)
                {
                    var line = new Line(new Point3d(i * 10, 0, 0), new Point3d((i + 1) * 10, 0, 0)); Add(db, tr, line); ids.Add(line.ObjectId);
                    Data(db, tr, line, i < 2 ? "SAME" : "VALUE" + i);
                    if (i == 3) { MetadataRecord = Dictionary(tr, line); MetadataGroup = Group(db, tr, line, "NATIVE_METADATA_GROUP"); }
                    if (i == 4) PlainDictionaryRecord = Dictionary(tr, line);
                }
                MetadataIds = ids.ToArray(); tr.Commit();
            }
        }
        [CommandMethod("XYD_METADATA_VERIFY", CommandFlags.NoUndoMarker)]
        public void MetadataVerify()
        {
            Record("native command converts all extended-data lines and retains dictionaries / group / handles", () =>
            {
                var db = Application.DocumentManager.MdiActiveDocument.Database;
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    Check(MetadataIds[1].IsErased, "equal data joins");
                    foreach (int i in new[] { 0, 2, 3, 4 }) { var p = (Polyline)tr.GetObject(MetadataIds[i], OpenMode.ForRead); CheckData(p, i == 0 ? "SAME" : "VALUE" + i); Near(p.Length, i == 0 ? 20 : 10, "converted length"); }
                    var associated = (Polyline)tr.GetObject(MetadataIds[3], OpenMode.ForRead); CheckDictionary(tr, associated, MetadataRecord); Check(associated.GetPersistentReactorIds().Contains(MetadataGroup), "reactor survives command");
                    CheckDictionary(tr, (Polyline)tr.GetObject(MetadataIds[4], OpenMode.ForRead), PlainDictionaryRecord);
                    Check(((Group)tr.GetObject(MetadataGroup, OpenMode.ForRead)).GetAllEntityIds().SequenceEqual(new[] { MetadataIds[3] }), "group membership survives command");
                }
            });
        }
        [CommandMethod("XYD_METADATA_UNDO_VERIFY", CommandFlags.NoUndoMarker)]
        public void MetadataUndoVerify()
        {
            Record("single U restores every extended-data LINE, dictionary identity and persistent group", () =>
            {
                var db = Application.DocumentManager.MdiActiveDocument.Database;
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    for (int i = 0; i < MetadataIds.Length; i++) { var line = (Line)tr.GetObject(MetadataIds[i], OpenMode.ForRead); try { CheckData(line, i < 2 ? "SAME" : "VALUE" + i); } catch (System.Exception ex) { throw new InvalidOperationException("undo original index " + i + " handle " + MetadataIds[i].Handle, ex); } Near(line.Length, 10, "original length after undo"); }
                    var associated = (Line)tr.GetObject(MetadataIds[3], OpenMode.ForRead); CheckDictionary(tr, associated, MetadataRecord); Check(associated.GetPersistentReactorIds().Contains(MetadataGroup), "reactor after undo");
                    CheckDictionary(tr, (Line)tr.GetObject(MetadataIds[4], OpenMode.ForRead), PlainDictionaryRecord);
                    Check(((Group)tr.GetObject(MetadataGroup, OpenMode.ForRead)).GetAllEntityIds().SequenceEqual(new[] { MetadataIds[3] }), "group after undo");
                }
            });
        }
        [CommandMethod("XYD_METADATA_DISCARD_VERIFY", CommandFlags.NoUndoMarker)]
        public void MetadataDiscardVerify()
        {
            Record("native default discards XData / dictionary / reactors and joins different data", () =>
            {
                var db = Application.DocumentManager.MdiActiveDocument.Database;
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var table = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var entities = ((BlockTableRecord)tr.GetObject(table[BlockTableRecord.ModelSpace], OpenMode.ForRead)).Cast<ObjectId>().Select(id => (Entity)tr.GetObject(id, OpenMode.ForRead)).ToList();
                    var p = entities.Single() as Polyline; Check(p != null, "all five data-bearing lines joined to one PL"); Near(p.Length, 50, "joined length");
                    Check(!CurveMetadataService.HasData(p), "output has no attached data"); Check(MetadataIds.All(id => id.IsErased), "original attached-data objects replaced");
                }
            });
        }
        private static ObjectId[] PolylineIds;
        private static double JoinedPolylineLength;
        [CommandMethod("XYD_PL_JOIN_FIXTURE")]
        public void PolylineFixture()
        {
            var db = Application.DocumentManager.MdiActiveDocument.Database;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var table = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead); var space = (BlockTableRecord)tr.GetObject(table[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                foreach (ObjectId id in space) { var entity = (Entity)tr.GetObject(id, OpenMode.ForRead); ((LayerTableRecord)tr.GetObject(entity.LayerId, OpenMode.ForWrite)).IsLocked = false; entity.UpgradeOpen(); entity.Erase(); }
                var a = new Polyline { Elevation = 8 }; a.AddVertexAt(0, new Point2d(0, 0), .5, 2, 4); a.AddVertexAt(1, new Point2d(5, 0), 0, 0, 0); a.AddVertexAt(2, new Point2d(10, 0), 0, 0, 0);
                var b = new Polyline { Elevation = 20 }; b.AddVertexAt(0, new Point2d(20, 0), 0, 0, 0); b.AddVertexAt(1, new Point2d(15, 0), 0, 0, 0); b.AddVertexAt(2, new Point2d(10.005, 0), 0, 0, 0);
                var c = new Polyline2d(Poly2dType.SimplePoly, new Point3dCollection(new[] { new Point3d(30, 0, 0), new Point3d(25, 0, 0), new Point3d(20, 0, 0) }), 0, false, 0, 0, new DoubleCollection(new[] { 0.0, 0, 0 })) { Elevation = -5 };
                var d = new Polyline3d(Poly3dType.SimplePoly, new Point3dCollection(new[] { new Point3d(60, 0, 7), new Point3d(70, 0, 9), new Point3d(80, 0, 20) }), false);
                var e = new Polyline { Elevation = -10 }; e.AddVertexAt(0, new Point2d(80.005, 0), 0, 0, 0); e.AddVertexAt(1, new Point2d(90, 0), 0, 0, 0);
                var f = Rectangle(110); f.Elevation = 4;
                foreach (var entity in new Entity[] { a, b, c, d, e, f }) Add(db, tr, entity);
                Data(db, tr, a, "PL_A"); Data(db, tr, b, "PL_B"); Dictionary(tr, c);
                JoinedPolylineLength = a.Length + b.Length + 10 + .005;
                PolylineIds = new[] { a.ObjectId, b.ObjectId, c.ObjectId, d.ObjectId, e.ObjectId, f.ObjectId }; tr.Commit();
            }
        }
        [CommandMethod("XYD_PL_JOIN_VERIFY", CommandFlags.NoUndoMarker)]
        public void PolylineVerify()
        {
            Record("native all-PL selection joins 2D / legacy / 3D paths after projection, retains widths / arcs / closed outline", () =>
            {
                var db = Application.DocumentManager.MdiActiveDocument.Database;
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var table = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var entities = ((BlockTableRecord)tr.GetObject(table[BlockTableRecord.ModelSpace], OpenMode.ForRead)).Cast<ObjectId>().Select(id => (Entity)tr.GetObject(id, OpenMode.ForRead)).ToList();
                    Check(entities.Count == 3 && entities.All(entity => entity is Polyline), "six PL become three groups");
                    var paths = entities.Cast<Polyline>().ToList(); Check(paths.All(p => p.Normal.IsEqualTo(Vector3d.ZAxis) && p.Elevation == 0 && !CurveMetadataService.HasData(p)), "defaults apply to all PL");
                    var arc = paths.Single(p => p.HasBulges); Near(arc.Length, JoinedPolylineLength, "joined arc path length including gap");
                    Near(arc.GetStartWidthAt(0), 2, "arc start width"); Near(arc.GetEndWidthAt(0), 4, "arc end width");
                    Near(paths.Single(p => !p.HasBulges && !p.Closed).Length, 30, "joined 3D / 2D projection length");
                    var closed = paths.Single(p => p.Closed); Near(closed.Area, 50, "closed rectangle remains independent");
                }
            });
        }
        [CommandMethod("XYD_PL_JOIN_UNDO_VERIFY", CommandFlags.NoUndoMarker)]
        public void PolylineUndoVerify()
        {
            Record("one U restores all PL types, heights, XData and dictionary after merge", () =>
            {
                var db = Application.DocumentManager.MdiActiveDocument.Database;
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    Check(PolylineIds.All(id => !id.IsErased), "all original identities restored");
                    var a = (Polyline)tr.GetObject(PolylineIds[0], OpenMode.ForRead); Near(a.Elevation, 8, "original elevation"); CheckData(a, "PL_A");
                    var b = (Polyline)tr.GetObject(PolylineIds[1], OpenMode.ForRead); Near(b.Elevation, 20, "second elevation"); CheckData(b, "PL_B");
                    var c = (Polyline2d)tr.GetObject(PolylineIds[2], OpenMode.ForRead); Near(c.Elevation, -5, "legacy elevation"); Check(!c.ExtensionDictionary.IsNull, "legacy dictionary restored");
                    Check(tr.GetObject(PolylineIds[3], OpenMode.ForRead) is Polyline3d, "3D type restored");
                }
            });
        }
        [CommandMethod("XYD_CURVE_REGRESSION")]
        public void Run()
        {
            Record("XData equality / conflict splitting / nested dictionary / handle and self reference / DWG roundtrip", TestMetadata);
            Record("associated entity keeps persistent reactors and identity, including 3D vertices", TestReactors);
            Record("metadata batch rollback and full floating-point equality", TestMetadataRollback);
            Record("existing LINE grouping / gaps / branches / loops / tolerance / high-Z", () =>
            {
                typeof(V13Tests).GetMethod("TestLines", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).Invoke(null, null);
            });
            Record("command defaults discard data and unify Z", () => { var options = new CurveConversionOptions(); Check(!options.PreserveData && options.UnifyZ, "option defaults"); });
            using (var db = new Database(true, true)) using (var tr = db.TransactionManager.StartTransaction())
            {
                Record("XY projection keeps horizontal / reversed-normal exact arcs and variable widths", () =>
                {
                    foreach (var normal in new[] { Vector3d.ZAxis, -Vector3d.ZAxis })
                    { var circle = new Circle(new Point3d(3, 5, 27), normal, 9); var p = Project(db, tr, circle, 1e-7); Near(p.Area, 81 * Math.PI, "projected exact circle"); Check(p.HasBulges && p.NumberOfVertices == 4, "exact circle arcs"); }
                    var wide = new Polyline { Elevation = 14 }; wide.AddVertexAt(0, new Point2d(0, 0), -.7, 2, 5); wide.AddVertexAt(1, new Point2d(10, 0), 0, 0, 0);
                    var projected = Project(db, tr, wide, 1e-7); Near(projected.GetBulgeAt(0), -.7, "projected bulge"); Near(projected.GetStartWidthAt(0), 2, "projected start width"); Near(projected.GetEndWidthAt(0), 5, "projected end width");
                });
                Record("tilted major arc / circle / variable-width PL project within fit error", () =>
                {
                    var normal = new Vector3d(1, 2, 3).GetNormal();
                    var arc = new Arc(new Point3d(4, 9, 20), normal, 12, .2, .2 + 1.7 * Math.PI); Check(!Project(db, tr, arc).HasBulges, "tilted arc uses chords");
                    var circle = new Circle(new Point3d(8, 2, 19), normal, 9); Project(db, tr, circle);
                    var wide = new Polyline { Normal = normal, Elevation = 15 }; wide.AddVertexAt(0, new Point2d(0, 0), .8, 2, 5); wide.AddVertexAt(1, new Point2d(10, 0), 0, 0, 0);
                    var projected = Project(db, tr, wide); Near(projected.GetStartWidthAt(0), 2, "tilted start width"); Near(projected.GetEndWidthAt(projected.NumberOfVertices - 2), 5, "tilted end width");
                });
                Record("spatial spline / helix project within fit error", () =>
                {
                    Project(db, tr, SampleSpline(true));
                    var helix = new Helix { AxisVector = Vector3d.ZAxis, StartPoint = new Point3d(5, 0, 0), BaseRadius = 5, TopRadius = 3, Height = 12, Turns = 3 }; helix.CreateHelix(); Project(db, tr, helix);
                });
                Record("unify Z enables height-separated joins; off keeps high elevations; collapsed vertical retained", () =>
                {
                    var a = new Line(new Point3d(40, 0, 8), new Point3d(50, 0, 8)); var b = new Line(new Point3d(50.005, 0, 27), new Point3d(60, 0, 27)); Add(db, tr, a); Add(db, tr, b);
                    var flat = LineToPolylineService.Build(new[] { LineToPolylineService.Read(a, tr, .001, false, true), LineToPolylineService.Read(b, tr, .001, false, true) }, .01);
                    Check(flat.Count == 1, "XY endpoints join"); using (var p = (Polyline)LineToPolylineService.Create(flat[0])) { Near(p.Length, 20, "original XY ends plus gap"); Near(p.Elevation, 0, "joined Z"); }
                    var spatial = LineToPolylineService.Build(new[] { LineToPolylineService.Read(a, tr, .001, false, false), LineToPolylineService.Read(b, tr, .001, false, false) }, .01); Check(spatial.Count == 2, "off does not join height-separated curves");
                    var vertical = new Line(new Point3d(70, 0, 3), new Point3d(70, 0, 18)); Add(db, tr, vertical); bool rejected = false;
                    try { LineToPolylineService.Read(vertical, tr, .001, false, true); } catch (InvalidOperationException) { rejected = true; } Check(rejected && !vertical.IsErased, "collapsed original retained");
                    var arc = new Arc(new Point3d(0, 0, 4), Vector3d.ZAxis, 10, 0, Math.PI / 2); Add(db, tr, arc);
                    var link = new Line(new Point3d(arc.EndPoint.X, arc.EndPoint.Y, 27), new Point3d(0, 20, 27)); Add(db, tr, link);
                    var joined = LineToPolylineService.Build(new[] { LineToPolylineService.Read(arc, tr, .001, false, true), LineToPolylineService.Read(link, tr, .001, false, true) }, .01).Single();
                    using (var p = (Polyline)LineToPolylineService.Create(joined)) { Near(p.Length, 10 + 5 * Math.PI, "elevated LINE / ARC join exact"); Check(p.HasBulges, "flattening keeps XY arc"); }
                });
                Record("exact quarter / major / tilted ARC and thickness", () =>
                {
                    foreach (double angle in new[] { Math.PI / 2, Math.PI * 1.5 }) foreach (var n in new[] { Vector3d.ZAxis, new Vector3d(1, 2, 3).GetNormal() })
                    {
                        var arc = new Arc(new Point3d(3, 4, 12), n, 10, .3, .3 + angle) { Thickness = 2 };
                        var p = (Polyline)Convert(db, tr, arc); Near(p.Length, arc.Length, "length"); Near(p.GetBulgeAt(0), Math.Tan(angle / 4), "bulge");
                        Near(p.Thickness, 2, "thickness"); Check(p.Normal.IsEqualTo(n), "normal"); Fit(arc, p, 1e-7);
                    }
                });
                Record("exact circles, four arc vertices, area / radius / elevation", () =>
                {
                    foreach (var n in new[] { Vector3d.ZAxis, new Vector3d(2, -3, 1).GetNormal() })
                    {
                        var circle = new Circle(new Point3d(20, 50, 7), n, 8); var p = (Polyline)Convert(db, tr, circle);
                        Check(p.Closed && p.NumberOfVertices == 4, "four closed arc vertices"); Near(p.Area, Math.PI * 64, "circle area"); Near(p.Length, Math.PI * 16, "circumference"); Fit(circle, p, 1e-7);
                    }
                });
                Record("rectangles and existing bulges / variable width / Plinegen", () =>
                {
                    var rectangle = Rectangle(); var p = (Polyline)Convert(db, tr, rectangle); Check(p.Closed && p.NumberOfVertices == 4, "rectangle"); Near(p.Area, 50, "area");
                    var arc = new Polyline { Normal = new Vector3d(2, 1, 3).GetNormal(), Elevation = 9, Plinegen = true };
                    arc.AddVertexAt(0, new Point2d(0, 0), -.5, 2, 4); arc.AddVertexAt(1, new Point2d(10, 0), 0, 4, 1); arc.AddVertexAt(2, new Point2d(10, 6), 0, 0, 0);
                    var copy = (Polyline)Convert(db, tr, arc); Check(copy.Plinegen, "Plinegen"); Near(copy.GetBulgeAt(0), -.5, "signed bulge"); Near(copy.GetStartWidthAt(0), 2, "start width"); Near(copy.GetEndWidthAt(0), 4, "end width"); Fit(arc, copy, 1e-7);
                });
                Record("reversed ARC joins LINE without changing curvature", () =>
                {
                    var arc = new Arc(Point3d.Origin, Vector3d.ZAxis, 10, 0, Math.PI / 2); Add(db, tr, arc);
                    var line = new Line(new Point3d(0, 20, 0), arc.EndPoint); Add(db, tr, line);
                    var inputs = new[] { LineToPolylineService.Read(line, tr, .01), LineToPolylineService.Read(arc, tr, .01) };
                    var path = LineToPolylineService.Build(inputs, 0).Single(); var p = (Polyline)LineToPolylineService.Create(path); Add(db, tr, p);
                    Near(p.Length, line.Length + arc.Length, "joined length"); Check(p.GetBulgeAt(1) < 0, "reverse bulge"); Fit(arc, p, 1e-7, false);
                });
                Record("legacy POLYLINE2D OCS coordinates / normal / elevation / bulge", () =>
                {
                    var p = new Polyline2d(Poly2dType.SimplePoly, new Point3dCollection(new[] { new Point3d(0, 0, 0), new Point3d(10, 0, 0), new Point3d(10, 5, 0) }), 0, false, 0, 0, new DoubleCollection(new[] { .4, 0, 0 }));
                    Add(db, tr, p); p.Normal = new Vector3d(2, 3, 4).GetNormal(); p.Elevation = 15;
                    var copy = (Polyline)Convert(db, tr, p); Near(copy.GetBulgeAt(0), .4, "legacy bulge"); Fit(p, copy, 1e-7);
                });
                Record("legacy fitted 2D / 3D polylines", () =>
                {
                    var p2 = new Polyline2d(Poly2dType.SimplePoly, new Point3dCollection(new[] { new Point3d(0, 0, 0), new Point3d(5, 8, 0), new Point3d(10, -5, 0), new Point3d(15, 0, 0) }), 0, false, 0, 0, new DoubleCollection(new[] { 0.0, 0, 0, 0 }));
                    Add(db, tr, p2); p2.SplineFit(Poly2dType.CubicSplinePoly, 12); Fit(p2, Convert(db, tr, p2), .001);
                    p2.ConstantWidth = 2; p2.LinetypeGenerationOn = true; var wide = (Polyline)Convert(db, tr, p2); Near(wide.GetStartWidthAt(0), 2, "legacy fit constant width"); Check(wide.Plinegen, "legacy fit linetype generation"); Fit(p2, wide, .001);
                    var fitVertices = p2.Cast<ObjectId>().Select(id => (Vertex2d)tr.GetObject(id, OpenMode.ForWrite)).Where(v => v.VertexType != Vertex2dType.SplineControlVertex).ToList();
                    for (int i = 0; i < fitVertices.Count; i++) { fitVertices[i].StartWidth = 1 + i * .1; fitVertices[i].EndWidth = 2 + i * .1; }
                    var variable = (Polyline)Convert(db, tr, p2); Near(variable.GetStartWidthAt(0), 1, "legacy variable start width"); Near(variable.GetEndWidthAt(variable.NumberOfVertices - 2), fitVertices[fitVertices.Count - 2].EndWidth, "legacy variable end width"); Fit(p2, variable, .001);
                    var p3 = new Polyline3d(Poly3dType.SimplePoly, new Point3dCollection(new[] { new Point3d(0, 0, 0), new Point3d(5, 8, 2), new Point3d(10, -5, 5), new Point3d(15, 0, 8) }), false);
                    Add(db, tr, p3); p3.SplineFit(Poly3dType.CubicSplinePoly, 12); Fit(p3, Convert(db, tr, p3), .001);
                });
                Record("ellipse / elliptic arc, dense error measurement", () =>
                {
                    foreach (double end in new[] { Math.PI * 2, Math.PI * 1.7 })
                    {
                        var ellipse = new Ellipse(new Point3d(30, 40, 9), Vector3d.ZAxis, new Vector3d(12, 3, 0), .3, 0, end);
                        var p = (Polyline)Convert(db, tr, ellipse); Check(p.Closed == ellipse.Closed, "ellipse closure"); Fit(ellipse, p, .001);
                    }
                });
                Record("planar and spatial SPLINE, dense error measurement", () =>
                {
                    foreach (bool spatial in new[] { false, true }) { var spline = SampleSpline(spatial); var p = Convert(db, tr, spline); Check((p is Polyline3d) == spatial, "spatial output"); Fit(spline, p, .001); }
                });
                Record("HELIX generic finite-curve conversion", () =>
                {
                    var helix = new Helix { AxisVector = Vector3d.ZAxis, StartPoint = new Point3d(5, 0, 0), BaseRadius = 5, TopRadius = 3, Height = 12, Turns = 3 };
                    helix.CreateHelix(); var p = Convert(db, tr, helix); Check(p is Polyline3d, "helix is 3D"); Fit(helix, p, .001);
                });
                Record("noncoplanar ARC join tessellates with bounded error", () =>
                {
                    var arc = new Arc(Point3d.Origin, Vector3d.ZAxis, 10, 0, Math.PI); Add(db, tr, arc);
                    var line = new Line(arc.EndPoint, arc.EndPoint + new Vector3d(0, 0, 7)); Add(db, tr, line);
                    var path = LineToPolylineService.Build(new[] { LineToPolylineService.Read(arc, tr, .001), LineToPolylineService.Read(line, tr, .001) }, 0).Single();
                    var output = (Curve)LineToPolylineService.Create(path, null, .001); Add(db, tr, output); Check(output is Polyline3d, "3D path");
                    for (int i = 0; i <= 1000; i++) { var pt = arc.GetPointAtParameter(arc.StartParam + (arc.EndParam - arc.StartParam) * i / 1000); Check(pt.DistanceTo(output.GetClosestPointTo(pt, false)) <= .001001, "3D arc error"); }
                    path.Pieces[0].StartWidth = 2; bool rejected = false; try { using (var e = LineToPolylineService.Create(path)) { } } catch (InvalidOperationException) { rejected = true; } Check(rejected, "3D width loss rejected");
                });
                Record("closed mixed LINE + ARC loop and isolated closed outlines", () =>
                {
                    var arc = new Arc(Point3d.Origin, Vector3d.ZAxis, 5, 0, Math.PI); Add(db, tr, arc); var diameter = new Line(arc.EndPoint, arc.StartPoint); Add(db, tr, diameter);
                    var circle = new Circle(new Point3d(10, 0, 0), Vector3d.ZAxis, 5); Add(db, tr, circle);
                    var paths = LineToPolylineService.Build(new[] { LineToPolylineService.Read(arc, tr, .01), LineToPolylineService.Read(diameter, tr, .01), LineToPolylineService.Read(circle, tr, .01) }, 0);
                    Check(paths.Count == 2 && paths.All(p => p.Closed), "two closed outlines");
                    using (var output = (Polyline)LineToPolylineService.Create(paths.Single(p => p.Originals.Count == 2))) Near(output.Area, Math.PI * 25 / 2, "semicircle area");
                });
                Record("REGION boundary and MLINE outlines", () =>
                {
                    using (var rectangle = Rectangle())
                    {
                        var regions = Region.CreateFromCurves(new DBObjectCollection { rectangle });
                        var region = (Region)regions[0]; Add(db, tr, region); var inputs = LineToPolylineService.ReadAll(region, tr, .001);
                        try { var paths = LineToPolylineService.Build(inputs, .001); Check(paths.Count == 1 && paths[0].Closed, "region loop"); using (var p = (Polyline)LineToPolylineService.Create(paths[0])) Near(p.Area, 50, "region area"); }
                        finally { foreach (var input in inputs) input.DisplaySource.Dispose(); }
                    }
                    var mline = new Mline { Normal = Vector3d.ZAxis }; Add(db, tr, mline); mline.Style = db.CmlstyleID; mline.Scale = 1; mline.AppendSegment(new Point3d(0, 0, 0)); mline.AppendSegment(new Point3d(10, 0, 0)); mline.AppendSegment(new Point3d(10, 10, 0));
                    var pieces = LineToPolylineService.ReadAll(mline, tr, .001); try { Check(pieces.Count >= 4 && LineToPolylineService.Build(pieces, .001).Count >= 2, "multiline outlines"); } finally { foreach (var input in pieces) input.DisplaySource.Dispose(); }
                });
                Record("unsupported infinite objects retained; zero fit error rejected", () =>
                {
                    using (var ray = new Ray()) using (var xline = new Xline()) { Check(!LineToPolylineService.IsSupported(ray) && !LineToPolylineService.IsSupported(xline), "infinite curves"); }
                    var spline = SampleSpline(false); Add(db, tr, spline); bool rejected = false; try { LineToPolylineService.Read(spline, tr, 0); } catch (ArgumentException) { rejected = true; } Check(rejected, "positive fitting error");
                });
                tr.Commit();
            }
        }
        [CommandMethod("XYD_CURVE_FIXTURE")]
        public void Fixture()
        {
            var db = Application.DocumentManager.MdiActiveDocument.Database;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead); var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                foreach (ObjectId id in ms) tr.GetObject(id, OpenMode.ForWrite).Erase();
                var arc = new Arc(Point3d.Origin, Vector3d.ZAxis, 10, 0, Math.PI / 2) { ColorIndex = 1 };
                var line = new Line(arc.EndPoint, new Point3d(0, 20, 0)) { ColorIndex = 1 }; Add(db, tr, line); Add(db, tr, arc);
                Add(db, tr, new Circle(new Point3d(30, 0, 0), Vector3d.ZAxis, 4)); Add(db, tr, Rectangle(50));
                Add(db, tr, new Ellipse(new Point3d(80, 0, 0), Vector3d.ZAxis, new Vector3d(10, 0, 0), .4, 0, Math.PI * 2));
                Add(db, tr, SampleSpline(false));
                Add(db, tr, new Polyline3d(Poly3dType.SimplePoly, new Point3dCollection(new[] { new Point3d(100, 0, 0), new Point3d(100, 10, 0), new Point3d(100, 10, 5), new Point3d(105, 10, 8) }), false));
                using (var rect = Rectangle(130)) { var regions = Region.CreateFromCurves(new DBObjectCollection { rect }); Add(db, tr, (Region)regions[0]); }
                var ml = new Mline { Normal = Vector3d.ZAxis }; Add(db, tr, ml); ml.Style = db.CmlstyleID; ml.AppendSegment(new Point3d(150, 0, 0)); ml.AppendSegment(new Point3d(160, 0, 0)); ml.AppendSegment(new Point3d(160, 10, 0));
                var layers = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForWrite); ObjectId lockedId;
                if (!layers.Has("XYD_CURVE_LOCKED")) { var l = new LayerTableRecord { Name = "XYD_CURVE_LOCKED" }; lockedId = layers.Add(l); tr.AddNewlyCreatedDBObject(l, true); } else lockedId = layers["XYD_CURVE_LOCKED"];
                Add(db, tr, new Circle(new Point3d(200, 0, 0), Vector3d.ZAxis, 3) { LayerId = lockedId }); ((LayerTableRecord)tr.GetObject(lockedId, OpenMode.ForWrite)).IsLocked = true;
                Add(db, tr, new DBText { TextString = "KEEP", Position = new Point3d(210, 0, 0), Height = 2 });
                tr.Commit();
            }
        }
        [CommandMethod("XYD_CURVE_VERIFY", CommandFlags.NoUndoMarker)]
        public void Verify()
        {
            VerifyMixed(false);
        }
        [CommandMethod("XYD_CURVE_FLAT_VERIFY", CommandFlags.NoUndoMarker)]
        public void VerifyFlat() { VerifyMixed(true); }
        private static void VerifyMixed(bool unifyZ)
        {
            Record("native mixed command, unify Z=" + unifyZ + ", preserves locked / text", () =>
            {
                var db = Application.DocumentManager.MdiActiveDocument.Database;
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead); var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                    var entities = ms.Cast<ObjectId>().Select(id => (Entity)tr.GetObject(id, OpenMode.ForRead)).ToList();
                    Check(entities.Count(e => e is Circle) == 1 && entities.Count(e => e is DBText) == 1, "only locked circle and text remain");
                    Check(!entities.Any(e => e is Line || e is Arc || e is Ellipse || e is Spline || e is Region || e is Mline || e is Polyline2d), "all convertible originals erased");
                    Check(entities.Count(e => e is Polyline) >= (unifyZ ? 9 : 8) && entities.Count(e => e is Polyline3d) == (unifyZ ? 0 : 1), "all output paths, including region and both multiline sides");
                    if (unifyZ) Check(entities.OfType<Polyline>().All(p => p.Elevation == 0 && p.Normal.IsEqualTo(Vector3d.ZAxis)), "all PL vertices at WCS Z=0");
                    var joined = entities.OfType<Polyline>().Single(e => e.ColorIndex == 1); Near(joined.Length, 10 + 5 * Math.PI, "command joins LINE + ARC and keeps color"); Check(joined.HasBulges, "command retains circular arc");
                    Check(entities.OfType<Polyline>().Any(p => p.Closed && p.NumberOfVertices == 4 && Math.Abs(p.Area - 16 * Math.PI) < 1e-7), "command circle remains exact");
                }
            });
        }
        [CommandMethod("XYD_CURVE_UNDO_VERIFY", CommandFlags.NoUndoMarker)]
        public void VerifyUndo()
        {
            Record("single U restores mixed original curves, region and multiline", () =>
            {
                var db = Application.DocumentManager.MdiActiveDocument.Database;
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead); var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                    var entities = ms.Cast<ObjectId>().Select(id => (Entity)tr.GetObject(id, OpenMode.ForRead)).ToList();
                    Check(entities.Count == 11 && entities.Count(e => e is Circle) == 2 && entities.Count(e => e is Region) == 1 && entities.Count(e => e is Mline) == 1 && entities.Count(e => e is Polyline) == 1 && entities.Count(e => e is Ellipse) == 1 && entities.Count(e => e is Spline) == 1 && entities.Count(e => e is Line) == 1 && entities.Count(e => e is Arc) == 1, "original entity set");
                }
            });
        }
    }
}
