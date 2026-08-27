using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

namespace XYDSignTool
{
    public enum AutoNumberMode
    {
        SemiAutomatic,
        FullyAutomatic,
        InsertAndShift
    }

    public enum AutoNumberSkipMode
    {
        None,
        EndingDigit,
        AnyDigit
    }

    public enum AutoNumberSortMode
    {
        Horizontal,
        HorizontalSnake,
        Vertical,
        VerticalSnake
    }

    public sealed class AutoNumberSettings
    {
        public string AttributeTag { get; set; } = "";
        public string Prefix { get; set; } = "";
        public string Suffix { get; set; } = "";
        public long StartNumber { get; set; } = 1;
        public int DigitWidth { get; set; } = 1;
        public long Increment { get; set; } = 1;
        public AutoNumberSkipMode SkipMode { get; set; } = AutoNumberSkipMode.None;
        public string SkipDigits { get; set; } = "4";
        public AutoNumberSortMode SortMode { get; set; } = AutoNumberSortMode.HorizontalSnake;
        public double ManualTolerance { get; set; }

        public AutoNumberSettings Clone()
        {
            return (AutoNumberSettings)MemberwiseClone();
        }
    }

    public sealed class AutoNumberAttributeOption
    {
        public string Tag { get; set; }
        public int MatchingBlocks { get; set; }
        public int TotalBlocks { get; set; }
        public string SampleValue { get; set; }
        public string CoverageText { get { return MatchingBlocks + "/" + TotalBlocks; } }

        public override string ToString()
        {
            return Tag + "  (" + MatchingBlocks + "/" + TotalBlocks + ")";
        }
    }

    public sealed class AutoNumberSegmentOption
    {
        public int Index { get; set; }
        public string Digits { get; set; }
        public string Prefix { get; set; }
        public string Suffix { get; set; }
        public long Number { get; set; }

        public int DigitWidth
        {
            get { return string.IsNullOrEmpty(Digits) ? 1 : Digits.Length; }
        }

        public override string ToString()
        {
            return "第 " + (Index + 1) + " 段：" + Digits + "  (前缀“" + Prefix + "” / 后缀“" + Suffix + "”)";
        }
    }

    public sealed class AutoNumberCandidate
    {
        public ObjectId ObjectId { get; set; }
        public string EffectiveBlockName { get; set; }
        public string LayerName { get; set; }
        public double SortX { get; set; }
        public double SortY { get; set; }
        public double ProjectedWidth { get; set; }
        public double ProjectedHeight { get; set; }
        public string BaseSkipReason { get; set; }
        public Dictionary<string, List<string>> AttributeValues { get; } =
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> FieldAttributeTags { get; } =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public bool HasAttribute(string tag)
        {
            return !string.IsNullOrWhiteSpace(tag) && AttributeValues.ContainsKey(tag.Trim());
        }

        public string GetAttributeDisplayValue(string tag)
        {
            List<string> values;
            return AttributeValues.TryGetValue(tag ?? "", out values)
                ? string.Join(" | ", values)
                : "";
        }
    }

    public sealed class AutoNumberOldAttribute
    {
        public ObjectId AttributeId { get; set; }
        public string Text { get; set; }
    }

    public static class AutoNumberingService
    {
        private const string StateRecordName = "XYD_AUTONUMBER_STATE";
        private const string StateVersion = "V1";
        private static readonly Regex NumberRegex = new Regex("[0-9]+", RegexOptions.Compiled);

