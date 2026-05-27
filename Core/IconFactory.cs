using System;
using System.Windows;
using System.Windows.Media;

namespace XYDSignTool
{
    public static class IconFactory
    {
        private const double CanvasSize = 32.0;

        private static readonly Color PanelFill = Color.FromRgb(43, 54, 68);
        private static readonly Color PanelEdge = Color.FromRgb(103, 118, 137);
        private static readonly Color Ink = Color.FromRgb(229, 236, 244);
        private static readonly Color InkMuted = Color.FromRgb(148, 163, 184);
        private static readonly Color Shadow = Color.FromRgb(18, 25, 34);
        private static readonly Color Paper = Color.FromRgb(246, 249, 252);
        private static readonly Color Blue = Color.FromRgb(80, 174, 255);
        private static readonly Color Green = Color.FromRgb(78, 207, 132);
        private static readonly Color Yellow = Color.FromRgb(248, 197, 73);
        private static readonly Color Orange = Color.FromRgb(245, 159, 64);
        private static readonly Color Red = Color.FromRgb(239, 91, 91);
        private static readonly Color Magenta = Color.FromRgb(207, 112, 232);
        private static readonly Color Cyan = Color.FromRgb(72, 211, 226);

        public static ImageSource GetVectorIcon(string type)
        {
            if (string.IsNullOrWhiteSpace(type)) return null;

            var group = new DrawingGroup
            {
                ClipGeometry = new RectangleGeometry(new Rect(0, 0, CanvasSize, CanvasSize))
            };

            using (DrawingContext dc = group.Open())
            {
                dc.PushGuidelineSet(CreateGuidelines());
                DrawCadPlate(dc);

                switch (type.Trim().ToLowerInvariant())
                {
                    case "calc": DrawCalcIcon(dc); break;
                    case "print": DrawPrintIcon(dc); break;
                    case "table": DrawTableIcon(dc); break;
                    case "edit": DrawEditIcon(dc); break;
                    case "json": DrawJsonIcon(dc); break;
                    case "block": DrawBlockIcon(dc); break;
                    case "batchprint": DrawBatchPrintIcon(dc); break;
                    case "dir": DrawDirIcon(dc); break;
                    case "direxcel": DrawDirExcelIcon(dc); break;
                    case "frame": DrawFrameIcon(dc); break;
                    case "blockcount": DrawBlockCountIcon(dc); break;
                    case "dynlen": DrawDynamicLengthIcon(dc); break;
                    case "spec": DrawSpecIcon(dc); break;
                    case "linelen": DrawLineLengthIcon(dc); break;
                    case "findtext": DrawFindTextIcon(dc); break;
                    case "rotate": DrawRotateIcon(dc); break;
                    case "embedimage": DrawEmbedImageIcon(dc); break;
                    case "help": DrawHelpIcon(dc); break;
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
            for (int i = 0; i <= 32; i++)
            {
                guidelines.GuidelinesX.Add(i + 0.5);
                guidelines.GuidelinesY.Add(i + 0.5);
            }
            return guidelines;
        }

        private static void DrawCadPlate(DrawingContext dc)
        {
            DrawRoundedRect(dc, RectOf(3.4, 4.0, 25.2, 25.2), 2.2, BrushOf(Shadow, 120), null);
            DrawRoundedRect(dc, RectOf(2.8, 2.8, 26.4, 26.4), 2.2, BrushOf(PanelFill), PenOf(PanelEdge, 1.0, 220));
            dc.DrawLine(PenOf(Color.FromRgb(154, 169, 188), 1.0, 80), P(6, 5.2), P(26, 5.2));
            dc.DrawLine(PenOf(Color.FromRgb(8, 13, 20), 1.0, 90), P(6, 28.2), P(26, 28.2));
        }

        private static void DrawBatchPrintIcon(DrawingContext dc)
        {
            DrawPage(dc, 9, 4.8, 14, 10, false);
            DrawPage(dc, 7, 6.8, 15, 10, false);
            DrawPrinterBody(dc, 5, 14.6, 22, 8.6, Red);
            DrawRoundedRect(dc, RectOf(8.2, 21.6, 15.6, 5.6), 0.8, BrushOf(Paper), PenOf(Shadow, 1.0));
            dc.DrawLine(PenOf(InkMuted, 1.0), P(10.2, 24.0), P(20.4, 24.0));
            DrawGear(dc, P(25.1, 24.7), 4.0, Yellow);
        }

        private static void DrawDirIcon(DrawingContext dc)
        {
            DrawRoundedRect(dc, RectOf(5, 8, 20, 16), 1.5, BrushOf(Color.FromRgb(55, 69, 85)), PenOf(Yellow, 1.4));
            DrawPolyline(dc, PenOf(Yellow, 1.4), P(5, 10), P(11.5, 10), P(13, 12), P(25, 12));
            DrawPage(dc, 9, 6, 14, 19, true);
            DrawListLines(dc, 12, 12, 8, 4, InkMuted);
        }

        private static void DrawDirExcelIcon(DrawingContext dc)
        {
            DrawDirIcon(dc);
            DrawRoundedRect(dc, RectOf(17.2, 18, 9.5, 8.5), 1.2, BrushOf(Color.FromRgb(31, 94, 62)), PenOf(Green, 1.1));
            dc.DrawLine(RoundPen(Paper, 1.5), P(19.5, 20), P(24.2, 24.8));
            dc.DrawLine(RoundPen(Paper, 1.5), P(24.2, 20), P(19.5, 24.8));
        }

        private static void DrawFrameIcon(DrawingContext dc)
        {
            DrawPage(dc, 5.8, 5, 20.4, 21.8, true);
            DrawRoundedRect(dc, RectOf(8.0, 7.4, 16.0, 17.0), 0, null, PenOf(Blue, 1.0));
            DrawRoundedRect(dc, RectOf(17.4, 18.8, 6.6, 5.6), 0, BrushOf(Color.FromRgb(221, 235, 250)), PenOf(Shadow, 0.9));
            dc.DrawLine(PenOf(Shadow, 0.8), P(17.4, 21.6), P(24.0, 21.6));
            dc.DrawLine(PenOf(Shadow, 0.8), P(20.8, 18.8), P(20.8, 24.4));
        }

        private static void DrawBlockCountIcon(DrawingContext dc)
        {
            DrawMiniCube(dc, 6.3, 6.5, Blue);
            DrawMiniCube(dc, 16.2, 6.5, Cyan);
            DrawMiniCube(dc, 6.3, 16.4, Cyan);
            DrawMiniCube(dc, 16.2, 16.4, Blue);
            var pen = RoundPen(Yellow, 1.6);
            dc.DrawLine(pen, P(24.0, 17.8), P(28.0, 17.8));
            dc.DrawLine(pen, P(24.0, 22.0), P(28.0, 22.0));
            dc.DrawLine(pen, P(25.0, 15.8), P(25.0, 24.2));
            dc.DrawLine(pen, P(27.0, 15.8), P(27.0, 24.2));
        }

        private static void DrawDynamicLengthIcon(DrawingContext dc)
        {
            DrawRoundedRect(dc, RectOf(6.2, 8, 19.6, 11.8), 1.2, BrushOf(Color.FromRgb(54, 67, 82)), PenOf(Green, 1.5));
            dc.DrawLine(PenOf(InkMuted, 1.0), P(9, 10.8), P(22.8, 17.2));
            DrawDimensionLine(dc, 7.3, 24.2, 24.7, Green);
            dc.DrawLine(PenOf(Green, 1.0), P(7.3, 21.2), P(7.3, 26.7));
            dc.DrawLine(PenOf(Green, 1.0), P(24.7, 21.2), P(24.7, 26.7));
        }

        private static void DrawSpecIcon(DrawingContext dc)
        {
            DrawRoundedRect(dc, RectOf(6.2, 6, 19.6, 20), 1.4, BrushOf(Color.FromRgb(57, 69, 82)), PenOf(Orange, 1.4));
            DrawRoundedRect(dc, RectOf(9, 9, 5.2, 5), 0.7, BrushOf(Color.FromRgb(76, 87, 102)), PenOf(Yellow, 1.0));
            DrawRoundedRect(dc, RectOf(17, 9, 5.2, 5), 0.7, BrushOf(Color.FromRgb(76, 87, 102)), PenOf(Yellow, 1.0));
            DrawRoundedRect(dc, RectOf(9, 17, 13.2, 5), 0.7, BrushOf(Color.FromRgb(76, 87, 102)), PenOf(Yellow, 1.0));
            dc.DrawLine(PenOf(InkMuted, 0.9), P(11, 19.5), P(20.5, 19.5));
        }

        private static void DrawLineLengthIcon(DrawingContext dc)
        {
            var path = new PathGeometry();
            var fig = new PathFigure { StartPoint = P(6.5, 20.5), IsFilled = false };
            fig.Segments.Add(new BezierSegment(P(10, 8.5), P(18.3, 25.0), P(25.4, 10.8), true));
            path.Figures.Add(fig);
            dc.DrawGeometry(null, RoundPen(Cyan, 2.4), path);
            DrawDimensionLine(dc, 7.5, 25.6, 24.5, Yellow);
        }

        private static void DrawFindTextIcon(DrawingContext dc)
        {
            DrawPage(dc, 6.2, 5.8, 14.6, 19.8, true);
            DrawListLines(dc, 9.2, 11, 8, 4, InkMuted);
            dc.DrawEllipse(null, PenOf(Blue, 2.0), P(21.6, 20.5), 4.3, 4.3);
            dc.DrawLine(RoundPen(Blue, 2.0), P(24.7, 23.7), P(28.2, 27.2));
        }

        private static void DrawRotateIcon(DrawingContext dc)
        {
            DrawRoundedRect(dc, RectOf(10.5, 10.5, 10.8, 10.8), 1.3, BrushOf(Color.FromRgb(65, 76, 91)), PenOf(Magenta, 1.3));
            var arc = new PathGeometry();
            var fig = new PathFigure { StartPoint = P(8.4, 18.6), IsFilled = false };
            fig.Segments.Add(new ArcSegment(P(23.4, 9.3), new Size(10, 10), 0, false, SweepDirection.Clockwise, true));
            arc.Figures.Add(fig);
            dc.DrawGeometry(null, RoundPen(Magenta, 2.0), arc);
            dc.DrawLine(RoundPen(Magenta, 2.0), P(23.4, 9.3), P(22.5, 14.0));
            dc.DrawLine(RoundPen(Magenta, 2.0), P(23.4, 9.3), P(18.7, 9.4));
        }

        private static void DrawEmbedImageIcon(DrawingContext dc)
        {
            DrawRoundedRect(dc, RectOf(5.7, 6.0, 18.8, 16.4), 1.2, BrushOf(Color.FromRgb(53, 66, 80)), PenOf(Cyan, 1.4));
            DrawImageMountains(dc);
            dc.DrawEllipse(BrushOf(Yellow), null, P(19.4, 10.2), 1.8, 1.8);
            DrawRoundedRect(dc, RectOf(17.5, 17.4, 9.4, 9.4), 1.2, BrushOf(Color.FromRgb(42, 72, 62)), PenOf(Green, 1.2));
            DrawDownArrow(dc, 22.2, 19.4, 25.2, Green);
        }

        private static void DrawHelpIcon(DrawingContext dc)
        {
            dc.DrawEllipse(BrushOf(Color.FromRgb(46, 61, 78)), PenOf(Blue, 1.5), P(16, 16), 10.0, 10.0);
            dc.DrawEllipse(null, PenOf(InkMuted, 0.9, 150), P(16, 16), 7.0, 7.0);

            var mark = new PathGeometry();
            var fig = new PathFigure { StartPoint = P(12.1, 12.3), IsFilled = false };
            fig.Segments.Add(new BezierSegment(P(12.6, 8.8), P(19.2, 8.4), P(19.8, 12.8), true));
            fig.Segments.Add(new BezierSegment(P(20.2, 15.6), P(16.4, 16.1), P(16.0, 18.7), true));
            mark.Figures.Add(fig);
            dc.DrawGeometry(null, RoundPen(Yellow, 2.2), mark);
            dc.DrawEllipse(BrushOf(Yellow), null, P(16, 22.5), 1.3, 1.3);
        }

        private static void DrawCalcIcon(DrawingContext dc)
        {
            DrawPage(dc, 5.4, 5.2, 15.2, 20.2, true);
            DrawTableGrid(dc, 8.0, 10.0, 10.0, 12.0, 3, 3, Blue);
            DrawRoundedRect(dc, RectOf(15.0, 8.8, 11.2, 17.0), 1.4, BrushOf(Color.FromRgb(33, 42, 53)), PenOf(InkMuted, 1.0));
            DrawRoundedRect(dc, RectOf(17, 11, 7.2, 3.2), 0.6, BrushOf(Color.FromRgb(188, 231, 204)), PenOf(Green, 0.8));
            DrawCalcKey(dc, 17.0, 16.4, Paper);
            DrawCalcKey(dc, 20.4, 16.4, Paper);
            DrawCalcKey(dc, 23.8, 16.4, Yellow);
            DrawCalcKey(dc, 17.0, 19.8, Paper);
            DrawCalcKey(dc, 20.4, 19.8, Paper);
            DrawCalcKey(dc, 23.8, 19.8, Blue);
            DrawCalcKey(dc, 17.0, 23.2, Paper);
            DrawCalcKey(dc, 20.4, 23.2, Paper);
            DrawCalcKey(dc, 23.8, 23.2, Green);
        }

        private static void DrawPrintIcon(DrawingContext dc)
        {
            DrawPage(dc, 9, 5, 14, 10.5, false);
            DrawPrinterBody(dc, 5, 14.2, 22, 9.0, Green);
            DrawRoundedRect(dc, RectOf(8.2, 21.2, 15.6, 6.0), 0.8, BrushOf(Paper), PenOf(Shadow, 1.0));
            dc.DrawLine(PenOf(InkMuted, 1.0), P(10.2, 23.8), P(20.4, 23.8));
            dc.DrawLine(PenOf(InkMuted, 1.0), P(10.2, 25.6), P(18.6, 25.6));
        }

        private static void DrawTableIcon(DrawingContext dc)
        {
            DrawPage(dc, 6.2, 5.2, 16.0, 21.2, true);
            DrawTableGrid(dc, 8.4, 11.0, 11.7, 12.0, 3, 3, Orange);
            DrawArrow(dc, P(18.2, 24.6), P(26.2, 16.6), Blue, 2.0);
            DrawRoundedRect(dc, RectOf(22.2, 23.0, 5.2, 3.4), 0.7, BrushOf(Color.FromRgb(44, 71, 92)), PenOf(Blue, 1.0));
        }

        private static void DrawEditIcon(DrawingContext dc)
        {
            DrawPage(dc, 6.4, 5.4, 15.6, 20.8, true);
            DrawListLines(dc, 9.2, 10.5, 8, 3, InkMuted);

            dc.PushTransform(new RotateTransform(42, 19.5, 18.0));
            DrawRoundedRect(dc, RectOf(17.0, 10.0, 5.2, 14.0), 0.8, BrushOf(Yellow), PenOf(Shadow, 1.0));
            dc.DrawLine(PenOf(Shadow, 0.8), P(18.7, 10.0), P(18.7, 24.0));
            dc.DrawLine(PenOf(Shadow, 0.8), P(20.5, 10.0), P(20.5, 24.0));
            DrawRoundedRect(dc, RectOf(17.0, 7.2, 5.2, 3.0), 0.8, BrushOf(Magenta), PenOf(Shadow, 1.0));
            DrawTriangle(dc, P(17.0, 24.0), P(22.2, 24.0), P(19.6, 27.3), BrushOf(Color.FromRgb(245, 217, 145)), PenOf(Shadow, 1.0));
            dc.Pop();
        }

        private static void DrawJsonIcon(DrawingContext dc)
        {
            DrawRoundedRect(dc, RectOf(7.0, 6.2, 18.0, 19.6), 1.6, BrushOf(Color.FromRgb(30, 39, 50)), PenOf(Blue, 1.2));
            var left = new PathGeometry();
            var lf = new PathFigure { StartPoint = P(14.2, 10.0), IsFilled = false };
            lf.Segments.Add(new BezierSegment(P(10.5, 10.0), P(11.2, 15.7), P(8.8, 16.0), true));
            lf.Segments.Add(new BezierSegment(P(11.2, 16.3), P(10.5, 22.0), P(14.2, 22.0), true));
            left.Figures.Add(lf);
            dc.DrawGeometry(null, RoundPen(Cyan, 1.8), left);

            var right = new PathGeometry();
            var rf = new PathFigure { StartPoint = P(17.8, 10.0), IsFilled = false };
            rf.Segments.Add(new BezierSegment(P(21.5, 10.0), P(20.8, 15.7), P(23.2, 16.0), true));
            rf.Segments.Add(new BezierSegment(P(20.8, 16.3), P(21.5, 22.0), P(17.8, 22.0), true));
            right.Figures.Add(rf);
            dc.DrawGeometry(null, RoundPen(Cyan, 1.8), right);
            dc.DrawEllipse(BrushOf(Yellow), null, P(16, 16), 1.2, 1.2);
        }

        private static void DrawBlockIcon(DrawingContext dc)
        {
            DrawMiniCube(dc, 6.0, 6.0, Magenta);
            DrawMiniCube(dc, 16.4, 6.0, Magenta);
            DrawMiniCube(dc, 6.0, 16.4, Magenta);
            DrawMiniCube(dc, 16.4, 16.4, Magenta);
        }

        private static void DrawDefaultIcon(DrawingContext dc)
        {
            dc.DrawEllipse(null, PenOf(InkMuted, 1.6), P(16, 16), 7.2, 7.2);
            dc.DrawLine(RoundPen(Ink, 2.0), P(16, 10.8), P(16, 17.2));
            dc.DrawEllipse(BrushOf(Ink), null, P(16, 21), 1.0, 1.0);
        }

        private static void DrawPrinterBody(DrawingContext dc, double x, double y, double width, double height, Color accent)
        {
            DrawRoundedRect(dc, RectOf(x, y, width, height), 1.5, BrushOf(Color.FromRgb(30, 39, 50)), PenOf(accent, 1.4));
            DrawRoundedRect(dc, RectOf(x + 3, y + 2.1, width - 6, 3.0), 0.6, BrushOf(Color.FromRgb(218, 226, 234)), PenOf(InkMuted, 0.7));
            dc.DrawEllipse(BrushOf(accent), null, P(x + width - 4.0, y + height - 2.5), 0.9, 0.9);
        }

        private static void DrawPage(DrawingContext dc, double x, double y, double width, double height, bool corner)
        {
            DrawRoundedRect(dc, RectOf(x, y, width, height), 0.9, BrushOf(Paper), PenOf(Shadow, 1.0));
            if (!corner) return;

            DrawTriangle(dc, P(x + width - 4.5, y), P(x + width, y + 4.5), P(x + width - 4.5, y + 4.5),
                BrushOf(Color.FromRgb(211, 222, 235)), PenOf(Shadow, 0.7));
        }

        private static void DrawListLines(DrawingContext dc, double x, double y, double width, int count, Color color)
        {
            var pen = RoundPen(color, 1.0);
            for (int i = 0; i < count; i++)
            {
                dc.DrawLine(pen, P(x, y + i * 3.8), P(x + width, y + i * 3.8));
            }
        }

        private static void DrawTableGrid(DrawingContext dc, double x, double y, double width, double height, int columns, int rows, Color color)
        {
            var pen = PenOf(color, 0.9, 220);
            DrawRoundedRect(dc, RectOf(x, y, width, height), 0.4, null, pen);

            for (int col = 1; col < columns; col++)
            {
                double px = x + width * col / columns;
                dc.DrawLine(pen, P(px, y), P(px, y + height));
            }

            for (int row = 1; row < rows; row++)
            {
                double py = y + height * row / rows;
                dc.DrawLine(pen, P(x, py), P(x + width, py));
            }
        }

        private static void DrawMiniCube(DrawingContext dc, double x, double y, Color accent)
        {
            DrawRoundedRect(dc, RectOf(x, y, 8.2, 8.2), 1.0, BrushOf(Color.FromRgb(59, 71, 86)), PenOf(accent, 1.2));
            dc.DrawLine(PenOf(Color.FromRgb(189, 203, 217), 0.7, 170), P(x + 2.0, y + 2.1), P(x + 6.2, y + 2.1));
            dc.DrawLine(PenOf(Color.FromRgb(189, 203, 217), 0.7, 170), P(x + 2.0, y + 6.1), P(x + 6.2, y + 6.1));
        }

        private static void DrawCalcKey(DrawingContext dc, double x, double y, Color color)
        {
            DrawRoundedRect(dc, RectOf(x, y, 2.3, 2.3), 0.5, BrushOf(color), null);
        }

        private static void DrawImageMountains(DrawingContext dc)
        {
            var shape = new PathGeometry();
            var fig = new PathFigure { StartPoint = P(7.3, 20.6), IsClosed = true };
            fig.Segments.Add(new LineSegment(P(12.0, 14.0), true));
            fig.Segments.Add(new LineSegment(P(15.5, 17.8), true));
            fig.Segments.Add(new LineSegment(P(18.4, 13.4), true));
            fig.Segments.Add(new LineSegment(P(23.0, 20.6), true));
            shape.Figures.Add(fig);
            dc.DrawGeometry(BrushOf(Color.FromRgb(96, 181, 213), 210), null, shape);
        }

        private static void DrawGear(DrawingContext dc, Point center, double radius, Color color)
        {
            var pen = RoundPen(color, 1.3);
            dc.DrawEllipse(BrushOf(Color.FromRgb(76, 65, 39)), PenOf(color, 1.3), center, radius, radius);
            dc.DrawEllipse(BrushOf(Paper), null, center, radius * 0.35, radius * 0.35);
            dc.DrawLine(pen, P(center.X, center.Y - radius - 1.2), P(center.X, center.Y + radius + 1.2));
            dc.DrawLine(pen, P(center.X - radius - 1.2, center.Y), P(center.X + radius + 1.2, center.Y));
        }

        private static void DrawDimensionLine(DrawingContext dc, double x1, double y, double x2, Color color)
        {
            var pen = RoundPen(color, 1.5);
            dc.DrawLine(pen, P(x1, y), P(x2, y));
            dc.DrawLine(pen, P(x1, y), P(x1 + 3, y - 2.3));
            dc.DrawLine(pen, P(x1, y), P(x1 + 3, y + 2.3));
            dc.DrawLine(pen, P(x2, y), P(x2 - 3, y - 2.3));
            dc.DrawLine(pen, P(x2, y), P(x2 - 3, y + 2.3));
        }

        private static void DrawDownArrow(DrawingContext dc, double x, double y1, double y2, Color color)
        {
            var pen = RoundPen(color, 1.8);
            dc.DrawLine(pen, P(x, y1), P(x, y2));
            dc.DrawLine(pen, P(x, y2), P(x - 2.6, y2 - 2.6));
            dc.DrawLine(pen, P(x, y2), P(x + 2.6, y2 - 2.6));
        }

        private static void DrawArrow(DrawingContext dc, Point start, Point end, Color color, double thickness)
        {
            var pen = RoundPen(color, thickness);
            dc.DrawLine(pen, start, end);

            double angle = Math.Atan2(end.Y - start.Y, end.X - start.X);
            const double head = 4.0;
            const double spread = 0.65;
            dc.DrawLine(pen, end, P(end.X - head * Math.Cos(angle - spread), end.Y - head * Math.Sin(angle - spread)));
            dc.DrawLine(pen, end, P(end.X - head * Math.Cos(angle + spread), end.Y - head * Math.Sin(angle + spread)));
        }

        private static void DrawTriangle(DrawingContext dc, Point a, Point b, Point c, Brush fill, Pen stroke)
        {
            var geometry = new StreamGeometry();
            using (StreamGeometryContext ctx = geometry.Open())
            {
                ctx.BeginFigure(a, fill != null, true);
                ctx.LineTo(b, true, false);
                ctx.LineTo(c, true, false);
            }
            geometry.Freeze();
            dc.DrawGeometry(fill, stroke, geometry);
        }

        private static void DrawPolyline(DrawingContext dc, Pen pen, params Point[] points)
        {
            for (int i = 1; i < points.Length; i++)
            {
                dc.DrawLine(pen, points[i - 1], points[i]);
            }
        }

        private static void DrawRoundedRect(DrawingContext dc, Rect rect, double radius, Brush fill, Pen stroke)
        {
            dc.DrawRoundedRectangle(fill, stroke, rect, radius, radius);
        }

        private static Rect RectOf(double x, double y, double width, double height)
        {
            return new Rect(x, y, width, height);
        }

        private static Point P(double x, double y)
        {
            return new Point(x, y);
        }

        private static SolidColorBrush BrushOf(Color color, byte alpha = 255)
        {
            var brush = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
            brush.Freeze();
            return brush;
        }

        private static Pen PenOf(Color color, double thickness, byte alpha = 255)
        {
            var brush = BrushOf(color, alpha);
            var pen = new Pen(brush, thickness);
            pen.Freeze();
            return pen;
        }

        private static Pen RoundPen(Color color, double thickness, byte alpha = 255)
        {
            var brush = BrushOf(color, alpha);
            var pen = new Pen(brush, thickness)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            };
            pen.Freeze();
            return pen;
        }
    }
}
