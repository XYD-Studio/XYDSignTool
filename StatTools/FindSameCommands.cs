using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace XYDSignTool
{
    public class FindSameCommands
    {
        private sealed class PropertyValue
        {
            public string MatchValue { get; set; }
            public string DisplayValue { get; set; }
            public double[] Widths { get; set; }
        }

        private const double WidthTolerance = 1e-6;

        private sealed class EntitySnapshot
        {
            public Dictionary<FindSameCriterionKind, PropertyValue> Values { get; } =
                new Dictionary<FindSameCriterionKind, PropertyValue>();
        }

        [CommandMethod("XYD_FINDSAME", CommandFlags.Redraw)]
        public void FindSameObjects()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;

            ObjectId sampleId = GetSampleObjectId(ed);
            if (sampleId.IsNull) return;

            EntitySnapshot sample;
            List<FindSameCriterion> dialogCriteria;
            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                Entity entity = tr.GetObject(sampleId, OpenMode.ForRead, false) as Entity;
                if (entity == null)
                {
                    ed.WriteMessage("\n所选对象不可用，已取消查找。");
                    return;
                }

                sample = BuildSnapshot(entity, tr);
                dialogCriteria = BuildDialogCriteria(sample);
                tr.Commit();
            }

            FindSameWindow window = new FindSameWindow(dialogCriteria);
            bool? accepted = Application.ShowModalWindow(window);
            if (accepted != true) return;

            ObjectId[] manualIds = null;
            if (window.Scope == FindSameScope.ManualSelection)
            {
                PromptSelectionOptions options = new PromptSelectionOptions
                {
                    MessageForAdding = "\n请选择查找范围: "
                };
                PromptSelectionResult selected = ed.GetSelection(options);
                if (selected.Status != PromptStatus.OK || selected.Value == null || selected.Value.Count == 0)
                {
                    ed.WriteMessage("\n未选择查找范围，已取消。");
                    return;
                }
                manualIds = selected.Value.GetObjectIds();
            }

            List<ObjectId> allMatches = new List<ObjectId>();
            List<ObjectId> currentSpaceMatches = new List<ObjectId>();
            int skipped = 0;
            bool comparePolylineWidths = window.SelectedCriteria.Contains(FindSameCriterionKind.PolylineWidth);
            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                IEnumerable<ObjectId> candidateIds = GetCandidateIds(doc.Database, tr, window.Scope, manualIds);
                foreach (ObjectId id in candidateIds.Distinct())
                {
                    try
                    {
                        Entity candidate = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
                        if (candidate == null) continue;
                        EntitySnapshot candidateSnapshot = BuildSnapshot(candidate, tr, comparePolylineWidths);
                        if (!Matches(sample, candidateSnapshot, window.SelectedCriteria)) continue;

                        allMatches.Add(id);
                        if (candidate.OwnerId == doc.Database.CurrentSpaceId) currentSpaceMatches.Add(id);
                    }
                    catch
                    {
                        skipped++;
                    }
                }
                tr.Commit();
            }

            ObjectId[] selectedIds = currentSpaceMatches
                .Where(id => !id.IsNull && id.IsValid && !id.IsErased)
                .Distinct()
                .ToArray();
            ed.SetImpliedSelection(selectedIds);
            try { Application.UpdateScreen(); }
            catch { }

            int actualSelectedCount = 0;
            try
            {
                PromptSelectionResult confirmed = ed.SelectImplied();
                if (confirmed.Status == PromptStatus.OK && confirmed.Value != null)
                    actualSelectedCount = confirmed.Value.Count;
            }
            catch { }

            string report = "查找相同完成：共匹配 " + allMatches.Count + " 个对象。";
            if (window.Scope == FindSameScope.EntireDrawing && allMatches.Count != currentSpaceMatches.Count)
            {
                report += "\n当前空间已选中 " + actualSelectedCount + " 个，其他空间匹配 " +
                          (allMatches.Count - currentSpaceMatches.Count) + " 个。";
            }
            else
            {
                report += "\n已选中 " + actualSelectedCount + " 个对象。";
            }
            if (actualSelectedCount != selectedIds.Length)
                report += "\nAutoCAD 实际选中数与预期不一致，预期 " + selectedIds.Length + " 个。";
            if (skipped > 0) report += "\n跳过异常对象 " + skipped + " 个。";
            ed.WriteMessage("\n" + report.Replace("\n", "；"));
            if (allMatches.Count == 0) Application.ShowAlertDialog(report);
        }

        private static ObjectId GetSampleObjectId(Editor ed)
        {
            PromptSelectionResult implied = ed.SelectImplied();
            if (implied.Status == PromptStatus.OK && implied.Value != null && implied.Value.Count > 0)
            {
                ObjectId sample = implied.Value.GetObjectIds()[0];
                ed.SetImpliedSelection(new ObjectId[0]);
                return sample;
            }

            PromptEntityOptions options = new PromptEntityOptions("\n请选择一个样本对象: ");
            PromptEntityResult selected = ed.GetEntity(options);
            return selected.Status == PromptStatus.OK ? selected.ObjectId : ObjectId.Null;
        }

        private static EntitySnapshot BuildSnapshot(Entity entity, Transaction tr, bool includePolylineWidth = true)
        {
            EntitySnapshot snapshot = new EntitySnapshot();
            Add(snapshot, FindSameCriterionKind.ObjectType, GetObjectType(entity), GetObjectType(entity));
            TryAdd(snapshot, FindSameCriterionKind.Layer, () => entity.Layer, () => entity.Layer);
            TryAdd(snapshot, FindSameCriterionKind.Linetype, () => entity.Linetype, () => entity.Linetype);
            TryAdd(snapshot, FindSameCriterionKind.Color, () => GetColorKey(entity.Color), () => GetColorDisplay(entity.Color));
            TryAdd(snapshot, FindSameCriterionKind.LineWeight,
                () => ((int)entity.LineWeight).ToString(),
                () => GetLineWeightDisplay(entity.LineWeight));
            if (includePolylineWidth) AddPolylineWidth(snapshot, entity, tr);
            TryAdd(snapshot, FindSameCriterionKind.Transparency,
                () => GetTransparencyKey(entity.Transparency),
                () => GetTransparencyDisplay(entity.Transparency));

            BlockReference block = entity as BlockReference;
            if (block != null)
            {
                string blockName = GetEffectiveBlockName(block, tr);
                if (!string.IsNullOrWhiteSpace(blockName)) Add(snapshot, FindSameCriterionKind.BlockName, blockName, blockName);
            }

            DBText dbText = entity as DBText;
            if (dbText != null)
                TryAdd(snapshot, FindSameCriterionKind.TextStyle, () => dbText.TextStyleName, () => dbText.TextStyleName);

            MText mText = entity as MText;
            if (mText != null)
                TryAdd(snapshot, FindSameCriterionKind.TextStyle, () => mText.TextStyleName, () => mText.TextStyleName);

            Dimension dimension = entity as Dimension;
            if (dimension != null)
                TryAdd(snapshot, FindSameCriterionKind.DimensionStyle,
                    () => dimension.DimensionStyleName,
                    () => dimension.DimensionStyleName);

            Hatch hatch = entity as Hatch;
            if (hatch != null)
                TryAdd(snapshot, FindSameCriterionKind.HatchPattern, () => hatch.PatternName, () => hatch.PatternName);

            return snapshot;
        }

        private static List<FindSameCriterion> BuildDialogCriteria(EntitySnapshot snapshot)
        {
            FindSameCriterionKind[] order =
            {
                FindSameCriterionKind.ObjectType,
                FindSameCriterionKind.Layer,
                FindSameCriterionKind.BlockName,
                FindSameCriterionKind.Linetype,
                FindSameCriterionKind.Color,
                FindSameCriterionKind.PolylineWidth,
                FindSameCriterionKind.LineWeight,
                FindSameCriterionKind.Transparency,
                FindSameCriterionKind.TextStyle,
                FindSameCriterionKind.DimensionStyle,
                FindSameCriterionKind.HatchPattern
            };
            bool hasBlockName = snapshot.Values.ContainsKey(FindSameCriterionKind.BlockName);
            return order
                .Where(snapshot.Values.ContainsKey)
                .Select(kind => new FindSameCriterion
                {
                    Kind = kind,
                    Name = GetCriterionName(kind),
                    Value = snapshot.Values[kind].DisplayValue,
                    IsSelected = hasBlockName
                        ? kind == FindSameCriterionKind.BlockName
                        : kind == FindSameCriterionKind.ObjectType
                })
                .ToList();
        }

        private static bool Matches(
            EntitySnapshot sample,
            EntitySnapshot candidate,
            IEnumerable<FindSameCriterionKind> criteria)
        {
            foreach (FindSameCriterionKind criterion in criteria)
            {
                PropertyValue expected;
                PropertyValue actual;
                if (!sample.Values.TryGetValue(criterion, out expected) ||
                    !candidate.Values.TryGetValue(criterion, out actual) ||
                    !ValuesMatch(expected, actual))
                    return false;
            }
            return true;
        }

        private static bool ValuesMatch(PropertyValue expected, PropertyValue actual)
        {
            if (expected.Widths == null)
                return string.Equals(expected.MatchValue, actual.MatchValue, StringComparison.OrdinalIgnoreCase);
            return expected.Widths.Length == actual.Widths.Length &&
                   expected.Widths.Zip(actual.Widths, WidthsEqual).All(equal => equal);
        }

        private static bool WidthsEqual(double first, double second)
        {
            return Math.Abs(first - second) <= WidthTolerance;
        }

        private static void AddPolylineWidth(EntitySnapshot snapshot, Entity entity, Transaction tr)
        {
            double[] widths;
            if (entity is Polyline polyline)
            {
                int count = polyline.NumberOfVertices - (polyline.Closed ? 0 : 1);
                if (count < 1) return;
                widths = new double[count * 2];
                for (int i = 0; i < count; i++)
                {
                    widths[i * 2] = polyline.GetStartWidthAt(i);
                    widths[i * 2 + 1] = polyline.GetEndWidthAt(i);
                }
            }
            else if (entity is Polyline2d legacy && legacy.PolyType == Poly2dType.SimplePoly)
            {
                var vertices = legacy.Cast<ObjectId>()
                    .Select(id => (Vertex2d)tr.GetObject(id, OpenMode.ForRead)).ToList();
                int count = vertices.Count - (legacy.Closed ? 0 : 1);
                if (count < 1) return;
                widths = new double[count * 2];
                for (int i = 0; i < count; i++)
                {
                    widths[i * 2] = vertices[i].StartWidth;
                    widths[i * 2 + 1] = vertices[i].EndWidth;
                }
            }
            else return;

            double min = widths.Min();
            double max = widths.Max();
            if (double.IsNaN(min) || double.IsNaN(max) || double.IsInfinity(min) || double.IsInfinity(max)) return;
            bool constant = WidthsEqual(min, max);
            string display = constant
                ? min.ToString("0.######", CultureInfo.CurrentCulture)
                : string.Format(CultureInfo.CurrentCulture, "变宽 {0:0.######}–{1:0.######}（{2} 段）", min, max, widths.Length / 2);
            snapshot.Values[FindSameCriterionKind.PolylineWidth] = new PropertyValue
            {
                Widths = constant ? new[] { min } : widths,
                DisplayValue = display
            };
        }

        private static IEnumerable<ObjectId> GetCandidateIds(
            Database database,
            Transaction tr,
            FindSameScope scope,
            ObjectId[] manualIds)
        {
            if (scope == FindSameScope.ManualSelection) return manualIds ?? new ObjectId[0];
            if (scope == FindSameScope.CurrentSpace)
            {
                BlockTableRecord current = tr.GetObject(database.CurrentSpaceId, OpenMode.ForRead) as BlockTableRecord;
                return current == null ? new ObjectId[0] : current.Cast<ObjectId>().ToArray();
            }

            List<ObjectId> ids = new List<ObjectId>();
            BlockTable blockTable = tr.GetObject(database.BlockTableId, OpenMode.ForRead) as BlockTable;
            if (blockTable == null) return ids;
            foreach (ObjectId recordId in blockTable)
            {
                BlockTableRecord record = tr.GetObject(recordId, OpenMode.ForRead, false) as BlockTableRecord;
                if (record == null || !record.IsLayout) continue;
                ids.AddRange(record.Cast<ObjectId>());
            }
            return ids;
        }

        private static string GetEffectiveBlockName(BlockReference block, Transaction tr)
        {
            try
            {
                ObjectId definitionId = block.IsDynamicBlock ? block.DynamicBlockTableRecord : block.BlockTableRecord;
                if (definitionId.IsNull) return "";
                BlockTableRecord definition = tr.GetObject(definitionId, OpenMode.ForRead, false) as BlockTableRecord;
                return definition == null ? "" : definition.Name;
            }
            catch
            {
                return "";
            }
        }

        private static string GetObjectType(Entity entity)
        {
            try
            {
                RXClass rxClass = entity.GetRXClass();
                return rxClass == null || string.IsNullOrWhiteSpace(rxClass.DxfName)
                    ? entity.GetType().Name
                    : rxClass.DxfName;
            }
            catch
            {
                return entity.GetType().Name;
            }
        }

        private static string GetColorKey(Color color)
        {
            if (color == null) return "";
            return color.ColorMethod + "|" + color.ColorIndex + "|" + color.Red + "|" + color.Green + "|" + color.Blue;
        }

        private static string GetColorDisplay(Color color)
        {
            if (color == null) return "无";
            if (color.ColorMethod == ColorMethod.ByLayer) return "随层";
            if (color.ColorMethod == ColorMethod.ByBlock) return "随块";
            if (color.ColorMethod == ColorMethod.ByAci) return "索引色 " + color.ColorIndex;
            return "RGB " + color.Red + "," + color.Green + "," + color.Blue;
        }

        private static string GetLineWeightDisplay(LineWeight lineWeight)
        {
            if (lineWeight == LineWeight.ByLayer) return "随层";
            if (lineWeight == LineWeight.ByBlock) return "随块";
            if (lineWeight == LineWeight.ByLineWeightDefault) return "默认";
            int hundredths = (int)lineWeight;
            return hundredths >= 0 ? (hundredths / 100.0).ToString("0.00") + " mm" : lineWeight.ToString();
        }

        private static string GetTransparencyKey(Transparency transparency)
        {
            if (transparency.IsByLayer) return "ByLayer";
            if (transparency.IsByBlock) return "ByBlock";
            if (transparency.IsInvalid) return "Invalid";
            return "Alpha|" + transparency.Alpha;
        }

        private static string GetTransparencyDisplay(Transparency transparency)
        {
            if (transparency.IsByLayer) return "随层";
            if (transparency.IsByBlock) return "随块";
            if (transparency.IsInvalid) return "默认";
            int percent = (int)Math.Round((255 - transparency.Alpha) * 100.0 / 255.0);
            return percent + "%";
        }

        private static string GetCriterionName(FindSameCriterionKind kind)
        {
            switch (kind)
            {
                case FindSameCriterionKind.ObjectType: return "对象类型";
                case FindSameCriterionKind.Layer: return "图层";
                case FindSameCriterionKind.BlockName: return "图块名";
                case FindSameCriterionKind.Linetype: return "线型";
                case FindSameCriterionKind.Color: return "颜色";
                case FindSameCriterionKind.LineWeight: return "打印线宽";
                case FindSameCriterionKind.PolylineWidth: return "PL 几何宽度";
                case FindSameCriterionKind.Transparency: return "透明度";
                case FindSameCriterionKind.TextStyle: return "文字样式";
                case FindSameCriterionKind.DimensionStyle: return "标注样式";
                case FindSameCriterionKind.HatchPattern: return "填充图案";
                default: return kind.ToString();
            }
        }

        private static void Add(
            EntitySnapshot snapshot,
            FindSameCriterionKind kind,
            string matchValue,
            string displayValue)
        {
            snapshot.Values[kind] = new PropertyValue
            {
                MatchValue = matchValue ?? "",
                DisplayValue = displayValue ?? ""
            };
        }

        private static void TryAdd(
            EntitySnapshot snapshot,
            FindSameCriterionKind kind,
            Func<string> matchValue,
            Func<string> displayValue)
        {
            try
            {
                string match = matchValue();
                Add(snapshot, kind, match, displayValue());
            }
            catch { }
        }
    }
}
