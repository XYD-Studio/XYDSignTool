using System;
using System.Windows;
using System.Windows.Media;

namespace XYDSignTool
{
    public static class IconFactory
    {
        private const double CanvasSize = 32.0;

        public static ImageSource GetVectorIcon(string type)
        {
            if (string.IsNullOrWhiteSpace(type)) return null;
            var group = new DrawingGroup();

            using (DrawingContext dc = group.Open())
            {
                dc.PushGuidelineSet(CreateGuidelines());
                switch (type.Trim().ToLowerInvariant())
                {
                    case "calc": DrawCalcIcon(dc); break;
                    case "print": DrawPrintIcon(dc); break;
                    case "table": DrawTableIcon(dc); break;
                    case "edit": DrawEditIcon(dc); break;
                    case "json": DrawJsonIcon(dc); break;
                    case "block": DrawBlockIcon(dc); break;

                    // ★ 新增：批量打印图标 (带齿轮和纸张)
                    case "batchprint": DrawBatchPrintIcon(dc); break;
                    // ★ 新增：图纸目录图标 (带文件堆叠和目录横线)
                    case "dir": DrawDirIcon(dc); break;
                    // ★ 新增：目录转 Excel (绿色表格与文件夹组合)
                    case "direxcel": DrawDirExcelIcon(dc); break;
                    case "frame": DrawFrameIcon(dc); break;


                    default: DrawDefaultIcon(dc); break;
                }
                dc.Pop();
            }

            group.Freeze();
            var image = new DrawingImage(group);
            image.Freeze();
            return image;
        }

        private static GuidelineSet CreateGuidelines()
        {
            var guidelines = new GuidelineSet();
            for (int i = 0; i <= 32; i++) { guidelines.GuidelinesX.Add(i + 0.5); guidelines.GuidelinesY.Add(i + 0.5); }
            return guidelines;
        }

        // ================= ★ 新增图标设计 =================
        private static void DrawBatchPrintIcon(DrawingContext dc)
        {
            DrawTile(dc, Color.FromRgb(239, 68, 68), Color.FromRgb(185, 28, 28)); // 红色热血底板

            // 堆叠的多张图纸
            DrawRoundedRect(dc, RectOf(9, 4, 13, 10), 1.5, BrushOf(253, 164, 175), PenOf(153, 27, 27, 1.0));
            DrawRoundedRect(dc, RectOf(7, 6, 15, 10), 1.5, BrushOf(254, 202, 202), PenOf(153, 27, 27, 1.0));
            DrawRoundedRect(dc, RectOf(5, 8, 17, 10), 1.5, BrushOf(248, 250, 252), PenOf(30, 41, 59, 1.2));

            // 打印机本体
            DrawRoundedRect(dc, RectOf(4, 15, 24, 9), 2, BrushOf(30, 41, 59), PenOf(15, 23, 42, 1.5));
            DrawRoundedRect(dc, RectOf(7, 17, 18, 3), 1, BrushOf(226, 232, 240), PenOf(100, 116, 139, 0.8));

            // 批量齿轮 (右下角)
            dc.DrawEllipse(BrushOf(250, 204, 21), PenOf(161, 98, 7, 1.5), P(25, 25), 4, 4);
            dc.DrawEllipse(BrushOf(255, 255, 255), null, P(25, 25), 1.5, 1.5);
            var gearPen = RoundPen(Color.FromRgb(161, 98, 7), 1.5);
            dc.DrawLine(gearPen, P(25, 20), P(25, 30)); dc.DrawLine(gearPen, P(20, 25), P(30, 25));
        }

