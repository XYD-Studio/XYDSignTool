using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace XYDSignTool
{
    /// <summary>
    /// AutoCAD 内部表格生成器引擎
    /// </summary>
    public static class CadTableGenerator
    {
        public static void CreateTable(Database db, Point3d pt, List<SignItem> data, bool includeThickness)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                BlockTableRecord btr = tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite) as BlockTableRecord;

                // 1. 计算行数与列数
                int rowNum = data.Count + 2; // 标题行 + 表头行 + 数据行
                int lastCatId = -1;
                foreach (var item in data)
                {
                    if (item.CategoryId != lastCatId)
                    {
                        rowNum++; // 为每个大类增加一行标题
                        lastCatId = item.CategoryId;
                    }
                }

                int colNum = includeThickness ? 10 : 9;
                double[] colWidths = includeThickness ?
                    new double[] { 3500, 4000, 2000, 1500, 1500, 1000, 2000, 1500, 3000, 2000 } :
                    new double[] { 3500, 4000, 2000, 1500, 1500, 2000, 1500, 3000, 2000 };

                string[] headers = includeThickness ?
                    new[] { "编号", "标识内容", "安装方式", "宽(mm)", "高(mm)", "厚(mm)", "重量", "数量", "工艺", "用电量" } :
                    new[] { "编号", "标识内容", "安装方式", "宽(mm)", "高(mm)", "重量", "数量", "工艺", "用电量" };

                // 2. 初始化 Table 对象
                Table table = new Table();
                table.TableStyle = db.Tablestyle;
                table.Position = pt;
                table.SetSize(rowNum, colNum);
                table.SetRowHeight(600.0);

                // 设置列宽
                for (int i = 0; i < colWidths.Length; i++)
                {
                    table.Columns[i].Width = colWidths[i];
                }

                // 3. 填充大标题
                table.Cells[0, 0].TextString = "标识数量清单";
                table.Cells[0, 0].TextHeight = 500.0;
                table.Cells[0, 0].Alignment = CellAlignment.MiddleCenter;

                // 4. 填充表头
                table.Rows[1].Height = 600.0;
                for (int i = 0; i < headers.Length; i++)
                {
                    table.Cells[1, i].TextString = headers[i];
                    table.Cells[1, i].TextHeight = 350.0;
                    table.Cells[1, i].Alignment = CellAlignment.MiddleCenter;
                }

                // 5. 循环填充数据
                int row = 2;
                lastCatId = -1;

                foreach (var item in data)
                {
                    // 插入分类标题行
                    if (item.CategoryId != lastCatId)
                    {
                        table.Cells[row, 0].TextString = item.Area + "数量清单";
                        table.Cells[row, 0].TextHeight = 300.0;
                        table.Cells[row, 0].Alignment = CellAlignment.MiddleLeft;
                        table.MergeCells(CellRange.Create(table, row, 0, row, colNum - 1));
                        row++;
                        lastCatId = item.CategoryId;
                    }

                    // 准备当前行的数据
                    string[] rowData = includeThickness ?
                        new[] { item.No, item.Name, item.InstallType, item.Width.ToString(), item.Height.ToString(), item.Thickness, item.Weight, item.Qty.ToString(), item.Tech, item.Power } :
                        new[] { item.No, item.Name, item.InstallType, item.Width.ToString(), item.Height.ToString(), item.Weight, item.Qty.ToString(), item.Tech, item.Power };

                    for (int i = 0; i < rowData.Length; i++)
                    {
                        table.Cells[row, i].TextString = rowData[i] ?? "";
                        table.Cells[row, i].TextHeight = 250.0;
                        table.Cells[row, i].Alignment = CellAlignment.MiddleCenter;
                    }
                    row++;
                }

                table.GenerateLayout();
                btr.AppendEntity(table);
                tr.AddNewlyCreatedDBObject(table, true);
                tr.Commit();
            }
        }
    }
}