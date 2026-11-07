using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace XYDSignTool.Tests
{
    public class UpgradeTests
    {
        private static string Root { get { return Environment.GetEnvironmentVariable("XYD_UPGRADE_ROOT") ?? File.ReadAllText(Path.Combine(Path.GetDirectoryName(typeof(UpgradeTests).Assembly.Location), "test-root.txt")).Trim(); } }
        private static void Guard()
        {
            string name = Application.DocumentManager.MdiActiveDocument.Name;
            string trusted = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Autodesk", "ApplicationPlugins", "XYDSignTool.bundle") + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(name).StartsWith(trusted, StringComparison.OrdinalIgnoreCase) || name.IndexOf("Verification_", StringComparison.OrdinalIgnoreCase) < 0)
                throw new InvalidOperationException("Tests are restricted to the dedicated temporary drawing.");
        }
        private static string Output { get { return Path.Combine(Root, "artifacts", "upgrade-regression"); } }
        private static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
        private static void Near(double a, double b, string message) { Check(Math.Abs(a - b) < 1e-7, message + ": " + a + " vs " + b); }
        private static void Record(string name, Action action)
        { try { action(); File.AppendAllText(Path.Combine(Output, "results.txt"), "PASS " + name + "\n"); } catch (System.Exception ex) { File.AppendAllText(Path.Combine(Output, "results.txt"), "FAIL " + name + " " + ex + "\n"); } }
        private static BlockTableRecord Model(Database db, Transaction tr, OpenMode mode)
        { return (BlockTableRecord)tr.GetObject(((BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead))[BlockTableRecord.ModelSpace], mode); }
        private static void Add(Database db, Transaction tr, Entity e) { Model(db, tr, OpenMode.ForWrite).AppendEntity(e); tr.AddNewlyCreatedDBObject(e, true); }
        [CommandMethod("XYD_UPGRADE_TESTS")]
        public void Run()
        {
            Guard();
            Record("icons have exact 16 / 32 DIP square bounds and frozen cache", () => {
                foreach (string name in new[] { "calc", "print", "table", "edit", "json", "block", "batchprint", "dir", "direxcel", "frame", "blockcount", "dynlen", "spec", "linelen", "plbreak", "findtext", "replacetext", "findsame", "autonumber", "rotate", "scale", "embedimage", "help" })
                    foreach (int size in new[] { 16, 32 }) { var image = IconFactory.GetVectorIcon(name, size); Near(image.Width, size, name + " width"); Near(image.Height, size, name + " height"); Check(image.IsFrozen && ReferenceEquals(image, IconFactory.GetVectorIcon(name, size)), "frozen cache"); }
            });
            Record("render compact icon atlas at 100 / 125 / 150 / 200 percent DPI", () => {
                var names = new[] { "block", "frame", "dir", "table", "calc", "edit", "batchprint", "json", "blockcount", "dynlen", "spec", "linelen", "plbreak", "rotate", "scale", "findtext", "autonumber", "embedimage", "help" };
                foreach (double dpi in new[] { 96.0, 120, 144, 192 })
                {
                    var v = new DrawingVisual(); using (var dc = v.RenderOpen())
                    {
                        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(57, 68, 84)), null, new Rect(0, 0, 460, 100));
                        for (int i = 0; i < names.Length; i++) { double x = 12 + i % 10 * 44, y = 12 + i / 10 * 44; dc.DrawImage(IconFactory.GetVectorIcon(names[i], 16), new Rect(x, y, 16, 16)); }
                    }
                    var image = new RenderTargetBitmap((int)(460 * dpi / 96), (int)(100 * dpi / 96), dpi, dpi, PixelFormats.Pbgra32); image.Render(v);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using (var file = File.Create(Path.Combine(Output, "icons-" + (int)dpi + ".png"))) encoder.Save(file);
                }
            });
            Record("scale independent centers and real Z, shrink inverse, no-op and invalid factors", () => {
                using (var db = new Database(true, true))
                {
                    ObjectId a, b; using (var tr = db.TransactionManager.StartTransaction()) { var line = new Line(new Point3d(10, 5, 8), new Point3d(20, 5, 12)); var circle = new Circle(new Point3d(50, 50, 25), Vector3d.ZAxis, 3); Add(db, tr, line); Add(db, tr, circle); a = line.ObjectId; b = circle.ObjectId; tr.Commit(); }
                    Check(ObjectScaleService.Scale(db, new[] { a, b }, 2).Scaled == 2, "two scaled");
                    using (var tr = db.TransactionManager.StartTransaction()) { var line = (Line)tr.GetObject(a, OpenMode.ForRead); Near(line.StartPoint.X, 5, "scaled start X"); Near(line.StartPoint.Z, 6, "scaled start Z around true Z=10"); var circle = (Circle)tr.GetObject(b, OpenMode.ForRead); Near(circle.Center.Z, 25, "circle center Z"); Near(circle.Radius, 6, "radius"); }
                    ObjectScaleService.Scale(db, new[] { a, b }, .5); ObjectScaleService.Scale(db, new[] { a, b }, 1);
                    using (var tr = db.TransactionManager.StartTransaction()) { var line = (Line)tr.GetObject(a, OpenMode.ForRead); Near(line.StartPoint.DistanceTo(new Point3d(10, 5, 8)), 0, "inverse restored"); }
                    foreach (double factor in new[] { 0, -1, double.NaN, double.PositiveInfinity }) { bool rejected = false; try { ObjectScaleService.Scale(db, new[] { a }, factor); } catch (ArgumentException) { rejected = true; } Check(rejected, "invalid factor rejected"); }
                }
            });
            Record("block attributes transform once, shared definition untouched, duplicate selection and locked rollback", () => {
                using (var db = new Database(true, true))
                {
                    ObjectId brId, attrId, otherId, layerId; Point3d oldPoint, center; double oldHeight;
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        var table = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite); var definition = new BlockTableRecord { Name = "SCALE_TEST" }; table.Add(definition); tr.AddNewlyCreatedDBObject(definition, true);
                        var line = new Line(Point3d.Origin, new Point3d(10, 10, 0)); definition.AppendEntity(line); tr.AddNewlyCreatedDBObject(line, true);
                        var ad = new AttributeDefinition { Tag = "NAME", TextString = "TEST", Position = new Point3d(2, 3, 0), Height = 2 }; definition.AppendEntity(ad); tr.AddNewlyCreatedDBObject(ad, true);
                        var br = new BlockReference(new Point3d(100, 100, 9), definition.ObjectId) { Rotation = .3 }; Add(db, tr, br); var other = new BlockReference(new Point3d(200, 100, 9), definition.ObjectId); Add(db, tr, other);
                        var attr = new AttributeReference(); attr.SetAttributeFromBlock(ad, br.BlockTransform); br.AttributeCollection.AppendAttribute(attr); tr.AddNewlyCreatedDBObject(attr, true);
                        brId = br.ObjectId; attrId = attr.ObjectId; otherId = other.ObjectId; oldPoint = attr.Position; oldHeight = attr.Height; center = ObjectScaleService.Center(br.GeometricExtents);
                        var layers = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForWrite); var layer = new LayerTableRecord { Name = "LOCKED_SCALE" }; layerId = layers.Add(layer); tr.AddNewlyCreatedDBObject(layer, true); tr.Commit();
                    }
                    var result = ObjectScaleService.Scale(db, new[] { brId, attrId, brId }, 2); Check(result.Scaled == 1 && result.Deduplicated == 1, "duplicate parent attribute handled");
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        var br = (BlockReference)tr.GetObject(brId, OpenMode.ForRead); var attr = (AttributeReference)tr.GetObject(attrId, OpenMode.ForWrite); Near(attr.Position.DistanceTo(oldPoint.TransformBy(Matrix3d.Scaling(2, center))), 0, "attribute position"); Near(attr.Height, oldHeight * 2, "attribute height");
                        Near(ObjectScaleService.Center(br.GeometricExtents).DistanceTo(center), 0, "block center invariant"); Near(((BlockReference)tr.GetObject(otherId, OpenMode.ForRead)).ScaleFactors.X, 1, "other instance unaffected");
                        attr.LayerId = layerId; ((LayerTableRecord)tr.GetObject(layerId, OpenMode.ForWrite)).IsLocked = true; tr.Commit();
                    }
                    result = ObjectScaleService.Scale(db, new[] { brId }, 3); Check(result.Scaled == 0 && result.Failures.Count == 1, "locked attribute skips whole block");
                    using (var tr = db.TransactionManager.StartTransaction()) Near(((BlockReference)tr.GetObject(brId, OpenMode.ForRead)).ScaleFactors.X, 2, "parent unchanged after failure");
                }
            });
        }
        private static ObjectId scaleLine;
        [CommandMethod("XYD_SCALE_FIXTURE")]
        public void ScaleFixture()
        {
            Guard();
            var db = Application.DocumentManager.MdiActiveDocument.Database;
            using (var tr = db.TransactionManager.StartTransaction()) { foreach (ObjectId id in Model(db, tr, OpenMode.ForWrite)) tr.GetObject(id, OpenMode.ForWrite).Erase(); var line = new Line(new Point3d(0, 0, 8), new Point3d(10, 0, 12)); Add(db, tr, line); scaleLine = line.ObjectId; tr.Commit(); }
        }
        [CommandMethod("XYD_SCALE_VERIFY", CommandFlags.NoUndoMarker)]
        public void ScaleVerify() { Record("native scale preserves own center / Z with rotated UCS", () => { using (var tr = scaleLine.Database.TransactionManager.StartTransaction()) { var p = (Line)tr.GetObject(scaleLine, OpenMode.ForRead); Near(p.StartPoint.DistanceTo(new Point3d(-5, 0, 6)), 0, "native scale start"); Near(p.EndPoint.DistanceTo(new Point3d(15, 0, 14)), 0, "native scale end"); } }); }
        [CommandMethod("XYD_SCALE_ORIGINAL", CommandFlags.NoUndoMarker)]
        public void ScaleOriginal() { Record("native undo / cancel / factor one leaves original geometry", () => { using (var tr = scaleLine.Database.TransactionManager.StartTransaction()) { var p = (Line)tr.GetObject(scaleLine, OpenMode.ForRead); Near(p.StartPoint.DistanceTo(new Point3d(0, 0, 8)), 0, "original start"); Near(p.EndPoint.DistanceTo(new Point3d(10, 0, 12)), 0, "original end"); } }); }
        [CommandMethod("XYD_UPGRADE_PRINT", CommandFlags.Session)]
        public void Print()
        {
            Guard();
            var origin = Application.DocumentManager.MdiActiveDocument;
            var list = new List<TitleBlockModel>(); var files = new List<string>(); var hashes = new Dictionary<string, string>();
            try
            {
                Trace("find template");
                string template = Directory.GetFiles(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Autodesk", "AutoCAD 2024"), "*Named Plot Styles.dwt", SearchOption.AllDirectories).First();
                Trace("printers");
                var printer = NativePlotEngine.GetAvailablePrinters().First(p => p.Equals("DWG To PDF.pc3", StringComparison.OrdinalIgnoreCase));
                Trace("media");
                string paper = NativePlotEngine.TryMatchMedia("A4", NativePlotEngine.GetMediaInfoList(printer)); Check(paper != null, "paper available");
                Trace("styles");
                string style = NativePlotEngine.GetAvailableColorPlotStyles().First(p => p.Equals("monochrome.ctb", StringComparison.OrdinalIgnoreCase));
                Trace("fixtures");
                for (int i = 0; i < 2; i++)
                {
                    string folder = Path.Combine(Output, "source" + i); Directory.CreateDirectory(folder); string path = Path.Combine(folder, "same.dwg");
                    using (var db = new Database(i == 0, true))
                    {
                        if (i != 0) { db.ReadDwgFile(template, FileShare.Read, true, ""); db.CloseInput(true); }
                        using (var tr = db.TransactionManager.StartTransaction())
                        {
                            foreach (ObjectId id in Model(db, tr, OpenMode.ForWrite)) tr.GetObject(id, OpenMode.ForWrite).Erase();
                            for (int c = 1; c <= 3; c++) Add(db, tr, new Line(new Point3d(20, c * 35, 0), new Point3d(190, c * 35, 0)) { ColorIndex = c, LineWeight = LineWeight.LineWeight100 });
                            tr.Commit();
                        }
                        Check(db.PlotStyleMode == (i == 0), "fixture plot mode"); db.SaveAs(path, DwgVersion.AC1032);
                    }
                    files.Add(path); hashes[path] = Hash(path);
                    list.Add(new TitleBlockModel { SourceFilePath = path, DocumentName = "same.dwg", SourceHandle = "FIXTURE", LayoutName = "Model", IsModelSpace = true, PageSize = "A4", DrawNum = "MODE" + i, DrawTitle = "TEST", MinPt = Point3d.Origin, MaxPt = new Point3d(210, 297, 0), IsExternalSource = true });
                }
                Record("CTB discovery works and STB direct printing is rejected", () => {
                    using (var db = new Database(false, true)) { db.ReadDwgFile(files[1], FileShare.Read, true, ""); Check(!NativePlotEngine.PlotToPdf(db, list[1], Path.Combine(Output, "rejected.pdf"), printer, paper, style), "STB rejected without conversion"); }
                });
                BatchPlotSession.Start(list, printer, style, Output, new Dictionary<string, string> { { "A4", paper } }, result => {
                    Record("native CTB / STB batch, same basename, conversion, cleanup and source preservation", () => {
                        Check(!result.Cancelled && result.Printed == 2 && result.Failures.Count == 0, "batch result: " + result.Printed + "; " + string.Join(";", result.Failures));
                        foreach (string file in files) Check(Hash(file) == hashes[file], "source hash unchanged");
                        Check(Application.DocumentManager.MdiActiveDocument == origin, "active document restored");
                        Check(!Application.DocumentManager.Cast<Autodesk.AutoCAD.ApplicationServices.Document>().Any(d => d.Name.IndexOf("XYDPlot_", StringComparison.OrdinalIgnoreCase) >= 0), "scratch documents closed");
                    });
                    Quit(origin);
                });
            }
            catch (System.Exception ex) { Record("print setup", () => { throw ex; }); Quit(origin); }
        }
        private static void Quit(Autodesk.AutoCAD.ApplicationServices.Document origin) { if (origin.Name.IndexOf("LiveVerification_", StringComparison.OrdinalIgnoreCase) >= 0) return; origin.SendStringToExecute("_.QUIT\n" + (System.Diagnostics.Process.GetCurrentProcess().ProcessName.IndexOf("accore", StringComparison.OrdinalIgnoreCase) >= 0 ? "_Y" : "_N") + "\n", true, false, false); }
        private static string Hash(string file) { using (var input = File.OpenRead(file)) using (var hash = System.Security.Cryptography.SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(input)); }
        private static void Trace(string text) { File.AppendAllText(Path.Combine(Output, "trace.txt"), text + "\n"); }
    }
}
