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
    public class UpgradeAdditionalTests
    {
        private static string Root { get { return File.ReadAllText(Path.Combine(Path.GetDirectoryName(typeof(UpgradeAdditionalTests).Assembly.Location), "test-root.txt")).Trim(); } }
        private static string Output { get { return Path.Combine(Root, "artifacts", "upgrade-regression"); } }
        private static void Check(bool ok, string why) { if (!ok) throw new InvalidOperationException(why); }
        private static void Record(string name, Action action) { try { action(); File.AppendAllText(Path.Combine(Output, "additional-results.txt"), "PASS " + name + "\n"); } catch (System.Exception ex) { File.AppendAllText(Path.Combine(Output, "additional-results.txt"), "FAIL " + name + " " + ex + "\n"); } }
        private static BlockTableRecord Model(Database db, Transaction tr, OpenMode mode) { return (BlockTableRecord)tr.GetObject(((BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead))[BlockTableRecord.ModelSpace], mode); }
        private static void Guard() { if (Application.DocumentManager.MdiActiveDocument.Name.IndexOf("LiveVerification_", StringComparison.OrdinalIgnoreCase) < 0) throw new InvalidOperationException("Dedicated temporary drawing required"); }
        [CommandMethod("XYD_UPGRADE_UI")]
        public void Ui() { Guard(); new RibbonBuilder().ForceLoadUI(); }
        [CommandMethod("XYD_UPGRADE_REFERENCES")]
        public void References()
        {
            Guard();
            Record("snapshot resolves relative xref and raster paths without changing source", () => {
                string folder = Path.Combine(Output, "relative-assets"); Directory.CreateDirectory(folder);
                string xref = Path.Combine(folder, "reference.dwg"), raster = Path.Combine(folder, "reference.png"), snapshot = Path.Combine(folder, "snapshot.dwg");
                using (var reference = new Database(true, true)) reference.SaveAs(xref, DwgVersion.AC1032);
                var bitmap = new System.Windows.Media.Imaging.WriteableBitmap(2, 2, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null);
                bitmap.WritePixels(new System.Windows.Int32Rect(0, 0, 2, 2), new byte[] { 0,0,255,255, 0,255,0,255, 255,0,0,255, 0,0,0,255 }, 8, 0);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap)); using (var stream = File.Create(raster)) encoder.Save(stream);
                string sourceFile = Path.Combine(folder, "source.dwg");
                using (var seed = new Database(true, true)) seed.SaveAs(sourceFile, DwgVersion.AC1032);
                using (var source = new Database(false, true))
                {
                    source.ReadDwgFile(sourceFile, FileShare.Read, true, ""); source.CloseInput(true);
                    var previousWorkingDatabase = HostApplicationServices.WorkingDatabase;
                    HostApplicationServices.WorkingDatabase = source;
                    try
                    {
                        var xrefId = source.AttachXref(xref, "REFERENCE");
                        using (var tr = source.TransactionManager.StartTransaction())
                        {
                            var btr = (BlockTableRecord)tr.GetObject(xrefId, OpenMode.ForWrite); btr.PathName = "reference.dwg";
                            var br = new BlockReference(Point3d.Origin, xrefId); Model(source, tr, OpenMode.ForWrite).AppendEntity(br); tr.AddNewlyCreatedDBObject(br, true);
                            var imageId = RasterImageDef.CreateImageDictionary(source); var imageDictionary = (DBDictionary)tr.GetObject(imageId, OpenMode.ForWrite);
                            var image = new RasterImageDef();
                            try { image.SourceFileName = raster; } catch (System.Exception ex) { throw new InvalidOperationException("fixture absolute raster path: " + raster, ex); }
                            imageDictionary.SetAt("REFERENCE", image); tr.AddNewlyCreatedDBObject(image, true);
                            image.Unload(false);
                            try { image.SourceFileName = ".\\reference.png"; } catch (System.Exception ex) { throw new InvalidOperationException("fixture relative raster path, database=" + source.Filename, ex); }
                            image.Load();
                            var rasterEntity = new RasterImage { ImageDefId = image.ObjectId, Orientation = new CoordinateSystem3d(Point3d.Origin, Vector3d.XAxis * 10, Vector3d.YAxis * 10), ShowImage = true };
                            Model(source, tr, OpenMode.ForWrite).AppendEntity(rasterEntity); tr.AddNewlyCreatedDBObject(rasterEntity, true); RasterImage.EnableReactors(true); rasterEntity.AssociateRasterDef(image);
                            tr.Commit();
                        }
                        BatchPlotSession.SaveSnapshot(source, folder, snapshot);
                        using (var tr = source.TransactionManager.StartTransaction()) Check(((BlockTableRecord)tr.GetObject(xrefId, OpenMode.ForRead)).PathName == "reference.dwg", "source relative path unchanged");
                    }
                    finally { HostApplicationServices.WorkingDatabase = previousWorkingDatabase; }
                }
                using (var copy = new Database(false, true))
                {
                    copy.ReadDwgFile(snapshot, FileShare.Read, true, ""); copy.CloseInput(true);
                    using (var tr = copy.TransactionManager.StartTransaction())
                    {
                        var table = (BlockTable)tr.GetObject(copy.BlockTableId, OpenMode.ForRead); Check(Path.IsPathRooted(((BlockTableRecord)tr.GetObject(table["REFERENCE"], OpenMode.ForRead)).PathName), "absolute xref in copy");
                        var images = (DBDictionary)tr.GetObject(RasterImageDef.GetImageDictionary(copy), OpenMode.ForRead); Check(((RasterImageDef)tr.GetObject(images.GetAt("REFERENCE"), OpenMode.ForRead)).SourceFileName == raster, "absolute raster in copy");
                    }
                }
            });
        }
        [CommandMethod("XYD_UPGRADE_ADDITIONAL", CommandFlags.Session)]
        public void Run()
        {
            Guard(); var origin = Application.DocumentManager.MdiActiveDocument; var db = origin.Database;
            Record("dynamic block independent instance scaling", () => {
                using (var library = new Database(false, true)) using (var target = new Database(true, true))
                {
                    library.ReadDwgFile(Path.Combine(Root, "Blocks", "XYD_Blocks.dwg"), FileShare.Read, true, ""); library.CloseInput(true); target.Insert("LIBRARY", library, false);
                    ObjectId blockId; Point3d center; double oldX;
                    using (var tr = target.TransactionManager.StartTransaction())
                    {
                        var table = (BlockTable)tr.GetObject(target.BlockTableId, OpenMode.ForRead); var def = table.Cast<ObjectId>().Select(id => (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead)).First(r => r.IsDynamicBlock);
                        var br = new BlockReference(new Point3d(50, 60, 17), def.ObjectId); Model(target, tr, OpenMode.ForWrite).AppendEntity(br); tr.AddNewlyCreatedDBObject(br, true); blockId = br.ObjectId; center = ObjectScaleService.Center(br.GeometricExtents); oldX = br.ScaleFactors.X; tr.Commit();
                    }
                    Check(ObjectScaleService.Scale(target, new[] { blockId }, .5).Scaled == 1, "dynamic instance scaled");
                    using (var tr = target.TransactionManager.StartTransaction()) { var br = (BlockReference)tr.GetObject(blockId, OpenMode.ForRead); Check(br.IsDynamicBlock && Math.Abs(br.ScaleFactors.X - oldX * .5) < 1e-8, "dynamic identity and scale"); Check(ObjectScaleService.Center(br.GeometricExtents).DistanceTo(center) < 1e-7, "dynamic center invariant"); }
                }
            });
            References();
            string printer = NativePlotEngine.GetAvailablePrinters().First(p => p.Equals("DWG To PDF.pc3", StringComparison.OrdinalIgnoreCase));
            string paper = NativePlotEngine.TryMatchMedia("A4", NativePlotEngine.GetMediaInfoList(printer));
            string mono = NativePlotEngine.GetAvailableColorPlotStyles().First(p => p.Equals("monochrome.ctb", StringComparison.OrdinalIgnoreCase));
            var sheets = new List<TitleBlockModel>(); bool sourceMode = db.PlotStyleMode; string sourceName = db.Filename;
            using (origin.LockDocument()) using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = Model(db, tr, OpenMode.ForWrite);
                foreach (ObjectId id in ms) tr.GetObject(id, OpenMode.ForWrite).Erase();
                for (int i = 1; i <= 3; i++) { var line = new Line(new Point3d(20, 30 * i, 0), new Point3d(190, 30 * i, 0)) { ColorIndex = i }; ms.AppendEntity(line); tr.AddNewlyCreatedDBObject(line, true); }
                foreach (DBDictionaryEntry entry in (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead))
                {
                    var layout = (Layout)tr.GetObject(entry.Value, OpenMode.ForRead);
                    if (!layout.ModelType) { var ps = (BlockTableRecord)tr.GetObject(layout.BlockTableRecordId, OpenMode.ForWrite); foreach (ObjectId id in ps) { var entity = tr.GetObject(id, OpenMode.ForRead) as Entity; if (!(entity is Viewport)) { entity.UpgradeOpen(); entity.Erase(); } } for (int i = 1; i <= 3; i++) { var line = new Line(new Point3d(20, 30 * i, 0), new Point3d(190, 30 * i, 0)) { ColorIndex = i }; ps.AppendEntity(line); tr.AddNewlyCreatedDBObject(line, true); } }
                    if (layout.ModelType || !sheets.Any(s => !s.IsModelSpace)) sheets.Add(new TitleBlockModel { DocumentName = origin.Name, SourceFilePath = origin.Name, SourceHandle = "MEMORY", LayoutName = layout.LayoutName, IsModelSpace = layout.ModelType, DrawNum = layout.ModelType ? "UNSAVED" : "LAYOUT", DrawTitle = "TEST", PageSize = "A4", MinPt = Point3d.Origin, MaxPt = new Point3d(210, 297, 0) });
                }
                tr.Commit();
            }
            BatchPlotSession.Start(sheets, printer, mono, Output, new Dictionary<string, string> { { "A4", paper } }, r => {
                Record("unsaved live model and layout print from copies, origin mode / name preserved", () => { Check(r.Printed == sheets.Count && r.Failures.Count == 0, string.Join(";", r.Failures)); Check(db.PlotStyleMode == sourceMode && db.Filename == sourceName, "original mode and identity"); using (var tr = db.TransactionManager.StartTransaction()) Check(Model(db, tr, OpenMode.ForRead).Cast<ObjectId>().Count() == 3, "unsaved source entities retained"); });
                // A real CTB chosen by the user can intentionally preserve colors.
                string color = NativePlotEngine.GetAvailableColorPlotStyles().First(p => p.Equals("acad.ctb", StringComparison.OrdinalIgnoreCase));
                var colored = BatchPlotSession.CopySheet(sheets.First(b => b.IsModelSpace)); colored.DrawNum = "COLOR";
                BatchPlotSession.Start(new List<TitleBlockModel> { colored }, printer, color, Output, new Dictionary<string, string> { { "A4", paper } }, c => {
                    Record("user selected color CTB is honored instead of forcing monochrome", () => Check(c.Printed == 1 && c.Failures.Count == 0, string.Join(";", c.Failures)));
                    var cancelled = BatchPlotSession.CopySheet(colored); cancelled.DrawNum = "CANCELLED";
                    BatchPlotSession.Start(new List<TitleBlockModel> { cancelled }, printer, mono, Output, new Dictionary<string, string> { { "A4", paper } }, stop => {
                        Record("cancelled queue cleans up and restores original drawing", () => { Check(stop.Cancelled && stop.Printed == 0 && !File.Exists(Path.Combine(Output, "CANCELLED_TEST.pdf")), "cancel without PDF"); Check(Application.DocumentManager.MdiActiveDocument == origin && !BatchPlotSession.IsRunning, "original restored"); });
                        var failed = BatchPlotSession.CopySheet(colored); failed.DrawNum = "KEEP";
                        string oldPdf = Path.Combine(Output, "KEEP_TEST.pdf"); File.Copy(Path.Combine(Output, "COLOR_TEST.pdf"), oldPdf, true); byte[] before = File.ReadAllBytes(oldPdf);
                        BatchPlotSession.Start(new List<TitleBlockModel> { failed }, printer, mono, Output, new Dictionary<string, string> { { "A4", "__MISSING_PAPER__" } }, broken => {
                            Record("failed print preserves previous PDF and cleans up", () => { Check(broken.Printed == 0 && broken.Failures.Count > 0, "expected failure"); Check(before.SequenceEqual(File.ReadAllBytes(oldPdf)), "previous PDF intact"); Check(!BatchPlotSession.IsRunning && Application.DocumentManager.MdiActiveDocument == origin, "failed queue cleaned up"); });
                            File.WriteAllText(Path.Combine(Output, "gui-complete.txt"), "complete");
                        });
                    });
                    BatchPlotSession.RequestCancel();
                });
            });
        }
    }
}
