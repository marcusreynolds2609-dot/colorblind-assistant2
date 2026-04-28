using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Color = System.Windows.Media.Color;
using Drawing = System.Drawing;
using DrawingImaging = System.Drawing.Imaging;
using Forms = System.Windows.Forms;

namespace Colorblind_Asisstant;

public partial class MainWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x20;
    private const int WmHotKey = 0x0312;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const int FreezeHotKeyId = 1;
    private const int ExitHotKeyId = 2;
    private const int ToggleDetailsHotKeyId = 3;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint VkSpace = 0x20;
    private const uint VkQ = 0x51;
    private const uint VkH = 0x48;
    private const int OverlayOffsetX = 24;
    private const int OverlayOffsetY = 24;
    private const int SamplingRadius = 1;
    private const int MagnifierRadius = 2;
    private const double DualColorDistanceThreshold = 30;
    private const double DisplayBlendFactor = 0.35;
    private const double LabelChangeDistanceThreshold = 24;
    private const int LabelConfirmationFrames = 3;
    private void ApplyCardTheme

    private readonly DispatcherTimer _scanTimer;
    private readonly SolidColorBrush _cardBackgroundBrush = new(Color.FromRgb(27, 27, 27));
    private readonly SolidColorBrush _cardBorderBrush = new(Color.FromArgb(153, 255, 255, 255));
    private readonly SolidColorBrush _primaryTextBrush = new(Colors.White);
    private readonly SolidColorBrush _secondaryTextBrush = new(Color.FromArgb(220, 255, 255, 255));
    private readonly SolidColorBrush _primaryPreviewBrush = new(Colors.Transparent);
    private readonly SolidColorBrush _secondaryPreviewBrush = new(Colors.Transparent);
    private readonly List<SolidColorBrush> _magnifierBrushes = [];
    private readonly List<Border> _magnifierCells = [];
    private readonly Drawing.Bitmap _captureBitmap = new(MagnifierRadius * 2 + 1, MagnifierRadius * 2 + 1, DrawingImaging.PixelFormat.Format32bppArgb);
    private readonly Drawing.Graphics _captureGraphics;
    private readonly byte[] _captureBuffer = new byte[(MagnifierRadius * 2 + 1) * (MagnifierRadius * 2 + 1) * 4];

    private IntPtr _windowHandle;
    private HwndSource? _windowSource;
    private Forms.NotifyIcon? _trayIcon;
    private Forms.ToolStripMenuItem? _toggleOverlayMenuItem;
    private Forms.ToolStripMenuItem? _toggleFreezeMenuItem;
    private Forms.ToolStripMenuItem? _toggleDetailsMenuItem;
    private bool _isFrozen;
    private bool _isOverlayVisible = true;
    private bool _allowClose;
    private bool _isRenderTrackingActive;
    private bool _isCompactMode;
    private bool _hasDisplayState;
    private int _lastCursorX = int.MinValue;
    private int _lastCursorY = int.MinValue;
    private int _lastScannedCursorX = int.MinValue;
    private int _lastScannedCursorY = int.MinValue;
    private int _pendingLabelFrameCount;
    private string? _stableDisplayLabel;
    private string? _pendingDisplayLabel;
    private Color _stableLabelAnchorColor;
    private SampleAnalysis? _displayedAnalysis;
    private string _lastScanSummary = "No scan data available yet.";

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr hwnd,
        IntPtr hwndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint flags);

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern uint GetPixel(IntPtr hdc, int x, int y);

    public MainWindow()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        Closing += OnClosing;

        InitializeTheme();
        InitializeMagnifier();
        InitializeTrayIcon();
        ApplyDisplayMode();

        _captureGraphics = Drawing.Graphics.FromImage(_captureBitmap);
        _captureGraphics.CompositingMode = Drawing.Drawing2D.CompositingMode.SourceCopy;
        _captureGraphics.CompositingQuality = Drawing.Drawing2D.CompositingQuality.HighSpeed;
        _captureGraphics.InterpolationMode = Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
        _captureGraphics.PixelOffsetMode = Drawing.Drawing2D.PixelOffsetMode.HighSpeed;
        _captureGraphics.SmoothingMode = Drawing.Drawing2D.SmoothingMode.None;

        _scanTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _scanTimer.Tick += ScanTimerOnTick;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _windowHandle = new WindowInteropHelper(this).Handle;
        _windowSource = HwndSource.FromHwnd(_windowHandle);
        _windowSource?.AddHook(WndProc);

        var currentStyle = GetWindowLong(_windowHandle, GwlExStyle);
        SetWindowLong(_windowHandle, GwlExStyle, currentStyle | WsExTransparent);
        RegisterHotKey(_windowHandle, FreezeHotKeyId, ModControl, VkSpace);
        RegisterHotKey(_windowHandle, ExitHotKeyId, ModControl | ModShift, VkQ);
        RegisterHotKey(_windowHandle, ToggleDetailsHotKeyId, ModControl | ModShift, VkH);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        StartScanningIfNeeded();
        UpdateColorReadout();
    }

    private void InitializeTheme()
    {
        ReadoutCard.Background = _cardBackgroundBrush;
        ReadoutCard.BorderBrush = _cardBorderBrush;

        PrimaryColorPreview.Background = _primaryPreviewBrush;
        PrimaryColorPreview.BorderBrush = _cardBorderBrush;
        SecondaryColorPreview.Background = _secondaryPreviewBrush;
        SecondaryColorPreview.BorderBrush = _cardBorderBrush;
        MagnifierFrame.BorderBrush = _cardBorderBrush;

        ApplyTextTheme(Colors.White, Color.FromArgb(220, 255, 255, 255));
    }

    private void InitializeMagnifier()
    {
        for (var index = 0; index < 25; index++)
        {
            var brush = new SolidColorBrush(Colors.Transparent);
            var cell = new Border
            {
                Background = brush,
                Margin = new Thickness(1),
                BorderThickness = index == 12 ? new Thickness(2) : new Thickness(1),
                CornerRadius = new CornerRadius(index == 12 ? 4 : 2)
            };

            _magnifierBrushes.Add(brush);
            _magnifierCells.Add(cell);
            MagnifierGrid.Children.Add(cell);
        }
    }

    private void InitializeTrayIcon()
    {
        _toggleOverlayMenuItem = new Forms.ToolStripMenuItem("Hide Overlay", null, (_, _) => ToggleOverlayVisibility());
        _toggleFreezeMenuItem = new Forms.ToolStripMenuItem("Freeze Scan", null, (_, _) => ToggleFreeze());
        _toggleDetailsMenuItem = new Forms.ToolStripMenuItem("Color Only Mode", null, (_, _) => ToggleDetailsMode());
        var exitMenuItem = new Forms.ToolStripMenuItem("Exit", null, (_, _) => ExitApplication());

        _trayIcon = new Forms.NotifyIcon
        {
            Icon = Drawing.SystemIcons.Information,
            Text = "Colorblind Assistant Overlay",
            Visible = true,
            ContextMenuStrip = new Forms.ContextMenuStrip()
        };

        _trayIcon.ContextMenuStrip.Items.Add(_toggleOverlayMenuItem);
        _trayIcon.ContextMenuStrip.Items.Add(_toggleFreezeMenuItem);
        _trayIcon.ContextMenuStrip.Items.Add(_toggleDetailsMenuItem);
        _trayIcon.ContextMenuStrip.Items.Add(new Forms.ToolStripSeparator());
        _trayIcon.ContextMenuStrip.Items.Add(exitMenuItem);
        _trayIcon.DoubleClick += (_, _) => ToggleOverlayVisibility();
    }

    private void ScanTimerOnTick(object? sender, EventArgs e)
    {
        if (_isFrozen || !_isOverlayVisible)
        {
            return;
        }

        UpdateColorReadout();
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (_isFrozen || !_isOverlayVisible)
        {
            return;
        }

        if (!GetCursorPos(out var cursorPosition))
        {
            return;
        }

        if (cursorPosition.X == _lastCursorX && cursorPosition.Y == _lastCursorY)
        {
            return;
        }

        _lastCursorX = cursorPosition.X;
        _lastCursorY = cursorPosition.Y;
        PositionReadout(cursorPosition.X, cursorPosition.Y);
    }

    private void UpdateColorReadout()
    {
        if (!GetCursorPos(out var cursorPosition))
        {
            return;
        }

        if (cursorPosition.X == _lastScannedCursorX && cursorPosition.Y == _lastScannedCursorY)
        {
            return;
        }

        _lastScannedCursorX = cursorPosition.X;
        _lastScannedCursorY = cursorPosition.Y;

        var magnifierSamples = TryCaptureSamples(cursorPosition.X, cursorPosition.Y);
        if (magnifierSamples is null || magnifierSamples.Count == 0)
        {
            return;
        }

        IReadOnlyList<SamplePoint> coreSamples;

        if (_isCompactMode)
        {
            coreSamples = ExtractCoreSamples(magnifierSamples);
        }
        else
        {
            UpdateMagnifier(magnifierSamples);
            coreSamples = ExtractCoreSamples(magnifierSamples);
        }

        if (coreSamples.Count == 0)
        {
            return;
        }

        var analysis = AnalyzeSamples(coreSamples);
        ApplyAnalysis(cursorPosition, analysis);
    }

    private void ApplyAnalysis(POINT cursorPosition, SampleAnalysis analysis)
    {
        var displayAnalysis = StabilizeAnalysis(analysis);

        _primaryPreviewBrush.Color = displayAnalysis.PrimaryDisplayColor;
        _secondaryPreviewBrush.Color = displayAnalysis.SecondaryDisplayColor;
        SecondaryColorPreview.Visibility = displayAnalysis.HasDualLabel ? Visibility.Visible : Visibility.Collapsed;

        StatusText.Text = _isFrozen ? "Frozen" : "Live Scan";
        CompactModeText.Text = displayAnalysis.HasDualLabel ? "Two colors" : "Color only";
        ColorLabelText.Text = displayAnalysis.DisplayLabel;
        ColorHexText.Text = $"Average: #{displayAnalysis.AverageColor.R:X2}{displayAnalysis.AverageColor.G:X2}{displayAnalysis.AverageColor.B:X2}";
        CursorPositionText.Text = $"X: {cursorPosition.X}  Y: {cursorPosition.Y}";

        var blackContrast = ContrastRatio(displayAnalysis.AverageColor, Colors.Black);
        var whiteContrast = ContrastRatio(displayAnalysis.AverageColor, Colors.White);
        var preferredTextColor = blackContrast >= whiteContrast ? Colors.Black : Colors.White;
        var secondaryTextColor = preferredTextColor == Colors.Black
            ? Color.FromArgb(220, 0, 0, 0)
            : Color.FromArgb(220, 255, 255, 255);

        ContrastText.Text = $"Best text contrast: {DescribeContrastChoice(preferredTextColor, blackContrast, whiteContrast)}";
        ContrastDetailText.Text =
            $"Black text: {DescribeContrastLevel(blackContrast)} ({blackContrast:F2}:1)  White text: {DescribeContrastLevel(whiteContrast)} ({whiteContrast:F2}:1)";

        ApplyCardTheme(displayAnalysis.AverageColor, preferredTextColor, secondaryTextColor);
        PositionReadout(cursorPosition.X, cursorPosition.Y);

        _lastScanSummary =
            $"{displayAnalysis.DisplayLabel}{Environment.NewLine}" +
            $"Average: #{displayAnalysis.AverageColor.R:X2}{displayAnalysis.AverageColor.G:X2}{displayAnalysis.AverageColor.B:X2}{Environment.NewLine}" +
            $"Cursor: {cursorPosition.X}, {cursorPosition.Y}{Environment.NewLine}" +
            $"Black text contrast: {DescribeContrastLevel(blackContrast)} ({blackContrast:F2}:1){Environment.NewLine}" +
            $"White text contrast: {DescribeContrastLevel(whiteContrast)} ({whiteContrast:F2}:1)";
    }

    private SampleAnalysis StabilizeAnalysis(SampleAnalysis analysis)
    {
        if (!_hasDisplayState || _displayedAnalysis is null || _stableDisplayLabel is null)
        {
            _hasDisplayState = true;
            _stableDisplayLabel = analysis.DisplayLabel;
            _stableLabelAnchorColor = analysis.AverageColor;
            _pendingDisplayLabel = null;
            _pendingLabelFrameCount = 0;
            _displayedAnalysis = analysis;
            return analysis;
        }

        var nextLabel = ResolveStableLabel(analysis);
        var previous = _displayedAnalysis.Value;

        var stabilized = new SampleAnalysis(
            BlendColor(previous.AverageColor, analysis.AverageColor, DisplayBlendFactor),
            BlendColor(previous.PrimaryDisplayColor, analysis.PrimaryDisplayColor, DisplayBlendFactor),
            BlendColor(previous.SecondaryDisplayColor, analysis.SecondaryDisplayColor, DisplayBlendFactor),
            nextLabel,
            analysis.HasDualLabel,
            analysis.Variance);

        _displayedAnalysis = stabilized;
        return stabilized;
    }

    private SampleAnalysis StabilizeAnalysis(SampleAnalysis analysis)
    {
        if (!_hasDisplayState || _displayedAnalysis is null || _stableDisplayLabel is null)
        {
            _hasDisplayState = true;
            _stableDisplayLabel = analysis.DisplayLabel;
            _stableLabelAnchorColor = analysis.AverageColor;
            _pendingDisplayLabel = null;
            _pendingLabelFrameCount = 0;
            _displayedAnalysis = analysis;
            return analysis;
        }

        var nextLabel = ResolveStableLabel(analysis);
        var previous = _displayedAnalysis.Value;

        var stabilized = new SampleAnalysis(
            BlendColor(previous.AverageColor, analysis.AverageColor, DisplayBlendFactor),
            BlendColor(previous.PrimaryDisplayColor, analysis.PrimaryDisplayColor, DisplayBlendFactor),
            BlendColor(previous.SecondaryDisplayColor, analysis.SecondaryDisplayColor, DisplayBlendFactor),
            nextLabel,
            analysis.HasDualLabel,
            analysis.Variance);

        _displayedAnalysis = stabilized;
        return stabilized;
    }

    private string ResolveStableLabel(SampleAnalysis analysis)
    {
        var candidateLabel = analysis.DisplayLabel;
        var currentLabel = _stableDisplayLabel ?? candidateLabel;

        if (string.Equals(candidateLabel, currentLabel, StringComparison.Ordinal))
        {
            _pendingDisplayLabel = null;
            _pendingLabelFrameCount = 0;
            _stableLabelAnchorColor = BlendColor(_stableLabelAnchorColor, analysis.AverageColor, 0.25);
            return currentLabel;
        }

        if (ColorDistance(analysis.AverageColor, _stableLabelAnchorColor) < LabelChangeDistanceThreshold)
        {
            _pendingDisplayLabel = null;
            _pendingLabelFrameCount = 0;
            return currentLabel;
        }

        if (!string.Equals(candidateLabel, _pendingDisplayLabel, StringComparison.Ordinal))
        {
            _pendingDisplayLabel = candidateLabel;
            _pendingLabelFrameCount = 1;
            return currentLabel;
        }

        _pendingLabelFrameCount++;
        if (_pendingLabelFrameCount < LabelConfirmationFrames)
        {
            return currentLabel;
        }

        _stableLabelAnchorColor = analysis.AverageColor;
        _stableDisplayLabel = candidateLabel;
        return candidateLabel;
    }
    {
        var candidateLabel = analysis.DisplayLabel;
        var currentLabel = _stableDisplayLabel ?? candidateLabel;

        if (string.Equals(candidateLabel, currentLabel, StringComparison.Ordinal))
        {
            _pendingDisplayLabel = null;
            _pendingLabelFrameCount = 0;
            _stableLabelAnchorColor = BlendColor(_stableLabelAnchorColor, analysis.AverageColor, 0.25);
            return currentLabel;
        }

        if (ColorDistance(analysis.AverageColor, _stableLabelAnchorColor) < LabelChangeDistanceThreshold)
        {
            _pendingDisplayLabel = null;
            _pendingLabelFrameCount = 0;
            return currentLabel;
        }

        if (!string.Equals(candidateLabel, _pendingDisplayLabel, StringComparison.Ordinal))
        {
            _pendingDisplayLabel = candidateLabel;
            _pendingLabelFrameCount = 1;
            return currentLabel;
        }

        _pendingLabelFrameCount++;
        if (_pendingLabelFrameCount < LabelConfirmationFrames)
        {
            return currentLabel;
        }

        _stableDisplayLabel = candidateLabel;
        _stableLabelAnchorColor = analysis.AverageColor;
        _pendingDisplayLabel = null;
        _pendingLabelFrameCount = 0;
        return candidateLabel;
    }

    private void ApplyCardTheme(Color averageColor, Color primaryTextColor, Color secondaryTextColor)
    {
        _cardBackgroundBrush.Color = Color.FromArgb(232, averageColor.R, averageColor.G, averageColor.B);
        _cardBorderBrush.Color = primaryTextColor == Colors.Black
            ? Color.FromArgb(160, 0, 0, 0)
            : Color.FromArgb(180, 255, 255, 255);

        ApplyTextTheme(primaryTextColor, secondaryTextColor);

        foreach (var cell in _magnifierCells)
        {
            cell.BorderBrush = _cardBorderBrush;
        }
    }

    private void ApplyTextTheme(Color primary, Color secondary)
    {
        _primaryTextBrush.Color = primary;
        _secondaryTextBrush.Color = secondary;

        StatusText.Foreground = _secondaryTextBrush;
        CompactModeText.Foreground = _secondaryTextBrush;
        ColorLabelText.Foreground = _primaryTextBrush;
        ColorHexText.Foreground = _primaryTextBrush;
        CursorPositionText.Foreground = _secondaryTextBrush;
        ContrastText.Foreground = _primaryTextBrush;
        ContrastDetailText.Foreground = _secondaryTextBrush;
        MagnifierTitleText.Foreground = _secondaryTextBrush;
        HintText.Foreground = _secondaryTextBrush;
    }

    private void UpdateMagnifier(IReadOnlyList<SamplePoint> samples)
    {
        for (var index = 0; index < samples.Count && index < _magnifierBrushes.Count; index++)
        {
            _magnifierBrushes[index].Color = samples[index].Color;
        }
    }

    private void PositionReadout(int screenX, int screenY)
    {
        const double fallbackWindowWidth = 400;
        const double fallbackWindowHeight = 320;

        var windowWidth = ActualWidth > 0 ? ActualWidth : fallbackWindowWidth;
        var windowHeight = ActualHeight > 0 ? ActualHeight : fallbackWindowHeight;

        var screenLeft = SystemParameters.VirtualScreenLeft;
        var screenTop = SystemParameters.VirtualScreenTop;
        var screenRight = screenLeft + SystemParameters.VirtualScreenWidth;
        var screenBottom = screenTop + SystemParameters.VirtualScreenHeight;

        double targetLeft = screenX + OverlayOffsetX;
        double targetTop = screenY + OverlayOffsetY;

        if (targetLeft + windowWidth > screenRight)
        {
            targetLeft = Math.Max(screenLeft + 12, screenX - windowWidth - OverlayOffsetX);
        }

        if (targetTop + windowHeight > screenBottom)
        {
            targetTop = Math.Max(screenTop + 12, screenY - windowHeight - OverlayOffsetY);
        }

        if (_windowHandle == IntPtr.Zero)
        {
            Left = targetLeft;
            Top = targetTop;
            return;
        }

        SetWindowPos(
            _windowHandle,
            IntPtr.Zero,
            (int)Math.Round(targetLeft),
            (int)Math.Round(targetTop),
            0,
            0,
            SwpNoSize | SwpNoZOrder | SwpNoActivate);
    }

    private List<SamplePoint>? TryCaptureSamples(int centerX, int centerY)
    {
        try
        {
            var sourceX = centerX - MagnifierRadius;
            var sourceY = centerY - MagnifierRadius;

            _captureGraphics.CopyFromScreen(
                sourceX,
                sourceY,
                0,
                0,
                _captureBitmap.Size,
                Drawing.CopyPixelOperation.SourceCopy);

            var rect = new Drawing.Rectangle(0, 0, _captureBitmap.Width, _captureBitmap.Height);
            var bitmapData = _captureBitmap.LockBits(rect, DrawingImaging.ImageLockMode.ReadOnly, DrawingImaging.PixelFormat.Format32bppArgb);

            try
            {
                System.Runtime.InteropServices.Marshal.Copy(bitmapData.Scan0, _captureBuffer, 0, _captureBuffer.Length);

                var samples = new List<SamplePoint>(_captureBitmap.Width * _captureBitmap.Height);
                var stride = bitmapData.Stride;

                for (var y = 0; y < _captureBitmap.Height; y++)
                {
                    for (var x = 0; x < _captureBitmap.Width; x++)
                    {
                        var bufferIndex = y * stride + x * 4;
                        var blue = _captureBuffer[bufferIndex];
                        var green = _captureBuffer[bufferIndex + 1];
                        var red = _captureBuffer[bufferIndex + 2];

                        samples.Add(new SamplePoint(
                            x - MagnifierRadius,
                            y - MagnifierRadius,
                            Color.FromRgb(red, green, blue)));
                    }
                }

                return samples;
            }
            finally
            {
                _captureBitmap.UnlockBits(bitmapData);
            }
        }
        catch
        {
            return TryCaptureSamplesFallback(centerX, centerY);
        }
    }

    private static List<SamplePoint> ExtractCoreSamples(IReadOnlyList<SamplePoint> magnifierSamples)
    {
        var coreSamples = new List<SamplePoint>(9);

        foreach (var sample in magnifierSamples)
        {
            if (Math.Abs(sample.OffsetX) <= SamplingRadius && Math.Abs(sample.OffsetY) <= SamplingRadius)
            {
                coreSamples.Add(sample);
            }
        }

        return coreSamples;
    }

    private static List<SamplePoint>? TryCaptureSamplesFallback(int centerX, int centerY)
    {
        var screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var samples = new List<SamplePoint>((MagnifierRadius * 2 + 1) * (MagnifierRadius * 2 + 1));

            for (var offsetY = -MagnifierRadius; offsetY <= MagnifierRadius; offsetY++)
            {
                for (var offsetX = -MagnifierRadius; offsetX <= MagnifierRadius; offsetX++)
                {
                    var pixel = GetPixel(screenDc, centerX + offsetX, centerY + offsetY);
                    if (pixel == 0xFFFFFFFF)
                    {
                        continue;
                    }

                    var red = (byte)(pixel & 0x000000FF);
                    var green = (byte)((pixel & 0x0000FF00) >> 8);
                    var blue = (byte)((pixel & 0x00FF0000) >> 16);

                    samples.Add(new SamplePoint(offsetX, offsetY, Color.FromRgb(red, green, blue)));
                }
            }

            return samples;
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private static SampleAnalysis AnalyzeSamples(IReadOnlyList<SamplePoint> samples)
    {
        var totalRed = 0d;
        var totalGreen = 0d;
        var totalBlue = 0d;

        foreach (var sample in samples)
        {
            totalRed += sample.Color.R;
            totalGreen += sample.Color.G;
            totalBlue += sample.Color.B;
        }

        var averageColor = Color.FromRgb(
            (byte)Math.Round(totalRed / samples.Count),
            (byte)Math.Round(totalGreen / samples.Count),
            (byte)Math.Round(totalBlue / samples.Count));

        var variance = CalculateColorSpread(samples, averageColor);
        var dominantColors = FindDominantColors(samples);

        var primaryColor = dominantColors.Count > 0 ? dominantColors[0].AverageColor : averageColor;
        var secondaryColor = dominantColors.Count > 1 ? dominantColors[1].AverageColor : averageColor;
        var primaryLabel = dominantColors.Count > 0 ? DescribeColor(primaryColor) : DescribeColor(averageColor);
        var secondaryLabel = dominantColors.Count > 1 ? DescribeColor(secondaryColor) : string.Empty;
        var hasDualLabel = variance >= DualColorDistanceThreshold &&
                           dominantColors.Count > 1 &&
                           !string.Equals(primaryLabel, secondaryLabel, StringComparison.Ordinal);

        return new SampleAnalysis(
            averageColor,
            primaryColor,
            secondaryColor,
            hasDualLabel ? $"{primaryLabel} & {secondaryLabel}" : DescribeColor(averageColor),
            hasDualLabel,
            variance);
    }

    private static double CalculateColorSpread(IReadOnlyList<SamplePoint> samples, Color averageColor)
    {
        var distanceSum = 0d;

        foreach (var sample in samples)
        {
            var deltaRed = sample.Color.R - averageColor.R;
            var deltaGreen = sample.Color.G - averageColor.G;
            var deltaBlue = sample.Color.B - averageColor.B;
            distanceSum += deltaRed * deltaRed + deltaGreen * deltaGreen + deltaBlue * deltaBlue;
        }

        return Math.Sqrt(distanceSum / samples.Count);
    }

    private static List<DominantColor> FindDominantColors(IReadOnlyList<SamplePoint> samples)
    {
        var groups = new Dictionary<string, ColorAccumulator>(StringComparer.Ordinal);

        foreach (var sample in samples)
        {
            var key = GetClusterKey(sample.Color);
            if (!groups.TryGetValue(key, out var accumulator))
            {
                accumulator = new ColorAccumulator();
                groups[key] = accumulator;
            }

            accumulator.Add(sample.Color);
        }

        DominantColor? first = null;
        DominantColor? second = null;

        foreach (var group in groups)
        {
            var dominantColor = new DominantColor(group.Key, group.Value.GetAverageColor(), group.Value.Count);

            if (first is null || dominantColor.Count > first.Value.Count)
            {
                second = first;
                first = dominantColor;
            }
            else if (second is null || dominantColor.Count > second.Value.Count)
            {
                second = dominantColor;
            }
        }

        var results = new List<DominantColor>(2);
        if (first is not null)
        {
            results.Add(first.Value);
        }

        if (second is not null)
        {
            results.Add(second.Value);
        }

        return results;
    }

    private static string GetClusterKey(Color color)
    {
        var (_, saturation, lightness) = ToHsl(color);

        if (lightness <= 0.08)
        {
            return "Black";
        }

        if (lightness >= 0.94)
        {
            return "White";
        }

        if (saturation <= 0.10)
        {
            return "Gray";
        }

        var hue = ToHsl(color).Hue;
        if (hue is >= 18 and <= 55 && lightness < 0.42)
        {
            return saturation < 0.45 ? "Tan" : "Brown";
        }

        return ClosestNamedColor(color).Name;
    }

    private static string DescribeColor(Color color)
    {
        var (hue, saturation, lightness) = ToHsl(color);

        if (lightness <= 0.08)
        {
            return "Black";
        }

        if (lightness >= 0.94)
        {
            return "White";
        }

        if (saturation <= 0.10)
        {
            if (lightness < 0.25)
            {
                return "Charcoal Gray";
            }

            if (lightness < 0.60)
            {
                return "Gray";
            }

            return "Silver";
        }

        if (hue is >= 18 and <= 55 && lightness < 0.42)
        {
            return saturation < 0.45 ? "Tan" : "Brown";
        }

        if (lightness > 0.82 && saturation < 0.35)
        {
            return "Pastel " + ClosestNamedColor(color).Name;
        }

        var prefix = lightness < 0.28
            ? "Dark "
            : lightness > 0.78
                ? "Light "
                : saturation > 0.75 && lightness > 0.45 && lightness < 0.75
                    ? "Bright "
                    : string.Empty;

        return $"{prefix}{ClosestNamedColor(color).Name}".Trim();
    }

    private static NamedColorDefinition ClosestNamedColor(Color color)
    {
        NamedColorDefinition bestMatch = NamedColors[0];
        var bestDistance = double.MaxValue;

        foreach (var namedColor in NamedColors)
        {
            var distance = ColorDistance(color, namedColor.Color);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestMatch = namedColor;
            }
        }

        return bestMatch;
    }

    private static double ColorDistance(Color left, Color right)
    {
        var red = left.R - right.R;
        var green = left.G - right.G;
        var blue = left.B - right.B;
        return Math.Sqrt(red * red + green * green + blue * blue);
    }

    private static (double Hue, double Saturation, double Lightness) ToHsl(Color color)
    {
        var red = color.R / 255d;
        var green = color.G / 255d;
        var blue = color.B / 255d;

        var max = Math.Max(red, Math.Max(green, blue));
        var min = Math.Min(red, Math.Min(green, blue));
        var delta = max - min;
        var lightness = (max + min) / 2d;

        if (delta == 0)
        {
            return (0, 0, lightness);
        }

        var saturation = lightness > 0.5
            ? delta / (2 - max - min)
            : delta / (max + min);

        var hue = max switch
        {
            var value when value == red => ((green - blue) / delta + (green < blue ? 6 : 0)) * 60,
            var value when value == green => (((blue - red) / delta) + 2) * 60,
            _ => (((red - green) / delta) + 4) * 60
        };

        return (hue, saturation, lightness);
    }

    private static double RelativeLuminance(Color color)
    {
        var red = Linearize(color.R / 255d);
        var green = Linearize(color.G / 255d);
        var blue = Linearize(color.B / 255d);

        return 0.2126 * red + 0.7152 * green + 0.0722 * blue;
    }

    private static double ContrastRatio(Color background, Color foreground)
    {
        var backgroundLuminance = RelativeLuminance(background);
        var foregroundLuminance = RelativeLuminance(foreground);
        var lighter = Math.Max(backgroundLuminance, foregroundLuminance);
        var darker = Math.Min(backgroundLuminance, foregroundLuminance);

        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double Linearize(double channel)
    {
        return channel <= 0.03928
            ? channel / 12.92
            : Math.Pow((channel + 0.055) / 1.055, 2.4);
    }

    private static string DescribeContrastChoice(Color preferredColor, double blackContrast, double whiteContrast)
    {
        return preferredColor == Colors.Black
            ? $"Black, {DescribeContrastLevel(blackContrast)}"
            : $"White, {DescribeContrastLevel(whiteContrast)}";
    }

    private static string DescribeContrastLevel(double ratio)
    {
        if (ratio >= 7)
        {
            return "AAA";
        }

        if (ratio >= 4.5)
        {
            return "AA";
        }

        return "Fail";
    }

    private static Color BlendColor(Color from, Color to, double blendFactor)
    {
        var clampedFactor = Math.Clamp(blendFactor, 0, 1);
        var inverse = 1 - clampedFactor;

        return Color.FromArgb(
            (byte)Math.Round(from.A * inverse + to.A * clampedFactor),
            (byte)Math.Round(from.R * inverse + to.R * clampedFactor),
            (byte)Math.Round(from.G * inverse + to.G * clampedFactor),
            (byte)Math.Round(from.B * inverse + to.B * clampedFactor));
    }

    private void ToggleFreeze()
    {
        _isFrozen = !_isFrozen;
        UpdateFreezeState();

        if (_isFrozen)
        {
            try
            {
                System.Windows.Clipboard.SetText(_lastScanSummary);
                StatusText.Text = "Frozen and copied";
            }
            catch
            {
                StatusText.Text = "Frozen";
            }
        }
        else if (_isOverlayVisible)
        {
            UpdateColorReadout();
        }
    }

    private void ToggleDetailsMode()
    {
        _isCompactMode = !_isCompactMode;
        ApplyDisplayMode();
        _lastCursorX = int.MinValue;
        _lastCursorY = int.MinValue;
        _lastScannedCursorX = int.MinValue;
        _lastScannedCursorY = int.MinValue;
        ResetDisplayStabilization();

        if (_isOverlayVisible)
        {
            UpdateLayout();
            UpdateColorReadout();
        }
    }

    private void ApplyDisplayMode()
    {
        DetailPanel.Visibility = _isCompactMode ? Visibility.Collapsed : Visibility.Visible;
        StatusText.Visibility = _isCompactMode ? Visibility.Collapsed : Visibility.Visible;
        CompactModeText.Visibility = _isCompactMode ? Visibility.Visible : Visibility.Collapsed;

        if (_toggleDetailsMenuItem is not null)
        {
            _toggleDetailsMenuItem.Text = _isCompactMode ? "Show Details" : "Color Only Mode";
        }
    }

    private void UpdateFreezeState()
    {
        if (_toggleFreezeMenuItem is not null)
        {
            _toggleFreezeMenuItem.Text = _isFrozen ? "Resume Scan" : "Freeze Scan";
        }

        if (!_isCompactMode)
        {
            StatusText.Text = _isFrozen ? "Frozen" : "Live Scan";
        }

        StartScanningIfNeeded();
    }

    private void ToggleOverlayVisibility()
    {
        _isOverlayVisible = !_isOverlayVisible;

        if (_isOverlayVisible)
        {
            Show();
            Topmost = true;
            StartScanningIfNeeded();
            if (!_isFrozen)
            {
                UpdateColorReadout();
            }
        }
        else
        {
            Hide();
            _scanTimer.Stop();
            StopRenderTracking();
        }

        if (_toggleOverlayMenuItem is not null)
        {
            _toggleOverlayMenuItem.Text = _isOverlayVisible ? "Hide Overlay" : "Show Overlay";
        }
    }

    private void StartScanningIfNeeded()
    {
        if (_isOverlayVisible && !_isFrozen)
        {
            _lastScannedCursorX = int.MinValue;
            _lastScannedCursorY = int.MinValue;
            ResetDisplayStabilization();
            _scanTimer.Start();
            StartRenderTracking();
        }
        else
        {
            _scanTimer.Stop();
            StopRenderTracking();
        }
    }

    private void StartRenderTracking()
    {
        if (_isRenderTrackingActive)
        {
            return;
        }

        CompositionTarget.Rendering += OnRendering;
        _isRenderTrackingActive = true;
    }

    private void StopRenderTracking()
    {
        if (!_isRenderTrackingActive)
        {
            return;
        }

        CompositionTarget.Rendering -= OnRendering;
        _isRenderTrackingActive = false;
    }

    private void ExitApplication()
    {
        _allowClose = true;
        Close();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;
        if (_isOverlayVisible)
        {
            ToggleOverlayVisibility();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _scanTimer.Stop();
        StopRenderTracking();

        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
        {
            UnregisterHotKey(handle, FreezeHotKeyId);
            UnregisterHotKey(handle, ExitHotKeyId);
            UnregisterHotKey(handle, ToggleDetailsHotKeyId);
        }

        if (_windowSource is not null)
        {
            _windowSource.RemoveHook(WndProc);
        }

        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }

        _captureGraphics.Dispose();
        _captureBitmap.Dispose();

        base.OnClosed(e);
    }

    private void ResetDisplayStabilization()
    {
        _hasDisplayState = false;
        _pendingLabelFrameCount = 0;
        _pendingDisplayLabel = null;
        _stableDisplayLabel = null;
        _displayedAnalysis = null;
        _stableLabelAnchorColor = Colors.Transparent;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotKey)
        {
            switch (wParam.ToInt32())
            {
                case FreezeHotKeyId:
                    ToggleFreeze();
                    handled = true;
                    break;
                case ExitHotKeyId:
                    ExitApplication();
                    handled = true;
                    break;
                case ToggleDetailsHotKeyId:
                    ToggleDetailsMode();
                    handled = true;
                    break;
            }
        }

        return IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    private sealed class ColorAccumulator
    {
        private int _redTotal;
        private int _greenTotal;
        private int _blueTotal;

        public int Count { get; private set; }

        public void Add(Color color)
        {
            _redTotal += color.R;
            _greenTotal += color.G;
            _blueTotal += color.B;
            Count++;
        }

        public Color GetAverageColor()
        {
            if (Count == 0)
            {
                return Colors.Transparent;
            }

            return Color.FromRgb(
                (byte)(_redTotal / Count),
                (byte)(_greenTotal / Count),
                (byte)(_blueTotal / Count));
        }
    }

    private readonly record struct SamplePoint(int OffsetX, int OffsetY, Color Color);

    private readonly record struct DominantColor(string Key, Color AverageColor, int Count);

    private readonly record struct NamedColorDefinition(string Name, Color Color);

    private readonly record struct SampleAnalysis(
        Color AverageColor,
        Color PrimaryDisplayColor,
        Color SecondaryDisplayColor,
        string DisplayLabel,
        bool HasDualLabel,
        double Variance);

    private static class ProtanopiaSimulator
    {
        /// <summary>
        /// LMS cone space transformation matrix from RGB to LMS.
        /// Based on Stockman & Sharpe (2000) fundamentals of the visual pigments.
        /// </summary>
        private static readonly double[][] LmsMatrix = {
            { 0.38, 0.597, 0.023 },
            { 0.141, 0.527, 0.332 },
            { 0.028, 0.465, 0.732 }
        };

        /// <summary>
        /// Non-linearity function for LMS cone space.
        /// </summary>
        private static double ConeNonLinearity(double C)
        {
            // Sigmoidal non-linearity based on Stockman & Sharpe 2000
            return Math.Max(0, Math.Log(C + 7) / Math.Log(8)) * C;
        }

        /// <summary>
        /// LMS to RGB inverse transformation matrix.
        /// </summary>
        private static readonly double[][] RgbFromLmsMatrix = {
            { 1.045, 0.262, -0.001 },
            {-0.968, 1.713, -0.127 },
            { 0.028, -0.033, 1.028 }
        };

        /// <summary>
        /// Converts RGB to LMS cone space.
        /// </summary>
        private static double[] RgbToLms(double r, double g, double b)
        {
            var l = LmsMatrix[0][0] * r + LmsMatrix[0][1] * g + LmsMatrix[0][2] * b;
            var m = LmsMatrix[1][0] * r + LmsMatrix[1][1] * g + LmsMatrix[1][2] * b;
            var s = LmsMatrix[2][0] * r + LmsMatrix[2][1] * g + LmsMatrix[2][2] * b;

            return new double[] { ConeNonLinearity(l), ConeNonLinearity(m), ConeNonLinearity(s) };
        }

        /// <summary>
        /// Converts back from LMS to RGB.
        /// </summary>
        private static double[] LmsToRgb(double l, double m, double s)
        {
            var r = RgbFromLmsMatrix[0][0] * l + RgbFromLmsMatrix[0][1] * m + RgbFromLmsMatrix[0][2] * s;
            var g = RgbFromLmsMatrix[1][0] * l + RgbFromLmsMatrix[1][1] * m + RgbFromLmsMatrix[1][2] * s;
            var b = RgbFromLmsMatrix[2][0] * l + RgbFromLmsMatrix[2][1] * m + RgbFromLmsMatrix[2][2] * s;

            return new double[] { Math.Clamp(r, 0, 255), Math.Clamp(g, 0, 255), Math.Clamp(b, 0, 255) };
        }

        /// <summary>
        /// Simulates Protanopia (red-blindness) color vision defect.
        /// In protanopia, L and M cone signals are combined equally,
        /// effectively reducing red-green discrimination ability.
        /// </summary>
        public static Color SimulateProtanopia(Color inputColor)
        {
            // Normalize RGB to 0-1 range
            var r = inputColor.R / 255d;
            var g = inputColor.G / 255d;
            var b = inputColor.B / 255d;

            // Convert to LMS cone space
            var lms = RgbToLms(r, g, b);

            // Simulate protanopia: combine L and M cones equally (protanopic substitution)
            var lPrime = 0.56 * lms[0] + 0.44 * lms[1];
            var mPrime = 0.56 * lms[0] + 0.44 * lms[1];
            // S cone remains unchanged
            var sPrime = lms[2];

            // Convert back to RGB
            var rgb = LmsToRgb(lPrime, mPrime, sPrime);

            return Color.FromArgb(inputColor.A, (byte)rgb[0], (byte)rgb[1], (byte)rgb[2]);
        }
    }
}
