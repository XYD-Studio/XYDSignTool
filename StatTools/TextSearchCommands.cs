using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace XYDSignTool
{
    public class TextSearchCommands
    {
        private enum MatchMode
        {
            Fuzzy,
            Exact
        }

        [CommandMethod("XYD_FINDTEXT")]
        [CommandMethod("MCOUNT")]
        public void CountAndSelectTextMatches()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;

            string history = LoadHistory();
            string input = PromptSearchText(ed, history);
            if (string.IsNullOrWhiteSpace(input))
            {
                ed.WriteMessage("\n未输入内容，已退出。");
                return;
            }

            SaveHistory(input);
            List<string> keys = SplitSearchKeys(input);
            if (keys.Count == 0)
            {
                ed.WriteMessage("\n未输入有效字段，已退出。");
                return;
            }

            MatchMode matchMode = PromptMatchMode(ed);
            SelectionSet range = PromptTextSearchRange(ed);
            Dictionary<string, int> results = keys.ToDictionary(k => k, k => 0, StringComparer.OrdinalIgnoreCase);
            HashSet<ObjectId> matchedIds = new HashSet<ObjectId>();

            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                if (range != null)
                {
                    foreach (SelectedObject selected in range)
                    {
                        if (selected == null) continue;
                        Entity entity = tr.GetObject(selected.ObjectId, OpenMode.ForRead) as Entity;
                        if (entity == null) continue;
                        if (!IsLayerVisible(entity, tr)) continue;

                        bool objectMatched = false;
                        foreach (string text in GetEntitySearchTexts(entity, tr))
                        {
                            foreach (string key in keys)
                            {
                                if (CheckMatch(text, key, matchMode))
                                {
                                    results[key]++;
                                    objectMatched = true;
                                }
                            }
                        }

                        if (objectMatched)
                        {
                            matchedIds.Add(entity.ObjectId);
                        }
                    }
                }

                tr.Commit();
            }

            if (matchedIds.Count > 0)
            {
                ed.SetImpliedSelection(matchedIds.ToArray());
                ed.WriteMessage($"\n>>> 已自动选中 {matchedIds.Count} 个包含目标字段的对象。");
            }
            else
            {
                ed.SetImpliedSelection(new ObjectId[0]);
                ed.WriteMessage("\n>>> 未找到匹配的对象，无选中内容。");
            }

            string report = BuildReport(input, matchMode, results);
            ed.WriteMessage("\n" + report.Replace("\n", "\n"));
            Application.ShowAlertDialog(report);
        }

        private static string PromptSearchText(Editor ed, string history)
        {
            string prompt = string.IsNullOrWhiteSpace(history)
                ? "\n请输入查找字段(逗号分隔): "
                : $"\n请输入查找字段(逗号分隔) <{history}>: ";

            PromptStringOptions pso = new PromptStringOptions(prompt)
            {
                AllowSpaces = true
            };
            PromptResult result = ed.GetString(pso);
            if (result.Status == PromptStatus.Cancel) return null;
            if (string.IsNullOrWhiteSpace(result.StringResult)) return history;
            return result.StringResult.Trim();
        }

        private static MatchMode PromptMatchMode(Editor ed)
        {
            PromptKeywordOptions pko = new PromptKeywordOptions("\n请选择匹配模式 [模糊(Fuzzy)/严格(Exact)] <模糊>: ");
            pko.Keywords.Add("Fuzzy");
            pko.Keywords.Add("Exact");
            pko.Keywords.Default = "Fuzzy";

            PromptResult result = ed.GetKeywords(pko);
            if (result.Status != PromptStatus.OK) return MatchMode.Fuzzy;
            return string.Equals(result.StringResult, "Exact", StringComparison.OrdinalIgnoreCase)
                ? MatchMode.Exact
                : MatchMode.Fuzzy;
        }

        private static SelectionSet PromptTextSearchRange(Editor ed)
        {
            ed.WriteMessage("\n请框选要搜索的对象 (直接回车 = 全图搜索): ");
            SelectionFilter filter = new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "TEXT,MTEXT,INSERT") });
            PromptSelectionResult selected = ed.GetSelection(filter);
            if (selected.Status == PromptStatus.OK)
            {
                ed.WriteMessage($"\n已选择 {selected.Value.Count} 个对象，正在统计...");
                return selected.Value;
            }

            ed.WriteMessage("\n未选择对象，自动切换为【全图搜索】并忽略不可见图层...");
            PromptSelectionResult all = ed.SelectAll(filter);
            return all.Status == PromptStatus.OK ? all.Value : null;
        }

        private static List<string> SplitSearchKeys(string input)
        {
            return input
                .Split(new[] { ',', '，', ';', '；', '|', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(k => k.Trim())
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool CheckMatch(string source, string key, MatchMode mode)
        {
            if (source == null) source = "";
            if (key == null) key = "";

            return mode == MatchMode.Exact
                ? string.Equals(source, key, StringComparison.OrdinalIgnoreCase)
                : source.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static IEnumerable<string> GetEntitySearchTexts(Entity entity, Transaction tr)
        {
            DBText dbText = entity as DBText;
            if (dbText != null)
            {
                yield return dbText.TextString;
                yield break;
            }

            MText mText = entity as MText;
            if (mText != null)
            {
                yield return StripMText(mText.Contents);
                yield break;
            }

            BlockReference br = entity as BlockReference;
            if (br != null)
            {
                foreach (ObjectId attId in br.AttributeCollection)
                {
                    AttributeReference att = tr.GetObject(attId, OpenMode.ForRead) as AttributeReference;
                    if (att != null) yield return att.TextString;
                }
            }
        }

        private static bool IsLayerVisible(Entity entity, Transaction tr)
        {
            try
            {
                LayerTableRecord layer = tr.GetObject(entity.LayerId, OpenMode.ForRead) as LayerTableRecord;
                if (layer == null) return true;
                return !layer.IsOff && !layer.IsFrozen;
            }
            catch
            {
                return true;
            }
        }

        private static string StripMText(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";

            value = Regex.Replace(value, @"\\P", " ", RegexOptions.IgnoreCase);
            value = Regex.Replace(value, @"\\[ACFHQTWKO][^;]*;", "", RegexOptions.IgnoreCase);
            value = Regex.Replace(value, @"[{}]", "");
            return value.Trim();
        }

        private static string BuildReport(string input, MatchMode mode, Dictionary<string, int> results)
        {
            string report = "==========================================\n";
            report += "          MCOUNT 统计报告\n";
            report += "==========================================\n";
            report += $"查找字段: {input}\n";
            report += $"匹配模式: {(mode == MatchMode.Exact ? "严格完全匹配" : "模糊包含匹配")}\n";
            report += "------------------------------------------";

            foreach (KeyValuePair<string, int> item in results)
            {
                report += $"\n字段 [{item.Key}] \t-> {item.Value} 次";
            }

            report += "\n==========================================\n统计完成。";
            return report;
        }

        private static string LoadHistory()
        {
            try
            {
                string path = GetHistoryPath();
                return File.Exists(path) ? File.ReadAllText(path).Trim() : "";
            }
            catch
            {
                return "";
            }
        }

        private static void SaveHistory(string value)
        {
            try
            {
                string path = GetHistoryPath();
                string dir = Path.GetDirectoryName(path);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(path, value ?? "");
            }
            catch { }
        }

        private static string GetHistoryPath()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XYDSignTool");
            return Path.Combine(dir, "FindTextHistory.txt");
        }
    }
}
