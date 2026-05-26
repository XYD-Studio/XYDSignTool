using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace XYDSignTool
{
    /// <summary>
    /// 高速造价与清单报表导出引擎
    /// </summary>
    public static class ExcelExporter
    {
        // ==================== 1. 导出造价清单 (XYD_COST 使用) ====================
        public static void ExportCostReport(List<SignItem> data, List<string> areaOrder, string savePath, bool includeThickness)
        {
            using (StreamWriter sw = new StreamWriter(savePath, false, Encoding.UTF8))
            {
                sw.WriteLine("<html><head><meta http-equiv=Content-Type content='text/html; charset=utf-8'>");
                sw.WriteLine("<style>");
                sw.WriteLine("table {border-collapse: collapse; width: 100%;}");
                sw.WriteLine("td {border: 1px solid #000; font-family: '宋体'; font-size: 11pt; text-align: center; mso-number-format:'\\@';}");
                sw.WriteLine(".title {font-size: 16pt; font-weight: bold;}");
                sw.WriteLine(".header {font-weight: bold; background-color: #D9D9D9;}");
                sw.WriteLine(".category {text-align: left; font-weight: bold; background-color: #D9D9D9;}");
                sw.WriteLine(".subtotal {font-weight: bold; text-align: right;}");
                sw.WriteLine(".grandtotal {font-weight: bold; text-align: right; background-color: #FFFFCC;}");
                sw.WriteLine(".num {text-align: right; mso-number-format:'\\#\\,\\#\\#0\\.00';}");
                sw.WriteLine("</style></head><body><table>");

                int colSpan = includeThickness ? 14 : 13;
                int mergeCols = includeThickness ? 7 : 6;

                sw.WriteLine($"<tr><td colspan='{colSpan}' class='title'>跨图纸标识汇总造价清单</td></tr>");

                sw.WriteLine("<tr class='header'>");
                var headers = includeThickness ?
                    new[] { "编号", "标识内容", "安装方式", "宽(mm)", "高(mm)", "厚(mm)", "重量", "数量", "面积(㎡)", "工艺", "综合单价", "单价", "合价", "用电量" } :
                    new[] { "编号", "标识内容", "安装方式", "宽(mm)", "高(mm)", "重量", "数量", "面积(㎡)", "工艺", "综合单价", "单价", "合价", "用电量" };

                foreach (var h in headers) sw.WriteLine($"<td>{h}</td>");
                sw.WriteLine("</tr>");

                int lastCatId = -1;
                string lastAreaName = "其他";
                int subQty = 0; double subTotal = 0.0;
                int grandQty = 0; double grandTotal = 0.0;

                foreach (var item in data)
                {
                    if (lastCatId != -1 && item.CategoryId != lastCatId)
                    {
                        WriteSubtotalRow(sw, lastAreaName, subQty, subTotal, mergeCols);
                        subQty = 0; subTotal = 0.0;
                    }

                    if (item.CategoryId != lastCatId)
                    {
                        sw.WriteLine($"<tr><td colspan='{colSpan}' class='category'>{item.Area}数量清单</td></tr>");
                        lastCatId = item.CategoryId; lastAreaName = item.Area;
                    }

                    sw.WriteLine("<tr>");
                    sw.WriteLine($"<td>{item.No}</td><td style='text-align:left;'>{item.Name}</td><td>{item.InstallType}</td><td>{item.Width}</td><td>{item.Height}</td>");
                    if (includeThickness) sw.WriteLine($"<td>{item.Thickness}</td>");
                    sw.WriteLine($"<td>{item.Weight}</td><td>{item.Qty}</td><td class='num'>{item.AreaSqM:F2}</td><td style='text-align:left;'>{item.Tech}</td>");
                    sw.WriteLine($"<td class='num'>{item.BasePrice:F2}</td><td class='num'>{item.UnitPrice:F2}</td><td class='num'>{item.TotalPrice:F2}</td><td>{item.Power}</td>");
                    sw.WriteLine("</tr>");

                    subQty += item.Qty; grandQty += item.Qty;
                    subTotal += item.TotalPrice; grandTotal += item.TotalPrice;
                }

                if (lastCatId != -1) WriteSubtotalRow(sw, lastAreaName, subQty, subTotal, mergeCols);
                WriteGrandTotalRow(sw, grandQty, grandTotal, mergeCols);
                sw.WriteLine("</table></body></html>");
            }
        }

        // ==================== 2. ★新增：导出普通清单 (XYD_XLS 使用) ====================
        public static void ExportNormalReport(List<SignItem> data, List<string> areaOrder, string savePath, bool includeThickness)
        {
            using (StreamWriter sw = new StreamWriter(savePath, false, Encoding.UTF8))
            {
                sw.WriteLine("<html><head><meta http-equiv=Content-Type content='text/html; charset=utf-8'>");
                sw.WriteLine("<style>");
                sw.WriteLine("table {border-collapse: collapse; width: 100%;}");
                sw.WriteLine("td {border: 1px solid #000; font-family: '宋体'; font-size: 11pt; text-align: center; mso-number-format:'\\@';}");
                sw.WriteLine(".title {font-size: 16pt; font-weight: bold;}");
                sw.WriteLine(".header {font-weight: bold; background-color: #D9D9D9;}");
                sw.WriteLine(".category {text-align: left; font-weight: bold; background-color: #D9D9D9;}");
                sw.WriteLine("</style></head><body><table>");

                int colSpan = includeThickness ? 10 : 9;

                sw.WriteLine($"<tr><td colspan='{colSpan}' class='title'>标识数量清单</td></tr>");

                sw.WriteLine("<tr class='header'>");
                var headers = includeThickness ?
                    new[] { "编号", "标识内容", "安装方式", "宽(mm)", "高(mm)", "厚(mm)", "重量", "数量", "工艺", "用电量" } :
                    new[] { "编号", "标识内容", "安装方式", "宽(mm)", "高(mm)", "重量", "数量", "工艺", "用电量" };

                foreach (var h in headers) sw.WriteLine($"<td>{h}</td>");
                sw.WriteLine("</tr>");

                int lastCatId = -1;

                foreach (var item in data)
                {
                    if (item.CategoryId != lastCatId)
                    {
                        sw.WriteLine($"<tr><td colspan='{colSpan}' class='category'>{item.Area}数量清单</td></tr>");
                        lastCatId = item.CategoryId;
                    }

                    sw.WriteLine("<tr>");
                    sw.WriteLine($"<td>{item.No}</td><td style='text-align:left;'>{item.Name}</td><td>{item.InstallType}</td><td>{item.Width}</td><td>{item.Height}</td>");
                    if (includeThickness) sw.WriteLine($"<td>{item.Thickness}</td>");
                    sw.WriteLine($"<td>{item.Weight}</td><td>{item.Qty}</td><td style='text-align:left;'>{item.Tech}</td><td>{item.Power}</td>");
                    sw.WriteLine("</tr>");
                }
                sw.WriteLine("</table></body></html>");
            }
        }

        private static void WriteSubtotalRow(StreamWriter sw, string areaName, int qty, double total, int mergeCols)
        {
            sw.WriteLine($"<tr><td colspan='{mergeCols}' class='subtotal'>{areaName} 小计：</td><td>{qty}</td><td colspan='4'></td><td class='subtotal num'>{total:F2}</td><td></td></tr>");
        }

        private static void WriteGrandTotalRow(StreamWriter sw, int qty, double total, int mergeCols)
        {
            sw.WriteLine($"<tr><td colspan='{mergeCols}' class='grandtotal'>总   计：</td><td class='grandtotal'>{qty}</td><td class='grandtotal' colspan='4'></td><td class='grandtotal num'>{total:F2}</td><td class='grandtotal'></td></tr>");
        }
    }
}