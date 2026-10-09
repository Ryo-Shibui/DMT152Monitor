using System.Drawing.Drawing2D;
using System.Globalization;

namespace DMT152Monitor;

internal sealed class DualAxisPlotControl : Control
{
    private const int MaxPoints = 12_000;
    private const int Divisions = 5;
    private readonly List<PlotPoint> _points = new();

    public static readonly Color DewPointColor = Color.FromArgb(0, 105, 180);
    public static readonly Color H2OPpbColor = Color.FromArgb(210, 80, 55);

    public DualAxisPlotControl()
    {
        DoubleBuffered = true;
        BackColor = Color.White;
        ForeColor = Color.FromArgb(45, 45, 45);
        Font = new Font("Segoe UI", 10F, FontStyle.Regular);
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer, true);
    }

    public void ClearData()
    {
        _points.Clear();
        Invalidate();
    }

    public void AddPoint(double timeSeconds, double dewPointC, double h2oPpb)
    {
        if (!double.IsFinite(timeSeconds) || !double.IsFinite(dewPointC) || !double.IsFinite(h2oPpb))
            return;

        _points.Add(new PlotPoint(timeSeconds, dewPointC, h2oPpb));
        if (_points.Count > MaxPoints)
            Decimate();

        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);

        using var gridPen = new Pen(Color.FromArgb(228, 228, 228), 1F);
        using var xAxisPen = new Pen(Color.FromArgb(90, 90, 90), 1F);
        using var dewAxisPen = new Pen(DewPointColor, 1.4F);
        using var ppbAxisPen = new Pen(H2OPpbColor, 1.4F);
        using var dewTracePen = new Pen(DewPointColor, 1.8F);
        using var ppbTracePen = new Pen(H2OPpbColor, 1.8F);
        using var normalBrush = new SolidBrush(ForeColor);
        using var dewBrush = new SolidBrush(DewPointColor);
        using var ppbBrush = new SolidBrush(H2OPpbColor);
        using var tickFont = new Font("Segoe UI", 9.5F, FontStyle.Regular);
        using var axisFont = new Font("Segoe UI", 11.5F, FontStyle.Regular);

        double xMax = _points.Count == 0 ? 1.0 : Math.Max(1.0, _points[^1].TimeSeconds);
        (double dewMin, double dewMax) = GetRange(p => p.DewPointC, defaultMin: -80.0, defaultMax: -30.0, clampLowerToZero: false);
        (double ppbMin, double ppbMax) = GetRange(p => p.H2OPpb, defaultMin: 0.0, defaultMax: 1000.0, clampLowerToZero: true);

        string[] xLabels = new string[Divisions + 1];
        string[] dewLabels = new string[Divisions + 1];
        string[] ppbLabels = new string[Divisions + 1];

        float maxXLabelWidth = 0F;
        float maxXLabelHeight = 0F;
        float maxDewLabelWidth = 0F;
        float maxDewLabelHeight = 0F;
        float maxPpbLabelWidth = 0F;
        float maxPpbLabelHeight = 0F;

        for (int i = 0; i <= Divisions; i++)
        {
            xLabels[i] = FormatAxis(xMax * i / Divisions);
            dewLabels[i] = FormatAxis(dewMax - (dewMax - dewMin) * i / Divisions);
            ppbLabels[i] = FormatAxis(ppbMax - (ppbMax - ppbMin) * i / Divisions);

            SizeF xSize = g.MeasureString(xLabels[i], tickFont);
            SizeF dewSize = g.MeasureString(dewLabels[i], tickFont);
            SizeF ppbSize = g.MeasureString(ppbLabels[i], tickFont);

            maxXLabelWidth = Math.Max(maxXLabelWidth, xSize.Width);
            maxXLabelHeight = Math.Max(maxXLabelHeight, xSize.Height);
            maxDewLabelWidth = Math.Max(maxDewLabelWidth, dewSize.Width);
            maxDewLabelHeight = Math.Max(maxDewLabelHeight, dewSize.Height);
            maxPpbLabelWidth = Math.Max(maxPpbLabelWidth, ppbSize.Width);
            maxPpbLabelHeight = Math.Max(maxPpbLabelHeight, ppbSize.Height);
        }

        const string xTitle = "Measurement Time (s)";
        const string leftTitle = "Dew Point (°C)";
        const string rightTitle = "H2O (ppb)";

        SizeF xTitleSize = g.MeasureString(xTitle, axisFont);
        SizeF leftTitleSize = g.MeasureString(leftTitle, axisFont);
        SizeF rightTitleSize = g.MeasureString(rightTitle, axisFont);

        const float outerPadding = 10F;
        const float labelGap = 7F;
        const float titleGap = 8F;

        float leftMargin = outerPadding + leftTitleSize.Height + titleGap + maxDewLabelWidth + labelGap;
        float rightMargin = outerPadding + rightTitleSize.Height + titleGap + maxPpbLabelWidth + labelGap;
        float topMargin = Math.Max(18F, Math.Max(maxDewLabelHeight, maxPpbLabelHeight) / 2F + outerPadding);
        float bottomMargin = outerPadding + xTitleSize.Height + titleGap + maxXLabelHeight + labelGap;

        float plotWidth = ClientSize.Width - leftMargin - rightMargin;
        float plotHeight = ClientSize.Height - topMargin - bottomMargin;
        if (plotWidth < 60F || plotHeight < 60F)
            return;

        var plot = new RectangleF(leftMargin, topMargin, plotWidth, plotHeight);

        for (int i = 0; i <= Divisions; i++)
        {
            float x = plot.Left + plot.Width * i / Divisions;
            float y = plot.Top + plot.Height * i / Divisions;

            g.DrawLine(gridPen, x, plot.Top, x, plot.Bottom);
            g.DrawLine(gridPen, plot.Left, y, plot.Right, y);

            SizeF xSize = g.MeasureString(xLabels[i], tickFont);
            g.DrawString(xLabels[i], tickFont, normalBrush, x - xSize.Width / 2F, plot.Bottom + labelGap / 2F);

            SizeF dewSize = g.MeasureString(dewLabels[i], tickFont);
            g.DrawString(dewLabels[i], tickFont, dewBrush, plot.Left - labelGap - dewSize.Width, y - dewSize.Height / 2F);

            SizeF ppbSize = g.MeasureString(ppbLabels[i], tickFont);
            g.DrawString(ppbLabels[i], tickFont, ppbBrush, plot.Right + labelGap, y - ppbSize.Height / 2F);
        }

        // X axis is neutral. Each Y axis uses exactly the same color as its trace.
        g.DrawLine(xAxisPen, plot.Left, plot.Bottom, plot.Right, plot.Bottom);
        g.DrawLine(xAxisPen, plot.Left, plot.Top, plot.Right, plot.Top);
        g.DrawLine(dewAxisPen, plot.Left, plot.Top, plot.Left, plot.Bottom);
        g.DrawLine(ppbAxisPen, plot.Right, plot.Top, plot.Right, plot.Bottom);

        if (_points.Count >= 2)
        {
            var dewScreen = new PointF[_points.Count];
            var ppbScreen = new PointF[_points.Count];
            double dewSpan = dewMax - dewMin;
            double ppbSpan = ppbMax - ppbMin;

            for (int i = 0; i < _points.Count; i++)
            {
                PlotPoint p = _points[i];
                float x = plot.Left + (float)(p.TimeSeconds / xMax * plot.Width);
                float dewY = plot.Bottom - (float)((p.DewPointC - dewMin) / dewSpan * plot.Height);
                float ppbY = plot.Bottom - (float)((p.H2OPpb - ppbMin) / ppbSpan * plot.Height);
                dewScreen[i] = new PointF(x, dewY);
                ppbScreen[i] = new PointF(x, ppbY);
            }

            g.DrawLines(dewTracePen, dewScreen);
            g.DrawLines(ppbTracePen, ppbScreen);
        }
        else if (_points.Count == 1)
        {
            PlotPoint p = _points[0];
            float x = plot.Left + (float)(p.TimeSeconds / xMax * plot.Width);
            float dewY = plot.Bottom - (float)((p.DewPointC - dewMin) / (dewMax - dewMin) * plot.Height);
            float ppbY = plot.Bottom - (float)((p.H2OPpb - ppbMin) / (ppbMax - ppbMin) * plot.Height);
            g.FillEllipse(dewBrush, x - 2.5F, dewY - 2.5F, 5F, 5F);
            g.FillEllipse(ppbBrush, x - 2.5F, ppbY - 2.5F, 5F, 5F);
        }
        else
        {
            const string message = "Press Start Measurement to begin plotting.";
            SizeF size = g.MeasureString(message, tickFont);
            g.DrawString(message, tickFont, normalBrush,
                plot.Left + (plot.Width - size.Width) / 2F,
                plot.Top + (plot.Height - size.Height) / 2F);
        }

        float xTitleY = plot.Bottom + labelGap / 2F + maxXLabelHeight + titleGap / 2F;
        g.DrawString(xTitle, axisFont, normalBrush,
            plot.Left + (plot.Width - xTitleSize.Width) / 2F,
            xTitleY);

        GraphicsState leftState = g.Save();
        float leftTitleCenterX = outerPadding + leftTitleSize.Height / 2F;
        g.TranslateTransform(leftTitleCenterX, plot.Top + plot.Height / 2F);
        g.RotateTransform(-90F);
        g.DrawString(leftTitle, axisFont, dewBrush, -leftTitleSize.Width / 2F, -leftTitleSize.Height / 2F);
        g.Restore(leftState);

        GraphicsState rightState = g.Save();
        float rightTitleCenterX = ClientSize.Width - outerPadding - rightTitleSize.Height / 2F;
        g.TranslateTransform(rightTitleCenterX, plot.Top + plot.Height / 2F);
        g.RotateTransform(90F);
        g.DrawString(rightTitle, axisFont, ppbBrush, -rightTitleSize.Width / 2F, -rightTitleSize.Height / 2F);
        g.Restore(rightState);
    }

    private (double Min, double Max) GetRange(
        Func<PlotPoint, double> selector,
        double defaultMin,
        double defaultMax,
        bool clampLowerToZero)
    {
        if (_points.Count == 0)
            return (defaultMin, defaultMax);

        double min = double.PositiveInfinity;
        double max = double.NegativeInfinity;
        foreach (PlotPoint point in _points)
        {
            double value = selector(point);
            min = Math.Min(min, value);
            max = Math.Max(max, value);
        }

        if (Math.Abs(max - min) < 1e-12)
        {
            double pad = Math.Max(1.0, Math.Abs(max) * 0.05);
            double low = min - pad;
            if (clampLowerToZero && min >= 0.0)
                low = Math.Max(0.0, low);
            return (low, max + pad);
        }

        double range = max - min;
        double padding = range * 0.08;
        double lower = min - padding;
        if (clampLowerToZero && min >= 0.0)
            lower = Math.Max(0.0, lower);

        return (lower, max + padding);
    }

    private void Decimate()
    {
        var reduced = new List<PlotPoint>((_points.Count + 1) / 2);
        for (int i = 0; i < _points.Count; i += 2)
        {
            if (i + 1 >= _points.Count)
            {
                reduced.Add(_points[i]);
                continue;
            }

            PlotPoint a = _points[i];
            PlotPoint b = _points[i + 1];
            reduced.Add(new PlotPoint(
                (a.TimeSeconds + b.TimeSeconds) * 0.5,
                (a.DewPointC + b.DewPointC) * 0.5,
                (a.H2OPpb + b.H2OPpb) * 0.5));
        }

        _points.Clear();
        _points.AddRange(reduced);
    }

    private static string FormatAxis(double value)
    {
        double abs = Math.Abs(value);
        if ((abs >= 1_000_000.0) || (abs > 0.0 && abs < 0.01))
            return value.ToString("0.##E+0", CultureInfo.InvariantCulture);
        if (abs >= 1000.0)
            return value.ToString("0", CultureInfo.InvariantCulture);
        if (abs >= 10.0)
            return value.ToString("0.0", CultureInfo.InvariantCulture);
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private readonly record struct PlotPoint(double TimeSeconds, double DewPointC, double H2OPpb);
}
