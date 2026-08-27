using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace XYDSignTool
{
    public class TextReplaceCommands
    {
        private sealed class ReplacementResult
        {
            public int ChangedObjects { get; set; }
            public int ChangedFields { get; set; }
            public int SkippedObjects { get; set; }
        }

        private sealed class TableCellChange
        {
            public int Row { get; set; }
            public int Column { get; set; }
            public string Value { get; set; }
        }

        [CommandMethod("XYD_REPLACETEXT", CommandFlags.UsePickSet)]
        public void ReplaceTextInBatch()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;

            ObjectId[] impliedIds = GetImpliedSelectionIds(ed);
            TextReplaceWindow window = new TextReplaceWindow(impliedIds.Length > 0);
            bool? accepted = Application.ShowModalWindow(window);
            if (accepted != true) return;

            ObjectId[] manualIds = null;
            if (window.Scope == TextReplaceScope.ManualSelection)
            {
                manualIds = impliedIds.Length > 0 ? impliedIds : PromptManualSelection(ed);
                if (manualIds == null || manualIds.Length == 0)
                {
                    ed.WriteMessage("\n未选择要处理的对象，已取消批量替换。");
                    return;
                }
            }

            ReplacementResult result = new ReplacementResult();
            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                IEnumerable<ObjectId> ids = GetTargetEntityIds(doc.Database, tr, window.Scope, manualIds);
                foreach (ObjectId id in ids.Distinct())
                {
                    try
                    {
                        Entity entity = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
                        if (entity == null || IsOnLockedLayer(entity, tr))
                        {
                            result.SkippedObjects++;
                            continue;
                        }

                        int changedFields = ReplaceEntityText(entity, tr, window.Rules, window.MatchMode);
                        if (changedFields > 0)
                        {
                            result.ChangedObjects++;
                            result.ChangedFields += changedFields;
                        }
                    }
                    catch
                    {
                        result.SkippedObjects++;
                    }
                }
                tr.Commit();
            }

            string report = "批量文本替换完成。\n" +
                            "修改对象：" + result.ChangedObjects + " 个\n" +
                            "修改文字：" + result.ChangedFields + " 处";
            if (result.SkippedObjects > 0) report += "\n跳过对象：" + result.SkippedObjects + " 个";
            ed.WriteMessage("\n" + report.Replace("\n", "；"));
            Application.ShowAlertDialog(report);
        }

        private static int ReplaceEntityText(
            Entity entity,
            Transaction tr,
            IEnumerable<TextReplaceRule> rules,
            TextReplaceMatchMode mode)
        {
            DBText dbText = entity as DBText;
            if (dbText != null) return ReplaceSimpleText(dbText, dbText.TextString, value => dbText.TextString = value, rules, mode);

            MText mText = entity as MText;
            if (mText != null)
            {
                string replacement = TextReplacementEngine.ApplyToMTextContents(mText.Contents, rules, mode);
                if (string.Equals(mText.Contents ?? "", replacement, StringComparison.Ordinal)) return 0;
                mText.UpgradeOpen();
                mText.Contents = replacement;
                return 1;
            }

            BlockReference block = entity as BlockReference;
            if (block != null) return ReplaceBlockAttributes(block, tr, rules, mode);

            Dimension dimension = entity as Dimension;
            if (dimension != null && !string.IsNullOrEmpty(dimension.DimensionText))
            {
                return ReplaceSimpleText(
                    dimension,
                    dimension.DimensionText,
                    value => dimension.DimensionText = value,
                    rules,
                    mode);
            }

            MLeader leader = entity as MLeader;
            if (leader != null) return ReplaceMLeaderText(leader, rules, mode);

            Table table = entity as Table;
            if (table != null) return ReplaceTableText(table, rules, mode);

            return 0;
        }

        private static int ReplaceSimpleText(
            Entity entity,
            string source,
            Action<string> setter,
            IEnumerable<TextReplaceRule> rules,
            TextReplaceMatchMode mode)
        {
            string replacement = TextReplacementEngine.Apply(source, rules, mode);
            if (string.Equals(source ?? "", replacement, StringComparison.Ordinal)) return 0;
            entity.UpgradeOpen();
            setter(replacement);
            return 1;
        }

        private static int ReplaceBlockAttributes(
            BlockReference block,
            Transaction tr,
            IEnumerable<TextReplaceRule> rules,
            TextReplaceMatchMode mode)
        {
            int count = 0;
            foreach (ObjectId attributeId in block.AttributeCollection)
            {
                try
                {
                    AttributeReference attribute = tr.GetObject(attributeId, OpenMode.ForRead, false) as AttributeReference;
                    if (attribute == null) continue;
                    string replacement = TextReplacementEngine.Apply(attribute.TextString, rules, mode);
                    if (string.Equals(attribute.TextString ?? "", replacement, StringComparison.Ordinal)) continue;
                    attribute.UpgradeOpen();
                    attribute.TextString = replacement;
                    count++;
                }
                catch { }
            }
            return count;
        }

        private static int ReplaceMLeaderText(
            MLeader leader,
            IEnumerable<TextReplaceRule> rules,
            TextReplaceMatchMode mode)
        {
            try
            {
                if (leader.ContentType != ContentType.MTextContent) return 0;
                using (MText text = leader.MText)
                {
                    if (text == null) return 0;
                    string replacement = TextReplacementEngine.ApplyToMTextContents(text.Contents, rules, mode);
                    if (string.Equals(text.Contents ?? "", replacement, StringComparison.Ordinal)) return 0;
                    text.Contents = replacement;
                    leader.UpgradeOpen();
                    leader.MText = text;
                    return 1;
                }
            }
            catch
            {
                return 0;
            }
        }

        private static int ReplaceTableText(
            Table table,
            IEnumerable<TextReplaceRule> rules,
            TextReplaceMatchMode mode)
        {
            List<TableCellChange> changes = new List<TableCellChange>();
            for (int row = 0; row < table.Rows.Count; row++)
            {
                for (int column = 0; column < table.Columns.Count; column++)
                {
                    try
                    {
                        string source = table.Cells[row, column].TextString;
                        string replacement = TextReplacementEngine.ApplyToMTextContents(source, rules, mode);
                        if (!string.Equals(source ?? "", replacement, StringComparison.Ordinal))
                        {
                            changes.Add(new TableCellChange { Row = row, Column = column, Value = replacement });
                        }
                    }
                    catch { }
                }
            }

            if (changes.Count == 0) return 0;
            table.UpgradeOpen();
            int changedCount = 0;
            foreach (TableCellChange change in changes)
            {
                try
                {
                    table.Cells[change.Row, change.Column].TextString = change.Value;
                    changedCount++;
                }
                catch { }
            }
            return changedCount;
        }

        private static IEnumerable<ObjectId> GetTargetEntityIds(
            Database database,
            Transaction tr,
            TextReplaceScope scope,
            ObjectId[] manualIds)
        {
            if (scope == TextReplaceScope.ManualSelection)
                return manualIds ?? new ObjectId[0];

            if (scope == TextReplaceScope.CurrentSpace)
            {
                BlockTableRecord currentSpace = tr.GetObject(database.CurrentSpaceId, OpenMode.ForRead) as BlockTableRecord;
                return currentSpace == null ? new ObjectId[0] : currentSpace.Cast<ObjectId>().ToArray();
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

        private static ObjectId[] PromptManualSelection(Editor ed)
        {
            PromptSelectionOptions options = new PromptSelectionOptions
            {
                MessageForAdding = "\n请选择要批量替换文字的对象: "
            };
            PromptSelectionResult result = ed.GetSelection(options);
            return result.Status == PromptStatus.OK && result.Value != null
                ? result.Value.GetObjectIds()
                : null;
        }

        private static ObjectId[] GetImpliedSelectionIds(Editor ed)
        {
            PromptSelectionResult implied = ed.SelectImplied();
            return implied.Status == PromptStatus.OK && implied.Value != null
                ? implied.Value.GetObjectIds()
                : new ObjectId[0];
        }

        private static bool IsOnLockedLayer(Entity entity, Transaction tr)
        {
            try
            {
                LayerTableRecord layer = tr.GetObject(entity.LayerId, OpenMode.ForRead, false) as LayerTableRecord;
                return layer != null && layer.IsLocked;
            }
            catch
            {
                return false;
            }
        }
    }
}
