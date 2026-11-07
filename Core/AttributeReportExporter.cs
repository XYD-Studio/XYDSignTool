using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace XYDSignTool
{
    public static class AttributeReportExporter
    {
        public static string EscapeJson(string text)
        {
            var output = new StringBuilder();
            foreach (char c in text ?? "")
            {
                switch (c) { case '"': output.Append("\\\""); break; case '\\': output.Append("\\\\"); break; case '\r': output.Append("\\r"); break; case '\n': output.Append("\\n"); break; case '\t': output.Append("\\t"); break; default: if (c < 32) output.Append("\\u" + ((int)c).ToString("x4")); else output.Append(c); break; }
            }
            return output.ToString();
        }
        public static void Json(string file, IEnumerable<AttributeRecord> rows, IList<MappingColumn> columns)
        {
            using (var sw = new StreamWriter(file, false, Encoding.UTF8))
            {
                sw.WriteLine("["); bool first = true;
                foreach (var row in rows)
                {
                    if (!first) sw.WriteLine(","); first = false;
                    sw.Write("  {" + string.Join(", ", columns.Select(c => "\"" + EscapeJson(c.Name) + "\": \"" + EscapeJson(row.Value(c.Id)) + "\"")) + "}");
                }
                sw.WriteLine(); sw.WriteLine("]");
            }
        }
        public static void FullJson(string file, IEnumerable<SignItem> items)
        {
            string[] keys = { "AREA", "NO", "NAME", "TYPE", "WIDTH", "HEIGHT", "THICKNESS", "WEIGHT", "QTY", "TECH", "POWER", "REMARKS", "ISCOUNT", "CATEGORY_ID", "MODE", "BLOCKNAME", "BASEPRICE" };
            using (var sw = new StreamWriter(file, false, Encoding.UTF8))
            {
                sw.WriteLine("["); bool first = true;
                foreach (var item in items)
                {
                    object[] values = { item.Area, item.No, item.Name, item.InstallType, item.Width, item.Height, item.Thickness, item.Weight, item.Qty, item.Tech, item.Power, item.Remarks, item.IsCount, item.CategoryId, item.Mode, item.BlockName, item.BasePrice };
                    if (!first) sw.WriteLine(","); first = false;
                    sw.Write("  {" + string.Join(", ", keys.Select((key, i) => "\"" + key + "\": " + (values[i] is double || values[i] is int ? Convert.ToString(values[i], CultureInfo.InvariantCulture) : "\"" + EscapeJson(Convert.ToString(values[i])) + "\""))) + "}");
                }
                sw.WriteLine(); sw.WriteLine("]");
            }
        }
        public static void Excel(string file, IList<AttributeRecord> rows, IList<MappingColumn> columns, bool cost, bool standard)
        {
            using (var sw = new StreamWriter(file, false, Encoding.UTF8))
            {
                sw.WriteLine("<html><head><meta charset='utf-8'><style>table{border-collapse:collapse}td{border:1px solid #000;font-family:宋体;text-align:center;mso-number-format:'\\@'} .title{font-size:16pt;font-weight:bold}.header,.category,.total{background:#D9D9D9;font-weight:bold}</style></head><body><table>");
                sw.WriteLine("<tr><td class='title' colspan='" + columns.Count + "'>" + WebUtility.HtmlEncode(Path.GetFileNameWithoutExtension(file)) + "</td></tr>");
                sw.WriteLine("<tr class='header'>" + string.Join("", columns.Select(c => "<td>" + WebUtility.HtmlEncode(c.Name) + "</td>")) + "</tr>");
                int? category = null; var current = new List<AttributeRecord>();
                foreach (var row in rows)
                {
                    if (standard && category != row.Category)
                    {
                        if (cost && current.Count > 0) TotalRow(sw, current, columns, "小计");
                        current.Clear(); category = row.Category;
                        sw.WriteLine("<tr><td class='category' colspan='" + columns.Count + "'>" + WebUtility.HtmlEncode(row.Area) + "数量清单</td></tr>");
                    }
                    sw.WriteLine("<tr>" + string.Join("", columns.Select(c => "<td>" + WebUtility.HtmlEncode(row.Value(c.Id)) + "</td>")) + "</tr>"); current.Add(row);
                }
                if (cost) { if (current.Count > 0) TotalRow(sw, current, columns, "小计"); TotalRow(sw, rows, columns, "总计"); }
                sw.WriteLine("</table></body></html>");
            }
        }
        private static void TotalRow(StreamWriter sw, IEnumerable<AttributeRecord> rows, IList<MappingColumn> columns, string title)
        {
            int label = columns.ToList().FindIndex(c => c.Id != "Qty" && c.Id != "TotalPrice");
            sw.Write("<tr class='total'>");
            for (int i = 0; i < columns.Count; i++)
            {
                var c = columns[i]; string value = i == label ? title : "";
                if (c.Id == "Qty" || c.Id == "TotalPrice")
                {
                    decimal sum = rows.Sum(r => decimal.Parse(r.Value(c.Id), CultureInfo.InvariantCulture)); value = sum.ToString("0.##", CultureInfo.InvariantCulture);
                    if (label < 0 && i == 0) value = title + "：" + value;
                }
                sw.Write("<td>" + WebUtility.HtmlEncode(value) + "</td>");
            }
            sw.WriteLine("</tr>");
        }
        public static void Cad(Database db, Point3d point, IList<AttributeRecord> rows, IList<MappingColumn> columns, string title, bool standard)
        {
            var entries = new List<Tuple<AttributeRecord, string>>(); int? category = null;
            foreach (var row in rows)
            {
                if (standard && category != row.Category) { entries.Add(Tuple.Create<AttributeRecord, string>(null, (row.Area ?? "") + "数量清单")); category = row.Category; }
                entries.Add(Tuple.Create(row, (string)null));
            }
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var table = new Table { Position = point, TableStyle = db.Tablestyle };
                table.SetSize(entries.Count + 2, columns.Count); table.SetRowHeight(standard ? 600 : 7);
                for (int i = 0; i < columns.Count; i++) { table.Columns[i].Width = standard ? 2200 : 35; table.Cells[1, i].TextString = columns[i].Name; }
                table.Cells[0, 0].TextString = title;
                for (int r = 0; r < entries.Count; r++)
                {
                    if (entries[r].Item1 == null) { if (columns.Count > 1) table.MergeCells(CellRange.Create(table, r + 2, 0, r + 2, columns.Count - 1)); table.Cells[r + 2, 0].TextString = entries[r].Item2; }
                    else for (int c = 0; c < columns.Count; c++) table.Cells[r + 2, c].TextString = entries[r].Item1.Value(columns[c].Id);
                }
                for (int r = 0; r < table.Rows.Count; r++) for (int c = 0; c < columns.Count; c++) { table.Cells[r, c].TextHeight = standard ? 250 : 2.5; table.Cells[r, c].Alignment = CellAlignment.MiddleCenter; }
                table.GenerateLayout(); var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite); space.AppendEntity(table); tr.AddNewlyCreatedDBObject(table, true); tr.Commit();
            }
        }
    }
}
