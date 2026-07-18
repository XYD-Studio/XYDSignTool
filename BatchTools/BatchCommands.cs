using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Microsoft.Win32;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Document = Autodesk.AutoCAD.ApplicationServices.Document;

namespace XYDSignTool
{
    public class BatchCommands
    {
        private const string FolderPickerPlaceholder = "\u200B";

        // ==================== 1. 动态生成 CAD 目录表格 ====================
        [CommandMethod("XYD_DIRECTORY", CommandFlags.Session)]
        public void DirectoryPanel()
        {
            RunDirectoryPanel();
        }

        [CommandMethod("XYD_DIRALL", CommandFlags.Session)]
        public void ExtractDirAll()
        {
            RunDirectoryPanel();
        }

        // ==================== 2. 动态导出 JSON ====================
        [CommandMethod("XYD_DIRJSON", CommandFlags.Session)]
        public void ExportDirJson()
        {
            RunDirectoryPanel();
        }

        // ==================== 3. 动态导出 Excel ====================
        [CommandMethod("XYD_DIREXCEL", CommandFlags.Session)]
        public void ExportDirExcel()
        {
            RunDirectoryPanel();
        }

        [CommandMethod("XYD_TITLEBLOCK_RULES", CommandFlags.Session)]
        public void ConfigureTitleBlockRecognition()
        {
            TitleBlockRecognitionWindow window = new TitleBlockRecognitionWindow();
            Application.ShowModalWindow(window);
        }

        private void RunDirectoryPanel()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;

            var allBlocks = GatherTitleBlocks(ed, false, out _);
            if (allBlocks == null || allBlocks.Count == 0) return;

            DirectoryEditorWindow dirWin = new DirectoryEditorWindow(allBlocks, "目录中心");

            dirWin.GenerateInDrawingRequested += () =>
            {
                try
                {
                    dirWin.Hide();
                    PromptPointResult ppr = ed.GetPoint("\n请指定目录表格的左上角插入点: ");
                    if (ppr.Status == PromptStatus.OK)
                    {
                        using (DocumentLock loc = doc.LockDocument())
                        {
                            DrawDynamicDirectoryTable(doc.Database, ppr.Value, new List<TitleBlockModel>(dirWin.Items), dirWin.FinalColumns);
                        }
                        WriteBatchMessage("\n[成功] 图纸动态目录生成完毕。");
                    }
                }
                catch (System.Exception ex) { WriteBatchMessage($"\n[错误] {ex.Message}"); }
                finally
                {
                    dirWin.Show();
                    dirWin.Activate();
                }
            };

            dirWin.ImportJsonRequested += () => ImportDirectoryJson(dirWin);
            dirWin.ExportExcelRequested += () => ExportDirectoryExcel(new List<TitleBlockModel>(dirWin.Items), dirWin.FinalColumns);
            dirWin.ExportJsonRequested += () => ExportDirectoryJson(new List<TitleBlockModel>(dirWin.Items), dirWin.FinalColumns);

            Application.ShowModalWindow(dirWin);
        }

