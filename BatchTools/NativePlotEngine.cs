using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.PlottingServices;
using Application = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace XYDSignTool
{
    public static class NativePlotEngine
    {
        private class PlotContextState
        {
            public string CurrentLayout { get; set; }
            public object TileMode { get; set; }
            public object CvPort { get; set; }
        }

        public static List<string> GetAvailablePrinters()
        {
            List<string> printers = new List<string>();
            PlotSettingsValidator psv = PlotSettingsValidator.Current;
            foreach (string device in psv.GetPlotDeviceList()) printers.Add(device);
            return printers;
        }

        public static List<string> GetAvailablePlotStyles()
        {
            List<string> styles = new List<string>();
            PlotSettingsValidator psv = PlotSettingsValidator.Current;
            foreach (string style in psv.GetPlotStyleSheetList()) styles.Add(style);
            return styles;
        }

        public static void RefreshPlotConfigurationList()
        {
            try
            {
                PlotConfigManager.RefreshList(RefreshCode.All);
            }
            catch { }
        }

        public static List<MediaInfo> GetMediaInfoList(string printerName)
        {
            List<MediaInfo> list = new List<MediaInfo>();
            RefreshPlotConfigurationList();
            using (PlotSettings ps = new PlotSettings(true))
            {
                PlotSettingsValidator psv = PlotSettingsValidator.Current;
                try
                {
                    psv.SetPlotConfigurationName(ps, printerName, null);
                    psv.RefreshLists(ps);
                    var mediaList = psv.GetCanonicalMediaNameList(ps);
                    foreach (string canonName in mediaList)
                    {
                        string localName = psv.GetLocaleMediaName(ps, canonName);
                        list.Add(new MediaInfo { CanonicalName = canonName, LocalName = localName });
                    }
                }
                catch { }
            }
            return list;
        }

        public static string TryMatchMedia(string requestedSize, List<MediaInfo> mediaList)
        {
            if (string.IsNullOrWhiteSpace(requestedSize) || mediaList == null || mediaList.Count == 0) return null;

            string requested = requestedSize.Trim();
            foreach (MediaInfo media in mediaList)
            {
                if (EqualsIgnoreCase(media.LocalName, requested) || EqualsIgnoreCase(media.CanonicalName, requested))
                    return media.CanonicalName;
            }

            if (requested.Equals("A4", StringComparison.OrdinalIgnoreCase))
            {
                MediaInfo match = FindA4Media(mediaList, true, true) ??
                                  FindA4Media(mediaList, true, false) ??
                                  FindA4Media(mediaList, false, true);
                if (match != null) return match.CanonicalName;
                return null;
            }

            foreach (MediaInfo media in mediaList)
            {
                if (ContainsPaperToken(media.LocalName, requested) || ContainsPaperToken(media.CanonicalName, requested))
                    return media.CanonicalName;
            }

            foreach (MediaInfo media in mediaList)
            {
                if (ContainsIgnoreCase(media.LocalName, requested) || ContainsIgnoreCase(media.CanonicalName, requested))
                    return media.CanonicalName;
            }
            return null;
        }

        private static MediaInfo FindA4Media(List<MediaInfo> mediaList, bool requireA4Token, bool requireDimensions)
        {
            foreach (MediaInfo media in mediaList)
            {
                string combined = (media.LocalName ?? "") + " " + (media.CanonicalName ?? "");
                if (requireA4Token && !ContainsPaperToken(combined, "A4")) continue;
                if (requireDimensions && !ContainsA4Dimensions(combined)) continue;
                return media;
            }
            return null;
        }

        private static bool ContainsA4Dimensions(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            return Regex.IsMatch(value, @"(?i)(?<!\d)210(?:[\.,]0+)?\D{0,12}297(?:[\.,]0+)?(?!\d)") ||
                   Regex.IsMatch(value, @"(?i)(?<!\d)297(?:[\.,]0+)?\D{0,12}210(?:[\.,]0+)?(?!\d)");
        }

        private static bool ContainsPaperToken(string value, string requested)
        {
            if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(requested)) return false;
            string pattern = @"(?i)(?<![A-Z0-9])" + Regex.Escape(requested.Trim()) + @"(?![A-Z0-9+\.])";
            return Regex.IsMatch(value, pattern);
        }

        private static bool EqualsIgnoreCase(string value, string expected)
        {
            return !string.IsNullOrEmpty(value) && value.Equals(expected, StringComparison.OrdinalIgnoreCase);
        }

        private static bool ContainsIgnoreCase(string value, string expected)
        {
            return !string.IsNullOrEmpty(value) && value.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool PlotToPdf(Database db, TitleBlockModel block, string outputPath, string printerName, string canonicalMediaName, string styleSheet)
        {
            bool success = false;
            PlotContextState plotContextState = null;
            Dictionary<string, object> oldSystemVariables = CaptureSystemVariables("BACKGROUNDPLOT", "OLEHIDE", "OLEQUALITY", "OLESTARTUP", "IMAGEQUALITY");
            try
            {
                ApplyOlePlotSystemVariables();
                plotContextState = ActivatePlotContext(block);

                Extents2d plotWindow;
                string windowError;
                if (!TryBuildPlotWindow(block, out plotWindow, out windowError))
                {
                    WriteMessage($"\n[打印拦截] {windowError}");
                    return false;
                }

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    DBDictionary layoutDict = tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead) as DBDictionary;
                    if (layoutDict == null || !layoutDict.Contains(block.LayoutName))
                    {
                        WriteMessage($"\n[打印拦截] 找不到图框所属布局: {block.LayoutName}");
                        return false;
                    }

                    ObjectId layoutId = layoutDict.GetAt(block.LayoutName);
                    Layout layout = tr.GetObject(layoutId, OpenMode.ForRead) as Layout;
                    if (layout == null)
                    {
                        WriteMessage($"\n[打印拦截] 布局对象无效: {block.LayoutName}");
                        return false;
                    }

                    int preparedOleCount = PrepareOleFramesForPlot(tr, db);
                    int preparedRasterCount = PrepareRasterImagesForPlot(tr, db);
                    if (preparedOleCount > 0 || preparedRasterCount > 0)
                    {
                        WriteMessage($"\n[打印调试] 已准备 OLE 对象 {preparedOleCount} 个，嵌入图片 {preparedRasterCount} 个。");
                    }

                    PlotInfo pi = new PlotInfo();
                    pi.Layout = layoutId;

                    PlotSettings ps = new PlotSettings(layout.ModelType);
                    PlotSettingsValidator psv = PlotSettingsValidator.Current;

                    string configError;
                    if (!TryConfigurePlotSettings(psv, ps, layout, plotWindow, printerName, canonicalMediaName, styleSheet, out configError))
                    {
                        WriteMessage($"\n[底层异常] 打印核心配置失败: {configError}");
                        WriteMessage($"\n[打印调试] 布局={block.LayoutName}, 空间={(block.IsModelSpace ? "Model" : "Paper")}, 窗口={FormatWindow(plotWindow)}, 设备={printerName}, 纸张={canonicalMediaName}, 样式={styleSheet}");
                        return false;
                    }

                    pi.OverrideSettings = ps;
                    PlotInfoValidator piv = new PlotInfoValidator();
                    piv.MediaMatchingPolicy = MatchingPolicy.MatchEnabled;

                    try
                    {
                        piv.Validate(pi);
                    }
                    catch (System.Exception ex)
                    {
                        WriteMessage($"\n[底层异常] 打印参数验证失败: {ex.Message}");
                        return false;
                    }

                    if (PlotFactory.ProcessPlotState != ProcessPlotState.NotPlotting)
                    {
                        WriteMessage("\n[打印拦截] AutoCAD 当前已有打印任务正在运行，请稍后重试。");
                        return false;
                    }

                    try
                    {
                        using (PlotEngine pe = PlotFactory.CreatePublishEngine())
                        {
                            pe.BeginPlot(null, null);
                            pe.BeginDocument(pi, block.DrawTitle, null, 1, true, outputPath);
                            PlotPageInfo ppi = new PlotPageInfo();
                            pe.BeginPage(ppi, pi, true, null);
                            pe.BeginGenerateGraphics(null);
                            pe.EndGenerateGraphics(null);
                            pe.EndPage(null);
                            pe.EndDocument(null);
                            pe.EndPlot(null);
                            success = true;
                        }
                    }
                    catch (System.Exception ex) { WriteMessage($"\n[出图异常] 驱动打印失败: {ex.Message}"); }

                    tr.Commit();
                }
            }
            finally
            {
                RestorePlotContext(plotContextState);
                RestoreSystemVariables(oldSystemVariables);
            }

            return success;
        }

        private static bool TryConfigurePlotSettings(PlotSettingsValidator psv, PlotSettings ps, Layout layout, Extents2d plotWindow, string printerName, string canonicalMediaName, string styleSheet, out string error)
        {
            error = null;

            string stepError;
            if (!TryPlotStep("复制布局打印上下文", () => ps.CopyFrom(layout), out stepError))
            {
                WriteMessage($"\n[打印警告] {stepError}，改用干净 PlotSettings 继续。");
            }

            if (!TryPlotStep($"绑定打印设备[{printerName}]", () => psv.SetPlotConfigurationName(ps, printerName, null), out error)) return false;
            if (!TryPlotStep("刷新打印设备纸张列表", () => psv.RefreshLists(ps), out error)) return false;
            if (!TryPlotStep("设置纸张单位为毫米", () => psv.SetPlotPaperUnits(ps, PlotPaperUnit.Millimeters), out error)) return false;

            string resolvedMedia = ResolveCanonicalMediaName(psv, ps, canonicalMediaName);
            if (string.IsNullOrEmpty(resolvedMedia))
            {
                error = $"当前打印设备找不到纸张[{canonicalMediaName}]";
                return false;
            }

            if (!TryPlotStep($"设置纸张[{resolvedMedia}]", () => psv.SetCanonicalMediaName(ps, resolvedMedia), out error)) return false;
            if (!TryPlotStep("应用窗口打印范围", () => SetWindowPlotArea(psv, ps, plotWindow), out error)) return false;
            if (!TryPlotStep("设置自动旋转方向", () => psv.SetPlotRotation(ps, ChooseRotation(plotWindow, ps.PlotPaperSize)), out error)) return false;
            if (!TryPlotStep("设置布满图纸并居中", () => ApplyFitToPaperAndCenter(psv, ps), out error)) return false;

            ps.ShowPlotStyles = true;
            ps.PlotPlotStyles = true;
            ps.PrintLineweights = true;
            ps.ScaleLineweights = false;
            ApplyOleFriendlyPlotSettings(ps);
            if (!string.IsNullOrWhiteSpace(styleSheet))
            {
                if (!TryPlotStep($"设置打印样式表[{styleSheet}]", () => psv.SetCurrentStyleSheet(ps, styleSheet), out error)) return false;
            }

            if (!TryPlotStep("复核布满图纸并居中", () => ApplyFitToPaperAndCenter(psv, ps), out error)) return false;
            WriteMessage($"\n[打印调试] 最终设置: 布满={ps.UseStandardScale && ps.StdScaleType == StdScaleType.ScaleToFit}, 居中={ps.PlotCentered}, 原点=({ps.PlotOrigin.X:0.###},{ps.PlotOrigin.Y:0.###}), 旋转={ps.PlotRotation}, 纸张=({ps.PlotPaperSize.X:0.###},{ps.PlotPaperSize.Y:0.###}), 窗口={FormatWindow(plotWindow)}");
            return true;
        }

        private static void ApplyOleFriendlyPlotSettings(PlotSettings ps)
        {
            try { ps.PlotHidden = false; } catch { }
            try { ps.PlotTransparency = true; } catch { }
            try { ps.ShadePlot = PlotSettingsShadePlotType.AsDisplayed; } catch { }
            try { ps.ShadePlotResLevel = ShadePlotResLevel.Maximum; } catch { }
            try { ps.ShadePlotCustomDpi = 600; } catch { }
        }

        private static int PrepareOleFramesForPlot(Transaction tr, Database db)
        {
            int count = 0;
            if (tr == null || db == null || db.BlockTableId.IsNull) return count;

            BlockTable blockTable = tr.GetObject(db.BlockTableId, OpenMode.ForRead, false) as BlockTable;
            if (blockTable == null) return count;

            foreach (ObjectId btrId in blockTable)
            {
                BlockTableRecord btr = tr.GetObject(btrId, OpenMode.ForRead, false) as BlockTableRecord;
                if (btr == null) continue;

                foreach (ObjectId id in btr)
                {
                    try
                    {
                        Ole2Frame ole = tr.GetObject(id, OpenMode.ForWrite, false) as Ole2Frame;
                        if (ole == null) continue;

                        try { ole.Visible = true; } catch { }
                        try { ole.OutputQuality = 2; } catch { }
                        try { ole.AutoOutputQuality = 2; } catch { }
                        try { ole.RecordGraphicsModified(true); } catch { }
                        try { ole.Draw(); } catch { }
                        count++;
                    }
                    catch { }
                }
            }

            return count;
        }

        private static int PrepareRasterImagesForPlot(Transaction tr, Database db)
        {
            int count = 0;
            if (tr == null || db == null || db.BlockTableId.IsNull) return count;

            BlockTable blockTable = tr.GetObject(db.BlockTableId, OpenMode.ForRead, false) as BlockTable;
            if (blockTable == null) return count;

            foreach (ObjectId btrId in blockTable)
            {
                BlockTableRecord btr = tr.GetObject(btrId, OpenMode.ForRead, false) as BlockTableRecord;
                if (btr == null) continue;

                foreach (ObjectId id in btr)
                {
                    try
                    {
                        RasterImage rasterImage = tr.GetObject(id, OpenMode.ForWrite, false) as RasterImage;
                        if (rasterImage == null) continue;

                        rasterImage.ShowImage = true;
                        rasterImage.DisplayOptions = ImageDisplayOptions.Show;
                        try { rasterImage.RecordGraphicsModified(true); } catch { }
                        try { rasterImage.Draw(); } catch { }

                        if (!rasterImage.ImageDefId.IsNull)
                        {
                            RasterImageDef imageDef = tr.GetObject(rasterImage.ImageDefId, OpenMode.ForWrite, false) as RasterImageDef;
                            if (imageDef != null)
                            {
                                try { if (!imageDef.IsLoaded) imageDef.Load(); } catch { }
                                try { imageDef.ImageModified = true; } catch { }
                                try { imageDef.UpdateEntities(); } catch { }
                            }
                        }

                        count++;
                    }
                    catch { }
                }
            }

            return count;
        }

        private static Dictionary<string, object> CaptureSystemVariables(params string[] names)
        {
            Dictionary<string, object> values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in names)
            {
                try { values[name] = Application.GetSystemVariable(name); }
                catch { }
            }
            return values;
        }

        private static void ApplyOlePlotSystemVariables()
        {
            TrySetSystemVariable("BACKGROUNDPLOT", 0);
            TrySetSystemVariable("OLEHIDE", 0);
            TrySetSystemVariable("OLEQUALITY", 2);
            TrySetSystemVariable("OLESTARTUP", 1);
            TrySetSystemVariable("IMAGEQUALITY", 1);
        }

        private static void RestoreSystemVariables(Dictionary<string, object> values)
        {
            if (values == null) return;
            foreach (KeyValuePair<string, object> item in values)
            {
                TrySetSystemVariable(item.Key, item.Value);
            }
        }

        private static void TrySetSystemVariable(string name, object value)
        {
            try { Application.SetSystemVariable(name, value); } catch { }
        }

        private static bool TryGetSystemVariable(string name, out object value)
        {
            value = null;
            try
            {
                value = Application.GetSystemVariable(name);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static PlotContextState ActivatePlotContext(TitleBlockModel block)
        {
            PlotContextState state = new PlotContextState();
            TryGetSystemVariable("TILEMODE", out object tileMode);
            TryGetSystemVariable("CVPORT", out object cvPort);
            state.TileMode = tileMode;
            state.CvPort = cvPort;

            try { state.CurrentLayout = LayoutManager.Current.CurrentLayout; } catch { }

            if (block != null && block.IsModelSpace)
            {
                TrySetSystemVariable("TILEMODE", 1);
                try { LayoutManager.Current.CurrentLayout = "Model"; } catch { }
                SetModelTopView();
            }
            else if (block != null)
            {
                TrySetSystemVariable("TILEMODE", 0);
                try { LayoutManager.Current.CurrentLayout = block.LayoutName; } catch { }
                TrySetSystemVariable("CVPORT", 1);
            }

            return state;
        }

        private static void RestorePlotContext(PlotContextState state)
        {
            if (state == null) return;

            try
            {
                int tileMode = Convert.ToInt32(state.TileMode);
                if (tileMode == 0)
                {
                    TrySetSystemVariable("TILEMODE", 0);
                    if (!string.IsNullOrWhiteSpace(state.CurrentLayout))
                    {
                        try { LayoutManager.Current.CurrentLayout = state.CurrentLayout; } catch { }
                    }
                    if (state.CvPort != null) TrySetSystemVariable("CVPORT", state.CvPort);
                }
                else
                {
                    TrySetSystemVariable("TILEMODE", 1);
                }
            }
            catch
            {
                if (state.TileMode != null) TrySetSystemVariable("TILEMODE", state.TileMode);
                if (state.CvPort != null) TrySetSystemVariable("CVPORT", state.CvPort);
            }
        }

        private static void SetModelTopView()
        {
            try
            {
                var doc = Application.DocumentManager.MdiActiveDocument;
                if (doc == null || doc.Editor == null) return;

                using (ViewTableRecord view = doc.Editor.GetCurrentView())
                {
                    view.ViewDirection = Vector3d.ZAxis;
                    view.Target = Point3d.Origin;
                    view.ViewTwist = 0.0;
                    doc.Editor.SetCurrentView(view);
                }
            }
            catch { }
        }

        private static void ApplyFitToPaperAndCenter(PlotSettingsValidator psv, PlotSettings ps)
        {
            psv.SetUseStandardScale(ps, true);
            psv.SetStdScaleType(ps, StdScaleType.ScaleToFit);
            psv.SetPlotOrigin(ps, new Point2d(0, 0));
            psv.SetPlotCentered(ps, true);
        }

        private static void SetWindowPlotArea(PlotSettingsValidator psv, PlotSettings ps, Extents2d plotWindow)
        {
            string firstAreaError = null;
            string firstTypeError = null;

            bool firstAreaOk = TryPlotStep($"设置窗口坐标{FormatWindow(plotWindow)}", () => psv.SetPlotWindowArea(ps, plotWindow), out firstAreaError);
            bool firstTypeOk = firstAreaOk && TryPlotStep("切换打印区域为窗口", () => psv.SetPlotType(ps, Autodesk.AutoCAD.DatabaseServices.PlotType.Window), out firstTypeError);
            if (firstAreaOk && firstTypeOk) return;

            string secondTypeError = null;
            string secondAreaError = null;
            bool secondTypeOk = TryPlotStep("切换打印区域为窗口", () => psv.SetPlotType(ps, Autodesk.AutoCAD.DatabaseServices.PlotType.Window), out secondTypeError);
            bool secondAreaOk = secondTypeOk && TryPlotStep($"设置窗口坐标{FormatWindow(plotWindow)}", () => psv.SetPlotWindowArea(ps, plotWindow), out secondAreaError);
            if (secondTypeOk && secondAreaOk) return;

            throw new Autodesk.AutoCAD.Runtime.Exception(
                Autodesk.AutoCAD.Runtime.ErrorStatus.InvalidInput,
                $"窗口范围不被当前布局接受。先设窗口再设类型: {firstAreaError ?? firstTypeError}; 先设类型再设窗口: {secondTypeError ?? secondAreaError}");
        }

        private static bool TryPlotStep(string stepName, Action action, out string error)
        {
            try
            {
                action();
                error = null;
                return true;
            }
            catch (System.Exception ex)
            {
                error = $"{stepName}失败: {ex.Message}";
                return false;
            }
        }

        private static string ResolveCanonicalMediaName(PlotSettingsValidator psv, PlotSettings ps, string requestedMedia)
        {
            if (string.IsNullOrWhiteSpace(requestedMedia)) return null;

            var mediaList = psv.GetCanonicalMediaNameList(ps);
            foreach (string media in mediaList)
            {
                if (media.Equals(requestedMedia, StringComparison.OrdinalIgnoreCase)) return media;
            }

            foreach (string media in mediaList)
            {
                string localName = "";
                try { localName = psv.GetLocaleMediaName(ps, media); } catch { }
                if (localName.Equals(requestedMedia, StringComparison.OrdinalIgnoreCase)) return media;
            }

            foreach (string media in mediaList)
            {
                string localName = "";
                try { localName = psv.GetLocaleMediaName(ps, media); } catch { }
                if (media.IndexOf(requestedMedia, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    localName.IndexOf(requestedMedia, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return media;
                }
            }

            return null;
        }

        private static bool TryBuildPlotWindow(TitleBlockModel block, out Extents2d window, out string error)
        {
            window = new Extents2d();
            error = null;

            if (block == null)
            {
                error = "图框数据为空。";
                return false;
            }

            double minX = Math.Min(block.MinPt.X, block.MaxPt.X);
            double minY = Math.Min(block.MinPt.Y, block.MaxPt.Y);
            double maxX = Math.Max(block.MinPt.X, block.MaxPt.X);
            double maxY = Math.Max(block.MinPt.Y, block.MaxPt.Y);

            if (!IsFinite(minX) || !IsFinite(minY) || !IsFinite(maxX) || !IsFinite(maxY))
            {
                error = $"{block.DrawNum} {block.DrawTitle} 的图框坐标包含非法数值。";
                return false;
            }

            double width = maxX - minX;
            double height = maxY - minY;
            if (width < 1.0 || height < 1.0)
            {
                error = $"{block.DrawNum} {block.DrawTitle} 的图框范围过小，无法打印。";
                return false;
            }

            if (block.IsModelSpace && TryBuildModelSpaceDcsWindow(minX, minY, maxX, maxY, out window))
            {
                return true;
            }

            window = new Extents2d(minX, minY, maxX, maxY);
            return true;
        }

        private static bool TryBuildModelSpaceDcsWindow(double minX, double minY, double maxX, double maxY, out Extents2d window)
        {
            window = new Extents2d();

            try
            {
                var doc = Application.DocumentManager.MdiActiveDocument;
                if (doc == null || doc.Editor == null) return false;

                using (ViewTableRecord view = doc.Editor.GetCurrentView())
                {
                    Matrix3d wcsToDcs = Matrix3d.PlaneToWorld(view.ViewDirection);
                    wcsToDcs = Matrix3d.Displacement(view.Target - Point3d.Origin) * wcsToDcs;
                    wcsToDcs = Matrix3d.Rotation(-view.ViewTwist, view.ViewDirection, view.Target) * wcsToDcs;
                    wcsToDcs = wcsToDcs.Inverse();

                    Point3d[] points =
                    {
                        new Point3d(minX, minY, 0.0),
                        new Point3d(maxX, minY, 0.0),
                        new Point3d(maxX, maxY, 0.0),
                        new Point3d(minX, maxY, 0.0)
                    };

                    double dcsMinX = double.MaxValue;
                    double dcsMinY = double.MaxValue;
                    double dcsMaxX = double.MinValue;
                    double dcsMaxY = double.MinValue;

                    foreach (Point3d point in points)
                    {
                        Point3d transformed = point.TransformBy(wcsToDcs);
                        dcsMinX = Math.Min(dcsMinX, transformed.X);
                        dcsMinY = Math.Min(dcsMinY, transformed.Y);
                        dcsMaxX = Math.Max(dcsMaxX, transformed.X);
                        dcsMaxY = Math.Max(dcsMaxY, transformed.Y);
                    }

                    if (!IsFinite(dcsMinX) || !IsFinite(dcsMinY) || !IsFinite(dcsMaxX) || !IsFinite(dcsMaxY)) return false;
                    if (dcsMaxX - dcsMinX < 1.0 || dcsMaxY - dcsMinY < 1.0) return false;

                    window = new Extents2d(dcsMinX, dcsMinY, dcsMaxX, dcsMaxY);
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        private static PlotRotation ChooseRotation(Extents2d window, Point2d paperSize)
        {
            double windowWidth = Math.Abs(window.MaxPoint.X - window.MinPoint.X);
            double windowHeight = Math.Abs(window.MaxPoint.Y - window.MinPoint.Y);

            if (!IsFinite(paperSize.X) || !IsFinite(paperSize.Y) || paperSize.X <= 0.0 || paperSize.Y <= 0.0)
            {
                return windowHeight > windowWidth ? PlotRotation.Degrees090 : PlotRotation.Degrees000;
            }

            bool windowLandscape = windowWidth >= windowHeight;
            bool paperLandscape = paperSize.X >= paperSize.Y;
            return windowLandscape == paperLandscape ? PlotRotation.Degrees000 : PlotRotation.Degrees090;
        }

        private static string FormatWindow(Extents2d window)
        {
            return $"[{window.MinPoint.X:0.###},{window.MinPoint.Y:0.###}] - [{window.MaxPoint.X:0.###},{window.MaxPoint.Y:0.###}]";
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static void WriteMessage(string message)
        {
            try
            {
                var doc = Application.DocumentManager.MdiActiveDocument;
                if (doc != null && doc.Editor != null) doc.Editor.WriteMessage(message);
            }
            catch { }
        }
    }
}
