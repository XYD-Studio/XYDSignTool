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
        [CommandMethod("XYD_CAD", CommandFlags.Session)]
        public void GenerateCadTable()
        {
            var doc = Application.DocumentManager.MdiActiveDocument; if (doc == null) return;
            var data = GatherAndSortData(doc.Editor, out List<string> areaOrder);
            AttributeCenterCommands.ShowStandard(data, false);
        }

        // ==================== ★新增：导出 JSON 数据库★ ====================
        [CommandMethod("XYD_JSON", CommandFlags.Session)]
        public void ExportJsonList()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;

            var data = GatherAndSortData(ed, out List<string> areaOrder);
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
                    AttributeReportExporter.FullJson(sfd.FileName, data);
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
            return AttributeReportExporter.EscapeJson(val);
        }

        // ==================== ★2. 导出普通 Excel 清单★ ====================
        [CommandMethod("XYD_XLS", CommandFlags.Session)]
        public void ExportXlsList()
        {
            var doc = Application.DocumentManager.MdiActiveDocument; if (doc == null) return;
            var data = GatherAndSortData(doc.Editor, out List<string> areaOrder);
            AttributeCenterCommands.ShowStandard(data, false);
        }

        // ==================== 3. 终极造价结算命令 ====================
        [CommandMethod("XYD_COST", CommandFlags.Session)]
        public void CalculateCost()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;

            var data = GatherAndSortData(ed, out List<string> areaOrder);
            if (data == null || data.Count == 0) return;

            // 提取工艺并弹出单价管理器
            List<string> techs = new List<string>();
            HashSet<string> seenTechs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in data)
            {
                string tech = NormalizeTechName(item.Tech);
                item.Tech = tech;
                if (tech.Length > 0 && seenTechs.Add(tech)) techs.Add(tech);
            }

            Dictionary<string, double> priceDict = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in techs)
            {
                double price;
                priceDict[t] = RibbonBuilder.PriceDictCache.TryGetValue(t, out price) ? price : 0.0;
            }

            PriceManagerWindow pmWin = new PriceManagerWindow(priceDict, techs);
            Application.ShowModalWindow(pmWin);

            if (!pmWin.IsConfirmed)
            {
                ed.WriteMessage("\n已取消造价结算。");
                return;
            }

            foreach (var item in pmWin.PriceItems)
            {
                string tech = NormalizeTechName(item.Tech);
                if (tech.Length == 0) continue;
                RibbonBuilder.PriceDictCache[tech] = item.Price;
                priceDict[tech] = item.Price;
            }

            foreach (var item in data)
            {
                double price;
                string tech = NormalizeTechName(item.Tech);
                item.BasePrice = priceDict.TryGetValue(tech, out price) ? price : 0.0;
            }

            AttributeCenterCommands.ShowStandard(data, true);
        }

        // ==================== 4. 可视化网格编辑器回写命令 ====================
        [CommandMethod("XYD_EDIT", CommandFlags.Session)]
        public void EditDrawingAttributes()
        {
            AttributeCenterCommands.OpenStandard(true);
        }

        // ==================== 高度聚合的内部提取与排序引擎 ====================
        private List<SignItem> GatherAndSortData(Editor ed, out List<string> areaOrder)
        {
            areaOrder = new List<string>();

            List<SignItem> allData = AttributeCenterCommands.GatherSigns(false);
            if (allData.Count == 0) return null;

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

        private static string NormalizeTechName(string value)
        {
            return (value ?? "").Trim();
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