        // ==================== 4. 智能预检批量打印 (保留上个版本的完美代码) ====================
        [CommandMethod("XYD_BATCHPRINT", CommandFlags.Session)]
        public void BatchPrintAll()
        {
            Document activeDoc = Application.DocumentManager.MdiActiveDocument;
            if (activeDoc == null) return;
            Editor ed = activeDoc.Editor;

            var allBlocks = GatherTitleBlocks(ed, true, out List<string> externalFilesToOpen);
            if (allBlocks == null || allBlocks.Count == 0) return;
            if (!ValidateBatchPrintBlocks(allBlocks)) return;

            List<string> requiredSizes = new List<string>();
            foreach (var b in allBlocks)
            {
                string pName = GetPaperName(b.PageSize);
                if (!requiredSizes.Contains(pName)) requiredSizes.Add(pName);
            }

            var availablePrinters = NativePlotEngine.GetAvailablePrinters();
            var availableStyles = NativePlotEngine.GetAvailablePlotStyles();
            Dictionary<string, string> finalPaperMapping = new Dictionary<string, string>();
            string defaultPrinter = availablePrinters.Contains("AutoCAD PDF (General Documentation).pc3") ? "AutoCAD PDF (General Documentation).pc3" : (availablePrinters.Count > 0 ? availablePrinters[0] : "");
            string defaultCtb = availableStyles.Contains("acad.ctb") ? "acad.ctb" : (availableStyles.Count > 0 ? availableStyles[0] : "");

            PlotPreflightWindow pfw = new PlotPreflightWindow(availablePrinters, defaultPrinter, availableStyles, defaultCtb, requiredSizes);
            Application.ShowModalWindow(pfw);
            if (!pfw.IsConfirmed) { WriteBatchMessage("\n[拦截] 已取消批量打印任务。"); return; }

            defaultPrinter = pfw.FinalPrinterName;
            defaultCtb = pfw.FinalStyleSheet;
            foreach (var kvp in pfw.FinalMapping) finalPaperMapping[kvp.Key] = kvp.Value;

            string outDir = SelectPdfOutputDirectory(defaultPrinter, defaultCtb);
            if (string.IsNullOrEmpty(outDir)) return;

            outDir = outDir.Replace("\\", "/");
            if (!outDir.EndsWith("/")) outDir += "/";

            WriteBatchMessage("\n====== 开始原生高级批量打印 ======");
            int successCount = 0;

            foreach (Document openDoc in Application.DocumentManager)
            {
                List<TitleBlockModel> blocksForOpenDoc = allBlocks.Where(b => b.DocumentName == openDoc.Name).ToList();
                if (blocksForOpenDoc.Count == 0) continue;

                Application.DocumentManager.MdiActiveDocument = openDoc;
                using (DocumentLock loc = openDoc.LockDocument())
                {
                    foreach (var b in blocksForOpenDoc)
                    {
                        string safeTitle = SafeStr(b.DrawTitle).Replace("/", "_").Replace(":", "_").Replace("\\", "_").Replace("*", "_").Replace("?", "_").Replace("\"", "_").Replace("<", "_").Replace(">", "_").Replace("|", "_");
                        string safeNum = SafeStr(b.DrawNum).Replace("/", "_").Replace("\\", "_").Replace(":", "_");
                        string pdfName = $"{safeNum}_{safeTitle}.pdf";
                        string requiredPaper = GetPaperName(b.PageSize);
                        string mappedCanonicalPaper = finalPaperMapping[requiredPaper];

                        WriteBatchMessage($"\n正在打印: {pdfName}...");
                        if (NativePlotEngine.PlotToPdf(openDoc.Database, b, outDir + pdfName, defaultPrinter, mappedCanonicalPaper, defaultCtb)) successCount++;
                    }
                }
            }

            foreach (string filePath in externalFilesToOpen)
            {
                Document tempDoc = Application.DocumentManager.Open(filePath, true);
                Application.DocumentManager.MdiActiveDocument = tempDoc;

                using (DocumentLock loc = tempDoc.LockDocument())
                {
                    foreach (var b in allBlocks)
                    {
                        if (b.DocumentName == Path.GetFileName(filePath))
                        {
                            string safeTitle = SafeStr(b.DrawTitle).Replace("/", "_").Replace(":", "_").Replace("\\", "_").Replace("*", "_").Replace("?", "_").Replace("\"", "_").Replace("<", "_").Replace(">", "_").Replace("|", "_");
                            string safeNum = SafeStr(b.DrawNum).Replace("/", "_").Replace("\\", "_").Replace(":", "_");
                            string pdfName = $"{safeNum}_{safeTitle}.pdf";
                            string requiredPaper = GetPaperName(b.PageSize);
                            string mappedCanonicalPaper = finalPaperMapping[requiredPaper];

                            WriteBatchMessage($"\n正在打印: {pdfName}...");
                            if (NativePlotEngine.PlotToPdf(tempDoc.Database, b, outDir + pdfName, defaultPrinter, mappedCanonicalPaper, defaultCtb)) successCount++;
                        }
                    }
                }
                tempDoc.CloseAndDiscard();
            }

            Application.DocumentManager.MdiActiveDocument = activeDoc;
            Application.ShowAlertDialog($"批量打印结束！\n成功生成 {successCount} 份 PDF。");
        }

