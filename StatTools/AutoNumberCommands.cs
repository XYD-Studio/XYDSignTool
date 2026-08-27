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
    public sealed class AutoNumberCommands
    {
        private sealed class SemiHistoryItem
        {
            public ObjectId BlockId { get; set; }
            public List<AutoNumberOldAttribute> OldValues { get; set; }
            public long PreviousNextNumber { get; set; }
            public AutoNumberSettings PreviousStoredState { get; set; }
            public bool StateWasSaved { get; set; }
            public int ChangedAttributes { get; set; }
            public string WrittenValue { get; set; }
        }

        [CommandMethod("XYD_AUTONUMBER", CommandFlags.UsePickSet)]
        public void AutoNumber()
        {
            Document document = Application.DocumentManager.MdiActiveDocument;
            if (document == null) return;
            Editor editor = document.Editor;

            ObjectId[] impliedIds = GetImpliedIds(editor);
            AutoNumberMode? mode = PromptMode(editor);
            if (!mode.HasValue) return;

            if (mode.Value == AutoNumberMode.SemiAutomatic)
                RunSemiAutomatic(document, impliedIds);
            else if (mode.Value == AutoNumberMode.FullyAutomatic)
                RunFullyAutomatic(document, impliedIds);
            else
                RunInsertAndShift(document, impliedIds);
        }

        private static void RunSemiAutomatic(Document document, ObjectId[] impliedIds)
        {
            Editor editor = document.Editor;
            List<AutoNumberCandidate> preselected = AutoNumberingService.ScanCurrentSpaceCandidates(document.Database, editor, impliedIds);
            AutoNumberCandidate sample = preselected.Count == 1 ? preselected[0] : null;
            if (sample == null || sample.AttributeValues.Count == 0)
            {
                ObjectId sampleId = PromptBlock(editor, "\n请选择一个排号样本图块: ");
                if (sampleId.IsNull) return;
                sample = AutoNumberingService.ScanCurrentSpaceCandidates(document.Database, editor, new[] { sampleId }).FirstOrDefault();
            }

            if (sample == null || sample.AttributeValues.Count == 0)
            {
                editor.WriteMessage("\n所选图块没有可编辑的属性。");
                return;
            }

            List<AutoNumberCandidate> sampleList = new List<AutoNumberCandidate> { sample };
            List<AutoNumberAttributeOption> options = AutoNumberingService.BuildAttributeOptions(sampleList);
            Dictionary<string, AutoNumberSettings> savedStates = AutoNumberingService.LoadStates(document.Database);
            string initialTag = ChooseInitialAttribute(options, savedStates);
            AutoNumberSettings settings = ShowSettingsWindow(document, options, savedStates,
                BuildSampleValues(options), false, initialTag, null, false);
            if (settings == null) return;

            long nextNumber = AutoNumberingService.NormalizeNumber(settings.StartNumber, settings);
            Dictionary<string, AutoNumberSettings> persistedStates = AutoNumberingService.LoadStates(document.Database);
            AutoNumberSettings currentStoredState;
            persistedStates.TryGetValue(settings.AttributeTag, out currentStoredState);
            if (currentStoredState != null) currentStoredState = currentStoredState.Clone();

            HashSet<ObjectId> processed = new HashSet<ObjectId>();
            Stack<SemiHistoryItem> history = new Stack<SemiHistoryItem>();
            int changedBlocks = 0;
            int changedAttributes = 0;
            int unchangedBlocks = 0;
            int skipped = 0;

            while (true)
            {
                PromptEntityOptions prompt = new PromptEntityOptions(
                    "\n点选下一个需要排号的图块 [撤销(Undo)/设置(Settings)/结束(Exit)] <结束>: ")
                {
                    AllowNone = true,
                    AppendKeywordsToMessage = false
                };
                prompt.SetRejectMessage("\n请选择图块参照。");
                prompt.AddAllowedClass(typeof(BlockReference), true);
                prompt.Keywords.Add("Undo");
                prompt.Keywords.Add("Settings");
                prompt.Keywords.Add("Exit");

                PromptEntityResult picked = editor.GetEntity(prompt);
                if (picked.Status == PromptStatus.Keyword)
                {
                    if (string.Equals(picked.StringResult, "Undo", StringComparison.OrdinalIgnoreCase))
                    {
                        if (history.Count == 0)
                        {
                            editor.WriteMessage("\n当前会话没有可撤销的排号。");
                            continue;
                        }
                        SemiHistoryItem last = history.Pop();
                        using (DocumentLock documentLock = document.LockDocument())
                        using (Transaction tr = document.Database.TransactionManager.StartTransaction())
                        {
                            int restored;
                            AutoNumberingService.RestoreAttributes(tr, last.OldValues, out restored);
                            if (last.StateWasSaved)
                                AutoNumberingService.RestoreState(document.Database, tr, settings.AttributeTag, last.PreviousStoredState);
                            tr.Commit();
                        }
                        processed.Remove(last.BlockId);
                        nextNumber = last.PreviousNextNumber;
                        if (last.StateWasSaved)
                        {
                            currentStoredState = last.PreviousStoredState == null ? null : last.PreviousStoredState.Clone();
                            changedBlocks = Math.Max(0, changedBlocks - 1);
                            changedAttributes = Math.Max(0, changedAttributes - last.ChangedAttributes);
                        }
                        else
                        {
                            unchangedBlocks = Math.Max(0, unchangedBlocks - 1);
                        }
                        editor.WriteMessage("\n已撤销上一个排号：" + last.WrittenValue + "。");
                        continue;
                    }

                    if (string.Equals(picked.StringResult, "Settings", StringComparison.OrdinalIgnoreCase))
                    {
                        AutoNumberSettings editable = settings.Clone();
                        editable.StartNumber = nextNumber;
                        Dictionary<string, string> sampleValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        sampleValues[settings.AttributeTag] = history.Count == 0 ? sample.GetAttributeDisplayValue(settings.AttributeTag) : history.Peek().WrittenValue;
                        AutoNumberSettings changedSettings = ShowSettingsWindow(document, options,
                            AutoNumberingService.LoadStates(document.Database), sampleValues, false,
                            settings.AttributeTag, editable, true);
                        if (changedSettings != null)
                        {
                            settings = changedSettings;
                            nextNumber = AutoNumberingService.NormalizeNumber(settings.StartNumber, settings);
                            editor.WriteMessage("\n排号规则已更新，下一号：" + AutoNumberingService.BuildValue(nextNumber, settings));
                        }
                        continue;
                    }
                    break;
                }

                if (picked.Status == PromptStatus.None || picked.Status == PromptStatus.Cancel) break;
                if (picked.Status != PromptStatus.OK) continue;
                if (processed.Contains(picked.ObjectId))
                {
                    editor.WriteMessage("\n该图块已在当前会话排号，本次已忽略。");
                    continue;
                }

                long assignedNumber = AutoNumberingService.NormalizeNumber(nextNumber, settings);
                string value = AutoNumberingService.BuildValue(assignedNumber, settings);
                List<AutoNumberOldAttribute> oldValues;
                int changed;
                string failure;
                bool success;
                AutoNumberSettings previousState = currentStoredState == null ? null : currentStoredState.Clone();
                long following = AutoNumberingService.GetFollowingNumber(assignedNumber, settings);

                using (DocumentLock documentLock = document.LockDocument())
                using (Transaction tr = document.Database.TransactionManager.StartTransaction())
                {
                    success = AutoNumberingService.TryWriteAttribute(document.Database, tr, picked.ObjectId,
                        settings.AttributeTag, value, out oldValues, out changed, out failure);
                    if (success && changed > 0)
                        AutoNumberingService.SaveState(document.Database, tr, settings, following);
                    if (success) tr.Commit();
                }

                if (!success)
                {
                    skipped++;
                    editor.WriteMessage("\n未排号：" + failure + "。");
                    continue;
                }

                history.Push(new SemiHistoryItem
                {
                    BlockId = picked.ObjectId,
                    OldValues = oldValues,
                    PreviousNextNumber = assignedNumber,
                    PreviousStoredState = previousState,
                    StateWasSaved = changed > 0,
                    ChangedAttributes = changed,
                    WrittenValue = value
                });
                processed.Add(picked.ObjectId);
                nextNumber = following;
                if (changed > 0)
                {
                    changedBlocks++;
                    changedAttributes += changed;
                    currentStoredState = settings.Clone();
                    currentStoredState.StartNumber = following;
                }
                else
                {
                    unchangedBlocks++;
                }
                editor.WriteMessage("\n已写入 " + value + "，下一号 " + AutoNumberingService.BuildValue(nextNumber, settings) + "。");
            }

            try { editor.Regen(); }
            catch { }
            editor.WriteMessage("\n半自动排号完成：修改 " + changedBlocks + " 个图块 / " + changedAttributes +
                                " 个属性，无需修改 " + unchangedBlocks + " 个，跳过 " + skipped + " 个。");
        }

        private static void RunFullyAutomatic(Document document, ObjectId[] impliedIds)
        {
            Editor editor = document.Editor;
            List<AutoNumberCandidate> candidates = new List<AutoNumberCandidate>();
            if (impliedIds != null && impliedIds.Length > 0)
                candidates = AutoNumberingService.ScanCurrentSpaceCandidates(document.Database, editor, impliedIds);

            if (candidates.Count == 0)
            {
                PromptSelectionOptions options = new PromptSelectionOptions
                {
                    MessageForAdding = "\n请选择需要全自动排号的图块，直接回车处理当前空间全部图块: "
                };
                SelectionFilter filter = new SelectionFilter(new[]
                {
                    new TypedValue((int)DxfCode.Start, "INSERT")
                });
                PromptSelectionResult selected = editor.GetSelection(options, filter);
                if (selected.Status == PromptStatus.Cancel) return;
                ObjectId[] selectedIds = selected.Status == PromptStatus.OK && selected.Value != null
                    ? selected.Value.GetObjectIds()
                    : null;
                candidates = AutoNumberingService.ScanCurrentSpaceCandidates(document.Database, editor, selectedIds);
            }

            if (candidates.Count == 0)
            {
                editor.WriteMessage("\n当前范围没有可扫描的图块参照。");
                return;
            }

            List<AutoNumberAttributeOption> attributeOptions = AutoNumberingService.BuildAttributeOptions(candidates);
            if (attributeOptions.Count == 0)
            {
                editor.WriteMessage("\n候选图块中没有可编辑的属性。");
                return;
            }

            Dictionary<string, AutoNumberSettings> savedStates = AutoNumberingService.LoadStates(document.Database);
            string initialTag = ChooseInitialAttribute(attributeOptions, savedStates);
            AutoNumberSettings settings = ShowSettingsWindow(document, attributeOptions, savedStates,
                BuildSampleValues(attributeOptions), true, initialTag, null, false);
            if (settings == null) return;

            List<AutoNumberCandidate> eligible = candidates
                .Where(candidate => string.IsNullOrWhiteSpace(AutoNumberingService.GetEligibilityReason(candidate, settings.AttributeTag)))
                .ToList();
            List<AutoNumberCandidate> ordered = AutoNumberingService.SortCandidates(eligible, settings.SortMode, settings.ManualTolerance);
            HashSet<ObjectId> eligibleIds = new HashSet<ObjectId>(eligible.Select(candidate => candidate.ObjectId));
            ordered.AddRange(candidates.Where(candidate => !eligibleIds.Contains(candidate.ObjectId))
                .OrderBy(candidate => candidate.EffectiveBlockName, StringComparer.OrdinalIgnoreCase));

            AutoNumberPreviewWindow preview = new AutoNumberPreviewWindow(ordered, settings);
            bool? accepted = Application.ShowModalWindow(preview);
            if (accepted != true) return;

            int updatedBlocks = 0;
            int updatedAttributes = 0;
            int unchangedBlocks = 0;
            int failed = 0;
            List<string> failures = new List<string>();
            long nextNumber = settings.StartNumber;

            using (DocumentLock documentLock = document.LockDocument())
            using (Transaction tr = document.Database.TransactionManager.StartTransaction())
            {
                foreach (AutoNumberPreviewItem item in preview.IncludedItems)
                {
                    List<AutoNumberOldAttribute> oldValues;
                    int changed;
                    string failure;
                    if (!AutoNumberingService.TryWriteAttribute(document.Database, tr, item.Candidate.ObjectId,
                            settings.AttributeTag, item.NewValue, out oldValues, out changed, out failure))
                    {
                        failed++;
                        if (failures.Count < 8) failures.Add(item.BlockName + "：" + failure);
                        continue;
                    }

                    nextNumber = AutoNumberingService.GetFollowingNumber(item.AssignedNumber, settings);
                    if (changed > 0)
                    {
                        updatedBlocks++;
                        updatedAttributes += changed;
                    }
                    else
                    {
                        unchangedBlocks++;
                    }
                }

                if (updatedBlocks > 0)
                    AutoNumberingService.SaveState(document.Database, tr, settings, nextNumber);
                tr.Commit();
            }

            try { editor.Regen(); }
            catch { }
            int unavailable = preview.Items.Count(item => !item.CanInclude);
            int excluded = preview.Items.Count(item => item.CanInclude && !item.IsIncluded);
            string report = "全自动排号完成：修改 " + updatedBlocks + " 个图块 / " + updatedAttributes +
                            " 个属性，无需修改 " + unchangedBlocks + " 个，不可用 " + unavailable +
                            " 个，手动排除 " + excluded + " 个，写入失败 " + failed + " 个。";
            if (failures.Count > 0) report += "\n" + string.Join("\n", failures);
            Application.ShowAlertDialog(report);
        }

        private static void RunInsertAndShift(Document document, ObjectId[] impliedIds)
        {
            Editor editor = document.Editor;
            List<AutoNumberCandidate> targets = impliedIds != null && impliedIds.Length > 0
                ? AutoNumberingService.ScanCurrentSpaceCandidates(document.Database, editor, impliedIds)
                : new List<AutoNumberCandidate>();

            if (targets.Count == 0)
            {
                PromptSelectionOptions selectionOptions = new PromptSelectionOptions
                {
                    MessageForAdding = "\n请选择漏掉、需要补号的图块: "
                };
                SelectionFilter filter = new SelectionFilter(new[]
                {
                    new TypedValue((int)DxfCode.Start, "INSERT")
                });
                PromptSelectionResult selected = editor.GetSelection(selectionOptions, filter);
                if (selected.Status != PromptStatus.OK || selected.Value == null) return;
                targets = AutoNumberingService.ScanCurrentSpaceCandidates(
                    document.Database, editor, selected.Value.GetObjectIds());
            }

            if (targets.Count == 0)
            {
                editor.WriteMessage("\n没有选中可用的漏号图块。");
                return;
            }

            ObjectId referenceId = PromptBlock(editor,
                "\n请点选漏号位置后面第一个已有编号图块（它的当前号码将作为插入起始号）: ");
            if (referenceId.IsNull) return;
            if (targets.Any(candidate => candidate.ObjectId == referenceId))
            {
                Application.ShowAlertDialog("顺延起点图块不能同时作为漏号图块，请选择它后面第一个已有编号的图块。");
                return;
            }

            AutoNumberCandidate reference = AutoNumberingService.ScanCurrentSpaceCandidates(
                document.Database, editor, new[] { referenceId }).FirstOrDefault();
            if (reference == null || reference.AttributeValues.Count == 0)
            {
                editor.WriteMessage("\n顺延起点图块没有可编辑的属性。");
                return;
            }

            List<AutoNumberAttributeOption> options = AutoNumberingService.BuildAttributeOptions(new[] { reference })
                .Where(option => AutoNumberingService.ParseNumberSegments(option.SampleValue).Count > 0)
                .ToList();
            if (options.Count == 0)
            {
                editor.WriteMessage("\n顺延起点图块的属性中没有可解析的数字编号。");
                return;
            }

            Dictionary<string, AutoNumberSettings> savedStates = AutoNumberingService.LoadStates(document.Database);
            string initialTag = ChooseInitialAttribute(options, savedStates);
            AutoNumberSettings settings = ShowSettingsWindow(document, options, savedStates,
                BuildSampleValues(options), true, initialTag, null, false, true);
            if (settings == null) return;

            List<string> invalidTargets = targets
                .Select(candidate => new
                {
                    Candidate = candidate,
                    Reason = AutoNumberingService.GetEligibilityReason(candidate, settings.AttributeTag)
                })
                .Where(item => !string.IsNullOrWhiteSpace(item.Reason))
                .Select(item => item.Candidate.EffectiveBlockName + "：" + item.Reason)
                .ToList();
            if (invalidTargets.Count > 0)
            {
                Application.ShowAlertDialog("所选漏号图块不能全部写入，本次未修改图纸：\n" +
                    string.Join("\n", invalidTargets.Take(10)));
                return;
            }

            List<AutoNumberCandidate> orderedTargets = AutoNumberingService.SortCandidates(
                targets, settings.SortMode, settings.ManualTolerance);
            List<AutoNumberShiftPreviewItem> previewItems = new List<AutoNumberShiftPreviewItem>();
            long next = settings.StartNumber;
            try
            {
                foreach (AutoNumberCandidate target in orderedTargets)
                {
                    previewItems.Add(new AutoNumberShiftPreviewItem
                    {
                        Candidate = target,
                        ChangeKind = "新增编号",
                        AttributeTag = settings.AttributeTag,
                        OldValue = target.GetAttributeDisplayValue(settings.AttributeTag),
                        AssignedNumber = next,
                        NewValue = AutoNumberingService.BuildValue(next, settings)
                    });
                    next = AutoNumberingService.GetFollowingNumber(next, settings);
                }

                HashSet<ObjectId> targetIds = new HashSet<ObjectId>(targets.Select(candidate => candidate.ObjectId));
                List<AutoNumberCandidate> allCandidates = AutoNumberingService.ScanCurrentSpaceCandidates(
                    document.Database, editor, null);
                foreach (AutoNumberCandidate candidate in allCandidates.Where(item => !targetIds.Contains(item.ObjectId)))
                {
                    long currentNumber;
                    string reason;
                    if (!AutoNumberingService.TryGetCandidateNumber(candidate, settings.AttributeTag, settings,
                            out currentNumber, out reason) || currentNumber < settings.StartNumber)
                        continue;

                    long shiftedNumber = AutoNumberingService.AdvanceNumber(currentNumber, orderedTargets.Count, settings);
                    previewItems.Add(new AutoNumberShiftPreviewItem
                    {
                        Candidate = candidate,
                        ChangeKind = "后续顺延",
                        AttributeTag = settings.AttributeTag,
                        OldValue = candidate.GetAttributeDisplayValue(settings.AttributeTag),
                        AssignedNumber = shiftedNumber,
                        NewValue = AutoNumberingService.BuildValue(shiftedNumber, settings)
                    });
                }
            }
            catch (System.Exception ex)
            {
                Application.ShowAlertDialog("无法计算顺延编号：" + ex.Message);
                return;
            }

            int shiftedCount = previewItems.Count(item => item.ChangeKind == "后续顺延");
            if (shiftedCount == 0)
            {
                Application.ShowAlertDialog("当前规则和起始号没有命中任何后续编号，本次未修改图纸。\n" +
                    "请检查属性标签、前后缀和起始数字。");
                return;
            }

            previewItems = previewItems
                .Where(item => item.ChangeKind == "新增编号")
                .Concat(previewItems.Where(item => item.ChangeKind == "后续顺延")
                    .OrderBy(item => item.AssignedNumber)
                    .ThenBy(item => item.BlockName, StringComparer.OrdinalIgnoreCase))
                .ToList();

            AutoNumberShiftPreviewWindow preview = new AutoNumberShiftPreviewWindow(previewItems);
            if (Application.ShowModalWindow(preview) != true) return;

            int updatedBlocks = 0;
            int updatedAttributes = 0;
            string failureMessage = "";
            using (DocumentLock documentLock = document.LockDocument())
            using (Transaction tr = document.Database.TransactionManager.StartTransaction())
            {
                foreach (AutoNumberShiftPreviewItem item in previewItems
                    .OrderByDescending(value => value.ChangeKind == "后续顺延")
                    .ThenByDescending(value => value.AssignedNumber))
                {
                    List<AutoNumberOldAttribute> oldValues;
                    int changed;
                    string failure;
                    if (!AutoNumberingService.TryWriteAttribute(document.Database, tr, item.Candidate.ObjectId,
                            settings.AttributeTag, item.NewValue, out oldValues, out changed, out failure))
                    {
                        failureMessage = item.BlockName + "：" + failure;
                        break;
                    }
                    if (changed > 0)
                    {
                        updatedBlocks++;
                        updatedAttributes += changed;
                    }
                }

                if (string.IsNullOrEmpty(failureMessage) && updatedBlocks > 0)
                {
                    long highest = previewItems.Max(item => item.AssignedNumber);
                    long following = AutoNumberingService.GetFollowingNumber(highest, settings);
                    AutoNumberingService.SaveState(document.Database, tr, settings, following);
                    tr.Commit();
                }
            }

            if (!string.IsNullOrEmpty(failureMessage))
            {
                Application.ShowAlertDialog("插号顺延写入失败，所有修改已取消：\n" + failureMessage);
                return;
            }
            if (updatedBlocks == 0)
            {
                editor.WriteMessage("\n所有目标编号已是预期值，本次无需修改。");
                return;
            }

            try { editor.Regen(); }
            catch { }
            Application.ShowAlertDialog("插号并顺延完成：补号 " + orderedTargets.Count + " 个，顺延 " +
                shiftedCount + " 个，共修改 " + updatedBlocks + " 个图块 / " + updatedAttributes + " 个属性。");
        }

        private static AutoNumberSettings ShowSettingsWindow(
            Document document,
            List<AutoNumberAttributeOption> options,
            Dictionary<string, AutoNumberSettings> savedStates,
            Dictionary<string, string> sampleValues,
            bool fullyAutomatic,
            string initialTag,
            AutoNumberSettings initialSettings,
            bool lockAttribute,
            bool insertAndShift = false)
        {
            AutoNumberSettingsWindow window = new AutoNumberSettingsWindow(options, savedStates, sampleValues,
                fullyAutomatic, initialTag, initialSettings, lockAttribute, insertAndShift);
            window.PickSampleRequested += () =>
            {
                try
                {
                    window.Hide();
                    string tag = window.SelectedAttributeTag;
                    if (string.IsNullOrWhiteSpace(tag))
                    {
                        Application.ShowAlertDialog("请先选择或输入需要取样的属性标签。");
                        return;
                    }
                    string prompt = insertAndShift
                        ? "\n请点选属性 " + tag + " 的新顺延起点图块: "
                        : "\n请点选一个包含属性 " + tag + " 的续号样本图块: ";
                    ObjectId sampleId = PromptBlock(document.Editor, prompt);
                    string value;
                    if (!sampleId.IsNull && AutoNumberingService.TryReadAttributeValue(document.Database, sampleId, tag, out value))
                        window.SetSampleValue(value);
                    else if (!sampleId.IsNull)
                        Application.ShowAlertDialog("所选图块不包含属性 " + tag + "。");
                }
                finally
                {
                    window.Show();
                    window.Activate();
                }
            };
            bool? accepted = Application.ShowModalWindow(window);
            return accepted == true ? window.AcceptedSettings : null;
        }

        private static ObjectId[] GetImpliedIds(Editor editor)
        {
            try
            {
                PromptSelectionResult implied = editor.SelectImplied();
                ObjectId[] ids = implied.Status == PromptStatus.OK && implied.Value != null
                    ? implied.Value.GetObjectIds()
                    : new ObjectId[0];
                editor.SetImpliedSelection(new ObjectId[0]);
                return ids;
            }
            catch { return new ObjectId[0]; }
        }

        private static AutoNumberMode? PromptMode(Editor editor)
        {
            PromptKeywordOptions options = new PromptKeywordOptions(
                "\n请选择排号模式 [半自动(Semi)/全自动(Auto)/插号顺延(Insert)] <半自动>: ");
            options.Keywords.Add("Semi");
            options.Keywords.Add("Auto");
            options.Keywords.Add("Insert");
            options.Keywords.Default = "Semi";
            PromptResult result = editor.GetKeywords(options);
            if (result.Status == PromptStatus.Cancel) return null;
            if (result.Status == PromptStatus.OK && string.Equals(result.StringResult, "Auto", StringComparison.OrdinalIgnoreCase))
                return AutoNumberMode.FullyAutomatic;
            if (result.Status == PromptStatus.OK && string.Equals(result.StringResult, "Insert", StringComparison.OrdinalIgnoreCase))
                return AutoNumberMode.InsertAndShift;
            return AutoNumberMode.SemiAutomatic;
        }

        private static ObjectId PromptBlock(Editor editor, string message)
        {
            PromptEntityOptions options = new PromptEntityOptions(message);
            options.SetRejectMessage("\n请选择图块参照。");
            options.AddAllowedClass(typeof(BlockReference), true);
            PromptEntityResult result = editor.GetEntity(options);
            return result.Status == PromptStatus.OK ? result.ObjectId : ObjectId.Null;
        }

        private static string ChooseInitialAttribute(
            IEnumerable<AutoNumberAttributeOption> options,
            IDictionary<string, AutoNumberSettings> savedStates)
        {
            List<AutoNumberAttributeOption> list = (options ?? Enumerable.Empty<AutoNumberAttributeOption>()).ToList();
            AutoNumberAttributeOption saved = list.FirstOrDefault(option => savedStates != null && savedStates.ContainsKey(option.Tag));
            if (saved != null) return saved.Tag;
            AutoNumberAttributeOption numbered = list.FirstOrDefault(option => AutoNumberingService.ParseNumberSegments(option.SampleValue).Count > 0);
            return numbered == null ? (list.FirstOrDefault() == null ? "" : list.First().Tag) : numbered.Tag;
        }

        private static Dictionary<string, string> BuildSampleValues(IEnumerable<AutoNumberAttributeOption> options)
        {
            return (options ?? Enumerable.Empty<AutoNumberAttributeOption>())
                .GroupBy(option => option.Tag, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().SampleValue ?? "", StringComparer.OrdinalIgnoreCase);
        }
    }
}