        public static List<AutoNumberCandidate> ScanCurrentSpaceCandidates(
            Database database,
            Editor editor,
            IEnumerable<ObjectId> requestedIds)
        {
            List<AutoNumberCandidate> candidates = new List<AutoNumberCandidate>();
            if (database == null || editor == null) return candidates;

            Matrix3d worldToUcs;
            try { worldToUcs = editor.CurrentUserCoordinateSystem.Inverse(); }
            catch { worldToUcs = Matrix3d.Identity; }

            using (Transaction tr = database.TransactionManager.StartTransaction())
            {
                IEnumerable<ObjectId> ids = requestedIds;
                if (ids == null)
                {
                    BlockTableRecord current = tr.GetObject(database.CurrentSpaceId, OpenMode.ForRead, false) as BlockTableRecord;
                    ids = current == null ? Enumerable.Empty<ObjectId>() : current.Cast<ObjectId>();
                }

                foreach (ObjectId id in ids.Where(value => !value.IsNull).Distinct())
                {
                    try
                    {
                        if (!id.IsValid || id.IsErased) continue;
                        BlockReference block = tr.GetObject(id, OpenMode.ForRead, false) as BlockReference;
                        if (block == null || block.OwnerId != database.CurrentSpaceId) continue;

                        AutoNumberCandidate candidate = BuildCandidate(block, tr, worldToUcs);
                        if (candidate != null) candidates.Add(candidate);
                    }
                    catch { }
                }
                tr.Commit();
            }
            return candidates;
        }

