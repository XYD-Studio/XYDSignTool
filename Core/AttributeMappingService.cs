using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace XYDSignTool
{
    public static class AttributeMappingService
    {
        public static string EffectiveName(BlockReference block, Transaction tr)
        {
            var definition = (BlockTableRecord)tr.GetObject(block.IsDynamicBlock ? block.DynamicBlockTableRecord : block.BlockTableRecord, OpenMode.ForRead);
            return definition.Name;
        }
        public static List<AttributeSample> Samples(Database db, IEnumerable<ObjectId> ids)
        {
            var result = new List<AttributeSample>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var id in ids.Distinct())
                {
                    var block = tr.GetObject(id, OpenMode.ForRead, false) as BlockReference;
                    if (block == null) continue;
                    string name = EffectiveName(block, tr);
                    var definition = (BlockTableRecord)tr.GetObject(block.IsDynamicBlock ? block.DynamicBlockTableRecord : block.BlockTableRecord, OpenMode.ForRead);
                    if (definition.IsFromExternalReference || definition.IsFromOverlayReference) continue;
                    var values = ReadAttributes(block, tr);
                    foreach (var childId in definition)
                    {
                        var a = tr.GetObject(childId, OpenMode.ForRead) as AttributeDefinition;
                        if (a == null) continue;
                        AttributeReference actual;
                        values.TryGetValue(a.Tag, out actual);
                        result.Add(new AttributeSample { BlockName = name, Tag = a.Tag, Prompt = a.Prompt,
                            Example = actual == null ? a.TextString : actual.TextString,
                            ReadOnly = a.Constant || actual == null || IsField(actual) });
                    }
                    foreach (var pair in values)
                        if (!result.Any(s => s.BlockName == name && string.Equals(s.Tag, pair.Key, StringComparison.OrdinalIgnoreCase)))
                            result.Add(new AttributeSample { BlockName = name, Tag = pair.Key, Example = pair.Value.TextString, ReadOnly = IsField(pair.Value) });
                }
            }
            return result.GroupBy(s => s.BlockName + "\u001f" + s.Tag, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
        }
        public static Dictionary<string, AttributeReference> ReadAttributes(BlockReference block, Transaction tr)
        {
            var result = new Dictionary<string, AttributeReference>(StringComparer.OrdinalIgnoreCase);
            foreach (ObjectId id in block.AttributeCollection)
            {
                var attr = tr.GetObject(id, OpenMode.ForRead, false) as AttributeReference;
                if (attr == null) continue;
                if (result.ContainsKey(attr.Tag)) throw new InvalidOperationException("图块有重复属性标签：" + attr.Tag);
                result[attr.Tag] = attr;
            }
            return result;
        }
        public static bool IsField(AttributeReference attribute)
        {
            try { return attribute.HasFields; } catch { return true; }
        }
        public static List<AttributeRecord> Scan(Database db, string source, AttributeMappingProfile profile, string scope)
        {
            profile.Validate(); var result = new List<AttributeRecord>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var layouts = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
                foreach (DBDictionaryEntry entry in layouts)
                {
                    var layout = (Layout)tr.GetObject(entry.Value, OpenMode.ForRead);
                    if (scope == "模型" && !layout.ModelType || scope == "布局" && layout.ModelType) continue;
                    var space = (BlockTableRecord)tr.GetObject(layout.BlockTableRecordId, OpenMode.ForRead);
                    foreach (ObjectId id in space)
                    {
                        try
                        {
                            var block = tr.GetObject(id, OpenMode.ForRead, false) as BlockReference;
                            if (block == null) continue;
                            string name = EffectiveName(block, tr);
                            var rule = profile.Rules.FirstOrDefault(r => string.Equals(r.BlockName, name, StringComparison.OrdinalIgnoreCase));
                            if (rule == null) continue;
                            var record = Capture(db, source, layout.LayoutName, block, tr);
                            var attrs = ReadAttributes(block, tr);
                            var definition = (BlockTableRecord)tr.GetObject(block.IsDynamicBlock ? block.DynamicBlockTableRecord : block.BlockTableRecord, OpenMode.ForRead);
                            var layer = (LayerTableRecord)tr.GetObject(block.LayerId, OpenMode.ForRead);
                            bool locked = layer.IsLocked || definition.IsFromExternalReference || definition.IsFromOverlayReference;
                            foreach (var field in rule.Fields)
                            {
                                AttributeReference a; attrs.TryGetValue(field.Tag, out a);
                                string value = a == null ? "" : a.TextString ?? "";
                                if (a == null && field.ReadOnly)
                                {
                                    foreach (ObjectId definitionId in definition)
                                    {
                                        var constant = tr.GetObject(definitionId, OpenMode.ForRead) as AttributeDefinition;
                                        if (constant != null && constant.Constant && string.Equals(constant.Tag, field.Tag, StringComparison.OrdinalIgnoreCase)) value = constant.TextString ?? "";
                                    }
                                }
                                bool readOnly = field.ReadOnly || locked || a == null || IsField(a);
                                record.Cells[field.ColumnId] = new AttributeCell { Tag = field.Tag, Value = value, Original = value,
                                    ReadOnly = readOnly, Reason = locked ? "锁定或外部参照" : a == null ? "常量或缺失属性" : IsField(a) ? "字段控制" : field.ReadOnly ? "只读属性" : "" };
                            }
                            record.Status = locked ? "只读：锁定或外部参照" : "";
                            result.Add(record);
                        }
                        catch (System.Exception ex) { throw new InvalidOperationException(Path.GetFileName(source) + " / " + layout.LayoutName + " / " + id.Handle + "：" + ex.Message, ex); }
                    }
                }
            }
            return result;
        }
        public static AttributeRecord Capture(Database db, string path, string layout, BlockReference block, Transaction tr)
        {
            var r = new AttributeRecord { ObjectId = block.ObjectId, OpenDatabase = db, Fingerprint = db.FingerprintGuid,
                FilePath = path, Layout = layout, Handle = block.Handle.ToString(), BlockName = EffectiveName(block, tr) };
            var layer = (LayerTableRecord)tr.GetObject(block.LayerId, OpenMode.ForRead);
            var definition = (BlockTableRecord)tr.GetObject(block.BlockTableRecord, OpenMode.ForRead);
            bool locked = layer.IsLocked || definition.IsFromExternalReference || definition.IsFromOverlayReference;
            foreach (var pair in ReadAttributes(block, tr))
            {
                r.OriginalAttributes[pair.Key] = pair.Value.TextString ?? "";
                r.OriginalVisibility[pair.Key] = pair.Value.Invisible;
                if (locked || IsField(pair.Value)) r.Restrictions[pair.Key] = locked ? "锁定或外部参照" : "字段控制";
            }
            return r;
        }
        public static Document FindOpen(AttributeRecord record)
        {
            foreach (Document document in Application.DocumentManager)
                if (document.Database == record.OpenDatabase || DocumentMatchesPath(document, record.FilePath)) return document;
            return null;
        }
        public static bool DocumentMatchesPath(Document document, string path)
        {
            string file = document.Database.Filename;
            return !string.IsNullOrWhiteSpace(file) && Path.IsPathRooted(file) && SamePath(file, path) || Path.IsPathRooted(document.Name) && SamePath(document.Name, path);
        }
        public static bool SamePath(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
            try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); } catch { return false; }
        }
        public static ObjectId Resolve(Database db, AttributeRecord row)
        {
            if (row.OpenDatabase == db && !row.ObjectId.IsNull && row.ObjectId.IsValid) return row.ObjectId;
            if (!string.IsNullOrEmpty(row.Fingerprint) && db.FingerprintGuid != row.Fingerprint) throw new InvalidOperationException("源 DWG 标识已改变，请重新提取。");
            long handle;
            if (!long.TryParse(row.Handle, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out handle)) throw new InvalidOperationException("缺少有效源句柄。");
            return db.GetObjectId(false, new Handle(handle), 0);
        }
        public static List<AttributeRecord> Summarize(IEnumerable<AttributeRecord> rows, AttributeMappingProfile p)
        {
            var keys = p.Columns.Where(c => c.Group).ToList();
            if (keys.Count == 0) throw new InvalidOperationException("请至少选择一个汇总分组列。");
            if (keys.Any(c => c.Id == p.QuantityColumnId)) throw new InvalidOperationException("数量列不能同时作为分组列。");
            var result = new List<AttributeRecord>();
            foreach (var group in rows.GroupBy(r => string.Concat(keys.Select(c => r.Value(c.Id).Length + ":" + r.Value(c.Id)))))
            {
                var summary = new AttributeRecord { Imported = true };
                foreach (var col in p.Columns)
                {
                    string value;
                    if (col.Id == p.QuantityColumnId)
                    {
                        decimal sum = 0;
                        foreach (var row in group)
                        {
                            decimal n;
                            if (!decimal.TryParse(row.Value(col.Id), NumberStyles.Number, CultureInfo.InvariantCulture, out n) && !decimal.TryParse(row.Value(col.Id), out n))
                                throw new InvalidOperationException("数量不是数字：" + row.SourceDisplay + " / " + row.Handle + " / " + row.Value(col.Id));
                            sum += n;
                        }
                        value = sum.ToString(CultureInfo.InvariantCulture);
                    }
                    else value = string.Join("；", group.Select(r => r.Value(col.Id)).Distinct());
                    summary.Cells[col.Id] = new AttributeCell { Value = value, Original = value, ReadOnly = true };
                }
                if (string.IsNullOrWhiteSpace(p.QuantityColumnId)) summary.Cells["__COUNT"] = new AttributeCell { Value = group.Count().ToString(), ReadOnly = true };
                result.Add(summary);
            }
            return result;
        }
    }
    public class MappingWriteResult
    {
        public int Updated { get; set; }
        public int Backups { get; set; }
        public List<string> Details { get; set; } = new List<string>();
        public override string ToString() { return "已修改图块：" + Updated + "\n外部文件备份：" + Backups + "\n" + string.Join("\n", Details); }
    }
    public static class AttributeMappingWriter
    {
        public static MappingWriteResult Write(IEnumerable<AttributeRecord> source, AttributeMappingProfile profile, bool signatures, bool removeSignatures = false)
        {
            var result = new MappingWriteResult();
            var rows = source.Where(r => !r.Imported).ToList();
            var groups = rows.GroupBy(r => AttributeMappingService.FindOpen(r) != null ? "OPEN:" + AttributeMappingService.FindOpen(r).Database.GetHashCode() : "FILE:" + (Path.IsPathRooted(r.FilePath ?? "") ? Path.GetFullPath(r.FilePath) : r.FilePath ?? ""), StringComparer.OrdinalIgnoreCase);
            foreach (var group in groups)
            {
                var items = group.ToList(); Document doc = AttributeMappingService.FindOpen(items[0]);
                try
                {
                    if (doc != null)
                    {
                        using (var loc = doc.LockDocument()) { Process(doc.Database, items, profile, signatures, removeSignatures, result); AcceptChanges(doc.Database, items); }
                        doc.Editor.Regen(); result.Details.Add(items[0].SourceDisplay + "：内存已更新，等待保存。");
                    }
                    else
                    {
                        string file = items[0].FilePath;
                        if (string.IsNullOrWhiteSpace(file) || !Path.IsPathRooted(file)) throw new IOException("源图纸未保存或已关闭，请重新提取有效来源。");
                        if ((File.GetAttributes(file) & FileAttributes.ReadOnly) != 0) throw new IOException("文件只读。");
                        using (var db = new Database(false, true))
                        {
                            db.ReadDwgFile(file, FileOpenMode.OpenForReadAndWriteNoShare, true, "");
                            var version = db.OriginalFileVersion; db.CloseInput(true);
                            var local = new MappingWriteResult(); Process(db, items, profile, signatures, removeSignatures, local);
                            if (local.Updated > 0)
                            {
                                string backup = file + ".XYDBackup_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".bak";
                                File.Copy(file, backup, false); result.Backups++;
                                db.SaveAs(file, version);
                                result.Details.Add(Path.GetFileName(file) + "：已保存；备份 " + Path.GetFileName(backup));
                            }
                            result.Updated += local.Updated;
                            result.Details.AddRange(local.Details);
                            AcceptChanges(db, items);
                            foreach (var row in items) row.OpenDatabase = null;
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    foreach (var row in items) row.Status = "失败：" + ex.Message;
                    result.Details.Add(items[0].SourceDisplay + "：失败（本文件未保存） - " + ex.Message);
                }
            }
            return result;
        }
        private static void AcceptChanges(Database db, IEnumerable<AttributeRecord> rows)
        {
            using (var tr = db.TransactionManager.StartTransaction())
                foreach (var row in rows.Where(r => r.Status == "已回写"))
                {
                    var block = (BlockReference)tr.GetObject(AttributeMappingService.Resolve(db, row), OpenMode.ForRead);
                    StandardSignAdapter.Synchronize(row, AttributeMappingService.ReadAttributes(block, tr));
                }
        }
        private static void Process(Database db, List<AttributeRecord> rows, AttributeMappingProfile p, bool signatures, bool remove, MappingWriteResult result)
        {
            int updated = 0;
            var failures = new List<string>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                // Validate all edits for each original block before writing either language row.
                foreach (var group in rows.GroupBy(r => r.Handle, StringComparer.OrdinalIgnoreCase))
                {
                    var row = group.First();
                    List<PreparedSignature> prepared = null;
                    try
                    {
                        var rule = p.Rules.FirstOrDefault(r => string.Equals(r.BlockName, row.BlockName, StringComparison.OrdinalIgnoreCase));
                        var updates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var record in group)
                            foreach (var change in StandardSignAdapter.BuildUpdates(record))
                            {
                                string previous;
                                if (updates.TryGetValue(change.Key, out previous) && previous != change.Value) throw new InvalidOperationException("共享属性修改冲突：" + change.Key + "（模式：" + string.Join("、", group.Select(r => r.Mode)) + "）");
                                updates[change.Key] = change.Value;
                            }
                        if (updates.Count == 0 && (rule == null || rule.Signatures.Count == 0)) { foreach (var record in group) record.Status = "无修改"; continue; }
                        var block = tr.GetObject(AttributeMappingService.Resolve(db, row), OpenMode.ForRead, false) as BlockReference;
                        if (block == null || !string.Equals(AttributeMappingService.EffectiveName(block, tr), row.BlockName, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("源图块已改变或删除。");
                        var owner = (BlockTableRecord)tr.GetObject(block.OwnerId, OpenMode.ForRead);
                        if (!owner.IsLayout || ((Layout)tr.GetObject(owner.LayoutId, OpenMode.ForRead)).LayoutName != row.Layout) throw new InvalidOperationException("源图块所在空间已改变，请重新提取。");
                        var layer = (LayerTableRecord)tr.GetObject(block.LayerId, OpenMode.ForRead);
                        var definition = (BlockTableRecord)tr.GetObject(block.IsDynamicBlock ? block.DynamicBlockTableRecord : block.BlockTableRecord, OpenMode.ForRead);
                        if (layer.IsLocked || definition.IsFromExternalReference || definition.IsFromOverlayReference) throw new InvalidOperationException("锁定或外部参照，不能回写。");
                        var attrs = AttributeMappingService.ReadAttributes(block, tr);
                        foreach (var change in updates)
                        {
                            AttributeReference a; string original;
                            if (!attrs.TryGetValue(change.Key, out a)) throw new InvalidOperationException("缺少属性：" + change.Key);
                            if (AttributeMappingService.IsField(a)) throw new InvalidOperationException("字段控制属性：" + change.Key);
                            if (!row.OriginalAttributes.TryGetValue(change.Key, out original) || a.TextString != original) throw new InvalidOperationException("源属性已改变：" + change.Key + "，请重新提取。");
                        }
                        if (rule != null)
                        {
                            foreach (var slot in rule.Signatures)
                            {
                                AttributeReference a; string original;
                                if (!attrs.TryGetValue(slot.NameTag, out a)) throw new InvalidOperationException("缺少签名姓名属性：" + slot.NameTag);
                                if (AttributeMappingService.IsField(a)) throw new InvalidOperationException("签名姓名属性由字段控制：" + slot.NameTag);
                                if (!row.OriginalAttributes.TryGetValue(slot.NameTag, out original) || a.TextString != original) throw new InvalidOperationException("签名姓名源值已改变，请重新提取：" + slot.NameTag);
                            }
                        }
                        // Preflight performs no destination mutation. A failed signature blocks the entire frame.
                        prepared = signatures && rule != null ? ElectronicSignatureService.Prepare(p, rule, attrs, updates) : null;
                        bool changed = updates.Any(kv => attrs[kv.Key].TextString != kv.Value);
                        foreach (var change in updates)
                        {
                            var a = attrs[change.Key]; if (a.TextString == change.Value) continue;
                            a.UpgradeOpen(); a.TextString = change.Value;
                            if (a.IsMTextAttribute) a.UpdateMTextAttribute();
                        }
                        if (rule != null)
                        {
                            foreach (var slot in rule.Signatures)
                            {
                                var a = attrs[slot.NameTag]; bool invisible = slot.Countersign;
                                if (a.Invisible != invisible || a.Visible == invisible) { a.UpgradeOpen(); a.Invisible = invisible; a.Visible = !invisible; changed = true; }
                            }
                            if (signatures || remove) changed |= ElectronicSignatureService.Apply(db, tr, block, rule, prepared, remove);
                        }
                        if (changed) updated++;
                        foreach (var record in group) record.Status = "已回写";
                    }
                    catch (System.Exception ex)
                    {
                        // Do not commit a partial frame if an exception occurred during mutation.
                        failures.Add(row.SourceDisplay + " / " + row.Handle + "：" + ex.Message);
                    }
                    finally { if (prepared != null) foreach (var s in prepared) s.Dispose(); }
                }
                if (failures.Count > 0) throw new InvalidOperationException(string.Join("\n", failures));
                ElectronicSignatureService.PurgeUnused(db, tr);
                tr.Commit();
            }
            result.Updated += updated;
        }
    }
}