        // ==================== 辅助私有方法 ====================

        private List<TitleBlockModel> GatherTitleBlocks(Editor ed, bool includeCover, out List<string> externalFiles)
        {
            externalFiles = new List<string>();
            List<TitleBlockModel> allData = new List<TitleBlockModel>();

            ed.WriteMessage("\n正在扫描当前激活图纸...");
            AddTitleBlocksFromDatabase(allData, Application.DocumentManager.MdiActiveDocument.Database, Application.DocumentManager.MdiActiveDocument.Name, includeCover, ed);

            while (true)
            {
                PromptKeywordOptions pkoMerge = new PromptKeywordOptions("\n是否添加其他图纸进行处理？ [选择本地DWG(D) / 提取其他已打开标签页(O) / 结束添加并下一步(N)] <N>:");
                pkoMerge.Keywords.Add("D"); pkoMerge.Keywords.Add("O"); pkoMerge.Keywords.Add("N"); pkoMerge.Keywords.Default = "N";

                PromptResult prMerge = ed.GetKeywords(pkoMerge);
                if (prMerge.Status != PromptStatus.OK || prMerge.StringResult == "N") break;

                if (prMerge.StringResult == "D")
                {
                    OpenFileDialog ofd = new OpenFileDialog { Filter = "CAD图纸 (*.dwg)|*.dwg", Title = "选择外部图纸", Multiselect = true };
                    if (ofd.ShowDialog() == true)
                    {
                        foreach (string fileName in ofd.FileNames)
                        {
                            Document openDoc = FindOpenedDocument(fileName);
                            if (openDoc != null)
                            {
                                AddTitleBlocksFromDatabase(allData, openDoc.Database, openDoc.Name, includeCover, ed);
                            }
                            else
                            {
                                int ignoredObjectCount;
                                allData.AddRange(TitleBlockExtractor.ScanExternalDwg(fileName, includeCover, out ignoredObjectCount));
                                ReportIgnoredObjects(ed, fileName, ignoredObjectCount);
                                externalFiles.Add(fileName);
                            }
                        }
                    }
                }
                else if (prMerge.StringResult == "O")
                {
                    foreach (Document openedDoc in Application.DocumentManager)
                    {
                        if (openedDoc != Application.DocumentManager.MdiActiveDocument)
                            AddTitleBlocksFromDatabase(allData, openedDoc.Database, openedDoc.Name, includeCover, ed);
                    }
                }
            }

            if (allData.Count == 0) { ed.WriteMessage("\n未找到任何图框数据。"); return null; }
            allData.Sort((a, b) => string.Compare(a.DrawNum, b.DrawNum, StringComparison.OrdinalIgnoreCase));
            return allData;
        }

        private void AddTitleBlocksFromDatabase(List<TitleBlockModel> allData, Database db, string documentName, bool includeCover, Editor ed)
        {
            int ignoredObjectCount;
            allData.AddRange(TitleBlockExtractor.ScanDatabase(db, documentName, includeCover, out ignoredObjectCount));
            ReportIgnoredObjects(ed, documentName, ignoredObjectCount);
        }

        private void ReportIgnoredObjects(Editor ed, string documentName, int ignoredObjectCount)
        {
            if (ignoredObjectCount <= 0) return;

            string displayName = Path.GetFileName(documentName);
            ed.WriteMessage($"\n[警告] {displayName} 中有 {ignoredObjectCount} 个异常块对象已跳过。建议对该图执行 AUDIT（核查）并修复错误。");
        }