        public static List<AutoNumberAttributeOption> BuildAttributeOptions(IEnumerable<AutoNumberCandidate> source)
        {
            List<AutoNumberCandidate> candidates = (source ?? Enumerable.Empty<AutoNumberCandidate>()).ToList();
            Dictionary<string, AutoNumberAttributeOption> options =
                new Dictionary<string, AutoNumberAttributeOption>(StringComparer.OrdinalIgnoreCase);

            foreach (AutoNumberCandidate candidate in candidates)
            {
                foreach (KeyValuePair<string, List<string>> pair in candidate.AttributeValues)
                {
                    AutoNumberAttributeOption option;
                    if (!options.TryGetValue(pair.Key, out option))
                    {
                        option = new AutoNumberAttributeOption
                        {
                            Tag = pair.Key,
                            TotalBlocks = candidates.Count,
                            SampleValue = pair.Value.FirstOrDefault() ?? ""
                        };
                        options[pair.Key] = option;
                    }
                    option.MatchingBlocks++;
                    if (string.IsNullOrWhiteSpace(option.SampleValue))
                        option.SampleValue = pair.Value.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";
                }
            }

            return options.Values
                .OrderByDescending(option => option.MatchingBlocks)
                .ThenBy(option => option.Tag, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static string GetEligibilityReason(AutoNumberCandidate candidate, string attributeTag)
        {
            if (candidate == null) return "对象不可用";
            if (!string.IsNullOrWhiteSpace(candidate.BaseSkipReason)) return candidate.BaseSkipReason;
            if (!candidate.HasAttribute(attributeTag)) return "缺少属性 " + (attributeTag ?? "");
            if (candidate.FieldAttributeTags.Contains(attributeTag ?? "")) return "属性由字段控制";
            return "";
        }

        public static List<AutoNumberCandidate> SortCandidates(
            IEnumerable<AutoNumberCandidate> source,
            AutoNumberSortMode mode,
            double manualTolerance)
        {
            List<AutoNumberCandidate> candidates = (source ?? Enumerable.Empty<AutoNumberCandidate>()).ToList();
            if (candidates.Count < 2) return candidates;

            bool horizontal = mode == AutoNumberSortMode.Horizontal || mode == AutoNumberSortMode.HorizontalSnake;
            bool snake = mode == AutoNumberSortMode.HorizontalSnake || mode == AutoNumberSortMode.VerticalSnake;
            double autoTolerance = horizontal
                ? MedianPositive(candidates.Select(item => item.ProjectedHeight)) * 0.5
                : MedianPositive(candidates.Select(item => item.ProjectedWidth)) * 0.5;
            double tolerance = manualTolerance > 0 ? manualTolerance : autoTolerance;
            if (tolerance <= 0) tolerance = 1e-6;

            List<List<AutoNumberCandidate>> bands = new List<List<AutoNumberCandidate>>();
            IEnumerable<AutoNumberCandidate> primaryOrder = horizontal
                ? candidates.OrderByDescending(item => item.SortY).ThenBy(item => item.SortX)
                : candidates.OrderBy(item => item.SortX).ThenByDescending(item => item.SortY);

            foreach (AutoNumberCandidate candidate in primaryOrder)
            {
                double coordinate = horizontal ? candidate.SortY : candidate.SortX;
                List<AutoNumberCandidate> bestBand = null;
                double bestDistance = double.MaxValue;
                foreach (List<AutoNumberCandidate> band in bands)
                {
                    double center = horizontal ? band.Average(item => item.SortY) : band.Average(item => item.SortX);
                    double distance = Math.Abs(coordinate - center);
                    if (distance <= tolerance && distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestBand = band;
                    }
                }
                if (bestBand == null)
                {
                    bestBand = new List<AutoNumberCandidate>();
                    bands.Add(bestBand);
                }
                bestBand.Add(candidate);
            }

            bands = horizontal
                ? bands.OrderByDescending(band => band.Average(item => item.SortY)).ToList()
                : bands.OrderBy(band => band.Average(item => item.SortX)).ToList();

            List<AutoNumberCandidate> result = new List<AutoNumberCandidate>();
            for (int index = 0; index < bands.Count; index++)
            {
                IEnumerable<AutoNumberCandidate> ordered = horizontal
                    ? bands[index].OrderBy(item => item.SortX).ThenByDescending(item => item.SortY)
                    : bands[index].OrderByDescending(item => item.SortY).ThenBy(item => item.SortX);
                if (snake && index % 2 == 1) ordered = ordered.Reverse();
                result.AddRange(ordered);
            }
            return result;
        }

        public static List<AutoNumberSegmentOption> ParseNumberSegments(string value)
        {
            string text = value ?? "";
            List<AutoNumberSegmentOption> result = new List<AutoNumberSegmentOption>();
            MatchCollection matches = NumberRegex.Matches(text);
            for (int index = 0; index < matches.Count; index++)
            {
                Match match = matches[index];
                long number;
                if (!long.TryParse(match.Value, NumberStyles.None, CultureInfo.InvariantCulture, out number)) continue;
                result.Add(new AutoNumberSegmentOption
                {
                    Index = index,
                    Digits = match.Value,
                    Prefix = text.Substring(0, match.Index),
                    Suffix = text.Substring(match.Index + match.Length),
                    Number = number
                });
            }
            return result;
        }

        public static string NormalizeSkipDigits(string value)
        {
            return new string((value ?? "")
                .Where(character => character >= '0' && character <= '9')
                .Distinct()
                .ToArray());
        }

        public static long NormalizeNumber(long number, AutoNumberSettings settings)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            if (number < 0) throw new ArgumentOutOfRangeException("number");
            if (settings.Increment <= 0) throw new ArgumentOutOfRangeException("settings.Increment");

            string skipDigits = NormalizeSkipDigits(settings.SkipDigits);
            if (settings.SkipMode == AutoNumberSkipMode.None || skipDigits.Length == 0) return number;

            for (int attempts = 0; attempts < 1000000; attempts++)
            {
                string numeric = FormatNumericPart(number, settings.DigitWidth);
                bool blocked = settings.SkipMode == AutoNumberSkipMode.EndingDigit
                    ? numeric.Length > 0 && skipDigits.IndexOf(numeric[numeric.Length - 1]) >= 0
                    : numeric.Any(character => skipDigits.IndexOf(character) >= 0);
                if (!blocked) return number;
                number = checked(number + settings.Increment);
            }
            throw new InvalidOperationException("跳号规则无法在合理范围内找到可用编号。");
        }

        public static long GetFollowingNumber(long assignedNumber, AutoNumberSettings settings)
        {
            return NormalizeNumber(checked(assignedNumber + settings.Increment), settings);
        }

        public static string BuildValue(long number, AutoNumberSettings settings)
        {
            return (settings.Prefix ?? "") + FormatNumericPart(number, settings.DigitWidth) + (settings.Suffix ?? "");
        }

        public static long AdvanceNumber(long number, int positions, AutoNumberSettings settings)
        {
            if (positions < 0) throw new ArgumentOutOfRangeException("positions");
            long result = number;
            for (int index = 0; index < positions; index++)
                result = GetFollowingNumber(result, settings);
            return result;
        }

        public static bool TryParseFormattedNumber(string value, AutoNumberSettings settings, out long number)
        {
            number = 0;
            if (settings == null || value == null) return false;

            string prefix = settings.Prefix ?? "";
            string suffix = settings.Suffix ?? "";
            if (value.Length <= prefix.Length + suffix.Length ||
                !value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                !value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return false;

            int numericLength = value.Length - prefix.Length - suffix.Length;
            if (numericLength <= 0) return false;
            string numeric = value.Substring(prefix.Length, numericLength);
            if (numeric.Any(character => character < '0' || character > '9')) return false;
            return long.TryParse(numeric, NumberStyles.None, CultureInfo.InvariantCulture, out number);
        }

        public static bool TryGetCandidateNumber(
            AutoNumberCandidate candidate,
            string attributeTag,
            AutoNumberSettings settings,
            out long number,
            out string reason)
        {
            number = 0;
            reason = GetEligibilityReason(candidate, attributeTag);
            if (!string.IsNullOrWhiteSpace(reason)) return false;

            List<string> values;
            if (!candidate.AttributeValues.TryGetValue(attributeTag ?? "", out values) || values.Count == 0)
            {
                reason = "缺少属性 " + (attributeTag ?? "");
                return false;
            }

            bool found = false;
            foreach (string value in values)
            {
                long parsed;
                if (!TryParseFormattedNumber(value ?? "", settings, out parsed))
                {
                    reason = "属性值不符合当前编号规则";
                    return false;
                }
                if (found && parsed != number)
                {
                    reason = "同名属性的编号不一致";
                    return false;
                }
                number = parsed;
                found = true;
            }
            if (!found) reason = "属性值为空";
            return found;
        }

        public static string FormatNumericPart(long number, int digitWidth)
        {
            int width = Math.Max(1, Math.Min(19, digitWidth));
            return number.ToString("D" + width, CultureInfo.InvariantCulture);
        }

        public static bool TryReadAttributeValue(
            Database database,
            ObjectId blockId,
            string attributeTag,
            out string value)
        {
            value = "";
            if (database == null || blockId.IsNull || string.IsNullOrWhiteSpace(attributeTag)) return false;
            using (Transaction tr = database.TransactionManager.StartTransaction())
            {
                try
                {
                    BlockReference block = tr.GetObject(blockId, OpenMode.ForRead, false) as BlockReference;
                    if (block == null) return false;
                    foreach (ObjectId attributeId in block.AttributeCollection)
                    {
                        AttributeReference attribute = tr.GetObject(attributeId, OpenMode.ForRead, false) as AttributeReference;
                        if (attribute == null || !string.Equals(attribute.Tag, attributeTag.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
                        value = attribute.TextString ?? "";
                        return true;
                    }
                }
                catch { }
            }
            return false;
        }

        public static bool TryWriteAttribute(
            Database database,
            Transaction tr,
            ObjectId blockId,
            string attributeTag,
            string value,
            out List<AutoNumberOldAttribute> oldValues,
            out int changedAttributes,
            out string failureReason)
        {
            oldValues = new List<AutoNumberOldAttribute>();
            changedAttributes = 0;
            failureReason = "";
            try
            {
                BlockReference block = tr.GetObject(blockId, OpenMode.ForRead, false) as BlockReference;
                if (block == null) { failureReason = "不是图块参照"; return false; }

                BlockTableRecord definition = GetEffectiveDefinition(block, tr);
                if (definition == null || definition.IsFromExternalReference || definition.IsFromOverlayReference)
                {
                    failureReason = "外部参照不可编辑";
                    return false;
                }

                LayerTableRecord layer = tr.GetObject(block.LayerId, OpenMode.ForRead, false) as LayerTableRecord;
                if (layer != null && layer.IsLocked)
                {
                    failureReason = "图层已锁定";
                    return false;
                }

                List<AttributeReference> matches = new List<AttributeReference>();
                foreach (ObjectId attributeId in block.AttributeCollection)
                {
                    AttributeReference attribute = tr.GetObject(attributeId, OpenMode.ForRead, false) as AttributeReference;
                    if (attribute == null || !string.Equals(attribute.Tag, attributeTag.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
                    if (HasFields(attribute))
                    {
                        failureReason = "属性由字段控制";
                        return false;
                    }
                    matches.Add(attribute);
                }

                if (matches.Count == 0)
                {
                    failureReason = "缺少属性 " + attributeTag;
                    return false;
                }

                foreach (AttributeReference attribute in matches)
                {
                    oldValues.Add(new AutoNumberOldAttribute { AttributeId = attribute.ObjectId, Text = attribute.TextString ?? "" });
                    if (string.Equals(attribute.TextString ?? "", value ?? "", StringComparison.Ordinal)) continue;
                    attribute.UpgradeOpen();
                    attribute.TextString = value ?? "";
                    try { if (attribute.IsMTextAttribute) attribute.UpdateMTextAttribute(); }
                    catch { }
                    changedAttributes++;
                }
                return true;
            }
            catch (System.Exception ex)
            {
                failureReason = ex.Message;
                return false;
            }
        }

        public static bool RestoreAttributes(
            Transaction tr,
            IEnumerable<AutoNumberOldAttribute> oldValues,
            out int restoredAttributes)
        {
            restoredAttributes = 0;
            try
            {
                foreach (AutoNumberOldAttribute oldValue in oldValues ?? Enumerable.Empty<AutoNumberOldAttribute>())
                {
                    if (oldValue.AttributeId.IsNull || !oldValue.AttributeId.IsValid || oldValue.AttributeId.IsErased) continue;
                    AttributeReference attribute = tr.GetObject(oldValue.AttributeId, OpenMode.ForWrite, false) as AttributeReference;
                    if (attribute == null) continue;
                    attribute.TextString = oldValue.Text ?? "";
                    try { if (attribute.IsMTextAttribute) attribute.UpdateMTextAttribute(); }
                    catch { }
                    restoredAttributes++;
                }
                return true;
            }
            catch { return false; }
        }

        public static Dictionary<string, AutoNumberSettings> LoadStates(Database database)
        {
            if (database == null) return new Dictionary<string, AutoNumberSettings>(StringComparer.OrdinalIgnoreCase);
            using (Transaction tr = database.TransactionManager.StartTransaction())
            {
                return ReadStates(database, tr);
            }
        }

        public static void SaveState(
            Database database,
            Transaction tr,
            AutoNumberSettings settings,
            long nextNumber)
        {
            if (database == null || tr == null || settings == null || string.IsNullOrWhiteSpace(settings.AttributeTag)) return;
            Dictionary<string, AutoNumberSettings> states = ReadStates(database, tr);
            AutoNumberSettings stored = settings.Clone();
            stored.AttributeTag = settings.AttributeTag.Trim();
            stored.StartNumber = nextNumber;
            stored.SkipDigits = NormalizeSkipDigits(stored.SkipDigits);
            states[stored.AttributeTag] = stored;

            WriteStates(database, tr, states);
        }

        public static void RestoreState(
            Database database,
            Transaction tr,
            string attributeTag,
            AutoNumberSettings previousState)
        {
            if (database == null || tr == null || string.IsNullOrWhiteSpace(attributeTag)) return;
            Dictionary<string, AutoNumberSettings> states = ReadStates(database, tr);
            if (previousState == null)
                states.Remove(attributeTag.Trim());
            else
                states[attributeTag.Trim()] = previousState.Clone();
            WriteStates(database, tr, states);
        }

        private static void WriteStates(
            Database database,
            Transaction tr,
            Dictionary<string, AutoNumberSettings> states)
        {
            states = states ?? new Dictionary<string, AutoNumberSettings>(StringComparer.OrdinalIgnoreCase);

            List<TypedValue> values = new List<TypedValue>
            {
                new TypedValue((int)DxfCode.Text, StateVersion)
            };
            foreach (AutoNumberSettings item in states.Values.OrderBy(value => value.AttributeTag, StringComparer.OrdinalIgnoreCase))
            {
                values.Add(new TypedValue((int)DxfCode.Text, "ENTRY"));
                values.Add(new TypedValue((int)DxfCode.Text, item.AttributeTag ?? ""));
                values.Add(new TypedValue((int)DxfCode.Text, item.Prefix ?? ""));
                values.Add(new TypedValue((int)DxfCode.Text, item.Suffix ?? ""));
                values.Add(new TypedValue((int)DxfCode.Text, item.StartNumber.ToString(CultureInfo.InvariantCulture)));
                values.Add(new TypedValue((int)DxfCode.Int32, item.DigitWidth));
                values.Add(new TypedValue((int)DxfCode.Text, item.Increment.ToString(CultureInfo.InvariantCulture)));
                values.Add(new TypedValue((int)DxfCode.Int32, (int)item.SkipMode));
                values.Add(new TypedValue((int)DxfCode.Text, item.SkipDigits ?? ""));
                values.Add(new TypedValue((int)DxfCode.Int32, (int)item.SortMode));
                values.Add(new TypedValue((int)DxfCode.Real, item.ManualTolerance));
            }

            DBDictionary namedObjects = tr.GetObject(database.NamedObjectsDictionaryId, OpenMode.ForRead) as DBDictionary;
            if (namedObjects == null) return;
            Xrecord record;
            if (namedObjects.Contains(StateRecordName))
            {
                record = tr.GetObject(namedObjects.GetAt(StateRecordName), OpenMode.ForWrite, false) as Xrecord;
            }
            else
            {
                namedObjects.UpgradeOpen();
                record = new Xrecord();
                namedObjects.SetAt(StateRecordName, record);
                tr.AddNewlyCreatedDBObject(record, true);
            }
            if (record == null) return;
            using (ResultBuffer buffer = new ResultBuffer(values.ToArray()))
            {
                record.Data = buffer;
            }
        }

        private static Dictionary<string, AutoNumberSettings> ReadStates(Database database, Transaction tr)
        {
            Dictionary<string, AutoNumberSettings> states =
                new Dictionary<string, AutoNumberSettings>(StringComparer.OrdinalIgnoreCase);
            try
            {
                DBDictionary namedObjects = tr.GetObject(database.NamedObjectsDictionaryId, OpenMode.ForRead, false) as DBDictionary;
                if (namedObjects == null || !namedObjects.Contains(StateRecordName)) return states;
                Xrecord record = tr.GetObject(namedObjects.GetAt(StateRecordName), OpenMode.ForRead, false) as Xrecord;
                if (record == null) return states;
                TypedValue[] values;
                using (ResultBuffer buffer = record.Data)
                {
                    if (buffer == null) return states;
                    values = buffer.AsArray();
                }
                if (values.Length == 0 || Convert.ToString(values[0].Value, CultureInfo.InvariantCulture) != StateVersion) return states;

                int index = 1;
                while (index + 10 < values.Length)
                {
                    if (Convert.ToString(values[index].Value, CultureInfo.InvariantCulture) != "ENTRY") { index++; continue; }
                    string tag = Convert.ToString(values[index + 1].Value, CultureInfo.InvariantCulture) ?? "";
                    long next;
                    long increment;
                    if (!long.TryParse(Convert.ToString(values[index + 4].Value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out next)) next = 1;
                    if (!long.TryParse(Convert.ToString(values[index + 6].Value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out increment)) increment = 1;
                    AutoNumberSettings settings = new AutoNumberSettings
                    {
                        AttributeTag = tag,
                        Prefix = Convert.ToString(values[index + 2].Value, CultureInfo.InvariantCulture) ?? "",
                        Suffix = Convert.ToString(values[index + 3].Value, CultureInfo.InvariantCulture) ?? "",
                        StartNumber = Math.Max(0, next),
                        DigitWidth = Math.Max(1, Math.Min(19, Convert.ToInt32(values[index + 5].Value, CultureInfo.InvariantCulture))),
                        Increment = Math.Max(1, increment),
                        SkipMode = (AutoNumberSkipMode)Convert.ToInt32(values[index + 7].Value, CultureInfo.InvariantCulture),
                        SkipDigits = Convert.ToString(values[index + 8].Value, CultureInfo.InvariantCulture) ?? "",
                        SortMode = (AutoNumberSortMode)Convert.ToInt32(values[index + 9].Value, CultureInfo.InvariantCulture),
                        ManualTolerance = Math.Max(0, Convert.ToDouble(values[index + 10].Value, CultureInfo.InvariantCulture))
                    };
                    if (!string.IsNullOrWhiteSpace(tag)) states[tag] = settings;
                    index += 11;
                }
            }
            catch { }
            return states;
        }

        private static AutoNumberCandidate BuildCandidate(BlockReference block, Transaction tr, Matrix3d worldToUcs)
        {
            BlockTableRecord definition = GetEffectiveDefinition(block, tr);
            if (definition == null) return null;
            AutoNumberCandidate candidate = new AutoNumberCandidate
            {
                ObjectId = block.ObjectId,
                EffectiveBlockName = definition.Name ?? "",
                LayerName = block.Layer ?? ""
            };
            if (definition.IsFromExternalReference || definition.IsFromOverlayReference)
                candidate.BaseSkipReason = "外部参照不可编辑";

            try
            {
                LayerTableRecord layer = tr.GetObject(block.LayerId, OpenMode.ForRead, false) as LayerTableRecord;
                if (layer != null && layer.IsLocked) candidate.BaseSkipReason = "图层已锁定";
            }
            catch { }

            foreach (ObjectId attributeId in block.AttributeCollection)
            {
                try
                {
                    AttributeReference attribute = tr.GetObject(attributeId, OpenMode.ForRead, false) as AttributeReference;
                    if (attribute == null || string.IsNullOrWhiteSpace(attribute.Tag)) continue;
                    List<string> values;
                    if (!candidate.AttributeValues.TryGetValue(attribute.Tag, out values))
                    {
                        values = new List<string>();
                        candidate.AttributeValues[attribute.Tag] = values;
                    }
                    values.Add(attribute.TextString ?? "");
                    if (HasFields(attribute)) candidate.FieldAttributeTags.Add(attribute.Tag);
                }
                catch { }
            }

            Point3d center = block.Position;
            try
            {
                Extents3d extents = block.GeometricExtents;
                center = new Point3d(
                    (extents.MinPoint.X + extents.MaxPoint.X) * 0.5,
                    (extents.MinPoint.Y + extents.MaxPoint.Y) * 0.5,
                    (extents.MinPoint.Z + extents.MaxPoint.Z) * 0.5);
                Point3d[] corners =
                {
                    new Point3d(extents.MinPoint.X, extents.MinPoint.Y, center.Z),
                    new Point3d(extents.MinPoint.X, extents.MaxPoint.Y, center.Z),
                    new Point3d(extents.MaxPoint.X, extents.MinPoint.Y, center.Z),
                    new Point3d(extents.MaxPoint.X, extents.MaxPoint.Y, center.Z)
                };
                Point3d[] projected = corners.Select(point => point.TransformBy(worldToUcs)).ToArray();
                candidate.ProjectedWidth = projected.Max(point => point.X) - projected.Min(point => point.X);
                candidate.ProjectedHeight = projected.Max(point => point.Y) - projected.Min(point => point.Y);
            }
            catch { }

            Point3d ucsCenter = center.TransformBy(worldToUcs);
            candidate.SortX = ucsCenter.X;
            candidate.SortY = ucsCenter.Y;
            return candidate;
        }

        private static BlockTableRecord GetEffectiveDefinition(BlockReference block, Transaction tr)
        {
            try
            {
                ObjectId definitionId = block.IsDynamicBlock ? block.DynamicBlockTableRecord : block.BlockTableRecord;
                return definitionId.IsNull ? null : tr.GetObject(definitionId, OpenMode.ForRead, false) as BlockTableRecord;
            }
            catch { return null; }
        }

        private static bool HasFields(AttributeReference attribute)
        {
            try { return attribute.HasFields; }
            catch { return false; }
        }

        private static double MedianPositive(IEnumerable<double> values)
        {
            double[] ordered = (values ?? Enumerable.Empty<double>()).Where(value => value > 1e-9).OrderBy(value => value).ToArray();
            if (ordered.Length == 0) return 0;
            int middle = ordered.Length / 2;
            return ordered.Length % 2 == 1 ? ordered[middle] : (ordered[middle - 1] + ordered[middle]) * 0.5;
        }
    }
}
