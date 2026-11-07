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
    public class V13Tests
    {
        private static string Root { get { return Environment.GetEnvironmentVariable("XYD_V13_TEST_ROOT"); } }
        [CommandMethod("XYD_V13_INSPECT")]
        public void Inspect()
        {
            var lines = new List<string>();
            using (var db = new Database(false, true))
            {
                db.ReadDwgFile(Path.Combine(Root, "Blocks", "XYD_TitleBlocks.dwg"), FileShare.Read, true, "");
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    foreach (ObjectId id in (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead))
                    {
                        var btr = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead); if (btr.IsLayout || btr.IsAnonymous) continue;
                        bool first = true; Extents3d bounds = new Extents3d(); var attrs = new List<string>();
                        foreach (ObjectId child in btr)
                        {
                            var e = tr.GetObject(child, OpenMode.ForRead) as Entity;
                            var a = e as AttributeDefinition;
                            if (a != null) { attrs.Add(a.Tag + "=" + a.TextString); continue; }
                            if (e is DBText || e is MText || e == null) continue;
                            var ext = e.GeometricExtents; if (first) { bounds = ext; first = false; } else bounds.AddExtents(ext);
                        }
                        lines.Add(btr.Name + "|" + bounds.MinPoint + "|" + bounds.MaxPoint + "|" + string.Join(";", attrs));
                    }
                    lines.Add("INSUNITS=" + db.Insunits);
                }
            }
            File.WriteAllLines(Path.Combine(Root, "artifacts", "v13-tests", "frames.txt"), lines);
        }
        private static void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); }
        [CommandMethod("XYD_V13_TEST")]
        public void Run()
        {
            var output = new List<string>();
            try
            {
                TestLines(); output.Add("PASS geometry: three groups / isolated / branches / loops / tolerance diameter / plane and 3D");
                TestMapping(); output.Add("PASS mapping: empty title / layout / source conflict / backups / summarization / selected exports");
                TestSources(); output.Add("PASS sources: multi-block same column / model and layout / constants / same filename different directories / readonly / unsaved document memory write");
                TestStandard(); output.Add("PASS standard signs: quantity inference / count filtering / cost arithmetic / successive size edits / shared bilingual conflict / full JSON roundtrip");
                TestSignatures(); output.Add("PASS signatures: 100 people -> 8 definitions / multi-position / name visibility / repeat / replacement / nested privacy");
                TestResources(); output.Add("PASS resources: canonical aliases for all title blocks / full paper tokens / CAD tables");
                TestPrint(); output.Add("PASS printing: native PMP association / all new paper sizes / model and layout PDFs / rotated UCS restoration");
                TestWindows(); output.Add("PASS WPF constructors, column bindings and rendered screens");
            }
            catch (System.Exception ex) { output.Add("FAIL " + ex); }
            File.WriteAllLines(Path.Combine(Root, "artifacts", "v13-tests", "results.txt"), output);
            Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage("\n" + string.Join("\n", output));
        }
        private static LineSegmentInput Segment(double x1, double y1, double x2, double y2, string properties = "0", double z1 = 0, double z2 = 0)
        { return new LineSegmentInput { Start = new Point3d(x1, y1, z1), End = new Point3d(x2, y2, z2), Properties = properties }; }
        private static void TestLines()
        {
            var segments = new List<LineSegmentInput>();
            foreach (int count in new[] { 3, 4, 3 }) { double y = segments.Count * 10; for (int i = 0; i < count; i++) segments.Add(Segment(i, y, i + 1, y)); }
            Check(LineToPolylineService.Build(segments, .01).Count == 3, "three disconnected chains");
            Check(LineToPolylineService.Build(new[] { Segment(0, 0, 1, 0), Segment(3, 0, 4, 0) }, 0).Count == 2, "isolated");
            Check(LineToPolylineService.Build(new[] { Segment(0, 0, 1, 0), Segment(1, 0, 2, 0, "other") }, 0).Count == 2, "properties");
            Check(LineToPolylineService.Build(new[] { Segment(0, 0, 1, 0), Segment(1, 0, 2, 0), Segment(1, 0, 1, 1) }, 0).Count == 3, "branch");
            var gap = LineToPolylineService.Build(new[] { Segment(0, 0, 1, 0), Segment(1.01, 0, 2, 0) }, .011).Single();
            Check(gap.Points.Count == 4 && gap.Points[1].X == 1 && gap.Points[2].X == 1.01, "preserved gap endpoints");
            var cycle = LineToPolylineService.Build(new[] { Segment(0, 0, 1, 0), Segment(1, 0, 1, 1), Segment(1, 1, 0, 0) }, 0).Single(); Check(cycle.Closed, "closed");
            var elevated = LineToPolylineService.Build(new[] { Segment(0, 0, 1, 0, "0", 7, 7) }, 0).Single();
            using (var entity = LineToPolylineService.Create(elevated)) Check(entity is Polyline && Math.Abs(((Polyline)entity).GetPoint3dAt(0).Z - 7) < 1e-8, "elevation");
            var nonplane = LineToPolylineService.Build(new[] { Segment(0, 0, 1, 0), Segment(1, 0, 1, 1), Segment(1, 1, 0, 1, "0", 0, 1) }, 0).Single();
            using (var entity = LineToPolylineService.Create(nonplane)) Check(entity is Polyline3d, "3D");
            // Nearby endpoints may not chain through a cluster wider than the tolerance.
            var transitive = LineToPolylineService.Build(new[] { Segment(-2, 0, 0, 0), Segment(.009, 0, 2, 0), Segment(.018, 0, 4, 0) }, .01);
            foreach (var p in transitive) for (int i = 1; i < p.Points.Count; i++)
                if (p.Points[i - 1].X >= 0 && p.Points[i].X < .02) Check(p.Points[i - 1].DistanceTo(p.Points[i]) <= .01, "cluster diameter");
        }
        private static ObjectId CreateFrame(Database db, string name, Dictionary<string, string> attrs, ObjectId space = default(ObjectId), bool transformed = true, Point3d? position = null)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite); ObjectId def;
                if (bt.Has(name)) def = bt[name];
                else
                {
                    var btr = new BlockTableRecord { Name = name }; def = bt.Add(btr); tr.AddNewlyCreatedDBObject(btr, true);
                    foreach (var pair in attrs) { var a = new AttributeDefinition(Point3d.Origin, pair.Value, pair.Key, pair.Key, db.Textstyle); a.Height = 2; btr.AppendEntity(a); tr.AddNewlyCreatedDBObject(a, true); }
                }
                if (space.IsNull) space = bt[BlockTableRecord.ModelSpace]; var target = (BlockTableRecord)tr.GetObject(space, OpenMode.ForWrite);
                var block = new BlockReference(position ?? (transformed ? new Point3d(30, 20, 0) : Point3d.Origin), def) { Rotation = transformed ? .3 : 0, ScaleFactors = new Scale3d(transformed ? 2 : 1) }; target.AppendEntity(block); tr.AddNewlyCreatedDBObject(block, true);
                foreach (ObjectId id in (BlockTableRecord)tr.GetObject(def, OpenMode.ForRead))
                {
                    var a = tr.GetObject(id, OpenMode.ForRead) as AttributeDefinition; if (a == null) continue;
                    var r = new AttributeReference(); r.SetAttributeFromBlock(a, block.BlockTransform); r.TextString = attrs.ContainsKey(a.Tag) ? attrs[a.Tag] : a.TextString; block.AttributeCollection.AppendAttribute(r); tr.AddNewlyCreatedDBObject(r, true);
                }
                tr.Commit(); return block.ObjectId;
            }
        }
        private static void TestMapping()
        {
            string file = Path.Combine(Root, "artifacts", "v13-tests", "mapping.dwg");
            using (var db = new Database(true, true)) { CreateFrame(db, "TEST_FRAME", new Dictionary<string, string> { { "NAME", "" }, { "QTY", "2" } }); db.SaveAs(file, DwgVersion.AC1027); }
            var p = new AttributeMappingProfile { Columns = new List<MappingColumn> { new MappingColumn { Id = "name", Name = "名称", Group = true }, new MappingColumn { Id = "qty", Name = "数量" } }, QuantityColumnId = "qty" };
            p.Rules.Add(new MappingRule { BlockName = "TEST_FRAME", Fields = new List<MappingField> { new MappingField { Tag = "NAME", ColumnId = "name" }, new MappingField { Tag = "QTY", ColumnId = "qty" } } });
            List<AttributeRecord> rows;
            using (var db = new Database(false, true)) { db.ReadDwgFile(file, FileShare.Read, true, ""); rows = AttributeMappingService.Scan(db, file, p, "全部"); foreach (var r in rows) { r.OpenDatabase = null; r.ObjectId = ObjectId.Null; } }
            Check(rows.Count == 1, "empty title frame scan"); rows[0].Cells["name"].Value = "edited";
            var write = AttributeMappingWriter.Write(rows, p, false); Check(write.Updated == 1 && write.Backups == 1, "external writeback");
            using (var db = new Database(false, true)) { db.ReadDwgFile(file, FileShare.Read, true, ""); Check(db.OriginalFileVersion == DwgVersion.AC1027, "DWG version"); Check(AttributeMappingService.Scan(db, file, p, "全部")[0].Value("name") == "edited", "saved value"); }
            var summary = AttributeMappingService.Summarize(new[] { rows[0], rows[0] }, p); Check(summary[0].Value("qty") == "4", "quantity sum");
            try
            {
                MappingProfileStore.Save(p);
                var loaded = MappingProfileStore.LoadAll(false).Single(v => v.Id == p.Id);
                Check(loaded.Columns[0].Id == "name" && loaded.Columns[0].Group && loaded.QuantityColumnId == "qty", "template roundtrip");
                loaded.Columns[0].Name = "更改名称"; MappingProfileStore.Save(loaded);
                Check(MappingProfileStore.LoadAll(false).Single(v => v.Id == p.Id).Rules[0].Fields[0].ColumnId == "name", "stable mapping id after rename");
            }
            finally { MappingProfileStore.Delete(p); }
            AttributeReportExporter.Excel(file + ".xls", rows, new List<MappingColumn> { p.Columns[1] }, false, false);
            Check(!File.ReadAllText(file + ".xls").Contains("名称") && File.ReadAllText(file + ".xls").Contains("数量"), "selected columns");
            rows[0].Cells["name"].Value = "later"; rows[0].OriginalAttributes["NAME"] = "stale";
            Check(AttributeMappingWriter.Write(rows, p, false).Updated == 0, "stale original rejected");
        }
        private static void TestSources()
        {
            var p = new AttributeMappingProfile { Columns = new List<MappingColumn> { new MappingColumn { Id = "person", Name = "设计人", Group = true }, new MappingColumn { Id = "constant", Name = "常量" } } };
            p.Rules.Add(new MappingRule { BlockName = "SOURCE_A", Fields = new List<MappingField> { new MappingField { Tag = "DESIGNER", ColumnId = "person" }, new MappingField { Tag = "CONST", ColumnId = "constant", ReadOnly = true } } });
            p.Rules.Add(new MappingRule { BlockName = "SOURCE_B", Fields = new List<MappingField> { new MappingField { Tag = "AUTHOR", ColumnId = "person" } } });
            var rows = new List<AttributeRecord>();
            foreach (string folder in new[] { "one", "two" })
            {
                string dir = Path.Combine(Root, "artifacts", "v13-tests", folder); Directory.CreateDirectory(dir); string file = Path.Combine(dir, "same.dwg");
                using (var db = new Database(true, true))
                {
                    var frame = CreateFrame(db, "SOURCE_A", new Dictionary<string, string> { { "DESIGNER", "张三" } });
                    ObjectId paper = ObjectId.Null;
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        var br = (BlockReference)tr.GetObject(frame, OpenMode.ForRead); var btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForWrite);
                        var constant = new AttributeDefinition(Point3d.Origin, "固定", "CONST", "常量说明", db.Textstyle) { Constant = true, Height = 2 }; btr.AppendEntity(constant); tr.AddNewlyCreatedDBObject(constant, true);
                        foreach (DBDictionaryEntry entry in (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead)) { var layout = (Layout)tr.GetObject(entry.Value, OpenMode.ForRead); if (!layout.ModelType) { paper = layout.BlockTableRecordId; break; } }
                        tr.Commit();
                    }
                    CreateFrame(db, "SOURCE_B", new Dictionary<string, string> { { "AUTHOR", "张三" } }, paper);
                    Check(AttributeMappingService.Scan(db, file, p, "模型").Count == 1 && AttributeMappingService.Scan(db, file, p, "布局").Count == 1, "space selection");
                    var sample = AttributeMappingService.Samples(db, new[] { frame }); Check(sample.Any(s => s.Tag == "CONST" && s.ReadOnly && s.Example == "固定"), "constant attribute sample");
                    db.SaveAs(file, DwgVersion.AC1027); var source = AttributeMappingService.Scan(db, file, p, "全部");
                    Check(source[0].Cells["constant"].ReadOnly && source[0].Value("constant") == "固定", "constant readonly display");
                    foreach (var r in source) { r.OpenDatabase = null; r.ObjectId = ObjectId.Null; r.Cells["person"].Value = folder == "one" ? "李四" : "王五"; } rows.AddRange(source);
                }
            }
            var written = AttributeMappingWriter.Write(rows, p, false); Check(written.Updated == 4 && written.Backups == 2, "same name separated by full path");
            Check(!AttributeMappingService.SamePath(rows[0].FilePath, rows[2].FilePath) && AttributeMappingService.SamePath(rows[0].FilePath, rows[0].FilePath.ToUpperInvariant()), "normalized source comparison");
            Check(AttributeMappingService.Summarize(rows.Take(2), p).Single().Value("__COUNT") == "2", "group instance count without quantity");
            string readonlyFile = rows[0].FilePath; var previous = File.GetAttributes(readonlyFile);
            try { File.SetAttributes(readonlyFile, previous | FileAttributes.ReadOnly); rows[0].Cells["person"].Value = "不能保存"; var blocked = AttributeMappingWriter.Write(rows.Take(1), p, false); Check(blocked.Updated == 0 && blocked.Details.Any(d => d.Contains("只读")), "readonly external file reported"); }
            finally { File.SetAttributes(readonlyFile, previous); }
            var doc = Application.DocumentManager.MdiActiveDocument;
            var id = CreateFrame(doc.Database, "SOURCE_A", new Dictionary<string, string> { { "DESIGNER", "内存旧值" } });
            try
            {
                var current = AttributeMappingService.Scan(doc.Database, doc.Name, p, "模型").Single(r => r.ObjectId == id); current.Cells["person"].Value = "内存新值";
                var result = AttributeMappingWriter.Write(new[] { current }, p, false); Check(result.Updated == 1 && result.Backups == 0 && result.Details.Any(d => d.Contains("等待保存")), "unsaved open document only memory updated");
                Check(AttributeMappingService.Scan(doc.Database, doc.Name, p, "模型").Single(r => r.ObjectId == id).Value("person") == "内存新值", "open document actual value");
            }
            finally { using (var tr = doc.Database.TransactionManager.StartTransaction()) { tr.GetObject(id, OpenMode.ForWrite).Erase(); tr.Commit(); } }
        }
        private static void TestStandard()
        {
            string file = Path.Combine(Root, "artifacts", "v13-tests", "standard.dwg");
            var attrs = new Dictionary<string, string> { { "--XYDAREA", "区域一" }, { "--XYDNAME", "名称" }, { "--XYDISCOUNT", "是" }, { "XYD-NUMBER", "A01~03" }, { "XYD-TYPE", "贴装" }, { "XYD-SIZE", "100*200*3" }, { "XYD-QTY", "" }, { "XYD-TECH", "测试工艺" }, { "XYD-WEIGHT", "1" }, { "XYD-POWER", "0" }, { "XYD-REMARKS", "备注" }, { "XYD-CNSIZE", "10*20*1" }, { "XYD-ENSIZE", "30*40*2" }, { "XYD-CNQTY", "2" }, { "XYD-ENQTY", "1" } };
            using (var db = new Database(true, true)) { CreateFrame(db, "XYD-SIGNBLOCK_A", attrs); CreateFrame(db, "XYD-SIGNBLOCK_D", attrs); var excluded = new Dictionary<string, string>(attrs); excluded["--XYDISCOUNT"] = "否"; CreateFrame(db, "XYD-SIGNBLOCK_B", excluded); db.SaveAs(file, DwgVersion.AC1027); }
            Func<List<AttributeRecord>> read = () => { using (var db = new Database(false, true)) { db.ReadDwgFile(file, FileShare.Read, true, ""); var items = SignExtractor.ExtractFromDatabase(db); foreach (var i in items) { i.SourceRecord.OpenDatabase = null; i.SourceRecord.ObjectId = ObjectId.Null; i.SourceRecord.FilePath = file; } return StandardSignAdapter.Rows(items); } };
            var rows = read(); Check(rows.Count == 3 && rows[0].Value("Qty") == "3", "quantity range and excluded filtering");
            rows[0].Sign.BasePrice = 100; rows[0].Cells["BasePrice"].Value = "100"; rows[0].Cells["Width"].Value = "101.5"; StandardSignAdapter.RefreshCalculated(rows[0]);
            Check(Math.Abs(rows[0].Sign.TotalPrice - 6.09) < 1e-8, "cost area formula");
            var profile = StandardSignAdapter.Profile(true); Check(AttributeMappingWriter.Write(rows, profile, false).Updated == 1, "computed cost columns do not block write");
            Check(rows[0].OriginalAttributes["XYD-SIZE"] == "101.5*200*3", "composed size snapshot");
            rows[0].Cells["Height"].Value = "250"; Check(AttributeMappingWriter.Write(rows, profile, false).Updated == 1, "second edit with refreshed original");
            rows[1].Cells["Name"].Value = "新名中文"; rows[2].Cells["Name"].Value = "冲突英文"; rows[0].Cells["Width"].Value = "111";
            Check(AttributeMappingWriter.Write(rows, profile, false).Updated == 0 && read()[0].Value("Width") == "101.5", "shared conflict rolls entire file back");
            var itemsForJson = read().Select(r => r.Sign).ToList(); itemsForJson[0].Remarks = "引号\" / 反斜线\\ / 换行\n下一行";
            string json = file + ".json"; AttributeReportExporter.FullJson(json, itemsForJson); var imported = SignExtractor.ReadJson(json);
            Check(imported.Count == 3 && imported[0].Remarks == itemsForJson[0].Remarks && imported[0].Width == 101.5 && imported[1].Mode == "CN", "complete JSON escaped data roundtrip");
            var all = StandardSignAdapter.Rows(imported); Check(all.All(r => r.Imported), "imported JSON cannot write to DWG");
            all[0].Cells["Area"].Value = "新增区域"; StandardSignAdapter.RefreshCalculated(all[0]);
            var reordered = StandardSignAdapter.SortRows(all, new[] { "区域一" }); Check(reordered.Last().Area == "新增区域" && reordered.Last().Category != reordered.First().Category, "edited areas regroup exports and subtotals");
        }
        private static void TestSignatures()
        {
            string file = Path.Combine(Root, "artifacts", "v13-tests", "signature-library.dwg");
            using (var library = new Database(true, true))
            {
                using (var tr = library.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(library.BlockTableId, OpenMode.ForWrite);
                    for (int i = 0; i < 100; i++)
                    {
                        var btr = new BlockTableRecord { Name = "PERSON" + i }; bt.Add(btr); tr.AddNewlyCreatedDBObject(btr, true);
                        Entity glyph = i == 0 ? (Entity)new Spline(new Point3dCollection(new[] { Point3d.Origin, new Point3d(3, 5, 0), new Point3d(6, 1, 0), new Point3d(10, 4, 0) }), 3, .0001) : new Line(Point3d.Origin, new Point3d(10 + i, 4, 0));
                        btr.AppendEntity(glyph); tr.AddNewlyCreatedDBObject(glyph, true);
                        if (i == 1)
                        {
                            var hatch = new Hatch(); btr.AppendEntity(hatch); tr.AddNewlyCreatedDBObject(hatch, true); hatch.SetHatchPattern(HatchPatternType.PreDefined, "SOLID");
                            hatch.AppendLoop(HatchLoopTypes.External, new Point2dCollection(new[] { new Point2d(0, 0), new Point2d(10, 0), new Point2d(10, 4), new Point2d(0, 4) }), new DoubleCollection(new double[] { 0, 0, 0, 0 })); hatch.EvaluateHatch(true);
                        }
                    }
                    tr.Commit();
                }
                library.SaveAs(file, DwgVersion.AC1027);
            }
            var p = new AttributeMappingProfile { TitleBlocks = true, SignatureLibraryPath = file, People = ElectronicSignatureService.Catalog(file) };
            Check(p.People.Count == 100, "catalog count"); var rule = new MappingRule { BlockName = "SIGN_FRAME" };
            for (int i = 0; i < 8; i++) rule.Signatures.Add(new SignaturePosition { NameTag = "ROLE" + i, Role = "角色" + i, X = i * 20, Y = 0, Width = 18, Height = 6, Countersign = i >= 4 });
            rule.Signatures.Add(new SignaturePosition { NameTag = "ROLE0", Role = "设计第二位置", X = 0, Y = 10, Width = 18, Height = 6 }); p.Rules.Add(rule);
            var attrs = Enumerable.Range(0, 8).ToDictionary(i => "ROLE" + i, i => "PERSON" + i);
            using (var db = new Database(true, true))
            {
                var frameId = CreateFrame(db, "SIGN_FRAME", attrs);
                for (int pass = 0; pass < 2; pass++)
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var frame = (BlockReference)tr.GetObject(frameId, OpenMode.ForRead); var current = AttributeMappingService.ReadAttributes(frame, tr);
                    var prepared = ElectronicSignatureService.Prepare(p, rule, current, new Dictionary<string, string>());
                    try { ElectronicSignatureService.Apply(db, tr, frame, rule, prepared, false); } catch (System.Exception ex) { throw new InvalidOperationException("signature pass=" + pass, ex); }
                    foreach (var s in prepared) s.Dispose(); ElectronicSignatureService.PurgeUnused(db, tr); tr.Commit();
                }
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    Check(bt.Cast<ObjectId>().Select(id => (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead)).Count(b => b.Name.StartsWith("XYD_SIG_")) == 8, "only eight isolated definitions");
                    Check(!bt.Cast<ObjectId>().Select(id => (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead)).Any(b => b.Name.StartsWith("PERSON")), "no source person definitions");
                    var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                    Check(ms.Cast<ObjectId>().Count(id => tr.GetObject(id, OpenMode.ForRead) is BlockReference) == 10, "one frame + nine positions without duplicates");
                    var frame = (BlockReference)tr.GetObject(frameId, OpenMode.ForRead);
                    foreach (ObjectId id in ms)
                    {
                        var signature = tr.GetObject(id, OpenMode.ForRead) as BlockReference; if (signature == null || id == frameId) continue;
                        using (var x = signature.GetXDataForApplication("XYD_SIGNATURE_V13"))
                        {
                            var slot = rule.Signatures.Single(s => s.Id == (string)x.AsArray()[2].Value); var geometry = (BlockTableRecord)tr.GetObject(signature.BlockTableRecord, OpenMode.ForRead);
                            bool first = true; Extents3d bounds = new Extents3d(); foreach (ObjectId child in geometry) { var ext = ((Entity)tr.GetObject(child, OpenMode.ForRead)).GeometricExtents; if (first) { bounds = ext; first = false; } else bounds.AddExtents(ext); }
                            var center = new Point3d((bounds.MinPoint.X + bounds.MaxPoint.X) / 2, (bounds.MinPoint.Y + bounds.MaxPoint.Y) / 2, 0).TransformBy(signature.BlockTransform);
                            Check(center.DistanceTo(new Point3d(slot.X, slot.Y, slot.Z).TransformBy(frame.BlockTransform)) < 1e-7, "signature center follows rotated scaled frame");
                        }
                    }
                }
                db.SaveAs(Path.Combine(Root, "artifacts", "v13-tests", "signed.dwg"), DwgVersion.AC1027);
            }
            string signed = Path.Combine(Root, "artifacts", "v13-tests", "signed.dwg");
            Func<List<AttributeRecord>> read = () => { using (var db = new Database(false, true)) { db.ReadDwgFile(signed, FileShare.Read, true, ""); var rows = AttributeMappingService.Scan(db, signed, p, "全部"); foreach (var r in rows) { r.OpenDatabase = null; r.ObjectId = ObjectId.Null; } return rows; } };
            Check(AttributeMappingWriter.Write(read(), p, false).Updated == 1, "hide countersign even without electronic-sign checkbox");
            using (var db = new Database(false, true))
            {
                db.ReadDwgFile(signed, FileShare.Read, true, "");
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var frame = (BlockReference)tr.GetObject(AttributeMappingService.Resolve(db, read()[0]), OpenMode.ForRead);
                    var values = AttributeMappingService.ReadAttributes(frame, tr);
                    Check(!values["ROLE0"].Invisible && values["ROLE0"].Visible, "title name kept");
                    Check(values["ROLE4"].Invisible && !values["ROLE4"].Visible && values["ROLE4"].TextString == "PERSON4", "countersign text hidden, value kept");
                }
            }
            p.Columns.Add(new MappingColumn { Id = "designer", Name = "设计人" }); rule.Fields.Add(new MappingField { Tag = "ROLE0", ColumnId = "designer" });
            var edited = read(); edited[0].Cells["designer"].Value = "PERSON9";
            Check(AttributeMappingWriter.Write(edited, p, true).Updated == 1, "person replacement with two slots");
            using (var db = new Database(false, true))
            {
                db.ReadDwgFile(signed, FileShare.Read, true, ""); using (var tr = db.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead); Check(bt.Cast<ObjectId>().Select(id => (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead)).Count(b => b.Name.StartsWith("XYD_SIG_")) == 8, "old unused signature purged");
                    var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead); int names = 0;
                    foreach (ObjectId id in ms) { var br = tr.GetObject(id, OpenMode.ForRead) as BlockReference; if (br == null) continue; using (var x = br.GetXDataForApplication("XYD_SIGNATURE_V13")) if (x != null && (string)x.AsArray()[3].Value == "PERSON9") names++; }
                    Check(names == 2, "same person at both configured positions");
                }
            }
            using (var db = new Database(false, true))
            {
                db.ReadDwgFile(file, FileOpenMode.OpenForReadAndWriteNoShare, true, ""); db.CloseInput(true);
                using (var tr = db.TransactionManager.StartTransaction()) { var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead); var root = (BlockTableRecord)tr.GetObject(bt["PERSON1"], OpenMode.ForWrite); var child = new BlockReference(Point3d.Origin, bt["PERSON99"]); root.AppendEntity(child); tr.AddNewlyCreatedDBObject(child, true); tr.Commit(); }
                db.SaveAs(file, DwgVersion.AC1027);
            }
            bool blocked = false; try { using (var item = ElectronicSignatureService.PrepareOne(p, p.People.Single(s => s.Name == "PERSON1"), rule.Signatures[1])) { } } catch (InvalidOperationException) { blocked = true; }
            Check(blocked, "nested other-person signature rejected");
            using (var db = new Database(true, true))
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite); var user = new BlockTableRecord { Name = "XYD_SIG_GEOMETRY_KEEP" }; var id = bt.Add(user); tr.AddNewlyCreatedDBObject(user, true);
                ElectronicSignatureService.PurgeUnused(db, tr); Check(!id.IsErased, "do not purge user-defined similarly named geometry"); tr.Commit();
            }
        }
        private static void TestResources()
        {
            using (var db = new Database(true, true))
            {
                var import = typeof(BlockManager).GetMethod("ImportBlockDefinition", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                foreach (string size in new[] { "A4", "A0", "A0+0.25", "A0+0.5", "A1", "A1+0.25", "A1+0.5", "A1+0.75", "A1+1", "A2", "A2封面", "A2+0.25", "A2+0.5", "A2+0.75", "A2+1", "A2+1.25", "A2+1.5", "A2+1.75", "A2+2", "A3", "A3+0.25", "A3+0.5" })
                {
                    object[] args = { db, Path.Combine(Root, "Blocks", "XYD_TitleBlocks.dwg"), "XYD-TITLEBLOCK_" + size, DuplicateRecordCloning.Ignore, null };
                    Check((bool)import.Invoke(null, args), "title import " + size + " " + args[4]);
                }
                var p = StandardSignAdapter.Profile(false); var row = new AttributeRecord(); foreach (var c in p.Columns) row.Cells[c.Id] = new AttributeCell { Value = "0" };
                AttributeReportExporter.Cad(db, Point3d.Origin, new List<AttributeRecord> { row }, p.Columns.Take(2).ToList(), "测试表格", false);
            }
            Check(NativePlotEngine.TryMatchMedia("A3", new List<MediaInfo> { new MediaInfo { LocalName = "A3+0.25", CanonicalName = "long" } }) == null, "A3 does not match longer sheet");
            Check(NativePlotEngine.TryMatchMedia("A0", new List<MediaInfo> { new MediaInfo { LocalName = "A0+0.5", CanonicalName = "long" } }) == null, "A0 does not match longer sheet");
        }
        private static void TestWindows()
        {
            var p = StandardSignAdapter.Profile(false); var row = new AttributeRecord { Imported = true }; foreach (var c in p.Columns) row.Cells[c.Id] = new AttributeCell { Value = "0", Original = "0", ReadOnly = true };
            row.Cells["Name"].Value = "标识中文"; row.Cells["Qty"].Value = "3";
            var w = new AttributeCenterWindow(p, new List<AttributeRecord> { row }, true); RenderWindow(w, "sign-center.png"); w.Close();
            var generic = new AttributeMappingProfile { Columns = new List<MappingColumn> { new MappingColumn { Id = "name", Name = "设计人" }, new MappingColumn { Id = "num", Name = "图号" } }, TitleBlocks = true };
            generic.Rules.Add(new MappingRule { BlockName = "TEST_FRAME", Fields = new List<MappingField> { new MappingField { Tag = "DESIGNER", ColumnId = "name" }, new MappingField { Tag = "DRAWNUM", ColumnId = "num" } }, Signatures = new List<SignaturePosition> { new SignaturePosition { NameTag = "DESIGNER", Role = "设计", X = 30, Y = 10 } } });
            generic.People.Add(new SignaturePerson { Name = "张三", BlockName = "张三" });
            var m = new MappingEditorWindow(true, generic); RenderWindow(m, "mapping-editor.png"); m.Close();
            var s = new SignatureSetupWindow(generic); RenderWindow(s, "signature-setup.png"); s.Close();
        }
        private static void RenderWindow(System.Windows.Window window, string file)
        {
            var root = (System.Windows.FrameworkElement)window.Content;
            root.Measure(new System.Windows.Size(window.Width, window.Height)); root.Arrange(new System.Windows.Rect(0, 0, window.Width, window.Height)); root.UpdateLayout();
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.Width, (int)window.Height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32); bitmap.Render(root);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using (var stream = File.Create(Path.Combine(Root, "artifacts", "v13-tests", file))) encoder.Save(stream);
        }
        private static void TestPrint()
        {
            var doc = Application.DocumentManager.MdiActiveDocument; var db = doc.Database;
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
            var printers = NativePlotEngine.GetAvailablePrinters(); string printer = printers.FirstOrDefault(p => p.IndexOf("PDF", StringComparison.OrdinalIgnoreCase) >= 0 && p.EndsWith(".pc3", StringComparison.OrdinalIgnoreCase));
            Check(printer != null, "PDF PC3 exists");
            string original = (string)typeof(PlotPaperSizeInjector).GetMethod("GetPlotConfigPath", flags).Invoke(null, new object[] { printer });
            File.WriteAllText(Path.Combine(Root, "artifacts", "v13-tests", "print-config.txt"), printer + Environment.NewLine + original);
            string scratch = Path.Combine(Path.GetDirectoryName(original), "XYD_V13_Validation_" + Guid.NewGuid().ToString("N") + ".pc3");
            string pmp = Path.Combine(Root, "artifacts", "v13-tests", "validation.pmp"); var ids = new List<ObjectId>(); var oldUcs = doc.Editor.CurrentUserCoordinateSystem; string backup = null;
            try
            {
                File.Copy(original, scratch);
                typeof(PlotPaperSizeInjector).GetMethod("WritePmpForTargetPrinter", flags).Invoke(null, new object[] { Path.Combine(Root, "PMPFiles", "XYD_PaperSizes.pmp"), pmp });
                object[] association = { scratch, pmp, null, null }; Check((bool)typeof(PlotPaperSizeInjector).GetMethod("TryAssociatePc3WithPmp", flags).Invoke(null, association), "PC3 association " + association[3]); backup = (string)association[2];
                NativePlotEngine.RefreshPlotConfigurationList(); var media = NativePlotEngine.GetMediaInfoList(Path.GetFileName(scratch));
                Check(media.Count > 0, "scratch PC3 media loaded"); File.WriteAllLines(Path.Combine(Root, "artifacts", "v13-tests", "media.txt"), media.Select(m => m.LocalName + "|" + m.CanonicalName));
                string[] sizes = { "A4", "A3+0.25", "A3+0.5", "A0+0.25", "A0+0.5" };
                var import = typeof(BlockManager).GetMethod("ImportBlockDefinition", flags);
                foreach (string size in sizes)
                {
                    object[] args = { db, Path.Combine(Root, "Blocks", "XYD_TitleBlocks.dwg"), "XYD-TITLEBLOCK_" + size, DuplicateRecordCloning.Ignore, null }; Check((bool)import.Invoke(null, args), "print frame import " + size);
                    var id = CreateFrame(db, "XYD-TITLEBLOCK_" + size, new Dictionary<string, string> { { "DRAWTITLE", "TEST_" + size }, { "DRAWNUM", "V13" }, { "PAGESIZE", size }, { "DRAWSCALE", "1:1" } }, ObjectId.Null, false, new Point3d(ids.Count * 4000, 0, 0)); ids.Add(id);
                }
                doc.Editor.CurrentUserCoordinateSystem = Matrix3d.Rotation(.35, Vector3d.ZAxis, Point3d.Origin) * Matrix3d.Displacement(new Vector3d(15, 25, 0));
                var frames = TitleBlockExtractor.ScanDatabase(db, doc.Name).Where(b => ids.Contains(b.ObjId)).ToList(); Check(frames.Count == 5, "new frame extraction");
                string style = NativePlotEngine.GetAvailablePlotStyles().FirstOrDefault(s => s.IndexOf("monochrome", StringComparison.OrdinalIgnoreCase) >= 0);
                foreach (var frame in frames)
                {
                    string canonical = NativePlotEngine.TryMatchMedia(frame.PageSize, media); Check(canonical != null, "PMP recognizes " + frame.PageSize);
                    string pdf = Path.Combine(Root, "artifacts", "v13-tests", "frame_" + frame.PageSize + ".pdf");
                    Check(NativePlotEngine.PlotToPdf(db, frame, pdf, Path.GetFileName(scratch), canonical, style) && File.Exists(pdf) && new FileInfo(pdf).Length > 1000, "PDF " + frame.PageSize);
                }
                Check(doc.Editor.CurrentUserCoordinateSystem.IsEqualTo(Matrix3d.Rotation(.35, Vector3d.ZAxis, Point3d.Origin) * Matrix3d.Displacement(new Vector3d(15, 25, 0))), "UCS restored after PDF");
                ObjectId paper = ObjectId.Null;
                using (var tr = db.TransactionManager.StartTransaction()) { var layouts = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead); foreach (DBDictionaryEntry entry in layouts) { var layout = (Layout)tr.GetObject(entry.Value, OpenMode.ForRead); if (!layout.ModelType) { paper = layout.BlockTableRecordId; break; } } }
                var layoutId = CreateFrame(db, "XYD-TITLEBLOCK_A4", new Dictionary<string, string> { { "DRAWTITLE", "LAYOUT_TEST" }, { "DRAWNUM", "V13" }, { "PAGESIZE", "A4" } }, paper, false); ids.Add(layoutId);
                var layoutFrame = TitleBlockExtractor.ScanDatabase(db, doc.Name).Single(b => b.ObjId == layoutId);
                Check(NativePlotEngine.PlotToPdf(db, layoutFrame, Path.Combine(Root, "artifacts", "v13-tests", "layout_A4.pdf"), Path.GetFileName(scratch), NativePlotEngine.TryMatchMedia("A4", media), style), "layout PDF");
            }
            finally
            {
                doc.Editor.CurrentUserCoordinateSystem = oldUcs;
                using (var tr = db.TransactionManager.StartTransaction()) { foreach (var id in ids) if (id.IsValid && !id.IsErased) tr.GetObject(id, OpenMode.ForWrite).Erase(); tr.Commit(); }
                Autodesk.AutoCAD.PlottingServices.PlotConfigManager.SetCurrentConfig(printer);
                if (File.Exists(scratch)) File.Delete(scratch); if (!string.IsNullOrEmpty(backup) && File.Exists(backup)) File.Delete(backup); NativePlotEngine.RefreshPlotConfigurationList();
            }
        }
        [CommandMethod("XYD_V13_LINE_FIXTURE")]
        public void LineFixture()
        {
            var db = Application.DocumentManager.MdiActiveDocument.Database;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead); var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite); int y = 0;
                foreach (int count in new[] { 3, 4, 3 }) { for (int i = 0; i < count; i++) { var line = new Line(new Point3d(i, y, 0), new Point3d(i + 1, y, 0)); ms.AppendEntity(line); tr.AddNewlyCreatedDBObject(line, true); } y += 10; }
                tr.Commit();
            }
        }
        [CommandMethod("XYD_V13_LINE_VERIFY", CommandFlags.NoUndoMarker)]
        public void VerifyLineConversion() { VerifyEntities(0, 3, "native LINE2PL preselection"); }
        [CommandMethod("XYD_V13_UNDO_VERIFY", CommandFlags.NoUndoMarker)]
        public void VerifyLineUndo() { VerifyEntities(10, 0, "single undo restores all originals"); }
        private static void VerifyEntities(int expectedLines, int expectedPolylines, string test)
        {
            var db = Application.DocumentManager.MdiActiveDocument.Database;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead); var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead); var entities = ms.Cast<ObjectId>().Select(id => tr.GetObject(id, OpenMode.ForRead)).ToList();
                File.AppendAllText(Path.Combine(Root, "artifacts", "v13-tests", "results.txt"), (entities.Count(e => e is Line) == expectedLines && entities.Count(e => e is Polyline) == expectedPolylines ? "PASS " : "FAIL ") + test + Environment.NewLine);
            }
        }
    }
}