        private Document FindOpenedDocument(string path)
        {
            foreach (Document doc in Application.DocumentManager)
            {
                if (doc.Name.Equals(path, StringComparison.OrdinalIgnoreCase)) return doc;
            }
            return null;
        }

        private string SafeStr(string val) { return string.IsNullOrEmpty(val) ? "" : val; }

        private void WriteBatchMessage(string message)
        {
            try
            {
                Document doc = Application.DocumentManager.MdiActiveDocument;
                if (doc != null && doc.Editor != null) doc.Editor.WriteMessage(message);
            }
            catch { }
        }

        private string GetPaperName(string size)
        {
            if (string.IsNullOrEmpty(size)) return "A3";
            return size.ToUpper().Replace("+0.50", "+0.5").Replace("+1.00", "+1");
        }

        private bool ValidateBatchPrintBlocks(List<TitleBlockModel> blocks)
        {
            List<string> errors = new List<string>();

            var duplicateGroups = blocks
                .GroupBy(b => (SafeStr(b.DrawNum).Trim() + "\u001F" + SafeStr(b.DrawTitle).Trim()), StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .ToList();

            if (duplicateGroups.Count > 0)
            {
                errors.Add("发现 PDF 文件名冲突：以下图纸的“图号 + 图纸名称”完全相同，请先修改后再批量打印。");
                foreach (var group in duplicateGroups)
                {
                    foreach (var b in group)
                    {
                        errors.Add($"  - {DescribeBlock(b)}");
                    }
                }
            }

            var missingPageSize = blocks.Where(b => string.IsNullOrWhiteSpace(b.PageSize)).ToList();
            if (missingPageSize.Count > 0)
            {
                if (errors.Count > 0) errors.Add("");
                errors.Add("发现图框 PAGESIZE / 图幅属性为空，请先补齐后再批量打印。");
                foreach (var b in missingPageSize)
                {
                    errors.Add($"  - {DescribeBlock(b)}");
                }
            }

            if (errors.Count == 0) return true;

            string message = string.Join("\n", errors);
            Application.ShowAlertDialog(message);
            WriteBatchMessage("\n[拦截] 批量打印预检未通过，已终止。");
            return false;
        }

        private string SelectPdfOutputDirectory(string printerName, string styleName)
        {
            SaveFileDialog sfd = new SaveFileDialog
            {
                Title = $"使用 [{printerName}] & [{styleName}] 批量出图，请选择或粘贴保存目录",
                Filter = "文件夹路径 (*.*)|*.*",
                FileName = FolderPickerPlaceholder,
                AddExtension = false,
                DefaultExt = "",
                CheckPathExists = true,
                OverwritePrompt = false,
                ValidateNames = false
            };

            try
            {
                Document doc = Application.DocumentManager.MdiActiveDocument;
                if (doc != null && !string.IsNullOrEmpty(doc.Name))
                {
                    string docDir = Path.GetDirectoryName(doc.Name);
                    if (Directory.Exists(docDir)) sfd.InitialDirectory = docDir;
                }
            }
            catch { }

            if (sfd.ShowDialog() != true) return null;

            string selected = sfd.FileName;
            if (Path.GetFileName(selected) == FolderPickerPlaceholder)
            {
                selected = Path.GetDirectoryName(selected);
            }

            string dir = Directory.Exists(selected) ? selected : Path.GetDirectoryName(selected);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            {
                Application.ShowAlertDialog("选择的输出目录不存在，请重新选择。");
                return null;
            }

            return dir;
        }

        private void ExportDirectoryJson(List<TitleBlockModel> finalList, List<DirColumnDef> columns)
        {
            SaveFileDialog sfd = new SaveFileDialog { Title = "导出目录至 JSON", Filter = "JSON 文件 (*.json)|*.json", FileName = $"图纸目录_{DateTime.Now:yyMMdd}.json" };
            if (sfd.ShowDialog() != true) return;

            try
            {
                var exportColumns = columns.Where(c => c.BindingPath != "INDEX").ToList();
                using (StreamWriter sw = new StreamWriter(sfd.FileName, false, Encoding.UTF8))
                {
                    sw.WriteLine("[");
                    for (int i = 0; i < finalList.Count; i++)
                    {
                        TitleBlockModel b = finalList[i];
                        sw.WriteLine("  {");
                        sw.WriteLine($"    \"DocumentName\": \"{EscapeJson(SafeStr(b.DocumentName))}\"{(exportColumns.Count > 0 ? "," : "")}");
                        for (int j = 0; j < exportColumns.Count; j++)
                        {
                            DirColumnDef col = exportColumns[j];
                            string val = GetPropValue(b, col.BindingPath);
                            sw.WriteLine($"    \"{EscapeJson(col.BindingPath)}\": \"{EscapeJson(SafeStr(val))}\"{(j < exportColumns.Count - 1 ? "," : "")}");
                        }
                        sw.Write("  }");
                        sw.WriteLine(i < finalList.Count - 1 ? "," : "");
                    }
                    sw.WriteLine("]");
                }
                WriteBatchMessage("\n[成功] 动态目录已导出至 JSON。");
            }
            catch (System.Exception ex) { WriteBatchMessage($"\n[错误] {ex.Message}"); }
        }

        private void ImportDirectoryJson(DirectoryEditorWindow dirWin)
        {
            OpenFileDialog ofd = new OpenFileDialog
            {
                Title = "导入外部目录 JSON",
                Filter = "JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*",
                Multiselect = false
            };

            if (ofd.ShowDialog() != true) return;

            if (dirWin.Items.Count > 0)
            {
                var result = System.Windows.MessageBox.Show(
                    "导入 JSON 会把数据追加合并到当前目录中心表格，是否继续？",
                    "确认合并 JSON",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Information);
                if (result != System.Windows.MessageBoxResult.Yes) return;
            }

            try
            {
                string json = File.ReadAllText(ofd.FileName, Encoding.UTF8);
                List<Dictionary<string, string>> records = ParseFlatJsonArray(json);
                List<TitleBlockModel> importedItems = new List<TitleBlockModel>();
                HashSet<string> importedFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (Dictionary<string, string> record in records)
                {
                    TitleBlockModel item = new TitleBlockModel
                    {
                        DocumentName = GetJsonValue(record, "DocumentName", "文件名", "图纸文件", "DWG"),
                        LayoutName = GetJsonValue(record, "LayoutName", "布局", "布局名"),
                        DrawNum = GetJsonValue(record, "DrawNum", "DRAWNUM", "DRAWNO", "图号", "编号"),
                        DrawTitle = GetJsonValue(record, "DrawTitle", "DRAWTITLE", "TITLE", "图纸名称", "图名", "名称"),
                        PageSize = GetJsonValue(record, "PageSize", "PAGESIZE", "PAPER", "FORMAT", "SIZE", "图幅", "纸张"),
                        DrawScale = GetJsonValue(record, "DrawScale", "DRAWSCALE", "SCALE", "比例"),
                        Version = GetJsonValue(record, "Version", "VERSION", "VER", "REV", "版本"),
                        Date = GetJsonValue(record, "Date", "DATE", "日期"),
                        Remarks = GetJsonValue(record, "Remarks", "REMARKS", "REMARK", "NOTE", "备注")
                    };

                    if (IsEmptyDirectoryItem(item)) continue;
                    importedItems.Add(item);

                    if (HasJsonKey(record, "DrawScale", "DRAWSCALE", "SCALE", "比例")) importedFields.Add("DrawScale");
                    if (HasJsonKey(record, "Version", "VERSION", "VER", "REV", "版本")) importedFields.Add("Version");
                    if (HasJsonKey(record, "Date", "DATE", "日期")) importedFields.Add("Date");
                    if (HasJsonKey(record, "Remarks", "REMARKS", "REMARK", "NOTE", "备注")) importedFields.Add("Remarks");
                }

                if (importedItems.Count == 0)
                {
                    Application.ShowAlertDialog("JSON 中未找到可导入的目录数据。");
                    return;
                }

                dirWin.ApplyImportedItems(importedItems, importedFields.ToList());
                WriteBatchMessage($"\n[成功] 已从 JSON 合并导入 {importedItems.Count} 条目录数据。");
            }
            catch (System.Exception ex)
            {
                Application.ShowAlertDialog($"导入 JSON 失败：\n{ex.Message}");
            }
        }

        private void ExportDirectoryExcel(List<TitleBlockModel> finalList, List<DirColumnDef> columns)
        {
            SaveFileDialog sfd = new SaveFileDialog { Title = "导出目录至 Excel", Filter = "Excel 文件 (*.xls)|*.xls", FileName = $"图纸目录_{DateTime.Now:yyMMdd}.xls" };
            if (sfd.ShowDialog() != true) return;

            try
            {
                using (StreamWriter sw = new StreamWriter(sfd.FileName, false, Encoding.UTF8))
                {
                    sw.WriteLine("<html><head><meta http-equiv=Content-Type content='text/html; charset=utf-8'>");
                    sw.WriteLine("<style>table {border-collapse: collapse;} td {border: 1px solid #000; font-family: '宋体'; text-align: center; vertical-align: middle;} .header {font-weight: bold; background-color: #D9D9D9;}</style></head><body><table>");
                    sw.WriteLine($"<tr><td colspan='{columns.Count}' style='font-size:16pt; font-weight:bold; height: 40px;'>图 纸 目 录</td></tr>");

                    sw.Write("<tr class='header'>");
                    foreach (var col in columns) sw.Write($"<td style='height:30px;'>{HtmlEncode(col.Header)}</td>");
                    sw.WriteLine("</tr>");

                    for (int i = 0; i < finalList.Count; i++)
                    {
                        TitleBlockModel b = finalList[i];
                        sw.Write("<tr>");
                        foreach (var col in columns)
                        {
                            if (col.BindingPath == "INDEX")
                            {
                                sw.Write($"<td>{(i + 1).ToString("D2")}</td>");
                            }
                            else
                            {
                                string val = GetPropValue(b, col.BindingPath);
                                string align = (col.Header == "图纸名称") ? "left" : "center";
                                sw.Write($"<td style='text-align:{align};'>{HtmlEncode(SafeStr(val))}</td>");
                            }
                        }
                        sw.WriteLine("</tr>");
                    }
                    sw.WriteLine("</table></body></html>");
                }
                WriteBatchMessage("\n[成功] 动态目录已导出至 Excel。");
            }
            catch (System.Exception ex) { WriteBatchMessage($"\n[错误] {ex.Message}"); }
        }

        private string DescribeBlock(TitleBlockModel b)
        {
            return $"{SafeStr(b.DocumentName)} / {SafeStr(b.LayoutName)} / 图号:{SafeStr(b.DrawNum)} / 图名:{SafeStr(b.DrawTitle)} / 图幅:{SafeStr(b.PageSize)}";
        }

        private bool IsEmptyDirectoryItem(TitleBlockModel item)
        {
            return string.IsNullOrWhiteSpace(item.DocumentName) &&
                   string.IsNullOrWhiteSpace(item.LayoutName) &&
                   string.IsNullOrWhiteSpace(item.DrawNum) &&
                   string.IsNullOrWhiteSpace(item.DrawTitle) &&
                   string.IsNullOrWhiteSpace(item.PageSize) &&
                   string.IsNullOrWhiteSpace(item.DrawScale) &&
                   string.IsNullOrWhiteSpace(item.Version) &&
                   string.IsNullOrWhiteSpace(item.Date) &&
                   string.IsNullOrWhiteSpace(item.Remarks);
        }

        private string GetJsonValue(Dictionary<string, string> record, params string[] keys)
        {
            string value;
            foreach (string key in keys)
            {
                if (record.TryGetValue(key, out value)) return value;
            }
            return "";
        }

        private bool HasJsonKey(Dictionary<string, string> record, params string[] keys)
        {
            foreach (string key in keys)
            {
                if (record.ContainsKey(key)) return true;
            }
            return false;
        }

        private List<Dictionary<string, string>> ParseFlatJsonArray(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new InvalidOperationException("JSON 文件为空。");

            int index = 0;
            List<Dictionary<string, string>> records = new List<Dictionary<string, string>>();
            SkipJsonWhitespace(json, ref index);
            ExpectJsonChar(json, ref index, '[');
            SkipJsonWhitespace(json, ref index);

            if (index < json.Length && json[index] == ']')
            {
                index++;
                return records;
            }

            while (index < json.Length)
            {
                records.Add(ParseFlatJsonObject(json, ref index));
                SkipJsonWhitespace(json, ref index);

                if (index < json.Length && json[index] == ',')
                {
                    index++;
                    SkipJsonWhitespace(json, ref index);
                    continue;
                }

                if (index < json.Length && json[index] == ']')
                {
                    index++;
                    return records;
                }

                throw new InvalidOperationException("JSON 数组格式无效，缺少逗号或结束括号。");
            }

            throw new InvalidOperationException("JSON 数组未正确结束。");
        }

        private Dictionary<string, string> ParseFlatJsonObject(string json, ref int index)
        {
            Dictionary<string, string> record = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            SkipJsonWhitespace(json, ref index);
            ExpectJsonChar(json, ref index, '{');
            SkipJsonWhitespace(json, ref index);

            if (index < json.Length && json[index] == '}')
            {
                index++;
                return record;
            }

            while (index < json.Length)
            {
                string key = ParseJsonString(json, ref index);
                SkipJsonWhitespace(json, ref index);
                ExpectJsonChar(json, ref index, ':');
                SkipJsonWhitespace(json, ref index);

                string value = index < json.Length && json[index] == '"'
                    ? ParseJsonString(json, ref index)
                    : ParseJsonLiteral(json, ref index);
                record[key] = value;

                SkipJsonWhitespace(json, ref index);
                if (index < json.Length && json[index] == ',')
                {
                    index++;
                    SkipJsonWhitespace(json, ref index);
                    continue;
                }

                if (index < json.Length && json[index] == '}')
                {
                    index++;
                    return record;
                }

                throw new InvalidOperationException("JSON 对象格式无效，缺少逗号或结束花括号。");
            }

            throw new InvalidOperationException("JSON 对象未正确结束。");
        }

        private string ParseJsonString(string json, ref int index)
        {
            ExpectJsonChar(json, ref index, '"');
            StringBuilder sb = new StringBuilder();

            while (index < json.Length)
            {
                char ch = json[index++];
                if (ch == '"') return sb.ToString();

                if (ch != '\\')
                {
                    sb.Append(ch);
                    continue;
                }

                if (index >= json.Length) throw new InvalidOperationException("JSON 字符串转义不完整。");
                char esc = json[index++];
                switch (esc)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (index + 4 > json.Length) throw new InvalidOperationException("JSON Unicode 转义不完整。");
                        string hex = json.Substring(index, 4);
                        sb.Append((char)Convert.ToInt32(hex, 16));
                        index += 4;
                        break;
                    default:
                        throw new InvalidOperationException($"JSON 字符串包含不支持的转义字符: \\{esc}");
                }
            }

            throw new InvalidOperationException("JSON 字符串未正确结束。");
        }