        private static void DrawDirIcon(DrawingContext dc)
        {
            DrawTile(dc, Color.FromRgb(56, 189, 248), Color.FromRgb(2, 132, 199)); // 蓝色底板

            // 文件夹背景
            DrawRoundedRect(dc, RectOf(4, 8, 24, 18), 2, BrushOf(250, 204, 21), PenOf(161, 98, 7, 1.2));
            // 文件夹翻页
            DrawRoundedRect(dc, RectOf(4, 11, 24, 15), 2, BrushOf(253, 230, 138), PenOf(161, 98, 7, 1.2));

            // 目录纸张
            DrawRoundedRect(dc, RectOf(8, 5, 16, 21), 1.5, BrushOf(248, 250, 252), PenOf(51, 65, 85, 1.2));
            var linePen = RoundPen(Color.FromRgb(148, 163, 184), 1.2);
            dc.DrawLine(linePen, P(11, 10), P(21, 10));
            dc.DrawLine(linePen, P(11, 14), P(21, 14));
            dc.DrawLine(linePen, P(11, 18), P(17, 18));
            dc.DrawLine(linePen, P(11, 22), P(19, 22));
        }

        private static void DrawDirExcelIcon(DrawingContext dc)
        {
            DrawTile(dc, Color.FromRgb(34, 197, 94), Color.FromRgb(21, 128, 61)); // 绿色 Excel 底板

            DrawRoundedRect(dc, RectOf(5, 5, 18, 22), 2, BrushOf(248, 250, 252), PenOf(20, 83, 45, 1.2));
            var gridPen = PenOf(134, 239, 172, 1.0);
            dc.DrawLine(gridPen, P(5, 12), P(23, 12)); dc.DrawLine(gridPen, P(5, 19), P(23, 19));
            dc.DrawLine(gridPen, P(14, 12), P(14, 27));

            // 右侧绿色 X 图标
            DrawRoundedRect(dc, RectOf(17, 17, 12, 12), 2.5, BrushOf(21, 128, 61), PenOf(248, 250, 252, 1.5));
            var xPen = RoundPen(Color.FromRgb(255, 255, 255), 2);
            dc.DrawLine(xPen, P(20, 20), P(26, 26)); dc.DrawLine(xPen, P(26, 20), P(20, 26));
        }

        private static void DrawFrameIcon(DrawingContext dc)
        {
            DrawTile(dc, Color.FromRgb(79, 70, 229), Color.FromRgb(67, 56, 202)); // 紫蓝色底板

            // 绘制一张大图纸
            DrawRoundedRect(dc, RectOf(5, 5, 22, 22), 1, BrushOf(248, 250, 252), PenOf(30, 41, 59, 1.2));

            // 绘制图纸边框 (内框)
            DrawRoundedRect(dc, RectOf(7, 7, 18, 18), 0, null, PenOf(148, 163, 184, 1.0));

            // 绘制右下角的图签 (Title Block)
            DrawRoundedRect(dc, RectOf(18, 20, 7, 5), 0, BrushOf(191, 219, 254), PenOf(30, 41, 59, 1.0));
            var linePen = RoundPen(Color.FromRgb(30, 41, 59), 0.8);
            dc.DrawLine(linePen, P(18, 22.5), P(25, 22.5));
            dc.DrawLine(linePen, P(21.5, 20), P(21.5, 25));
        }

