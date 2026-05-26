using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Document = Autodesk.AutoCAD.ApplicationServices.Document;

namespace XYDSignTool
{
    public class Commands
    {
        [CommandMethod("XYD_HELLO")]
        public void SayHello()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc != null) doc.Editor.WriteMessage("\n====== 欢迎使用 XYD 标识系统 C# 核心引擎！ ======\n");
        }

        // ==================== ★1. 生成 CAD 原生表格★ ====================
        [CommandMethod("XYD_CAD")]
        public void GenerateCadTable()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;

            var data = GatherAndSortData(ed, out bool includeThickness, out List<string> areaOrder);
            if (data == null || data.Count == 0) return;

            PromptPointResult ppr = ed.GetPoint("\n请指定清单表格的左上角插入点: ");
            if (ppr.Status == PromptStatus.OK)
            {
                using (DocumentLock loc = doc.LockDocument())
                {
                    CadTableGenerator.CreateTable(doc.Database, ppr.Value, data, includeThickness);
                }
                ed.WriteMessage("\n[成功] CAD 图纸内表格生成完毕！");
            }
        }

        // ==================== ★新增：导出 JSON 数据库★ ====================
        [CommandMethod("XYD_JSON")]
        public void ExportJsonList()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;

            var data = GatherAndSortData(ed, out bool includeThickness, out List<string> areaOrder);
            if (data == null || data.Count == 0) return;

            SaveFileDialog sfd = new SaveFileDialog
            {
                Title = "导出 JSON 数据库",
                Filter = "JSON 数据文件 (*.json)|*.json",
                FileName = $"标识数据合并库_{DateTime.Now:yyMMdd}.json"
            };

            if (sfd.ShowDialog() == true)
            {
                try
                {
                    using (StreamWriter sw = new StreamWriter(sfd.FileName, false, System.Text.Encoding.UTF8))
                    {
                        sw.WriteLine("[");
                        for (int i = 0; i < data.Count; i++)
                        {
                            var item = data[i];
                            sw.WriteLine("  {");
                            sw.WriteLine($"    \"AREA\": \"{SafeJson(item.Area)}\",");
                            sw.WriteLine($"    \"NO\": \"{SafeJson(item.No)}\",");
                            sw.WriteLine($"    \"NAME\": \"{SafeJson(item.Name)}\",");
                            sw.WriteLine($"    \"TYPE\": \"{SafeJson(item.InstallType)}\",");
                            sw.WriteLine($"    \"WIDTH\": {item.Width},");
                            sw.WriteLine($"    \"HEIGHT\": {item.Height},");
                            if (includeThickness) sw.WriteLine($"    \"THICKNESS\": \"{SafeJson(item.Thickness)}\",");
                            sw.WriteLine($"    \"WEIGHT\": \"{SafeJson(item.Weight)}\",");
                            sw.WriteLine($"    \"QTY\": {item.Qty},");
                            sw.WriteLine($"    \"TECH\": \"{SafeJson(item.Tech)}\",");
                            sw.WriteLine($"    \"POWER\": \"{SafeJson(item.Power)}\",");
                            sw.WriteLine($"    \"CATEGORY_ID\": {item.CategoryId}");
                            sw.Write("  }");
                            if (i < data.Count - 1) sw.WriteLine(","); else sw.WriteLine();
                        }
                        sw.WriteLine("]");
                    }
                    ed.WriteMessage($"\n[成功] JSON 数据库已导出至:\n{sfd.FileName}\n");
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage($"\n[错误] 保存 JSON 失败: {ex.Message}");
                }
            }
        }

        // 辅助方法：处理 JSON 字符串转义
        private string SafeJson(string val)
        {
            if (string.IsNullOrEmpty(val)) return "";
            return val.Replace("\\", "/").Replace("\"", "\\\"");
        }

        // ==================== ★2. 导出普通 Excel 清单★ ====================
        [CommandMethod("XYD_XLS")]
        public void ExportXlsList()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;

            var data = GatherAndSortData(ed, out bool includeThickness, out List<string> areaOrder);
            if (data == null || data.Count == 0) return;

            SaveFileDialog sfd = new SaveFileDialog
            {
                Title = "保存普通数量清单",
                Filter = "Excel清单 (*.xls)|*.xls",
                FileName = $"标识普通数量清单_{DateTime.Now:yyMMdd}.xls"
            };

            if (sfd.ShowDialog() == true)
            {
                try
                {
                    ExcelExporter.ExportNormalReport(data, areaOrder, sfd.FileName, includeThickness);
                    ed.WriteMessage($"\n[成功] 清单已导出至:\n{sfd.FileName}\n");
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage($"\n[错误] 保存失败: {ex.Message}");
                }
            }
        }

        // ==================== 3. 终极造价结算命令 ====================
        [CommandMethod("XYD_COST")]
        public void CalculateCost()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;

            var data = GatherAndSortData(ed, out bool includeThickness, out List<string> areaOrder);
            if (data == null || data.Count == 0) return;

            // 提取工艺并弹出单价管理器
            HashSet<string> techs = new HashSet<string>();
            foreach (var item in data)
            {
                if (!string.IsNullOrEmpty(item.Tech)) techs.Add(item.Tech);
            }

            Dictionary<string, double> priceDict = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in RibbonBuilder.PriceDictCache) priceDict[p.Key] = p.Value;
            foreach (var t in techs)
            {
                if (!priceDict.ContainsKey(t)) priceDict[t] = 0.0;
            }

            PriceManagerWindow pmWin = new PriceManagerWindow(priceDict);
            Application.ShowModalWindow(pmWin);

            if (!pmWin.IsConfirmed)
            {
                ed.WriteMessage("\n已取消造价结算。");
                return;
            }

            foreach (var item in pmWin.PriceItems)
            {
                RibbonBuilder.PriceDictCache[item.Tech] = item.Price;
                priceDict[item.Tech] = item.Price;
            }

            foreach (var item in data)
            {
                item.BasePrice = priceDict.ContainsKey(item.Tech) ? priceDict[item.Tech] : 0.0;
            }

            SaveFileDialog sfd = new SaveFileDialog
            {
                Title = "导出造价清单",
                Filter = "Excel造价清单 (*.xls)|*.xls",
                FileName = $"标识造价清单_{DateTime.Now:yyMMdd}.xls"
            };

            if (sfd.ShowDialog() == true)
            {
                try
                {
                    ExcelExporter.ExportCostReport(data, areaOrder, sfd.FileName, includeThickness);
                    ed.WriteMessage($"\n[成功] 完整造价报表已成功生成并保存至:\n{sfd.FileName}\n");
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage($"\n[错误] 保存造价表失败: {ex.Message}");
                }
            }
        }

        // ==================== 4. 可视化网格编辑器回写命令 ====================
        [CommandMethod("XYD_EDIT")]
        public void EditDrawingAttributes()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;

            ed.WriteMessage("\n正在扫描当前图纸...");
            List<SignItem> currentData = SignExtractor.ExtractFromDatabase(doc.Database);
            if (currentData.Count == 0)
            {
                ed.WriteMessage("\n当前图纸未找到可编辑的标识属性块。");
                return;
            }

            SignEditorWindow editorWin = new SignEditorWindow(currentData);
            Application.ShowModalWindow(editorWin);

            if (!editorWin.IsConfirmed)
            {
                ed.WriteMessage("\n已取消属性修改。");
                return;
            }

            int updateCount = 0;
            using (DocumentLock loc = doc.LockDocument())
            {
                using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                {
                    foreach (var item in editorWin.Items)
                    {
                        if (item.ObjId != ObjectId.Null && item.ObjId.IsValid)
                        {
                            BlockReference br = tr.GetObject(item.ObjId, OpenMode.ForWrite) as BlockReference;
                            if (br != null)
                            {
                                Dictionary<string, string> updates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                                updates["--XYDAREA"] = item.Area;
                                updates["XYD-NUMBER"] = item.No;
                                updates["XYD-TYPE"] = item.InstallType;
                                updates["XYD-TECH"] = item.Tech;
                                updates["XYD-WEIGHT"] = item.Weight;

                                string sizeStr = $"{item.Width}*{item.Height}";
                                if (!string.IsNullOrEmpty(item.Thickness)) sizeStr += $"*{item.Thickness}";
                                string qtyStr = item.Qty.ToString();

                                if (item.Mode == "CN")
                                {
                                    updates["--XYDNAME"] = item.Name.EndsWith("中文") ? item.Name.Substring(0, item.Name.Length - 2) : item.Name;
                                    updates["XYD-CNSIZE"] = sizeStr;
                                    updates["XYD-CNQTY"] = qtyStr;
                                }
                                else if (item.Mode == "EN")
                                {
                                    updates["--XYDNAME"] = item.Name.EndsWith("英文") ? item.Name.Substring(0, item.Name.Length - 2) : item.Name;
                                    updates["XYD-ENSIZE"] = sizeStr;
                                    updates["XYD-ENQTY"] = qtyStr;
                                }
                                else
                                {
                                    updates["--XYDNAME"] = item.Name;
                                    updates["XYD-SIZE"] = sizeStr;
                                    updates["XYD-QTY"] = qtyStr;
                                }

                                foreach (ObjectId attId in br.AttributeCollection)
                                {
                                    AttributeReference attRef = tr.GetObject(attId, OpenMode.ForWrite) as AttributeReference;
                                    if (attRef != null && updates.ContainsKey(attRef.Tag))
                                    {
                                        attRef.TextString = updates[attRef.Tag];
                                    }
                                }
                                updateCount++;
                            }
                        }
                    }
                    tr.Commit();
                }
            }
            doc.SendStringToExecute("REGEN\n", true, false, false);
            Application.ShowAlertDialog($"属性回写同步完成！\n已成功同步修改 {updateCount} 个图件属性。");
        }

        // ==================== 高度聚合的内部提取与排序引擎 ====================
        private List<SignItem> GatherAndSortData(Editor ed, out bool includeThickness, out List<string> areaOrder)
        {
            areaOrder = new List<string>();
            includeThickness = true;

            PromptKeywordOptions pko = new PromptKeywordOptions("\n数据是否包含[厚度]列? [是(Y)/否(N)] <Y>:");
            pko.Keywords.Add("Y");
            pko.Keywords.Add("N");
            pko.Keywords.Default = "Y";
            PromptResult pr = ed.GetKeywords(pko);
            if (pr.Status == PromptStatus.Cancel) return null;
            includeThickness = (pr.Status == PromptStatus.None || pr.StringResult == "Y");

            ed.WriteMessage("\n正在扫描当前激活图纸...");
            List<SignItem> allData = SignExtractor.ExtractFromDatabase(Application.DocumentManager.MdiActiveDocument.Database);
            ed.WriteMessage($" 成功提取 {allData.Count} 条记录。");

            while (true)
            {
                PromptKeywordOptions pkoMerge = new PromptKeywordOptions("\n是否合并外部数据？ [选择本地DWG(D) / 提取其他已打开标签页(O) / 导入JSON(J) / 结束并下一步(N)] <N>:");
                pkoMerge.Keywords.Add("D");
                pkoMerge.Keywords.Add("O");
                pkoMerge.Keywords.Add("J");
                pkoMerge.Keywords.Add("N");
                pkoMerge.Keywords.Default = "N";
                PromptResult prMerge = ed.GetKeywords(pkoMerge);

                if (prMerge.Status != PromptStatus.OK || prMerge.StringResult == "N") break;

                string opt = prMerge.StringResult;

                if (opt == "D")
                {
                    OpenFileDialog ofd = new OpenFileDialog { Filter = "CAD图纸 (*.dwg)|*.dwg", Title = "选择外部图纸" };
                    if (ofd.ShowDialog() == true)
                    {
                        Document openDoc = FindOpenedDocument(ofd.FileName);
                        if (openDoc != null)
                        {
                            ed.WriteMessage($"\n正在提取已打开图纸: {openDoc.Name}...");
                            allData.AddRange(SignExtractor.ExtractFromDatabase(openDoc.Database));
                        }
                        else
                        {
                            allData.AddRange(SignExtractor.ExtractExternalDatabase(ofd.FileName));
                        }
                    }
                }
                else if (opt == "O")
                {
                    int oCount = 0;
                    foreach (Document openedDoc in Application.DocumentManager)
                    {
                        if (openedDoc != Application.DocumentManager.MdiActiveDocument)
                        {
                            ed.WriteMessage($"\n正在实时提取: {openedDoc.Name}...");
                            allData.AddRange(SignExtractor.ExtractFromDatabase(openedDoc.Database));
                            oCount++;
                        }
                    }
                    if (oCount == 0) ed.WriteMessage("\n未检测到其他打开的标签页！");
                }
                else if (opt == "J")
                {
                    OpenFileDialog ofd = new OpenFileDialog { Filter = "JSON数据 (*.json)|*.json", Title = "导入外部JSON数据并合并" };
                    if (ofd.ShowDialog() == true)
                    {
                        allData.AddRange(SignExtractor.ReadJson(ofd.FileName));
                    }
                }
            }

            if (allData.Count == 0)
            {
                ed.WriteMessage("\n未找到任何可结算的数据。");
                return null;
            }

            List<string> uniqueAreas = GetUniqueAreas(allData);
            AreaSorterWindow sorterWin = new AreaSorterWindow(uniqueAreas);
            Application.ShowModalWindow(sorterWin);

            if (!sorterWin.IsConfirmed)
            {
                ed.WriteMessage("\n已取消操作。");
                return null;
            }

            areaOrder = new List<string>(sorterWin.Areas);

            foreach (var item in allData)
            {
                item.CategoryId = areaOrder.IndexOf(item.Area);
            }

            allData.Sort((a, b) =>
            {
                int cmp = a.CategoryId.CompareTo(b.CategoryId);
                if (cmp != 0) return cmp;
                cmp = string.Compare(a.BlockName, b.BlockName, StringComparison.OrdinalIgnoreCase);
                if (cmp != 0) return cmp;
                return string.Compare(a.No, b.No, StringComparison.OrdinalIgnoreCase);
            });

            return allData;
        }

        private List<string> GetUniqueAreas(List<SignItem> data)
        {
            List<string> list = new List<string>();
            foreach (var item in data)
            {
                if (!list.Contains(item.Area)) list.Add(item.Area);
            }
            return list;
        }

        private Document FindOpenedDocument(string path)
        {
            foreach (Document doc in Application.DocumentManager)
            {
                if (doc.Name.Equals(path, StringComparison.OrdinalIgnoreCase)) return doc;
            }
            return null;
        }

        [CommandMethod("XYD_TEST_REG")]
        
        public void TestRegister()
        {
            try
            {
                // 获取当前 DLL 的完整路径
                string dllPath = System.Reflection.Assembly.GetExecutingAssembly().Location;

                // 获取当前运行的 CAD 的注册表根路径 (通杀任何版本)
                string acadRegPath = HostApplicationServices.Current.UserRegistryProductRootKey;
                string appPath = acadRegPath + "\\Applications\\XYDSignTool";

                // ★ 修正：明确告诉编译器使用 Microsoft.Win32 的注册表类
                using (Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(appPath))
                {
                    key.SetValue("DESCRIPTION", "XYD 工具集", Microsoft.Win32.RegistryValueKind.String);
                    key.SetValue("LOADCTRLS", 14, Microsoft.Win32.RegistryValueKind.DWord); // 14代表: 启动时加载+命令调用时加载
                    key.SetValue("LOADER", dllPath, Microsoft.Win32.RegistryValueKind.String);
                    key.SetValue("MANAGED", 1, Microsoft.Win32.RegistryValueKind.DWord);
                }

                Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage("\n[成功] 已成功将插件强制写入 AutoCAD 注册表启动项！\n");
            }
            catch (System.Exception ex)
            {
                Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage($"\n[失败] 写入注册表报错: {ex.Message}\n");
            }
        }
    }
}