        private string ParseJsonLiteral(string json, ref int index)
        {
            int start = index;
            while (index < json.Length && json[index] != ',' && json[index] != '}' && json[index] != ']')
            {
                index++;
            }

            string literal = json.Substring(start, index - start).Trim();
            return literal.Equals("null", StringComparison.OrdinalIgnoreCase) ? "" : literal;
        }

        private void SkipJsonWhitespace(string json, ref int index)
        {
            while (index < json.Length && char.IsWhiteSpace(json[index])) index++;
        }

        private void ExpectJsonChar(string json, ref int index, char expected)
        {
            SkipJsonWhitespace(json, ref index);
            if (index >= json.Length || json[index] != expected)
            {
                throw new InvalidOperationException($"JSON 格式无效，位置 {index} 需要字符 '{expected}'。");
            }
            index++;
        }

        private string EscapeJson(string value)
        {
            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t");
        }

        private string HtmlEncode(string value)
        {
            return SafeStr(value)
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;");
        }

        // 使用反射从 Model 中安全取值
        private string GetPropValue(TitleBlockModel model, string propName)
        {
            PropertyInfo prop = typeof(TitleBlockModel).GetProperty(propName);
            if (prop != null)
            {
                object val = prop.GetValue(model);
                return val == null ? "" : val.ToString();
            }
            return "";
        }