        // ============ 你原本就有的旧图标保留 ============
        private static void DrawCalcIcon(DrawingContext dc) { DrawTile(dc, Color.FromRgb(37, 99, 235), Color.FromRgb(14, 165, 233)); DrawRoundedRect(dc, RectOf(5, 5, 16, 20), 2.2, BrushOf(239, 246, 255), PenOf(191, 219, 254, 1.1)); var gridPen = PenOf(147, 197, 253, 0.6); dc.DrawLine(gridPen, P(8, 10), P(19, 10)); dc.DrawLine(gridPen, P(8, 14), P(19, 14)); dc.DrawLine(gridPen, P(8, 18), P(19, 18)); dc.DrawLine(gridPen, P(11, 7), P(11, 23)); dc.DrawLine(gridPen, P(15, 7), P(15, 23)); var cadPen = RoundPen(Color.FromRgb(2, 132, 199), 1.3); dc.DrawLine(cadPen, P(8, 21), P(13, 15)); dc.DrawLine(cadPen, P(13, 15), P(18.5, 19)); DrawRoundedRect(dc, RectOf(14, 8, 13, 18), 2.8, BrushOf(30, 41, 59), PenOf(15, 23, 42, 1.1)); DrawRoundedRect(dc, RectOf(16, 10, 9, 4), 1.1, BrushOf(220, 252, 231), PenOf(134, 239, 172, 0.7)); DrawCalcButton(dc, 16.2, 16.1, BrushOf(248, 250, 252)); DrawCalcButton(dc, 20.0, 16.1, BrushOf(248, 250, 252)); DrawCalcButton(dc, 23.8, 16.1, BrushOf(251, 191, 36)); DrawCalcButton(dc, 16.2, 19.8, BrushOf(248, 250, 252)); DrawCalcButton(dc, 20.0, 19.8, BrushOf(248, 250, 252)); DrawCalcButton(dc, 23.8, 19.8, BrushOf(56, 189, 248)); DrawCalcButton(dc, 16.2, 23.5, BrushOf(248, 250, 252)); DrawCalcButton(dc, 20.0, 23.5, BrushOf(248, 250, 252)); DrawCalcButton(dc, 23.8, 23.5, BrushOf(34, 197, 94)); }
        private static void DrawPrintIcon(DrawingContext dc) { DrawTile(dc, Color.FromRgb(22, 163, 74), Color.FromRgb(20, 184, 166)); DrawRoundedRect(dc, RectOf(9, 4.2, 13, 10), 1.7, BrushOf(224, 242, 254), PenOf(56, 189, 248, 0.9)); DrawRoundedRect(dc, RectOf(7, 6, 15, 10), 1.7, BrushOf(236, 253, 245), PenOf(52, 211, 153, 0.9)); DrawRoundedRect(dc, RectOf(5, 7.8, 17, 10), 1.7, BrushOf(248, 250, 252), PenOf(51, 65, 85, 1.1)); var planPen = RoundPen(Color.FromRgb(14, 165, 233), 1.1); dc.DrawLine(planPen, P(7, 15.5), P(11, 11.5)); dc.DrawLine(planPen, P(11, 11.5), P(17, 15.3)); DrawRoundedRect(dc, RectOf(4, 14.2, 24, 10.5), 2.7, BrushOf(30, 41, 59), PenOf(15, 23, 42, 1.2)); DrawRoundedRect(dc, RectOf(7.2, 16, 17.6, 3), 1.2, BrushOf(226, 232, 240), PenOf(148, 163, 184, 0.7)); DrawRoundedRect(dc, RectOf(20.6, 20, 4.8, 2.5), 1.0, BrushOf(56, 189, 248), null); dc.DrawEllipse(BrushOf(34, 197, 94), null, P(22.1, 21.25), 0.65, 0.65); dc.DrawEllipse(BrushOf(250, 204, 21), null, P(24, 21.25), 0.65, 0.65); DrawRoundedRect(dc, RectOf(8, 22.1, 16, 6.7), 1.8, BrushOf(255, 255, 255), PenOf(51, 65, 85, 1.0)); var textPen = RoundPen(Color.FromRgb(203, 213, 225), 0.9); dc.DrawLine(textPen, P(10.4, 24.2), P(21.3, 24.2)); dc.DrawLine(textPen, P(10.4, 26.1), P(20.4, 26.1)); }
        private static void DrawTableIcon(DrawingContext dc) { DrawTile(dc, Color.FromRgb(245, 158, 11), Color.FromRgb(234, 88, 12)); DrawRoundedRect(dc, RectOf(6, 5, 17, 22), 2.4, BrushOf(248, 250, 252), PenOf(51, 65, 85, 1.2)); DrawRoundedRect(dc, RectOf(8, 7, 13, 3.2), 1.0, BrushOf(34, 197, 94), null); var gridPen = PenOf(203, 213, 225, 0.8); dc.DrawLine(gridPen, P(8, 13), P(21, 13)); dc.DrawLine(gridPen, P(8, 17), P(21, 17)); dc.DrawLine(gridPen, P(8, 21), P(21, 21)); dc.DrawLine(gridPen, P(12.3, 11), P(12.3, 25)); dc.DrawLine(gridPen, P(16.6, 11), P(16.6, 25)); var arrowPen = RoundPen(Color.FromRgb(37, 99, 235), 2.2); dc.DrawLine(arrowPen, P(18.5, 23.5), P(26, 16)); dc.DrawLine(arrowPen, P(26, 16), P(26, 20.5)); dc.DrawLine(arrowPen, P(26, 16), P(21.5, 16)); DrawRoundedRect(dc, RectOf(22.3, 23.2, 5.3, 3.7), 1.0, BrushOf(224, 242, 254), PenOf(14, 165, 233, 1.0)); }
        private static void DrawEditIcon(DrawingContext dc) { DrawTile(dc, Color.FromRgb(168, 85, 247), Color.FromRgb(126, 34, 206)); DrawRoundedRect(dc, RectOf(6, 5, 16, 22), 2.0, BrushOf(248, 250, 252), PenOf(51, 65, 85, 1.1)); var textPen = RoundPen(Color.FromRgb(203, 213, 225), 1.2); dc.DrawLine(textPen, P(9, 9), P(19, 9)); dc.DrawLine(textPen, P(9, 13), P(19, 13)); dc.DrawLine(textPen, P(9, 17), P(15, 17)); dc.PushTransform(new RotateTransform(45, 18, 18)); DrawRoundedRect(dc, RectOf(15, 10, 6, 14), 1.0, BrushOf(250, 204, 21), PenOf(30, 41, 59, 1.0)); dc.DrawLine(PenOf(30, 41, 59, 0.8), P(17, 10), P(17, 24)); dc.DrawLine(PenOf(30, 41, 59, 0.8), P(19, 10), P(19, 24)); DrawRoundedRect(dc, RectOf(15, 7, 6, 3), 1.0, BrushOf(244, 114, 182), PenOf(30, 41, 59, 1.0)); PathGeometry tip = new PathGeometry(); PathFigure fig = new PathFigure { StartPoint = P(15, 24), IsClosed = true }; fig.Segments.Add(new LineSegment(P(21, 24), false)); fig.Segments.Add(new LineSegment(P(18, 28), false)); tip.Figures.Add(fig); dc.DrawGeometry(BrushOf(253, 230, 138), PenOf(30, 41, 59, 1.0), tip); PathGeometry core = new PathGeometry(); PathFigure coreFig = new PathFigure { StartPoint = P(17, 26.5), IsClosed = true }; coreFig.Segments.Add(new LineSegment(P(19, 26.5), false)); coreFig.Segments.Add(new LineSegment(P(18, 28), false)); core.Figures.Add(coreFig); dc.DrawGeometry(BrushOf(30, 41, 59), null, core); dc.Pop(); }
        private static void DrawJsonIcon(DrawingContext dc) { DrawTile(dc, Color.FromRgb(59, 130, 246), Color.FromRgb(29, 78, 216)); DrawRoundedRect(dc, RectOf(7, 6, 18, 20), 2.5, BrushOf(30, 41, 59), PenOf(15, 23, 42, 1.5)); var bracketPen = RoundPen(Color.FromRgb(56, 189, 248), 1.8); PathGeometry leftBracket = new PathGeometry(); PathFigure lFig = new PathFigure { StartPoint = P(14, 10), IsFilled = false }; lFig.Segments.Add(new BezierSegment(P(10, 10), P(11, 16), P(8, 16), true)); lFig.Segments.Add(new BezierSegment(P(11, 16), P(10, 22), P(14, 22), true)); leftBracket.Figures.Add(lFig); dc.DrawGeometry(null, bracketPen, leftBracket); PathGeometry rightBracket = new PathGeometry(); PathFigure rFig = new PathFigure { StartPoint = P(18, 10), IsFilled = false }; rFig.Segments.Add(new BezierSegment(P(22, 10), P(21, 16), P(24, 16), true)); rFig.Segments.Add(new BezierSegment(P(21, 16), P(22, 22), P(18, 22), true)); rightBracket.Figures.Add(rFig); dc.DrawGeometry(null, bracketPen, rightBracket); dc.DrawEllipse(BrushOf(250, 204, 21), null, P(16, 16), 1.2, 1.2); }
        private static void DrawBlockIcon(DrawingContext dc) { DrawTile(dc, Color.FromRgb(168, 85, 247), Color.FromRgb(126, 34, 206)); DrawRoundedRect(dc, RectOf(5, 5, 10, 10), 2, BrushOf(168, 85, 247), PenOf(126, 34, 206, 1.5)); DrawRoundedRect(dc, RectOf(17, 5, 10, 10), 2, BrushOf(192, 132, 252), PenOf(126, 34, 206, 1.5)); DrawRoundedRect(dc, RectOf(5, 17, 10, 10), 2, BrushOf(192, 132, 252), PenOf(126, 34, 206, 1.5)); DrawRoundedRect(dc, RectOf(17, 17, 10, 10), 2, BrushOf(233, 213, 255), PenOf(126, 34, 206, 1.5)); }
        private static void DrawDefaultIcon(DrawingContext dc) { DrawTile(dc, Color.FromRgb(100, 116, 139), Color.FromRgb(71, 85, 105)); dc.DrawEllipse(BrushOf(226, 232, 240), PenOf(51, 65, 85, 1.2), P(16, 16), 7, 7); var pen = RoundPen(Color.FromRgb(51, 65, 85), 2); dc.DrawLine(pen, P(16, 10.5), P(16, 17)); dc.DrawLine(pen, P(16, 21), P(16, 21.2)); }
        private static void DrawTile(DrawingContext dc, Color topColor, Color bottomColor) { DrawRoundedRect(dc, RectOf(3.2, 3.8, 25.6, 25.6), 5.5, new SolidColorBrush(Color.FromArgb(45, 15, 23, 42)), null); var brush = new LinearGradientBrush(topColor, bottomColor, new Point(0, 0), new Point(1, 1)); brush.Freeze(); DrawRoundedRect(dc, RectOf(2.5, 2.3, 27, 27), 5.5, brush, PenOf(255, 255, 255, 0.55, 90)); DrawRoundedRect(dc, RectOf(4.4, 4.2, 23.2, 8.5), 4.0, new SolidColorBrush(Color.FromArgb(38, 255, 255, 255)), null); }
        private static void DrawCalcButton(DrawingContext dc, double x, double y, Brush brush) { DrawRoundedRect(dc, RectOf(x, y, 2.4, 2.4), 0.7, brush, null); }
        private static void DrawRoundedRect(DrawingContext dc, Rect rect, double radius, Brush fill, Pen stroke) { dc.DrawRoundedRectangle(fill, stroke, rect, radius, radius); }
        private static Rect RectOf(double x, double y, double width, double height) { return new Rect(x, y, width, height); }
        private static Point P(double x, double y) { return new Point(x, y); }
        private static SolidColorBrush BrushOf(byte r, byte g, byte b) { var brush = new SolidColorBrush(Color.FromRgb(r, g, b)); brush.Freeze(); return brush; }
        private static Pen PenOf(byte r, byte g, byte b, double thickness, byte alpha = 255) { var brush = new SolidColorBrush(Color.FromArgb(alpha, r, g, b)); brush.Freeze(); var pen = new Pen(brush, thickness); pen.Freeze(); return pen; }
        private static Pen RoundPen(Color color, double thickness) { var brush = new SolidColorBrush(color); brush.Freeze(); var pen = new Pen(brush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }; pen.Freeze(); return pen; }
    }
}