using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace XYDSignTool
{
    public class TitleBlockWriteBackResult
    {
        public int UpdatedSources { get; set; }
        public int UpdatedBlocks { get; set; }
        public int UpdatedAttributes { get; set; }
        public int UnchangedBlocks { get; set; }
        public int SkippedRows { get; set; }
        public int FailedSources { get; set; }
        public int BackupCount { get; set; }
        public List<string> Details { get; } = new List<string>();

        public string BuildMessage()
        {
            List<string> lines = new List<string>
            {
                "图框属性回写完成。",
                "已修改图框：" + UpdatedBlocks + " 个",
                "已修改属性：" + UpdatedAttributes + " 处",
                "无需修改：" + UnchangedBlocks + " 个"
            };
            if (BackupCount > 0) lines.Add("外部 DWG 备份：" + BackupCount + " 份");
            if (SkippedRows > 0) lines.Add("跳过记录：" + SkippedRows + " 条");
            if (FailedSources > 0) lines.Add("失败来源：" + FailedSources + " 个");
            if (Details.Count > 0)
            {
                lines.Add("");
                lines.AddRange(Details);
            }
            return string.Join("\n", lines);
        }
    }

    public static class TitleBlockAttributeWriter
    {
        private sealed class BlockUpdateResult
        {
            public bool Found { get; set; }
            public bool Recognized { get; set; }
            public int ChangedAttributes { get; set; }
        }

        public static TitleBlockWriteBackResult WriteBack(IEnumerable<TitleBlockModel> sourceItems)
        {
            TitleBlockWriteBackResult result = new TitleBlockWriteBackResult();
            List<TitleBlockModel> items = (sourceItems ?? Enumerable.Empty<TitleBlockModel>())
                .Where(item => item != null)
                .GroupBy(GetItemIdentity, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Last())
                .ToList();

            Dictionary<Document, List<TitleBlockModel>> openGroups = new Dictionary<Document, List<TitleBlockModel>>();
            Dictionary<string, List<TitleBlockModel>> externalGroups =
                new Dictionary<string, List<TitleBlockModel>>(StringComparer.OrdinalIgnoreCase);

            foreach (TitleBlockModel item in items)
            {
                if (item.IsImportedSource)
                {
                    result.SkippedRows++;
                    continue;
                }

                Document openDocument = FindOpenDocument(item);
                if (openDocument != null)
                {
                    AddToGroup(openGroups, openDocument, item);
                    continue;
                }

                string sourcePath = NormalizePath(item.SourceFilePath);
                if (item.IsExternalSource && !string.IsNullOrWhiteSpace(sourcePath))
                {
                    AddToGroup(externalGroups, sourcePath, item);
                    continue;
                }

                result.SkippedRows++;
                result.Details.Add("跳过无法定位来源的记录：" + DescribeItem(item));
            }

            List<TitleBlockRecognitionRule> rules = TitleBlockRecognitionSettings.GetActiveRules();
            foreach (KeyValuePair<Document, List<TitleBlockModel>> group in openGroups)
                WriteOpenDocument(group.Key, group.Value, rules, result);
            foreach (KeyValuePair<string, List<TitleBlockModel>> group in externalGroups)
                WriteExternalFile(group.Key, group.Value, rules, result);

            return result;
        }

        private static void WriteOpenDocument(
            Document document,
            List<TitleBlockModel> items,
            List<TitleBlockRecognitionRule> rules,
            TitleBlockWriteBackResult result)
        {
            int sourceBlocks = 0;
            int sourceAttributes = 0;
            TitleBlockWriteBackResult sourceResult = new TitleBlockWriteBackResult();
            try
            {
                using (DocumentLock documentLock = document.LockDocument())
                using (Transaction tr = document.Database.TransactionManager.StartTransaction())
                {
                    foreach (TitleBlockModel item in items)
                    {
                        try
                        {
                            BlockUpdateResult update = UpdateBlock(document.Database, tr, item, rules);
                            RecordBlockResult(update, item, sourceResult, ref sourceBlocks, ref sourceAttributes);
                        }
                        catch (System.Exception ex)
                        {
                            sourceResult.SkippedRows++;
                            sourceResult.Details.Add("跳过图框 " + DescribeItem(item) + "：" + ex.Message);
                        }
                    }
                    tr.Commit();
                }

                MergeResult(result, sourceResult);
                if (sourceBlocks > 0) result.UpdatedSources++;
                result.Details.Add(GetDocumentDisplayName(document) + "：修改 " + sourceBlocks + " 个图框 / " + sourceAttributes + " 处属性（等待用户保存）");
                try { document.Editor.Regen(); }
                catch { }
            }
            catch (System.Exception ex)
            {
                result.FailedSources++;
                result.SkippedRows += items.Count;
                result.Details.Add(GetDocumentDisplayName(document) + "：回写失败 - " + ex.Message);
            }
        }

        private static void WriteExternalFile(
            string sourcePath,
            List<TitleBlockModel> items,
            List<TitleBlockRecognitionRule> rules,
            TitleBlockWriteBackResult result)
        {
            string displayName = SafeFileName(sourcePath);
            if (!File.Exists(sourcePath))
            {
                result.FailedSources++;
                result.SkippedRows += items.Count;
                result.Details.Add(displayName + "：源文件不存在");
                return;
            }
            try
            {
                if ((File.GetAttributes(sourcePath) & FileAttributes.ReadOnly) != 0)
                    throw new IOException("文件为只读状态");

                int sourceBlocks = 0;
                int sourceAttributes = 0;
                string backupPath = "";
                TitleBlockWriteBackResult sourceResult = new TitleBlockWriteBackResult();
                using (Database database = new Database(false, true))
                {
                    database.ReadDwgFile(sourcePath, FileOpenMode.OpenForReadAndWriteNoShare, true, "");
                    DwgVersion originalVersion = database.OriginalFileVersion;
                    database.CloseInput(true);

                    using (Transaction tr = database.TransactionManager.StartTransaction())
                    {
                        foreach (TitleBlockModel item in items)
                        {
                            try
                            {
                                BlockUpdateResult update = UpdateBlock(database, tr, item, rules);
                                RecordBlockResult(update, item, sourceResult, ref sourceBlocks, ref sourceAttributes);
                            }
                            catch (System.Exception ex)
                            {
                                sourceResult.SkippedRows++;
                                sourceResult.Details.Add("跳过图框 " + DescribeItem(item) + "：" + ex.Message);
                            }
                        }
                        tr.Commit();
                    }

                    if (sourceBlocks > 0)
                    {
                        backupPath = CreateBackup(sourcePath);
                        database.SaveAs(sourcePath, originalVersion);
                    }
                }

                MergeResult(result, sourceResult);
                if (sourceBlocks > 0) result.UpdatedSources++;
                if (!string.IsNullOrWhiteSpace(backupPath))
                {
                    result.BackupCount++;
                    result.Details.Add(displayName + "：修改 " + sourceBlocks + " 个图框 / " + sourceAttributes +
                                       " 处属性；备份 " + SafeFileName(backupPath));
                }
                else
                {
                    result.Details.Add(displayName + "：没有需要写入的变更");
                }
            }
            catch (System.Exception ex)
            {
                result.FailedSources++;
                result.SkippedRows += items.Count;
                result.Details.Add(displayName + "：回写失败 - " + ex.Message);
            }
        }

        private static BlockUpdateResult UpdateBlock(
            Database database,
            Transaction tr,
            TitleBlockModel item,
            List<TitleBlockRecognitionRule> rules)
        {
            BlockUpdateResult result = new BlockUpdateResult();
            ObjectId blockId = ResolveBlockId(database, item);
            if (blockId.IsNull || !blockId.IsValid) return result;

            BlockReference block = tr.GetObject(blockId, OpenMode.ForRead, false) as BlockReference;
            if (block == null) return result;
            result.Found = true;

            string effectiveName = GetEffectiveName(block, tr);
            TitleBlockRecognitionRule rule = rules.FirstOrDefault(candidate =>
                TitleBlockRecognitionSettings.MatchesBlockName(candidate, effectiveName));
            if (rule == null) return result;
            result.Recognized = true;

            Dictionary<string, string> updates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            AddAliases(updates, rule.DrawNumTags, item.DrawNum);
            AddAliases(updates, rule.DrawTitleTags, item.DrawTitle);
            AddAliases(updates, rule.PageSizeTags, item.PageSize);
            AddAliases(updates, rule.DrawScaleTags, item.DrawScale);
            AddAliases(updates, "VERSION,VER,版本,REV", item.Version);
            AddAliases(updates, "DATE,日期", item.Date);
            AddAliases(updates, "REMARKS,REMARK,备注,NOTE", item.Remarks);

            foreach (ObjectId attributeId in block.AttributeCollection)
            {
                AttributeReference attribute = tr.GetObject(attributeId, OpenMode.ForRead, false) as AttributeReference;
                if (attribute == null) continue;
                string value;
                if (!updates.TryGetValue(attribute.Tag, out value)) continue;
                value = value ?? "";
                if (string.Equals(attribute.TextString ?? "", value, StringComparison.Ordinal)) continue;
                attribute.UpgradeOpen();
                attribute.TextString = value;
                result.ChangedAttributes++;
            }
            return result;
        }

        private static void RecordBlockResult(
            BlockUpdateResult update,
            TitleBlockModel item,
            TitleBlockWriteBackResult result,
            ref int sourceBlocks,
            ref int sourceAttributes)
        {
            if (!update.Found)
            {
                result.SkippedRows++;
                result.Details.Add("未找到原图框：" + DescribeItem(item));
                return;
            }
            if (!update.Recognized)
            {
                result.SkippedRows++;
                result.Details.Add("图框已不符合当前识别规则：" + DescribeItem(item));
                return;
            }
            if (update.ChangedAttributes == 0)
            {
                result.UnchangedBlocks++;
                return;
            }

            result.UpdatedBlocks++;
            result.UpdatedAttributes += update.ChangedAttributes;
            sourceBlocks++;
            sourceAttributes += update.ChangedAttributes;
        }

        private static ObjectId ResolveBlockId(Database database, TitleBlockModel item)
        {
            try
            {
                if (!item.ObjId.IsNull && item.ObjId.IsValid && item.ObjId.Database == database)
                    return item.ObjId;
            }
            catch { }

            long handleValue;
            if (string.IsNullOrWhiteSpace(item.SourceHandle) ||
                !long.TryParse(item.SourceHandle, System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out handleValue))
                return ObjectId.Null;

            try { return database.GetObjectId(false, new Handle(handleValue), 0); }
            catch { return ObjectId.Null; }
        }

        private static string GetEffectiveName(BlockReference block, Transaction tr)
        {
            ObjectId definitionId = ObjectId.Null;
            try
            {
                definitionId = block.IsDynamicBlock ? block.DynamicBlockTableRecord : block.BlockTableRecord;
            }
            catch { }
            if (definitionId.IsNull) return "";
            try
            {
                BlockTableRecord definition = tr.GetObject(definitionId, OpenMode.ForRead, false) as BlockTableRecord;
                return definition == null ? "" : definition.Name;
            }
            catch { return ""; }
        }

        private static void AddAliases(Dictionary<string, string> updates, string aliases, string value)
        {
            foreach (string alias in TitleBlockRecognitionSettings.SplitNames(aliases))
                updates[alias] = value ?? "";
        }

        private static Document FindOpenDocument(TitleBlockModel item)
        {
            foreach (Document document in Application.DocumentManager)
            {
                try
                {
                    if (!item.ObjId.IsNull && item.ObjId.IsValid && item.ObjId.Database == document.Database)
                        return document;
                }
                catch { }

                if (PathsEqual(item.SourceFilePath, document.Name)) return document;
            }
            return null;
        }

        private static string CreateBackup(string sourcePath)
        {
            string directory = Path.GetDirectoryName(sourcePath);
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string baseName = Path.GetFileName(sourcePath) + ".XYDBackup_" + timestamp;
            string backupPath = Path.Combine(directory, baseName + ".bak");
            int index = 2;
            while (File.Exists(backupPath))
            {
                backupPath = Path.Combine(directory, baseName + "_" + index + ".bak");
                index++;
            }
            File.Copy(sourcePath, backupPath, false);
            return backupPath;
        }

        private static string GetItemIdentity(TitleBlockModel item)
        {
            if (item.IsImportedSource) return "JSON|" + Guid.NewGuid();
            string path = NormalizePath(item.SourceFilePath);
            string handle = item.SourceHandle ?? "";
            if (string.IsNullOrWhiteSpace(handle))
            {
                try { handle = item.ObjId.Handle.ToString(); }
                catch { }
            }
            return (path ?? "") + "|" + handle;
        }

        private static void AddToGroup<TKey>(Dictionary<TKey, List<TitleBlockModel>> groups, TKey key, TitleBlockModel item)
        {
            List<TitleBlockModel> group;
            if (!groups.TryGetValue(key, out group))
            {
                group = new List<TitleBlockModel>();
                groups[key] = group;
            }
            group.Add(item);
        }

        private static void MergeResult(TitleBlockWriteBackResult target, TitleBlockWriteBackResult source)
        {
            target.UpdatedBlocks += source.UpdatedBlocks;
            target.UpdatedAttributes += source.UpdatedAttributes;
            target.UnchangedBlocks += source.UnchangedBlocks;
            target.SkippedRows += source.SkippedRows;
            target.Details.AddRange(source.Details);
        }

        private static bool PathsEqual(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
            return string.Equals(NormalizePath(left), NormalizePath(right), StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "";
            try { return Path.GetFullPath(path.Trim()); }
            catch { return path.Trim(); }
        }

        private static string DescribeItem(TitleBlockModel item)
        {
            return item.SourceDisplayName + " / " + item.SpaceDisplayName + " / " + (item.DrawNum ?? "");
        }

        private static string GetDocumentDisplayName(Document document)
        {
            try { return Path.GetFileName(document.Name); }
            catch { return document.Name; }
        }

        private static string SafeFileName(string path)
        {
            try { return Path.GetFileName(path); }
            catch { return path ?? ""; }
        }
    }
}
