using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace XYDSignTool
{
    public class BlockStatisticsCommands
    {
        [CommandMethod("XYD_COUNTBLK")]
        [CommandMethod("TJTK")]
        public void CountBlockInstances()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;

            ObjectId sampleId;
            if (!TryPickBlock(ed, "\n请点击选择一个要统计【使用次数】的图块样本: ", out sampleId)) return;

            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                BlockReference sample = tr.GetObject(sampleId, OpenMode.ForRead) as BlockReference;
                if (sample == null)
                {
                    ed.WriteMessage("\n所选不是图块。");
                    return;
                }

                string blockName = GetEffectiveBlockName(sample, tr);
                ed.WriteMessage($"\n已识别图块名称: [{blockName}]");

                SelectionSet range = GetBlockSelectionRange(ed);
                int totalCount = 0;

                if (range != null)
                {
                    foreach (SelectedObject selected in range)
                    {
                        if (selected == null) continue;
                        BlockReference br = tr.GetObject(selected.ObjectId, OpenMode.ForRead) as BlockReference;
                        if (br == null) continue;

                        string currentName = GetEffectiveBlockName(br, tr);
                        if (string.Equals(currentName, blockName, StringComparison.OrdinalIgnoreCase))
                        {
                            totalCount++;
                        }
                    }
                }

                tr.Commit();

                if (totalCount > 0)
                {
                    Application.ShowAlertDialog($"数量统计完成！\n\n图块名称: {blockName}\n使用次数: {totalCount} 次");
                }
                else
                {
                    Application.ShowAlertDialog("未找到指定的图块。");
                }
            }
        }

        [CommandMethod("XYD_DYNLEN")]
        [CommandMethod("TJCD")]
        public void SumDynamicBlockLength()
        {
            SumDynamicBlockNumericProperty("长度", "长度");
        }

        [CommandMethod("XYD_DYNSPEC")]
        [CommandMethod("TJGG")]
        public void CountDynamicBlockSpec()
        {
            CountDynamicBlockTextProperty("可见性", "规格");
        }

        private void SumDynamicBlockNumericProperty(string defaultPropertyName, string displayName)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;

            ObjectId sampleId;
            if (!TryPickBlock(ed, $"\n请点击选择一个要统计【{displayName}】的动态图块样本: ", out sampleId)) return;

            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                BlockReference sample = tr.GetObject(sampleId, OpenMode.ForRead) as BlockReference;
                if (sample == null)
                {
                    ed.WriteMessage("\n所选不是图块。");
                    return;
                }
                if (!sample.IsDynamicBlock)
                {
                    ed.WriteMessage("\n所选不是动态图块。");
                    return;
                }

                string blockName = GetEffectiveBlockName(sample, tr);
                string propertyName = PromptPropertyName(ed, defaultPropertyName, $"请输入控制【{displayName}】的动态参数名");
                if (string.IsNullOrWhiteSpace(propertyName)) return;

                ed.WriteMessage($"\n已识别: [{blockName}]，准备统计属性: [{propertyName}]");
                SelectionSet range = GetBlockSelectionRange(ed);
                int totalCount = 0;
                double totalValue = 0.0;

                if (range != null)
                {
                    foreach (SelectedObject selected in range)
                    {
                        if (selected == null) continue;
                        BlockReference br = tr.GetObject(selected.ObjectId, OpenMode.ForRead) as BlockReference;
                        if (br == null || !br.IsDynamicBlock) continue;
                        if (!string.Equals(GetEffectiveBlockName(br, tr), blockName, StringComparison.OrdinalIgnoreCase)) continue;

                        object value;
                        if (TryGetDynamicPropertyValue(br, propertyName, out value))
                        {
                            double numericValue;
                            if (TryConvertToDouble(value, out numericValue))
                            {
                                totalValue += numericValue;
                                totalCount++;
                            }
                        }
                    }
                }

                tr.Commit();

                if (totalCount > 0)
                {
                    Application.ShowAlertDialog(
                        $"{displayName}统计完成！\n\n图块名称: {blockName}\n图块数量: {totalCount} 个\n【{propertyName}】总计: {totalValue:0.##}");
                }
                else
                {
                    Application.ShowAlertDialog("未找到包含该属性的对应图块。");
                }
            }
        }

        private void CountDynamicBlockTextProperty(string defaultPropertyName, string displayName)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;

            ObjectId sampleId;
            if (!TryPickBlock(ed, $"\n请点击选择一个要分类统计【{displayName}】的动态图块样本: ", out sampleId)) return;

            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                BlockReference sample = tr.GetObject(sampleId, OpenMode.ForRead) as BlockReference;
                if (sample == null)
                {
                    ed.WriteMessage("\n所选不是图块。");
                    return;
                }
                if (!sample.IsDynamicBlock)
                {
                    ed.WriteMessage("\n所选不是动态图块。");
                    return;
                }

                string blockName = GetEffectiveBlockName(sample, tr);
                string propertyName = PromptPropertyName(ed, defaultPropertyName, $"请输入控制【{displayName}】的动态参数名");
                if (string.IsNullOrWhiteSpace(propertyName)) return;

                ed.WriteMessage($"\n已识别: [{blockName}]，准备分类统计参数: [{propertyName}]");
                SelectionSet range = GetBlockSelectionRange(ed);
                Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                int totalCount = 0;

                if (range != null)
                {
                    foreach (SelectedObject selected in range)
                    {
                        if (selected == null) continue;
                        BlockReference br = tr.GetObject(selected.ObjectId, OpenMode.ForRead) as BlockReference;
                        if (br == null || !br.IsDynamicBlock) continue;
                        if (!string.Equals(GetEffectiveBlockName(br, tr), blockName, StringComparison.OrdinalIgnoreCase)) continue;

                        object value;
                        if (TryGetDynamicPropertyValue(br, propertyName, out value))
                        {
                            string textValue = Convert.ToString(value, CultureInfo.CurrentCulture);
                            if (string.IsNullOrWhiteSpace(textValue)) textValue = "(空)";

                            if (!counts.ContainsKey(textValue)) counts[textValue] = 0;
                            counts[textValue]++;
                            totalCount++;
                        }
                    }
                }

                tr.Commit();

                if (totalCount > 0)
                {
                    string report = $"图块名称: [{blockName}]\n\n--- 规格明细清单 ---\n";
                    foreach (KeyValuePair<string, int> pair in counts.OrderBy(p => p.Key))
                    {
                        report += $"\n{pair.Key} : \t{pair.Value} 个";
                    }
                    report += $"\n\n--------------------\n总计使用: \t{totalCount} 个";

                    Application.ShowAlertDialog(report);
                    ed.WriteMessage($"\n>> 统计完毕。共计 {totalCount} 个。");
                }
                else
                {
                    Application.ShowAlertDialog($"未找到包含参数 [{propertyName}] 的对应图块。\n请检查参数名是否与块编辑器中的一致！");
                }
            }
        }

        private static bool TryPickBlock(Editor ed, string message, out ObjectId objectId)
        {
            objectId = ObjectId.Null;
            PromptEntityOptions peo = new PromptEntityOptions(message);
            peo.SetRejectMessage("\n所选不是图块。");
            peo.AddAllowedClass(typeof(BlockReference), false);

            PromptEntityResult result = ed.GetEntity(peo);
            if (result.Status != PromptStatus.OK)
            {
                ed.WriteMessage("\n未选择对象。");
                return false;
            }

            objectId = result.ObjectId;
            return true;
        }

        private static SelectionSet GetBlockSelectionRange(Editor ed)
        {
            ed.WriteMessage("\n请框选统计范围 (按回车全图统计): ");
            SelectionFilter filter = new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "INSERT") });
            PromptSelectionResult selected = ed.GetSelection(filter);
            if (selected.Status == PromptStatus.OK) return selected.Value;

            PromptSelectionResult all = ed.SelectAll(filter);
            return all.Status == PromptStatus.OK ? all.Value : null;
        }

        private static string PromptPropertyName(Editor ed, string defaultValue, string message)
        {
            PromptStringOptions pso = new PromptStringOptions($"\n{message} <{defaultValue}>: ");
            pso.AllowSpaces = true;
            PromptResult result = ed.GetString(pso);
            if (result.Status == PromptStatus.Cancel) return null;
            if (string.IsNullOrWhiteSpace(result.StringResult)) return defaultValue;
            return result.StringResult.Trim();
        }

        private static string GetEffectiveBlockName(BlockReference br, Transaction tr)
        {
            if (br.IsDynamicBlock)
            {
                BlockTableRecord btr = tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
                if (btr != null) return btr.Name;
            }

            return br.Name;
        }

        private static bool TryGetDynamicPropertyValue(BlockReference br, string propertyName, out object value)
        {
            value = null;
            if (br == null || string.IsNullOrWhiteSpace(propertyName)) return false;

            DynamicBlockReferencePropertyCollection props = br.DynamicBlockReferencePropertyCollection;
            foreach (DynamicBlockReferenceProperty prop in props)
            {
                if (string.Equals(prop.PropertyName, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = prop.Value;
                    return true;
                }
            }

            return false;
        }

        private static bool TryConvertToDouble(object value, out double result)
        {
            result = 0.0;
            if (value == null) return false;

            try
            {
                result = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                return double.TryParse(Convert.ToString(value, CultureInfo.CurrentCulture), NumberStyles.Number, CultureInfo.CurrentCulture, out result);
            }
        }
    }
}
