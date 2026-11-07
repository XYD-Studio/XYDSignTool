using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace XYDSignTool
{
    public sealed class BatchPlotResult
    {
        public int Printed { get; set; }
        public bool Cancelled { get; set; }
        public List<string> Failures { get; } = new List<string>();
    }
    // One active foreground queue. Native conversion is executed by AutoCAD's
    // command loop, followed by a Session command; no timer or SendKeys races.
    public sealed class BatchPlotSession
    {
        private sealed class Job { public string Source; public List<TitleBlockModel> Sheets = new List<TitleBlockModel>(); }
        private static BatchPlotSession current;
        public static bool IsRunning { get { return current != null; } }
        private readonly Queue<Job> jobs = new Queue<Job>();
        private readonly Document original;
        private readonly string printer, ctb, output, root;
        private readonly Dictionary<string, string> papers;
        private readonly Action<BatchPlotResult> completed;
        private readonly BatchPlotResult result = new BatchPlotResult();
        private Document scratch;
        private Job active;
        private string scratchFolder;
        private bool cancelled;
        private bool cancelIdleRegistered;

        private BatchPlotSession(List<TitleBlockModel> sheets, string printerName, string style, string folder, Dictionary<string, string> mapping, Action<BatchPlotResult> callback)
        {
            original = Application.DocumentManager.MdiActiveDocument;
            printer = printerName; ctb = style; output = folder; papers = new Dictionary<string, string>(mapping); completed = callback;
            root = Path.Combine(Path.GetTempPath(), "XYDPlot_" + Guid.NewGuid().ToString("N"));
            var grouped = new Dictionary<string, Job>(StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var sheet in sheets)
            {
                if (sheet.IsImportedSource) throw new InvalidOperationException("JSON 行没有有效的打印来源。");
                string source = sheet.SourceFilePath ?? sheet.DocumentName;
                if (!sheet.ObjId.IsNull && sheet.ObjId.IsValid)
                    foreach (Document doc in Application.DocumentManager) if (doc.Database == sheet.ObjId.Database) { source = doc.Name; break; }
                source = SourceKey(source);
                if (!seen.Add(source + "|" + sheet.LayoutName + "|" + (sheet.SourceHandle ?? sheet.ObjId.ToString()))) continue;
                Job job; if (!grouped.TryGetValue(source, out job)) { job = new Job { Source = source }; grouped.Add(source, job); jobs.Enqueue(job); }
                job.Sheets.Add(CopySheet(sheet));
            }
        }
        public static void Start(List<TitleBlockModel> sheets, string printer, string ctb, string outputDirectory, Dictionary<string, string> papers, Action<BatchPlotResult> completed = null)
        {
            if (current != null) throw new InvalidOperationException("已有批量打印任务正在运行。");
            if (NativePlotEngine.ResolveColorPlotStyle(ctb) == null) throw new InvalidOperationException("找不到选定的 CTB：" + ctb);
            var task = new BatchPlotSession(sheets, printer, ctb, outputDirectory, papers, completed); current = task;
            try { Directory.CreateDirectory(task.root); task.Advance(); }
            catch { task.Finish(); throw; }
        }
        public static string SourceKey(string source)
        {
            if (string.IsNullOrWhiteSpace(source)) throw new InvalidOperationException("图纸来源为空。");
            return Path.IsPathRooted(source) ? Path.GetFullPath(source) : source;
        }
        public static TitleBlockModel CopySheet(TitleBlockModel b)
        {
            return new TitleBlockModel { DocumentName = b.DocumentName, SourceFilePath = b.SourceFilePath, SourceHandle = b.SourceHandle,
                LayoutName = b.LayoutName, IsModelSpace = b.IsModelSpace, DrawNum = b.DrawNum, DrawTitle = b.DrawTitle, PageSize = b.PageSize,
                MinPt = b.MinPt, MaxPt = b.MaxPt, DrawScale = b.DrawScale, Version = b.Version, Date = b.Date, Remarks = b.Remarks };
        }
        private static Document FindDocument(string path)
        {
            foreach (Document doc in Application.DocumentManager) if (SourceKey(doc.Name).Equals(path, StringComparison.OrdinalIgnoreCase)) return doc;
            return null;
        }
        public static void SaveSnapshot(Database source, string sourceDirectory, string target)
        {
            using (var snapshot = source.Wblock())
            {
                ResolveReferences(snapshot, source, sourceDirectory);
                snapshot.SaveAs(target, DwgVersion.Current);
            }
        }
        private static string ResolveReference(string name, Database source, string folder)
        {
            if (string.IsNullOrWhiteSpace(name) || Path.IsPathRooted(name)) return name;
            string file = Path.GetFullPath(Path.Combine(folder, name)); if (File.Exists(file)) return file;
            try { return HostApplicationServices.Current.FindFile(name, source, FindFileHint.Default); }
            catch { throw new FileNotFoundException("无法解析相对引用：" + name); }
        }
        private static void ResolveReferences(Database copy, Database source, string folder)
        {
            using (var tr = copy.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in (BlockTable)tr.GetObject(copy.BlockTableId, OpenMode.ForRead))
                {
                    var block = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
                    if (block.IsFromExternalReference && !string.IsNullOrWhiteSpace(block.PathName))
                    { string path = ResolveReference(block.PathName, source, folder); block.UpgradeOpen(); block.PathName = path; }
                }
                var nod = (DBDictionary)tr.GetObject(copy.NamedObjectsDictionaryId, OpenMode.ForRead);
                foreach (string key in new[] { "ACAD_IMAGE_DICT", "ACAD_PDFDEFINITIONS", "ACAD_DGNDEFINITIONS", "ACAD_DWFDEFINITIONS" })
                {
                    if (!nod.Contains(key)) continue;
                    var dictionary = tr.GetObject(nod.GetAt(key), OpenMode.ForRead) as DBDictionary; if (dictionary == null) continue;
                    foreach (DBDictionaryEntry entry in dictionary)
                    {
                        var obj = tr.GetObject(entry.Value, OpenMode.ForRead); var image = obj as RasterImageDef; var underlay = obj as UnderlayDefinition;
                        if (image != null && !string.IsNullOrWhiteSpace(image.SourceFileName)) { string name = ResolveReference(image.SourceFileName, source, folder); image.UpgradeOpen(); image.SourceFileName = name; }
                        if (underlay != null && !string.IsNullOrWhiteSpace(underlay.SourceFileName)) { string name = ResolveReference(underlay.SourceFileName, source, folder); underlay.UpgradeOpen(); underlay.SourceFileName = name; }
                    }
                }
                tr.Commit();
            }
        }
        private void Advance()
        {
            while (jobs.Count > 0 && !cancelled)
            {
                active = jobs.Dequeue();
                try
                {
                    Log("准备打印副本：" + active.Source);
                    scratchFolder = Path.Combine(root, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(scratchFolder);
                    string file = Path.Combine(scratchFolder, Path.GetFileName(active.Source));
                    var sourceDoc = FindDocument(active.Source);
                    string folder = Path.GetDirectoryName(Path.GetFullPath(active.Source));
                    if (sourceDoc != null)
                    {
                        using (sourceDoc.LockDocument()) SaveSnapshot(sourceDoc.Database, folder, file);
                    }
                    else using (var source = new Database(false, true))
                    {
                        source.ReadDwgFile(active.Source, FileShare.Read, true, ""); source.CloseInput(true); SaveSnapshot(source, folder, file);
                    }
                    Log("打开打印副本：" + file);
                    scratch = Application.DocumentManager.Open(file, false); Application.DocumentManager.MdiActiveDocument = scratch;
                    scratch.CommandCancelled += CommandCancelled;
                    string commands = scratch.Database.PlotStyleMode ? "" : "(command \"_.CONVERTPSTYLES\")\n";
                    scratch.SendStringToExecute(commands + "XYD_BATCHPLOT_RENDER\n", true, false, false);
                    Log("已排队：" + (scratch.Database.PlotStyleMode ? "CTB 出图" : "STB 转换后出图"));
                    return;
                }
                catch (System.Exception ex) { result.Failures.Add(active.Source + "：" + ex.Message); CloseScratch(); }
            }
            Finish();
        }
        private void CommandCancelled(object sender, CommandEventArgs e) { Cancel(); }
        private void Cancel()
        {
            cancelled = true;
            // Esc can discard the queued continuation itself. Clean up after
            // AutoCAD returns to idle even when that command never executes.
            if (!cancelIdleRegistered) { Application.Idle += CancelOnIdle; cancelIdleRegistered = true; }
        }
        private void CancelOnIdle(object sender, EventArgs e)
        {
            if (Convert.ToInt32(Application.GetSystemVariable("CMDACTIVE")) != 0) return;
            Application.Idle -= CancelOnIdle; cancelIdleRegistered = false;
            if (ReferenceEquals(current, this) && cancelled) Finish();
        }
        public static void RequestCancel() { if (current != null) current.Cancel(); }
        [CommandMethod("XYD_BATCHPRINT_CANCEL", CommandFlags.Session)]
        public static void CancelCurrent() { RequestCancel(); }
        [CommandMethod("XYD_BATCHPLOT_RENDER", CommandFlags.Session)]
        public static void RenderCurrent()
        {
            var task = current; if (task == null) return;
            try
            {
                if (task.cancelled) return;
                if (task.scratch == null || Application.DocumentManager.MdiActiveDocument != task.scratch) throw new InvalidOperationException("打印副本文档不匹配。");
                if (!task.scratch.Database.PlotStyleMode) throw new InvalidOperationException("STB→CTB 转换未完成，已阻止该文件输出。");
                using (task.scratch.LockDocument()) foreach (var sheet in task.active.Sheets)
                {
                    if (task.cancelled) break;
                    string size = TitleBlockRecognitionSettings.ExtractPageSizeFromBlockName(sheet.PageSize); if (string.IsNullOrWhiteSpace(size)) size = sheet.PageSize;
                    string paper; if (!task.papers.TryGetValue(size, out paper)) throw new InvalidOperationException("缺少纸张映射：" + size);
                    string file = Path.Combine(task.output, SafeName(sheet.DrawNum) + "_" + SafeName(sheet.DrawTitle) + ".pdf");
                    string part = Path.Combine(task.output, ".xyd_" + Guid.NewGuid().ToString("N") + ".pdf");
                    try
                    {
                        if (NativePlotEngine.PlotToPdf(task.scratch.Database, sheet, part, task.printer, paper, task.ctb))
                        {
                            // Keep an existing PDF intact if replacement fails.
                            if (File.Exists(file)) File.Replace(part, file, null); else File.Move(part, file);
                            task.result.Printed++;
                        }
                        else task.result.Failures.Add(task.active.Source + " / " + sheet.DrawNum + "：打印失败，详见命令行。");
                    }
                    finally { if (File.Exists(part)) File.Delete(part); }
                }
            }
            catch (System.Exception ex) { task.result.Failures.Add((task.active == null ? "" : task.active.Source) + "：" + ex.Message); }
            finally { task.CloseScratch(); task.Advance(); }
        }
        private static string SafeName(string text) { foreach (char c in Path.GetInvalidFileNameChars()) text = (text ?? "").Replace(c, '_'); return text ?? ""; }
        private static void Log(string message) { Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage("\n[XYD 打印] " + message); }
        private void CloseScratch()
        {
            if (scratch != null)
            {
                var doc = scratch; scratch = null; doc.CommandCancelled -= CommandCancelled;
                try { if (original != null && FindDocument(SourceKey(original.Name)) == original) Application.DocumentManager.MdiActiveDocument = original; doc.CloseAndDiscard(); }
                catch (System.Exception ex) { result.Failures.Add("关闭打印副本失败：" + ex.Message); }
            }
        }
        private void Finish()
        {
            if (!ReferenceEquals(current, this)) return;
            if (cancelIdleRegistered) { Application.Idle -= CancelOnIdle; cancelIdleRegistered = false; }
            CloseScratch(); result.Cancelled = cancelled;
            try { if (original != null && FindDocument(SourceKey(original.Name)) == original) Application.DocumentManager.MdiActiveDocument = original; }
            catch { }
            try { if (Directory.Exists(root)) Directory.Delete(root, true); }
            catch (System.Exception ex) { result.Failures.Add("临时打印目录清理失败：" + ex.Message); }
            current = null;
            string message = "批量打印" + (cancelled ? "已取消" : "结束") + "：成功 " + result.Printed + " 份 PDF，问题 " + result.Failures.Count + " 项。";
            if (original != null) original.Editor.WriteMessage("\n" + message + (result.Failures.Count == 0 ? "" : "\n" + string.Join("\n", result.Failures)));
            if (completed != null) completed(result); else Application.ShowAlertDialog(message + "\n源 DWG 未转换或保存，详细信息见命令行。");
        }
    }
}
