using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace XYDSignTool
{
    public class AttributeCenterCommands
    {
        [CommandMethod("XYD_SIGN_CENTER", CommandFlags.Session)]
        public void SignCenter() { OpenStandard(false); }
        [CommandMethod("XYD_SIGN_MAPPING", CommandFlags.Session)]
        public void SignMapping() { Configure(false); }
        [CommandMethod("XYD_TITLEBLOCK_EDIT", CommandFlags.Session)]
        public void TitleEdit() { Configure(true); }
        private static void Configure(bool frames)
        {
            try { var window = new MappingEditorWindow(frames); Application.ShowModalWindow(window); if (window.Accepted) OpenAdvanced(window.Profile); }
            catch (System.Exception ex) { Application.ShowAlertDialog(ex.Message); }
        }
        public static void OpenStandard(bool includeExcluded)
        {
            try
            {
                var data = GatherSigns(includeExcluded); if (data.Count == 0) return;
                var sorter = new AreaSorterWindow(data.Select(d => d.Area).Distinct().ToList()); Application.ShowModalWindow(sorter);
                if (!sorter.IsConfirmed) return;
                foreach (var item in data) item.CategoryId = sorter.Areas.IndexOf(item.Area);
                data = data.OrderBy(d => d.CategoryId).ThenBy(d => d.BlockName, StringComparer.OrdinalIgnoreCase).ThenBy(d => d.No, StringComparer.OrdinalIgnoreCase).ToList();
                ShowStandard(data, false);
            }
            catch (System.Exception ex) { Application.ShowAlertDialog(ex.Message); }
        }
        public static List<SignItem> GatherSigns(bool includeExcluded)
        {
            var selector = new AttributeSourceWindow(true); Application.ShowModalWindow(selector); var result = new List<SignItem>();
            if (!selector.Accepted) return result;
            var failures = new List<string>();
            foreach (var source in selector.Sources.Where(s => s.Selected))
            {
                try
                {
                if (source.Document != null)
                {
                    using (var loc = source.Document.LockDocument())
                    {
                        var list = SignExtractor.ExtractFromDatabase(source.Document.Database, includeExcluded);
                        foreach (var item in list) item.SourceRecord.FilePath = source.Document.Name;
                        result.AddRange(list);
                    }
                }
                else if (Path.GetExtension(source.Path).Equals(".json", StringComparison.OrdinalIgnoreCase)) result.AddRange(SignExtractor.ReadJson(source.Path));
                else
                {
                    using (var db = new Database(false, true))
                    {
                        db.ReadDwgFile(source.Path, FileShare.Read, true, "");
                        var list = SignExtractor.ExtractFromDatabase(db, includeExcluded);
                        foreach (var item in list) { item.ObjId = ObjectId.Null; item.SourceRecord.ObjectId = ObjectId.Null; item.SourceRecord.OpenDatabase = null; item.SourceRecord.FilePath = source.Path; }
                        result.AddRange(list);
                    }
                }
                }
                catch (System.Exception ex) { failures.Add(source.Path + "：" + ex.Message); }
            }
            if (failures.Count > 0) Application.ShowAlertDialog("以下来源提取失败：\n" + string.Join("\n", failures));
            return result;
        }
        public static void ShowStandard(List<SignItem> data, bool cost)
        {
            if (data == null || data.Count == 0) { Application.ShowAlertDialog("没有可提取的标识属性块。"); return; }
            if (!cost)
                foreach (var item in data) { double price; if (RibbonBuilder.PriceDictCache.TryGetValue((item.Tech ?? "").Trim(), out price)) item.BasePrice = price; }
            var profile = StandardSignAdapter.Profile(cost);
            Application.ShowModalWindow(new AttributeCenterWindow(profile, StandardSignAdapter.Rows(data), true, cost));
        }
        public static void OpenAdvanced(AttributeMappingProfile profile)
        {
            if (profile.TitleBlocks) MappingProfileStore.CurrentTitleProfile = profile;
            var selector = new AttributeSourceWindow(false); Application.ShowModalWindow(selector); if (!selector.Accepted) return;
            var rows = new List<AttributeRecord>(); var failures = new List<string>();
            foreach (var source in selector.Sources.Where(s => s.Selected))
            {
                try
                {
                    if (source.Document != null)
                    { using (var loc = source.Document.LockDocument()) rows.AddRange(AttributeMappingService.Scan(source.Document.Database, source.Document.Name, profile, selector.Scope)); }
                    else using (var db = new Database(false, true))
                    {
                        db.ReadDwgFile(source.Path, FileShare.Read, true, ""); var list = AttributeMappingService.Scan(db, source.Path, profile, selector.Scope);
                        foreach (var r in list) { r.OpenDatabase = null; r.ObjectId = ObjectId.Null; } rows.AddRange(list);
                    }
                }
                catch (System.Exception ex) { failures.Add(Path.GetFileName(source.Path) + "：" + ex.Message); }
            }
            if (failures.Count > 0) Application.ShowAlertDialog("以下来源提取失败：\n" + string.Join("\n", failures));
            if (rows.Count == 0) { Application.ShowAlertDialog("没有找到符合映射规则的属性块。"); return; }
            Application.ShowModalWindow(new AttributeCenterWindow(profile, rows));
        }
    }
}
