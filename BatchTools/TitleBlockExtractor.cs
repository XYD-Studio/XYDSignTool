using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.DatabaseServices;

namespace XYDSignTool
{
    public static class TitleBlockExtractor
    {
        public static List<TitleBlockModel> ScanDatabase(Database db, string docName)
        {
            int ignoredObjectCount;
            return ScanDatabase(db, docName, false, out ignoredObjectCount);
        }

        public static List<TitleBlockModel> ScanDatabase(Database db, string docName, out int ignoredObjectCount)
        {
            return ScanDatabase(db, docName, false, out ignoredObjectCount);
        }

        public static List<TitleBlockModel> ScanDatabase(Database db, string docName, bool includeCover, out int ignoredObjectCount)
        {
            ignoredObjectCount = 0;
            List<TitleBlockModel> list = new List<TitleBlockModel>();
            List<TitleBlockRecognitionRule> rules = TitleBlockRecognitionSettings.GetActiveRules();
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                DBDictionary layoutDict = tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead) as DBDictionary;
                foreach (var entry in layoutDict)
                {
                    Layout layout = tr.GetObject(entry.Value, OpenMode.ForRead) as Layout;
                    BlockTableRecord btr = tr.GetObject(layout.BlockTableRecordId, OpenMode.ForRead) as BlockTableRecord;

                    foreach (ObjectId id in btr)
                    {
                        try
                        {
                            BlockReference br = tr.GetObject(id, OpenMode.ForRead, false) as BlockReference;
                            if (br == null) continue;

                            string effName;
                            if (!TryGetEffectiveName(br, tr, out effName))
                            {
                                ignoredObjectCount++;
                                continue;
                            }

                            TitleBlockRecognitionRule rule = FindMatchingRule(effName, rules);
                            if (rule != null)
                            {
                                var model = BuildModel(br, tr, docName, layout.LayoutName, layout.ModelType, effName, rule, includeCover);
                                if (model != null) list.Add(model);
                            }
                        }
                        catch
                        {
                            // A malformed/proxy block must not abort the scan of the remaining drawing.
                            ignoredObjectCount++;
                        }
                    }
                }
                tr.Commit();
            }
            return list;
        }

        public static List<TitleBlockModel> ScanExternalDwg(string dwgPath)
        {
            int ignoredObjectCount;
            return ScanExternalDwg(dwgPath, false, out ignoredObjectCount);
        }

        public static List<TitleBlockModel> ScanExternalDwg(string dwgPath, out int ignoredObjectCount)
        {
            return ScanExternalDwg(dwgPath, false, out ignoredObjectCount);
        }

        public static List<TitleBlockModel> ScanExternalDwg(string dwgPath, bool includeCover, out int ignoredObjectCount)
        {
            ignoredObjectCount = 0;
            List<TitleBlockModel> results = new List<TitleBlockModel>();
            if (!File.Exists(dwgPath)) return results;

            using (Database extDb = new Database(false, true))
            {
                try
                {
                    extDb.ReadDwgFile(dwgPath, FileShare.Read, true, "");
                    results.AddRange(ScanDatabase(extDb, Path.GetFileName(dwgPath), includeCover, out ignoredObjectCount));
                }
                catch { }
            }
            return results;
        }

        private static TitleBlockRecognitionRule FindMatchingRule(string name, List<TitleBlockRecognitionRule> rules)
        {
            foreach (TitleBlockRecognitionRule rule in rules)
            {
                if (TitleBlockRecognitionSettings.MatchesBlockName(rule, name)) return rule;
            }
            return null;
        }

        private static TitleBlockModel BuildModel(BlockReference br, Transaction tr, string docName, string layoutName, bool isModelSpace, string effectiveName, TitleBlockRecognitionRule rule, bool includeCover)
        {
            var attrs = GetAttributes(br, tr);
            string title = GetMappedValue(attrs, rule.DrawTitleTags);
            if (string.IsNullOrEmpty(title) || (!includeCover && title.Contains("封面"))) return null;

            string pageSize = GetMappedValue(attrs, rule.PageSizeTags);
            if (string.IsNullOrWhiteSpace(pageSize) && rule.ExtractPageSizeFromBlockName)
            {
                pageSize = TitleBlockRecognitionSettings.ExtractPageSizeFromBlockName(effectiveName);
            }

            TitleBlockModel model = new TitleBlockModel
            {
                ObjId = br.ObjectId,
                DocumentName = docName,
                LayoutName = layoutName,
                IsModelSpace = isModelSpace,
                DrawNum = GetMappedValue(attrs, rule.DrawNumTags),
                DrawTitle = title,
                PageSize = pageSize,
                DrawScale = GetMappedValue(attrs, rule.DrawScaleTags),
                Version = GetMappedValue(attrs, new[] { "VERSION", "VER", "版本", "REV" }),
                Date = GetMappedValue(attrs, new[] { "DATE", "日期" }),
                Remarks = GetMappedValue(attrs, new[] { "REMARKS", "REMARK", "备注", "NOTE" })
            };

            try
            {
                Extents3d ext;
                if (!TryGetFrameExtents(br, tr, out ext)) return null;
                model.MinPt = ext.MinPoint;
                model.MaxPt = ext.MaxPoint;
            }
            catch { return null; }

            return model;
        }

        private static string GetMappedValue(Dictionary<string, string> attrs, string tagNames)
        {
            return GetMappedValue(attrs, TitleBlockRecognitionSettings.SplitNames(tagNames));
        }

        private static bool TryGetFrameExtents(BlockReference br, Transaction tr, out Extents3d extents)
        {
            extents = new Extents3d();
            bool hasExtents = false;

            try
            {
                BlockTableRecord btr = tr.GetObject(br.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
                if (btr != null)
                {
                    foreach (ObjectId id in btr)
                    {
                        Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (ent == null) continue;
                        if (ent is AttributeDefinition || ent is DBText || ent is MText) continue;

                        try
                        {
                            if (!ent.Visible) continue;
                        }
                        catch { }

                        try
                        {
                            Extents3d entExt = ent.GeometricExtents;
                            entExt.TransformBy(br.BlockTransform);
                            if (!hasExtents)
                            {
                                extents = entExt;
                                hasExtents = true;
                            }
                            else
                            {
                                extents.AddExtents(entExt);
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }

            if (hasExtents) return true;

            try
            {
                extents = br.GeometricExtents;
                return true;
            }
            catch { return false; }
        }

        private static string GetMappedValue(Dictionary<string, string> attrs, string[] keys)
        {
            foreach (var key in keys) { if (attrs.ContainsKey(key)) return StripMtext(attrs[key]); }
            return "";
        }

        private static string StripMtext(string str)
        {
            if (string.IsNullOrEmpty(str)) return "";
            str = Regex.Replace(str, @"\\P", " ", RegexOptions.IgnoreCase);
            str = Regex.Replace(str, @"\\[ACFHQTWKO][^;]*;", "", RegexOptions.IgnoreCase);
            str = Regex.Replace(str, @"[{}]", "");
            return str.Trim();
        }

        private static Dictionary<string, string> GetAttributes(BlockReference br, Transaction tr)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (ObjectId attId in br.AttributeCollection)
                {
                    try
                    {
                        AttributeReference attRef = tr.GetObject(attId, OpenMode.ForRead, false) as AttributeReference;
                        if (attRef != null) dict[attRef.Tag] = attRef.TextString;
                    }
                    catch { }
                }
            }
            catch { }
            return dict;
        }

        private static bool TryGetEffectiveName(BlockReference br, Transaction tr, out string name)
        {
            name = "";
            if (br == null) return false;

            ObjectId blockDefinitionId = ObjectId.Null;
            try
            {
                if (br.IsDynamicBlock) blockDefinitionId = br.DynamicBlockTableRecord;
            }
            catch { }

            if (blockDefinitionId.IsNull)
            {
                try { blockDefinitionId = br.BlockTableRecord; }
                catch { }
            }

            if (blockDefinitionId.IsNull) return false;

            try
            {
                BlockTableRecord btr = tr.GetObject(blockDefinitionId, OpenMode.ForRead, false) as BlockTableRecord;
                if (btr == null || string.IsNullOrWhiteSpace(btr.Name)) return false;
                name = btr.Name;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
