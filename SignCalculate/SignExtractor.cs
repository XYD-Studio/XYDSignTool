using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;

namespace XYDSignTool
{
    public static class SignExtractor
    {
        public static List<SignItem> ExtractFromDatabase(Database db)
        {
            List<SignItem> results = new List<SignItem>();
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                BlockTableRecord ms = tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead) as BlockTableRecord;

                results.AddRange(ExtractFromBlockTableRecord(ms, tr));
                tr.Commit();
            }
            return results;
        }

        public static List<SignItem> ExtractExternalDatabase(string dwgPath)
        {
            List<SignItem> results = new List<SignItem>();
            if (!File.Exists(dwgPath)) return results;

            using (Database extDb = new Database(false, true))
            {
                try
                {
                    extDb.ReadDwgFile(dwgPath, FileShare.Read, true, "");
                    results.AddRange(ExtractFromDatabase(extDb));
                }
                catch (System.Exception) { }
            }
            return results;
        }

        public static List<SignItem> ReadJson(string jsonPath)
        {
            List<SignItem> items = new List<SignItem>();
            if (!File.Exists(jsonPath)) return items;

            try
            {
                using (StreamReader sr = new StreamReader(jsonPath, System.Text.Encoding.UTF8))
                {
                    string line;
                    Dictionary<string, string> currentItem = null;

                    while ((line = sr.ReadLine()) != null)
                    {
                        line = line.Trim();
                        if (line.StartsWith("{"))
                        {
                            currentItem = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        }
                        else if (line.StartsWith("}") || line.StartsWith("},"))
                        {
                            if (currentItem != null)
                            {
                                items.Add(FormatJsonToItem(currentItem));
                                currentItem = null;
                            }
                        }
                        else if (currentItem != null && line.Contains(":"))
                        {
                            int colonIdx = line.IndexOf(':');
                            string key = line.Substring(0, colonIdx).Trim(' ', '\t', '"', ',');
                            string val = line.Substring(colonIdx + 1).Trim(' ', '\t', '"', ',');

                            val = val.Replace("\\\"", "\"").Replace("\\/", "/");
                            currentItem[key] = val;
                        }
                    }
                }
            }
            catch (System.Exception) { }
            return items;
        }

        private static SignItem FormatJsonToItem(Dictionary<string, string> raw)
        {
            SignItem item = new SignItem
            {
                ObjId = ObjectId.Null, // 外部导入的 JSON 没有实体 ID
                BlockName = "EXT_JSON",
                Mode = "NORMAL",
                Area = GetDictValue(raw, "AREA"),
                No = GetDictValue(raw, "NO"),
                Name = GetDictValue(raw, "NAME"),
                InstallType = GetDictValue(raw, "TYPE"),
                Width = double.TryParse(GetDictValue(raw, "WIDTH"), out double w) ? w : 0,
                Height = double.TryParse(GetDictValue(raw, "HEIGHT"), out double h) ? h : 0,
                Thickness = GetDictValue(raw, "THICKNESS"),
                Weight = GetDictValue(raw, "WEIGHT"),
                Qty = int.TryParse(GetDictValue(raw, "QTY"), out int q) ? q : 1,
                Tech = GetDictValue(raw, "TECH"),
                Power = GetDictValue(raw, "POWER")
            };
            return item;
        }

        private static List<SignItem> ExtractFromBlockTableRecord(BlockTableRecord btr, Transaction tr)
        {
            List<SignItem> items = new List<SignItem>();
            foreach (ObjectId id in btr)
            {
                DBObject dbObj = tr.GetObject(id, OpenMode.ForRead);
                BlockReference br = dbObj as BlockReference;

                if (br != null)
                {
                    string blockName = GetEffectiveBlockName(br, tr);
                    if (blockName.StartsWith("XYD-SIGNBLOCK_", StringComparison.OrdinalIgnoreCase))
                    {
                        var attrDict = GetAttributes(br, tr);
                        if (attrDict.ContainsKey("--XYDISCOUNT") && attrDict["--XYDISCOUNT"] == "否") continue;

                        string areaName = attrDict.ContainsKey("--XYDAREA") ? attrDict["--XYDAREA"] : "未分配区域";
                        if (string.IsNullOrEmpty(areaName)) areaName = "未分配区域";

                        if (blockName.Equals("XYD-SIGNBLOCK_D", StringComparison.OrdinalIgnoreCase))
                        {
                            // ★ 修正：传入 id (实体的 ObjectId)
                            items.Add(BuildItem(id, blockName, attrDict, areaName, "CN"));
                            items.Add(BuildItem(id, blockName, attrDict, areaName, "EN"));
                        }
                        else
                        {
                            items.Add(BuildItem(id, blockName, attrDict, areaName, "NORMAL"));
                        }
                    }
                }
            }
            return items;
        }

        // ★ 修正：参数列表增加 ObjectId objId
        private static SignItem BuildItem(ObjectId objId, string blockName, Dictionary<string, string> attrs, string area, string mode)
        {
            SignItem item = new SignItem
            {
                ObjId = objId, // ★ 绑定实体
                BlockName = blockName,
                Mode = mode,
                Area = area,
                No = GetDictValue(attrs, "XYD-NUMBER"),
                InstallType = GetDictValue(attrs, "XYD-TYPE"),
                Tech = GetDictValue(attrs, "XYD-TECH"),
                Power = GetDictValue(attrs, "XYD-POWER"),
                Weight = GetDictValue(attrs, "XYD-WEIGHT")
            };

            string name = GetDictValue(attrs, "--XYDNAME");
            string sizeStr = "";
            string qtyStr = "";

            if (mode == "CN")
            {
                item.Name = name + "中文";
                sizeStr = GetDictValue(attrs, "XYD-CNSIZE");
                qtyStr = GetDictValue(attrs, "XYD-CNQTY");
                item.Qty = ParseQty(qtyStr, item.No);
            }
            else if (mode == "EN")
            {
                item.Name = name + "英文";
                sizeStr = GetDictValue(attrs, "XYD-ENSIZE");
                qtyStr = GetDictValue(attrs, "XYD-ENQTY");
                item.Qty = ParseQty(qtyStr, item.No);
            }
            else
            {
                item.Name = name;
                sizeStr = GetDictValue(attrs, "XYD-SIZE");
                qtyStr = GetDictValue(attrs, "XYD-QTY");
                item.Qty = ParseQty(qtyStr, item.No);
            }

            ParseSize(sizeStr, item);
            return item;
        }

        private static void ParseSize(string sizeStr, SignItem item)
        {
            if (string.IsNullOrEmpty(sizeStr)) return;
            string[] parts = sizeStr.Split('*');
            if (parts.Length >= 1 && double.TryParse(parts[0], out double w)) item.Width = w;
            if (parts.Length >= 2 && double.TryParse(parts[1], out double h)) item.Height = h;
            if (parts.Length >= 3) item.Thickness = parts[2];
        }

        private static int ParseQty(string qtyStr, string numberStr)
        {
            if (!string.IsNullOrEmpty(qtyStr) && int.TryParse(qtyStr, out int q)) return q;
            if (!string.IsNullOrEmpty(numberStr) && numberStr.Contains("~"))
            {
                string[] parts = numberStr.Split('~');
                if (parts.Length == 2)
                {
                    int startNum = ExtractEndNumber(parts[0]);
                    int endNum = ExtractEndNumber(parts[1]);
                    if (startNum != -1 && endNum != -1 && endNum >= startNum)
                    {
                        return (endNum - startNum) + 1;
                    }
                }
            }
            return 1;
        }

        private static int ExtractEndNumber(string str)
        {
            if (string.IsNullOrEmpty(str)) return -1;
            string numStr = "";
            for (int i = str.Length - 1; i >= 0; i--)
            {
                if (char.IsDigit(str[i])) numStr = str[i] + numStr;
                else break;
            }
            if (numStr.Length > 0 && int.TryParse(numStr, out int res)) return res;
            return -1;
        }

        private static string GetEffectiveBlockName(BlockReference br, Transaction tr)
        {
            if (br.IsDynamicBlock)
            {
                BlockTableRecord btr = tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
                return btr.Name;
            }
            return br.Name;
        }

        private static Dictionary<string, string> GetAttributes(BlockReference br, Transaction tr)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (ObjectId attId in br.AttributeCollection)
            {
                AttributeReference attRef = tr.GetObject(attId, OpenMode.ForRead) as AttributeReference;
                if (attRef != null) dict[attRef.Tag] = attRef.TextString.Trim();
            }
            return dict;
        }

        private static string GetDictValue(Dictionary<string, string> dict, string key)
        {
            return dict.ContainsKey(key) ? dict[key] : "";
        }
    }
}