        // ★ 核心动态建表逻辑：根据用户拖拽的列生成 CAD 原生表格并注入 XYD-Style 字体
        private void DrawDynamicDirectoryTable(Database db, Point3d pt, List<TitleBlockModel> data, List<DirColumnDef> columns)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTableRecord btr = tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite) as BlockTableRecord;
                if (btr == null) throw new InvalidOperationException("当前绘图空间无效，无法生成目录表格。");

                ObjectId xydTextStyleId = ObjectId.Null;
                TextStyleTable tst = tr.GetObject(db.TextStyleTableId, OpenMode.ForRead) as TextStyleTable;
                if (tst.Has("XYD-Style")) xydTextStyleId = tst["XYD-Style"];
                else xydTextStyleId = db.Textstyle;

                Table table = new Table();
                table.TableStyle = db.Tablestyle;
                table.Position = pt;
                table.SetSize(data.Count + 2, columns.Count);
                table.SetRowHeight(6.0);

                for (int i = 0; i < columns.Count; i++) table.Columns[i].Width = columns[i].DefaultWidth;

                table.Cells[0, 0].TextString = "图 纸 目 录";
                table.Cells[0, 0].TextHeight = 3.0;
                table.Cells[0, 0].Alignment = CellAlignment.MiddleCenter;
                if (xydTextStyleId != ObjectId.Null) table.Cells[0, 0].TextStyleId = xydTextStyleId;

                for (int i = 0; i < columns.Count; i++)
                {
                    table.Cells[1, i].TextString = columns[i].Header;
                    table.Cells[1, i].TextHeight = 2.5;
                    table.Cells[1, i].Alignment = CellAlignment.MiddleCenter;
                    if (xydTextStyleId != ObjectId.Null) table.Cells[1, i].TextStyleId = xydTextStyleId;
                }

                for (int i = 0; i < data.Count; i++)
                {
                    int row = i + 2;
                    for (int j = 0; j < columns.Count; j++)
                    {
                        if (columns[j].BindingPath == "INDEX")
                        {
                            table.Cells[row, j].TextString = (i + 1).ToString("D2");
                        }
                        else
                        {
                            table.Cells[row, j].TextString = GetPropValue(data[i], columns[j].BindingPath);
                        }

                        table.Cells[row, j].TextHeight = 2.5;
                        table.Cells[row, j].Alignment = (columns[j].Header == "图纸名称" || columns[j].Header == "备注") ? CellAlignment.MiddleLeft : CellAlignment.MiddleCenter;
                        if (xydTextStyleId != ObjectId.Null) table.Cells[row, j].TextStyleId = xydTextStyleId;
                    }
                }

                table.GenerateLayout(); btr.AppendEntity(table); tr.AddNewlyCreatedDBObject(table, true); tr.Commit();
            }
        }
    }
}
