using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.PlottingServices;
using Application = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace XYDSignTool
{
    public static class NativePlotEngine
    {
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
            if (mediaList == null || mediaList.Count == 0) return null;
            foreach (var m in mediaList) { if (m.LocalName.Equals(requestedSize, StringComparison.OrdinalIgnoreCase) || m.CanonicalName.Equals(requestedSize, StringComparison.OrdinalIgnoreCase)) return m.CanonicalName; }
            foreach (var m in mediaList) { if (m.LocalName.ToUpper().Contains(requestedSize.ToUpper()) || m.CanonicalName.ToUpper().Contains(requestedSize.ToUpper())) return m.CanonicalName; }
            return null;
        }

        public static bool PlotToPdf(Database db, TitleBlockModel block, string outputPath, string printerName, string canonicalMediaName, string styleSheet)
        {
            Extents2d plotWindow;
            string windowError;
            if (!TryBuildPlotWindow(block, out plotWindow, out windowError))
            {
                WriteMessage($"\n[打印拦截] {windowError}");
                return false;
            }

            bool success = false;
            short bgPlot = (short)Application.GetSystemVariable("BACKGROUNDPLOT");
            try
            {
                Application.SetSystemVariable("BACKGROUNDPLOT", 0);

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
                Application.SetSystemVariable("BACKGROUNDPLOT", bgPlot);
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
            if (!string.IsNullOrWhiteSpace(styleSheet))
            {
                if (!TryPlotStep($"设置打印样式表[{styleSheet}]", () => psv.SetCurrentStyleSheet(ps, styleSheet), out error)) return false;
            }

            if (!TryPlotStep("复核布满图纸并居中", () => ApplyFitToPaperAndCenter(psv, ps), out error)) return false;
            WriteMessage($"\n[打印调试] 最终设置: 布满={ps.UseStandardScale && ps.StdScaleType == StdScaleType.ScaleToFit}, 居中={ps.PlotCentered}, 原点=({ps.PlotOrigin.X:0.###},{ps.PlotOrigin.Y:0.###}), 旋转={ps.PlotRotation}, 纸张=({ps.PlotPaperSize.X:0.###},{ps.PlotPaperSize.Y:0.###}), 窗口={FormatWindow(plotWindow)}");
            return true;
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
