using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using System;
using System.Collections.Generic;
using System.IO;
using Application = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace XYDSignTool
{
    public static class BlockManager
    {
        public static void InsertBlockFromLibrary(string blockName)
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var db = doc.Database;
            var ed = doc.Editor;

            string dllPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string dllDir = Path.GetDirectoryName(dllPath);
            string blocksDir = Path.Combine(dllDir, "Blocks");

            if (!Directory.Exists(blocksDir))
            {
                ed.WriteMessage($"\n[XYD 错误] 找不到图库文件夹: {blocksDir}\n");
                return;
            }

            string[] libraryFiles = Directory.GetFiles(blocksDir, "*.dwg");
            if (libraryFiles.Length == 0) return;

            bool isTitleBlock = blockName.StartsWith("XYD-TITLEBLOCK_", StringComparison.OrdinalIgnoreCase);
            LibraryBlockData libraryBlock = FindLibraryBlock(libraryFiles, blockName);
            ObjectId blockId = ObjectId.Null;

            using (DocumentLock loc = doc.LockDocument())
            {
                // 1. 尝试从当前图纸获取块定义
                bool hasBlockInCurrentDrawing = false;
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTable bt = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                    if (bt.Has(blockName))
                    {
                        blockId = bt[blockName];
                        hasBlockInCurrentDrawing = true;
                    }
                    tr.Commit();
                }

                // 2. 图框块每次都刷新定义，避免当前图纸里的旧块定义反复带出空 PAGESIZE。
                if (blockId == ObjectId.Null || isTitleBlock)
                {
                    if (libraryBlock == null || !libraryBlock.Found)
                    {
                        if (blockId == ObjectId.Null)
                        {
                            ed.WriteMessage($"\n[XYD 错误] 扫描了所有母盘，均未找到图块 '{blockName}'！\n");
                            return;
                        }

                        ed.WriteMessage($"\n[XYD 警告] 未找到图块母盘，已使用当前图纸中的旧定义插入 '{blockName}'。\n");
                    }
                    else
                    {
                        DuplicateRecordCloning cloneMode = hasBlockInCurrentDrawing && isTitleBlock
                            ? DuplicateRecordCloning.Replace
                            : DuplicateRecordCloning.Ignore;

                        string importError;
                        if (!ImportBlockDefinition(db, libraryBlock.LibraryPath, blockName, cloneMode, out importError))
                        {
                            if (blockId == ObjectId.Null)
                            {
                                ed.WriteMessage($"\n[XYD 错误] 导入图块 '{blockName}' 失败: {importError}\n");
                                return;
                            }

                            ed.WriteMessage($"\n[XYD 警告] 刷新图块定义失败，将使用当前图纸中的旧定义: {importError}\n");
                        }
                    }

                    // 重新获取新克隆的 ID
                    using (Transaction tr2 = db.TransactionManager.StartTransaction())
                    {
                        BlockTable bt2 = tr2.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                        blockId = bt2[blockName];
                        tr2.Commit();
                    }
                }

                if (blockId == ObjectId.Null) return;

                // 3. 提示用户插入点
                PromptPointOptions ppo = new PromptPointOptions($"\n请指定 [{blockName}] 的插入点: ");
                PromptPointResult ppr = ed.GetPoint(ppo);

                if (ppr.Status == PromptStatus.OK)
                {
                    using (Transaction tr3 = db.TransactionManager.StartTransaction())
                    {
                        BlockTableRecord currentSpace = tr3.GetObject(db.CurrentSpaceId, OpenMode.ForWrite) as BlockTableRecord;
                        BlockReference br = new BlockReference(ppr.Value, blockId);
                        currentSpace.AppendEntity(br);
                        tr3.AddNewlyCreatedDBObject(br, true);

                        // 4. ★ 核心修复：遍历块定义（无论是新克隆的还是以前就在图里的），直接把定义的默认值赋给新的块引用！
                        BlockTableRecord btr = tr3.GetObject(blockId, OpenMode.ForRead) as BlockTableRecord;
                        if (btr.HasAttributeDefinitions)
                        {
                            foreach (ObjectId id in btr)
                            {
                                DBObject obj = tr3.GetObject(id, OpenMode.ForRead);
                                AttributeDefinition attDef = obj as AttributeDefinition;
                                if (attDef != null && !attDef.Constant)
                                {
                                    using (AttributeReference attRef = new AttributeReference())
                                    {
                                        attRef.SetAttributeFromBlock(attDef, br.BlockTransform);
                                        attRef.TextString = ResolveAttributeValue(blockName, attDef, libraryBlock);
                                        br.AttributeCollection.AppendAttribute(attRef);
                                        tr3.AddNewlyCreatedDBObject(attRef, true);
                                    }
                                }
                            }
                        }
                        tr3.Commit();
                    }
                    ed.WriteMessage($"\n[XYD] 成功插入 [{blockName}]。\n");
                }
            }
        }

        private static LibraryBlockData FindLibraryBlock(string[] libraryFiles, string blockName)
        {
            foreach (string libPath in libraryFiles)
            {
                using (Database extDb = new Database(false, true))
                {
                    try
                    {
                        extDb.ReadDwgFile(libPath, FileShare.Read, true, "");
                        using (Transaction extTr = extDb.TransactionManager.StartTransaction())
                        {
                            BlockTable extBt = extTr.GetObject(extDb.BlockTableId, OpenMode.ForRead) as BlockTable;
                            if (extBt.Has(blockName))
                            {
                                ObjectId srcBlockId = extBt[blockName];
                                Dictionary<string, string> defaults = ReadAttributeDefaults(extTr, srcBlockId);
                                Dictionary<string, string> referenceValues = ReadLibraryReferenceAttributes(extTr, extDb, blockName);
                                foreach (var kvp in referenceValues)
                                {
                                    if (!defaults.ContainsKey(kvp.Key) || !string.IsNullOrWhiteSpace(kvp.Value))
                                    {
                                        defaults[kvp.Key] = kvp.Value;
                                    }
                                }

                                LibraryBlockData data = new LibraryBlockData
                                {
                                    Found = true,
                                    LibraryPath = libPath,
                                    AttributeDefaults = defaults
                                };
                                extTr.Commit();
                                return data;
                            }
                            extTr.Commit();
                        }
                    }
                    catch { }
                }
            }

            return new LibraryBlockData
            {
                Found = false,
                AttributeDefaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            };
        }

        private static Dictionary<string, string> ReadAttributeDefaults(Transaction tr, ObjectId blockId)
        {
            Dictionary<string, string> defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            BlockTableRecord btr = tr.GetObject(blockId, OpenMode.ForRead) as BlockTableRecord;
            if (btr == null) return defaults;

            foreach (ObjectId id in btr)
            {
                AttributeDefinition attDef = tr.GetObject(id, OpenMode.ForRead) as AttributeDefinition;
                if (attDef == null || attDef.Constant) continue;
                defaults[attDef.Tag] = attDef.TextString ?? "";
            }

            return defaults;
        }

        private static Dictionary<string, string> ReadLibraryReferenceAttributes(Transaction tr, Database db, string blockName)
        {
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                DBDictionary layoutDict = tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead) as DBDictionary;
                if (layoutDict == null) return values;

                foreach (DBDictionaryEntry entry in layoutDict)
                {
                    Layout layout = tr.GetObject(entry.Value, OpenMode.ForRead) as Layout;
                    if (layout == null) continue;

                    BlockTableRecord space = tr.GetObject(layout.BlockTableRecordId, OpenMode.ForRead) as BlockTableRecord;
                    if (space == null) continue;

                    foreach (ObjectId id in space)
                    {
                        BlockReference br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                        if (br == null) continue;

                        string effectiveName = GetEffectiveBlockName(br, tr);
                        if (!effectiveName.Equals(blockName, StringComparison.OrdinalIgnoreCase)) continue;

                        foreach (ObjectId attId in br.AttributeCollection)
                        {
                            AttributeReference attRef = tr.GetObject(attId, OpenMode.ForRead) as AttributeReference;
                            if (attRef == null) continue;

                            string text = attRef.TextString ?? "";
                            if (!values.ContainsKey(attRef.Tag) || !string.IsNullOrWhiteSpace(text))
                            {
                                values[attRef.Tag] = text;
                            }
                        }
                    }
                }
            }
            catch { }

            return values;
        }

        private static bool ImportBlockDefinition(Database targetDb, string libraryPath, string blockName, DuplicateRecordCloning cloneMode, out string error)
        {
            error = null;

            try
            {
                using (Database extDb = new Database(false, true))
                {
                    extDb.ReadDwgFile(libraryPath, FileShare.Read, true, "");
                    using (Transaction extTr = extDb.TransactionManager.StartTransaction())
                    {
                        BlockTable extBt = extTr.GetObject(extDb.BlockTableId, OpenMode.ForRead) as BlockTable;
                        if (!extBt.Has(blockName))
                        {
                            error = "母盘中不存在该图块。";
                            return false;
                        }

                        ObjectIdCollection ids = new ObjectIdCollection { extBt[blockName] };
                        IdMapping mapping = new IdMapping();
                        targetDb.WblockCloneObjects(ids, targetDb.BlockTableId, mapping, cloneMode, false);
                        extTr.Commit();
                    }
                }
                return true;
            }
            catch (System.Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static string ResolveAttributeValue(string blockName, AttributeDefinition attDef, LibraryBlockData libraryBlock)
        {
            string value = "";
            bool hasLibraryDefault = libraryBlock != null &&
                                     libraryBlock.AttributeDefaults != null &&
                                     libraryBlock.AttributeDefaults.TryGetValue(attDef.Tag, out value);

            if (!hasLibraryDefault) value = attDef.TextString ?? "";

            if (attDef.Tag.Equals("PAGESIZE", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(value))
            {
                string inferred = InferPageSizeFromBlockName(blockName);
                if (!string.IsNullOrWhiteSpace(inferred)) value = inferred;
            }

            return value ?? "";
        }

        private static string InferPageSizeFromBlockName(string blockName)
        {
            const string prefix = "XYD-TITLEBLOCK_";
            if (string.IsNullOrWhiteSpace(blockName)) return "";
            if (!blockName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return "";
            string suffix = blockName.Substring(prefix.Length).Trim();
            string extracted = TitleBlockRecognitionSettings.ExtractPageSizeFromBlockName(suffix);
            return string.IsNullOrWhiteSpace(extracted) ? suffix : extracted;
        }

        private static string GetEffectiveBlockName(BlockReference br, Transaction tr)
        {
            try
            {
                if (br.IsDynamicBlock)
                {
                    BlockTableRecord dynamicBtr = tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
                    if (dynamicBtr != null) return dynamicBtr.Name;
                }
            }
            catch { }

            return br.Name;
        }

        private class LibraryBlockData
        {
            public bool Found { get; set; }
            public string LibraryPath { get; set; }
            public Dictionary<string, string> AttributeDefaults { get; set; }
        }
    }
}
