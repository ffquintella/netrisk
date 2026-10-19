using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using DAL.Enums;
using Serilog;
using SkiaSharp;

namespace GUIClient.Tools.RiskIndex;

/// <summary>
/// The Avalonia half of the chart palette (S40 §10.4, T257): resolves each <see cref="ChartPaletteMap"/> token key against
/// <c>Application.Current</c> into an <see cref="SKColor"/> for LiveCharts, and drops its cache when the theme variant
/// changes so the next chart is painted from the new tokens. The only place in the index's code that builds an SKColor.
/// </summary>
public sealed class ChartPalette : IDisposable
{
    private static readonly Lazy<ChartPalette> Instance = new(() => new ChartPalette(Application.Current));

    /// <summary>The application's palette; charts subscribe to <see cref="Changed"/> to repaint.</summary>
    public static ChartPalette Current => Instance.Value;

    private readonly Application? _application;
    private readonly Dictionary<string, SKColor> _cache = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    /// <summary>Raised after the theme variant changed and the cached colours were dropped.</summary>
    public event EventHandler? Changed;

    private ChartPalette(Application? application)
    {
        _application = application;
        if (_application is not null) _application.ActualThemeVariantChanged += OnThemeChanged;
    }

    /// <summary>A band's fill (charts, pills): <c>NrCriticality{step}</c>, at <paramref name="opacity"/>.</summary>
    public SKColor Band(int position, int count, double opacity = 1) =>
        WithOpacity(Resolve(ChartPaletteMap.BandTokenKey(position, count)), opacity);

    /// <summary>A band's area behind the trend, at <see cref="ChartPaletteMap.BandAreaOpacity"/>.</summary>
    public SKColor BandArea(int position, int count) => Band(position, count, ChartPaletteMap.BandAreaOpacity);

    public SKColor Series(RiskIndexCategory category) => Resolve(ChartPaletteMap.SeriesTokenKey(category));

    public SKColor Ewma => Resolve(ChartPaletteMap.EwmaToken);

    public SKColor Marker => Resolve(ChartPaletteMap.MarkerToken);

    public SKColor FloorShade => WithOpacity(Resolve(ChartPaletteMap.FloorShadeToken), ChartPaletteMap.FloorShadeOpacity);

    public SKColor Grid => Resolve(ChartPaletteMap.GridToken);

    public SKColor Axis => Resolve(ChartPaletteMap.AxisToken);

    /// <summary>
    /// The colour a token resolves to under the current theme variant. A key the theme does not define resolves to
    /// transparent and is logged: a chart drawn without a colour is visible as a defect, a guessed colour is not.
    /// </summary>
    public SKColor Resolve(string tokenKey)
    {
        lock (_gate)
        {
            if (_cache.TryGetValue(tokenKey, out var cached)) return cached;
        }

        var color = Lookup(tokenKey);
        lock (_gate) _cache[tokenKey] = color;
        return color;
    }

    private SKColor Lookup(string tokenKey)
    {
        if (_application is not null &&
            _application.TryGetResource(tokenKey, _application.ActualThemeVariant, out var value))
        {
            switch (value)
            {
                case ISolidColorBrush brush:
                    return WithOpacity(ToSkia(brush.Color), brush.Opacity);
                case Color color:
                    return ToSkia(color);
            }
        }

        Log.Warning("Chart palette token {Token} is not defined by the current theme", tokenKey);
        return SKColor.Empty;
    }

    private static SKColor ToSkia(Color color) => new(color.R, color.G, color.B, color.A);

    private static SKColor WithOpacity(SKColor color, double opacity) =>
        color.WithAlpha((byte)Math.Round(color.Alpha * Math.Clamp(opacity, 0, 1)));

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        lock (_gate) _cache.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_application is not null) _application.ActualThemeVariantChanged -= OnThemeChanged;
    }
}
