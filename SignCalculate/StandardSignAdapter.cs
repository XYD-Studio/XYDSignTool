using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;

namespace XYDSignTool
{
    public static class StandardSignAdapter
    {
        public static AttributeMappingProfile Profile(bool cost)
        {
            var p = new AttributeMappingProfile { Name = "内置标识清单" };
            string[] ids = { "Area", "No", "Name", "InstallType", "Width", "Height", "Thickness", "Weight", "Qty", "Tech", "Power", "Remarks", "IsCount", "AreaSqM", "BasePrice", "UnitPrice", "TotalPrice" };
            string[] names = { "标识区域", "编号", "标识内容", "安装方式", "宽(mm)", "高(mm)", "厚(mm)", "重量", "数量", "工艺", "用电量", "备注", "是否计数", "面积(㎡)", "综合单价", "单价", "合价" };
            for (int i = 0; i < ids.Length; i++)
                p.Columns.Add(new MappingColumn { Id = ids[i], Name = names[i], Order = i, Visible = i > 0 && i <= 10 || cost && i >= 13 });
            return p;
        }
        public static List<AttributeRecord> Rows(IEnumerable<SignItem> items)
        {
            var rows = new List<AttributeRecord>();
            foreach (var item in items)
            {
                var row = item.SourceRecord ?? new AttributeRecord { Imported = true, FilePath = "JSON" };
                row.Sign = item; row.Mode = item.Mode; row.Area = item.Area; row.Category = item.CategoryId;
                var p = Profile(true);
                foreach (var c in p.Columns)
                {
                    string value = Convert.ToString(typeof(SignItem).GetProperty(c.Id).GetValue(item, null), CultureInfo.InvariantCulture);
                    string tag = Tag(c.Id, item.Mode);
                    string restriction; row.Restrictions.TryGetValue(tag ?? "", out restriction);
                    row.Cells[c.Id] = new AttributeCell { Value = value, Original = value, Tag = tag,
                        Reason = restriction, ReadOnly = restriction != null || c.Id == "AreaSqM" || c.Id == "BasePrice" || c.Id == "UnitPrice" || c.Id == "TotalPrice" || row.Imported || !row.OriginalAttributes.ContainsKey(tag ?? "") };
                }
                rows.Add(row);
            }
            return rows;
        }
        private static string Tag(string column, string mode)
        {
            switch (column)
            {
                case "Area": return "--XYDAREA";
                case "No": return "XYD-NUMBER";
                case "Name": return "--XYDNAME";
                case "InstallType": return "XYD-TYPE";
                case "Width": case "Height": case "Thickness": return mode == "CN" ? "XYD-CNSIZE" : mode == "EN" ? "XYD-ENSIZE" : "XYD-SIZE";
                case "Qty": return mode == "CN" ? "XYD-CNQTY" : mode == "EN" ? "XYD-ENQTY" : "XYD-QTY";
                case "Tech": return "XYD-TECH";
                case "Power": return "XYD-POWER";
                case "Weight": return "XYD-WEIGHT";
                case "Remarks": return "XYD-REMARKS";
                case "IsCount": return "--XYDISCOUNT";
                default: return "";
            }
        }
        public static Dictionary<string, string> BuildUpdates(AttributeRecord row)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in row.Cells)
            {
                var cell = pair.Value;
                if (!cell.Dirty) continue;
                if (string.IsNullOrWhiteSpace(cell.Tag)) continue;
                if (cell.ReadOnly) throw new InvalidOperationException("属性只读：" + cell.Tag);
                if (row.Sign != null && (pair.Key == "Width" || pair.Key == "Height" || pair.Key == "Thickness")) continue;
                string value = cell.Value;
                if (row.Sign != null)
                {
                    if (pair.Key == "Name")
                    {
                        string suffix = row.Mode == "CN" ? "中文" : row.Mode == "EN" ? "英文" : "";
                        if (suffix.Length > 0 && value.EndsWith(suffix, StringComparison.Ordinal)) value = value.Substring(0, value.Length - suffix.Length);
                    }
                    if (pair.Key == "Qty") { int qty; if (!int.TryParse(value, out qty) || qty < 0) throw new InvalidOperationException("数量必须为非负整数。"); }
                    if (pair.Key == "IsCount" && value != "是" && value != "否") throw new InvalidOperationException("是否计数只能为“是”或“否”。");
                }
                result[cell.Tag] = value;
            }
            if (row.Sign != null && new[] { "Width", "Height", "Thickness" }.Any(id => row.Cells[id].Dirty))
            {
                foreach (string id in new[] { "Width", "Height" })
                {
                    double n; if (!double.TryParse(row.Value(id), NumberStyles.Float, CultureInfo.InvariantCulture, out n) || double.IsNaN(n) || double.IsInfinity(n) || n < 0)
                        throw new InvalidOperationException("宽、高必须是有效非负数字。");
                }
                result[Tag("Width", row.Mode)] = row.Value("Width") + "*" + row.Value("Height") + (string.IsNullOrEmpty(row.Value("Thickness")) ? "" : "*" + row.Value("Thickness"));
            }
            return result;
        }
        public static void RefreshCalculated(AttributeRecord row)
        {
            if (row.Sign == null) return;
            if (row.Value("IsCount") != "是" && row.Value("IsCount") != "否") throw new InvalidOperationException("是否计数只能为“是”或“否”。");
            foreach (var c in Profile(false).Columns.Where(c => c.Id != "AreaSqM" && c.Id != "UnitPrice" && c.Id != "TotalPrice"))
            {
                var prop = typeof(SignItem).GetProperty(c.Id);
                string value = row.Value(c.Id);
                if (prop.PropertyType == typeof(double)) { double n; if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out n) || double.IsNaN(n) || double.IsInfinity(n) || n < 0) throw new InvalidOperationException(c.Name + "不是有效非负数字。"); prop.SetValue(row.Sign, n, null); }
                else if (prop.PropertyType == typeof(int)) { int n; if (!int.TryParse(value, out n) || n < 0) throw new InvalidOperationException("数量不是非负整数。"); prop.SetValue(row.Sign, n, null); }
                else prop.SetValue(row.Sign, value, null);
            }
            double price;
            if (RibbonBuilder.PriceDictCache.TryGetValue((row.Sign.Tech ?? "").Trim(), out price)) row.Sign.BasePrice = price;
            row.Cells["BasePrice"].Value = row.Sign.BasePrice.ToString(CultureInfo.InvariantCulture);
            row.Area = row.Sign.Area;
            foreach (string id in new[] { "AreaSqM", "UnitPrice", "TotalPrice" }) row.Cells[id].Value = Convert.ToString(typeof(SignItem).GetProperty(id).GetValue(row.Sign, null), CultureInfo.InvariantCulture);
        }
        public static void Synchronize(AttributeRecord row, IDictionary<string, AttributeReference> attributes)
        {
            foreach (var pair in row.Cells)
            {
                AttributeReference a;
                if (string.IsNullOrWhiteSpace(pair.Value.Tag) || !attributes.TryGetValue(pair.Value.Tag, out a)) continue;
                string raw = a.TextString ?? "";
                if (row.Sign != null)
                {
                    if (pair.Key == "Width" || pair.Key == "Height" || pair.Key == "Thickness")
                    {
                        var parts = raw.Split('*'); int index = pair.Key == "Width" ? 0 : pair.Key == "Height" ? 1 : 2;
                        pair.Value.Value = parts.Length > index ? parts[index] : index < 2 ? "0" : "";
                    }
                    else if (pair.Key == "Name") pair.Value.Value = raw + (row.Mode == "CN" ? "中文" : row.Mode == "EN" ? "英文" : "");
                    else if (pair.Key == "Qty") { int n; if (int.TryParse(raw, out n)) pair.Value.Value = raw; }
                    else pair.Value.Value = raw;
                }
                else pair.Value.Value = raw;
            }
            RefreshCalculated(row);
            row.OriginalAttributes.Clear(); row.OriginalVisibility.Clear();
            foreach (var pair in attributes) { row.OriginalAttributes[pair.Key] = pair.Value.TextString ?? ""; row.OriginalVisibility[pair.Key] = pair.Value.Invisible; }
            foreach (var cell in row.Cells.Values) cell.Original = cell.Value;
        }
        public static List<AttributeRecord> SortRows(IEnumerable<AttributeRecord> source, IList<string> areaOrder)
        {
            var result = new List<AttributeRecord>(); int category = 0;
            foreach (var group in source.GroupBy(r => r.Area ?? "").OrderBy(g => { int i = areaOrder.IndexOf(g.Key); return i < 0 ? int.MaxValue : i; }))
            {
                foreach (var row in group.OrderBy(r => r.Sign == null ? "" : r.Sign.BlockName, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Value("No"), StringComparer.OrdinalIgnoreCase))
                { row.Category = category; if (row.Sign != null) row.Sign.CategoryId = category; result.Add(row); }
                category++;
            }
            return result;
        }
    }
}
