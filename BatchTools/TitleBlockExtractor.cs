using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.DatabaseServices;

namespace XYDSignTool
{
    public static class TitleBlockExtractor
    {
        private static readonly string[] TargetPrefixes = { "MYTITLEBLOCK", "TEMPLATE_", "建筑图签", "A0", "A1", "A2", "A3", "A4", "XYD-TITLEBLOCK" };

        public static List<TitleBlockModel> ScanDatabase(Database db, string docName)
        {
            List<TitleBlockModel> list = new List<TitleBlockModel>();
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                DBDictionary layoutDict = tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead) as DBDictionary;
                foreach (var entry in layoutDict)
                {
                    Layout layout = tr.GetObject(entry.Value, OpenMode.ForRead) as Layout;
                    BlockTableRecord btr = tr.GetObject(layout.BlockTableRecordId, OpenMode.ForRead) as BlockTableRecord;

                    foreach (ObjectId id in btr)
                    {
                        BlockReference br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                        if (br != null)
                        {
                            string effName = GetEffectiveName(br, tr).ToUpper();
                            if (IsTargetBlock(effName))
                            {
                                var model = BuildModel(br, tr, docName, layout.LayoutName, layout.ModelType);
                                if (model != null) list.Add(model);
                            }
                        }
                    }
                }
                tr.Commit();
            }
            return list;
        }

        public static List<TitleBlockModel> ScanExternalDwg(string dwgPath)
        {
            List<TitleBlockModel> results = new List<TitleBlockModel>();
            if (!File.Exists(dwgPath)) return results;

            using (Database extDb = new Database(false, true))
            {
                try
                {
                    extDb.ReadDwgFile(dwgPath, FileShare.Read, true, "");
                    results.AddRange(ScanDatabase(extDb, Path.GetFileName(dwgPath)));
                }
                catch { }
            }
            return results;
        }

        private static bool IsTargetBlock(string name)
        {
            foreach (var prefix in TargetPrefixes) { if (name.Contains(prefix)) return true; }
            return false;
        }

        private static TitleBlockModel BuildModel(BlockReference br, Transaction tr, string docName, string layoutName, bool isModelSpace)
        {
            var attrs = GetAttributes(br, tr);
            string title = GetMappedValue(attrs, new[] { "DRAWTITLE", "TITLE", "图名", "DR_TITLE", "NAME" });
            if (string.IsNullOrEmpty(title) || title.Contains("封面")) return null;

            TitleBlockModel model = new TitleBlockModel
            {
                ObjId = br.ObjectId,
                DocumentName = docName,
                LayoutName = layoutName,
                IsModelSpace = isModelSpace,
                DrawNum = GetMappedValue(attrs, new[] { "DRAWNUM", "DRAWNO", "图号", "DR_NUM", "PROJECT_NO" }),
                DrawTitle = title,
                PageSize = GetMappedValue(attrs, new[] { "PAGESIZE", "PAPER", "图幅", "FORMAT", "SIZE" }),
                DrawScale = GetMappedValue(attrs, new[] { "DRAWSCALE", "SCALE", "比例", "SC" }),
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
            foreach (ObjectId attId in br.AttributeCollection)
            {
                AttributeReference attRef = tr.GetObject(attId, OpenMode.ForRead) as AttributeReference;
                if (attRef != null) dict[attRef.Tag] = attRef.TextString;
            }
            return dict;
        }

        private static string GetEffectiveName(BlockReference br, Transaction tr)
        {
            if (br.IsDynamicBlock)
            {
                BlockTableRecord btr = tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
                return btr.Name;
            }
            return br.Name;
        }
    }
}
