"""Generate BijouHub's fx themes (Themes/*.xaml) from one spec. Run: python tools/themes/gen_fx_themes.py

Values for the ported looks come from the sibling apps' CSS (Bijou Footage themes-fx.css,
BijouMusic themes/*.css, BijouDocs styles.css); the rest are new art direction.
"""
import os

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "Themes")
ASSET = "pack://application:,,,/Assets/Themes/"


def a(hex6, alpha):
    """#RRGGBB + 0..1 alpha -> #AARRGGBB."""
    return "#%02X%s" % (round(alpha * 255), hex6.lstrip("#").upper())


def solid(k, c):
    return f'<SolidColorBrush x:Key="{k}" Color="{c}"/>'


def lin(k, stops, s="0,0", e="0,1"):
    gs = "".join(f'<GradientStop Color="{c}" Offset="{o}"/>' for c, o in stops)
    return f'<LinearGradientBrush x:Key="{k}" StartPoint="{s}" EndPoint="{e}">{gs}</LinearGradientBrush>'


def rad(k, stops, center="0.5,0.5", rx=0.5, ry=0.5, origin=None):
    gs = "".join(f'<GradientStop Color="{c}" Offset="{o}"/>' for c, o in stops)
    o = origin or center
    return (f'<RadialGradientBrush x:Key="{k}" Center="{center}" GradientOrigin="{o}" '
            f'RadiusX="{rx}" RadiusY="{ry}">{gs}</RadialGradientBrush>')


def bevel(k, tl, br):
    """Hard diagonal split: top/left edges get tl, bottom/right get br (a Win95 bevel)."""
    return lin(k, [(tl, 0), (tl, 0.5), (br, 0.5), (br, 1)], "0,0", "1,1")


def null(k):
    return f'<x:Null x:Key="{k}"/>'


def shadow(k, color, blur, depth=0, opacity=0.5, direction=270):
    return (f'<DropShadowEffect x:Key="{k}" x:Shared="False" Color="{color}" BlurRadius="{blur}" '
            f'ShadowDepth="{depth}" Opacity="{opacity}" Direction="{direction}"/>')


def thick(k, v): return f'<Thickness x:Key="{k}">{v}</Thickness>'
def corner(k, v): return f'<CornerRadius x:Key="{k}">{v}</CornerRadius>'
def vis(k, on): return f'<Visibility x:Key="{k}">{"Visible" if on else "Collapsed"}</Visibility>'
def font(k, f): return f'<FontFamily x:Key="{k}">{f}</FontFamily>'
def weight(k, w): return f'<FontWeight x:Key="{k}">{w}</FontWeight>'
def color(k, c): return f'<Color x:Key="{k}">{c}</Color>'
def double(k, v): return f'<sys:Double x:Key="{k}">{v}</sys:Double>'
def translate(k, y): return f'<TranslateTransform x:Key="{k}" X="0" Y="{y}"/>'


def image(k, name, align_y="Center", stretch="UniformToFill"):
    return (f'<ImageBrush x:Key="{k}" ImageSource="{ASSET}{name}" Stretch="{stretch}" '
            f'AlignmentX="Center" AlignmentY="{align_y}" RenderOptions.BitmapScalingMode="HighQuality"/>')


def tiled_image(k, name, size, bg=None):
    return (f'<ImageBrush x:Key="{k}" ImageSource="{ASSET}{name}" TileMode="Tile" Stretch="Fill" '
            f'Viewport="0,0,{size},{size}" ViewportUnits="Absolute" RenderOptions.BitmapScalingMode="HighQuality"/>')


def _tile(k, w, h, rects, bg="#00000000"):
    """A tiled DrawingBrush: rects = [(color, x, y, w, h)] on a transparent w x h cell."""
    parts = [f'<GeometryDrawing Brush="{bg}"><GeometryDrawing.Geometry><RectangleGeometry Rect="0,0,{w},{h}"/></GeometryDrawing.Geometry></GeometryDrawing>']
    for c, x, y, rw, rh in rects:
        parts.append(f'<GeometryDrawing Brush="{c}"><GeometryDrawing.Geometry><RectangleGeometry Rect="{x},{y},{rw},{rh}"/></GeometryDrawing.Geometry></GeometryDrawing>')
    return (f'<DrawingBrush x:Key="{k}" TileMode="Tile" Viewport="0,0,{w},{h}" ViewportUnits="Absolute" Stretch="None">'
            f'<DrawingBrush.Drawing><DrawingGroup>{"".join(parts)}</DrawingGroup></DrawingBrush.Drawing></DrawingBrush>')


def hairlines(k, light, dark, period=3):
    return _tile(k, 4, period, [(light, 0, 0, 4, 1), (dark, 0, 1, 4, period - 1)])


def graph(k, c, cell=40):
    return _tile(k, cell, cell, [(c, 0, 0, cell, 1), (c, 0, 0, 1, cell)])


def dotmatrix(k, c):
    return _tile(k, 3, 3, [(c, 0, 0, 3, 1), (c, 0, 1, 1, 2)])


def scanlines(k, c):
    return _tile(k, 4, 3, [(c, 0, 2, 4, 1)])


def checker(k, c1, c2):
    return _tile(k, 2, 2, [(c2, 0, 0, 1, 1), (c2, 1, 1, 1, 1)], bg=c1)


def blocks(k, fill, gap):
    return _tile(k, 10, 14, [(fill, 0, 1, 8, 12)], bg=gap)


def dots(k, c, cell=5, r=1.1):
    half = cell / 2
    return (f'<DrawingBrush x:Key="{k}" TileMode="Tile" Viewport="0,0,{cell},{cell}" ViewportUnits="Absolute" Stretch="None">'
            f'<DrawingBrush.Drawing><DrawingGroup>'
            f'<GeometryDrawing Brush="#00000000"><GeometryDrawing.Geometry><RectangleGeometry Rect="0,0,{cell},{cell}"/></GeometryDrawing.Geometry></GeometryDrawing>'
            f'<GeometryDrawing Brush="{c}"><GeometryDrawing.Geometry><EllipseGeometry Center="{half},{half}" RadiusX="{r}" RadiusY="{r}"/></GeometryDrawing.Geometry></GeometryDrawing>'
            f'</DrawingGroup></DrawingBrush.Drawing></DrawingBrush>')


# The diagonal glass reflection BijouMusic lays over its LCD screens (172deg, 34%).
def glass(k, a1=0.10, a2=0.04):
    return lin(k, [(a("#FFFFFF", a1), 0), (a("#FFFFFF", a2), 0.34), ("#00FFFFFF", 0.343), ("#00FFFFFF", 1)], "0,0", "0.14,1")


def tokens(bg, panel, card, card_hover, text, muted, faint, border, soft, accent, hazard, pink, danger, success,
           body, heading=None, radius=8):
    heading = heading or body
    names = [("Bg", bg), ("Panel", panel), ("Card", card), ("CardHover", card_hover), ("Text", text),
             ("MutedText", muted), ("FaintText", faint), ("Border", border), ("SoftBorder", soft),
             ("Accent", accent), ("Hazard", hazard), ("Pink", pink), ("Danger", danger), ("Success", success)]
    out = [color(n + "Color", v) for n, v in names]
    out += [f'<SolidColorBrush x:Key="{n}Brush" Color="{{DynamicResource {n}Color}}"/>' for n, _ in names]
    out += [font("HeadingFont", heading), font("BodyFont", body), corner("CornerRadiusBase", radius)]
    return out


def bevel_set():
    """Windows 95 bevels for MS-DOS: each ring is a light top-left edge and a dark bottom-right edge."""
    def ring(stem, ol, od, il, id_):
        return [solid(f"{stem}OuterLightBrush", ol), solid(f"{stem}OuterDarkBrush", od),
                solid(f"{stem}InnerLightBrush", il), solid(f"{stem}InnerDarkBrush", id_)]
    return [vis("FxBevelVisibility", True),
            *ring("FxBevel", "#FFFFFF", "#000000", "#DFDFDF", "#808080"),          # raised
            *ring("FxBevelPressed", "#000000", "#FFFFFF", "#808080", "#DFDFDF"),   # pushed in
            *ring("FxBevelSunken", "#808080", "#FFFFFF", "#000000", "#DFDFDF")]    # fields


THEMES = {}

# ------------------------------------------------------------------ Frutiger Aero
THEMES["Aero"] = dict(blurb="Frutiger Aero — frosted glass over the sky, glossy candy buttons (BijouDocs / Bijou Footage).", res=[
    *tokens("#E3F2FC", "#EAF6FF", a("#FFFFFF", 0.80), a("#FFFFFF", 0.96), "#0B2545", "#35516B", "#5C7791",
            a("#5C9CCF", 0.45), "#D8EDFA", "#0A78C2", "#C9850E", "#D64C92", "#C8371F", "#24924F", "Segoe UI", radius=12),
    color("TitleBarColor", "#DCEFFC"), color("TitleBarTextColor", "#0B2545"),
    image("WallpaperBrush", "aero.jpg"),
    solid("SidebarBrush", a("#EAF6FF", 0.80)), solid("SidebarEdgeBrush", a("#FFFFFF", 0.90)),
    solid("ContentBrush", a("#F2F9FF", 0.78)), solid("ContentEdgeBrush", a("#FFFFFF", 0.90)),
    corner("PanelRadius", "14"), corner("PanelTopRadius", "13,13,0,0"),
    shadow("PanelShadow", "#0A2846", 28, 8, 0.28),
    lin("SidebarHeaderBrush", [(a("#FFFFFF", 0.80), 0), (a("#D6EEFF", 0.60), 0.5), (a("#BFDCF6", 0.55), 0.51), (a("#D6EAF9", 0.62), 1)]),
    solid("SidebarHeaderEdgeBrush", a("#FFFFFF", 0.90)), solid("SidebarHeaderInkBrush", "#0B2545"),
    lin("FxBtnBrush", [(a("#FFFFFF", 0.95), 0), (a("#FFFFFF", 0.62), 0.45), (a("#C8E8FF", 0.50), 0.5), (a("#AAD7FA", 0.42), 1)]),
    solid("FxBtnEdgeBrush", a("#FFFFFF", 0.90)), corner("FxBtnRadius", "12"), corner("FxTileRadius", "16"),
    solid("FxBtnSheenBrush", a("#FFFFFF", 0.95)), solid("FxBtnInkBrush", "#0B2545"),
    shadow("FxBtnShadow", "#0A2846", 3, 1, 0.22), shadow("FxBtnHoverEffect", "#0F9FDB", 12, 0, 0.6),
    solid("FxHoverWashBrush", a("#FFFFFF", 0.18)), solid("FxPressWashBrush", a("#1E78C8", 0.14)),
    lin("FxPrimaryBrush", [(a("#AAE1FF", 0.98), 0), (a("#5ABEFA", 0.85), 0.45), (a("#2896E6", 0.80), 0.5), (a("#1478C8", 0.75), 1)]),
    solid("FxPrimaryEdgeBrush", a("#FFFFFF", 0.80)), solid("FxPrimaryInkBrush", "#04213A"),
    shadow("FxPrimaryShadow", "#0F9FDB", 10, 0, 0.45), shadow("FxPrimaryHoverEffect", "#0F9FDB", 16, 0, 0.75),
    solid("FxRowBrush", a("#FFFFFF", 0.42)), solid("FxRowEdgeBrush", a("#FFFFFF", 0.70)), corner("FxRowRadius", "10"),
    solid("FxRowHoverBrush", a("#FFFFFF", 0.75)),
    lin("FxSelectedBrush", [(a("#B4E4FF", 0.95), 0), (a("#5ABEFA", 0.60), 1)]), solid("FxSelectedEdgeBrush", a("#FFFFFF", 0.85)),
    solid("FxSelectedInkBrush", "#04213A"), shadow("FxSelectedGlow", "#0F9FDB", 12, 0, 0.5),
    solid("FxFieldBrush", a("#FFFFFF", 0.92)), solid("FxFieldEdgeBrush", "#9CC4E4"), corner("FxFieldRadius", "10"),
    solid("FxFieldInkBrush", "#0B2545"), solid("FxFieldFocusBrush", "#0F9FDB"), shadow("FxFieldFocusGlow", "#0F9FDB", 10, 0, 0.45),
    solid("FxPopupBrush", a("#FFFFFF", 0.97)), solid("FxPopupEdgeBrush", "#9CC4E4"), corner("FxPopupRadius", "10"),
    shadow("FxPopupShadow", "#0A2846", 18, 6, 0.3),
    solid("FxCheckBrush", a("#FFFFFF", 0.95)), solid("FxCheckEdgeBrush", "#86A9D0"),
    lin("FxCheckOnBrush", [("#9FDCFF", 0), ("#4AB4F0", 0.45), ("#1470C0", 0.5), ("#2F9BE6", 1)]),
    solid("FxCheckOnEdgeBrush", "#0E5FA0"), solid("FxCheckMarkBrush", "#FFFFFF"),
    lin("FxThumbBrush", [("#FFFFFF", 0), ("#D7E8F9", 0.5), ("#B9D5F2", 0.51), ("#CFE3F8", 1)], "0,0", "1,0"),
    solid("FxThumbEdgeBrush", "#86A9D0"), thick("FxThumbBorderThickness", "1"),
    # Vista's glossy green progress bar.
    lin("ProgressFillBrush", [("#C6F79A", 0), ("#7BD94A", 0.45), ("#46B020", 0.5), ("#64C93B", 1)]),
    # Timer: BijouMusic's deep-blue LCD, glass reflection, Vista-lime digits.
    lin("TimerScreenBrush", [("#0F3A7A", 0), ("#0A2B5E", 0.6), ("#07204A", 1)]),
    rad("TimerScreenOverlayBrush", [(a("#6EB9FF", 0.30), 0), (a("#6EB9FF", 0.0), 1)], "0.5,0", 0.75, 0.9),
    glass("TimerScreenGlassBrush"), solid("TimerScreenEdgeBrush", a("#A0D2FF", 0.35)), thick("TimerScreenBorderThickness", "1"),
    corner("TimerScreenRadius", "12"), thick("TimerScreenPadding", "22,6,22,8"),
    solid("TimerTextBrush", "#8CF04A"), solid("TimerScreenInkBrush", "#A9D4FF"),
    lin("PopoutScreenBrush", [("#0F3A7A", 0), ("#07204A", 1)]),
    shadow("TimerGlow", "#8CF04A", 14, 0, 0.55), font("TimerFont", "Segoe UI"),
])

# ------------------------------------------------------------------ Frutiger Aero Dark
THEMES["AeroDark"] = dict(blurb="Frutiger Aero Dark — Vista/WMP11 black glass over a night sky.", res=[
    *tokens("#0A1120", "#101A2C", a("#16223A", 0.78), a("#22324F", 0.92), "#EEF4FB", "#A9BDD6", "#8193AD",
            "#2F4568", "#22324F", "#4CC2FF", "#E0B84F", "#FF7AB8", "#FF6B6B", "#6BCF7F", "Corbel, Segoe UI", radius=12),
    color("TitleBarColor", "#1E2A3E"), color("TitleBarTextColor", "#F2F7FD"),
    image("WallpaperBrush", "aerodark.jpg"),
    solid("SidebarBrush", a("#0C1424", 0.80)), solid("SidebarEdgeBrush", a("#78B4FF", 0.22)),
    solid("ContentBrush", a("#0E1830", 0.76)), solid("ContentEdgeBrush", a("#78B4FF", 0.22)),
    corner("PanelRadius", "14"), corner("PanelTopRadius", "13,13,0,0"),
    shadow("PanelShadow", "#000000", 30, 10, 0.5),
    lin("SidebarHeaderBrush", [(a("#3A4A64", 0.85), 0), (a("#1E2A3E", 0.80), 0.5), (a("#0E1624", 0.82), 0.51), (a("#162032", 0.85), 1)]),
    solid("SidebarHeaderEdgeBrush", a("#000000", 0.55)), solid("SidebarHeaderInkBrush", "#F2F7FD"),
    lin("FxBtnBrush", [("#45566F", 0), ("#28354B", 0.49), ("#141C2B", 0.5), ("#1D283B", 1)]),
    solid("FxBtnEdgeBrush", "#4A6184"), corner("FxBtnRadius", "15"), corner("FxTileRadius", "16"),
    solid("FxBtnSheenBrush", a("#FFFFFF", 0.20)), solid("FxBtnInkBrush", "#EEF4FB"),
    shadow("FxBtnShadow", "#000000", 3, 1, 0.55), shadow("FxBtnHoverEffect", "#4CC2FF", 14, 0, 0.6),
    solid("FxHoverWashBrush", a("#5C8CC8", 0.22)), solid("FxPressWashBrush", a("#000000", 0.25)),
    lin("FxPrimaryBrush", [("#9FDCFF", 0), ("#4AB4F0", 0.45), ("#1470C0", 0.5), ("#2F9BE6", 1)]),
    solid("FxPrimaryEdgeBrush", "#031A3D"), solid("FxPrimaryInkBrush", "#FFFFFF"),
    shadow("FxPrimaryShadow", "#4CC2FF", 12, 0, 0.55), shadow("FxPrimaryHoverEffect", "#4CC2FF", 18, 0, 0.8),
    solid("FxRowBrush", a("#16223A", 0.55)), solid("FxRowEdgeBrush", a("#4A6184", 0.55)), corner("FxRowRadius", "10"),
    solid("FxRowHoverBrush", a("#22324F", 0.85)),
    lin("FxSelectedBrush", [(a("#4CC2FF", 0.34), 0), (a("#145AAA", 0.34), 1)]), solid("FxSelectedEdgeBrush", "#4CC2FF"),
    solid("FxSelectedInkBrush", "#FFFFFF"), shadow("FxSelectedGlow", "#4CC2FF", 12, 0, 0.35),
    solid("FxFieldBrush", "#101A2C"), solid("FxFieldEdgeBrush", "#2F4568"), corner("FxFieldRadius", "10"),
    solid("FxFieldInkBrush", "#EEF4FB"), solid("FxFieldFocusBrush", "#4CC2FF"), shadow("FxFieldFocusGlow", "#4CC2FF", 10, 0, 0.45),
    solid("FxPopupBrush", "#121D33"), solid("FxPopupEdgeBrush", "#2F4568"), corner("FxPopupRadius", "10"),
    shadow("FxPopupShadow", "#000000", 22, 8, 0.6),
    solid("FxCheckBrush", "#101A2C"), solid("FxCheckEdgeBrush", "#4A6184"),
    lin("FxCheckOnBrush", [("#9FDCFF", 0), ("#4AB4F0", 0.45), ("#1470C0", 0.5), ("#2F9BE6", 1)]),
    solid("FxCheckOnEdgeBrush", "#031A3D"), solid("FxCheckMarkBrush", "#FFFFFF"),
    lin("FxThumbBrush", [("#56789F", 0), ("#2F5079", 0.49), ("#16345A", 0.5), ("#1F4670", 1)], "0,0", "1,0"),
    solid("FxThumbEdgeBrush", "#4A6184"), thick("FxThumbBorderThickness", "1"),
    lin("ProgressFillBrush", [("#9FDCFF", 0), ("#4AB4F0", 0.45), ("#1470C0", 0.5), ("#2F9BE6", 1)]),
    # Near-black glass screen; digits in the aurora teal of this theme's night sky.
    lin("TimerScreenBrush", [("#0A1628", 0), ("#060E1C", 0.6), ("#030812", 1)]),
    rad("TimerScreenOverlayBrush", [(a("#4CAAFF", 0.20), 0), (a("#4CAAFF", 0.0), 1)], "0.5,0", 0.75, 0.9),
    glass("TimerScreenGlassBrush", 0.08, 0.03), solid("TimerScreenEdgeBrush", a("#78B4FF", 0.25)), thick("TimerScreenBorderThickness", "1"),
    corner("TimerScreenRadius", "12"), thick("TimerScreenPadding", "22,6,22,8"),
    solid("TimerTextBrush", "#3EF0C4"), solid("TimerScreenInkBrush", "#9CC4EA"),
    lin("PopoutScreenBrush", [("#0A1628", 0), ("#030812", 1)]),
    shadow("TimerGlow", "#3EF0C4", 16, 0, 0.6), font("TimerFont", "Corbel"),
])

# ------------------------------------------------------------------ Frutiger Aqua (new to BijouHub)
THEMES["Aqua"] = dict(blurb="Frutiger Aqua — the underwater side of Aero: light rays, bubbles, water-droplet buttons (BijouMusic).", res=[
    *tokens("#E2F5F6", "#EEFAFA", a("#FFFFFF", 0.80), a("#FFFFFF", 0.96), "#06303A", "#235A63", "#5E8F96",
            a("#3E9EA8", 0.45), "#CDEEF0", "#077E8C", "#B7811E", "#B83A74", "#B8312F", "#1F7A45", "Candara, Corbel, Segoe UI", radius=12),
    color("TitleBarColor", "#D2F3F6"), color("TitleBarTextColor", "#06303A"),
    image("WallpaperBrush", "aqua.jpg"),
    solid("SidebarBrush", a("#ECFCFD", 0.80)), solid("SidebarEdgeBrush", a("#FFFFFF", 0.85)),
    solid("ContentBrush", a("#F2FCFD", 0.80)), solid("ContentEdgeBrush", a("#FFFFFF", 0.85)),
    corner("PanelRadius", "14"), corner("PanelTopRadius", "13,13,0,0"),
    shadow("PanelShadow", "#04466E", 26, 8, 0.30),
    lin("SidebarHeaderBrush", [(a("#FFFFFF", 0.82), 0), (a("#EAFBFC", 0.70), 0.5), (a("#CEF2F6", 0.66), 0.51), (a("#E2F9FB", 0.72), 1)]),
    solid("SidebarHeaderEdgeBrush", a("#FFFFFF", 0.85)), solid("SidebarHeaderInkBrush", "#06303A"),
    lin("FxBtnBrush", [("#FFFFFF", 0), ("#ECFAFB", 0.49), ("#CDEFF2", 0.5), ("#E6F8FA", 1)]),
    solid("FxBtnEdgeBrush", "#7FC0C7"), corner("FxBtnRadius", "15"), corner("FxTileRadius", "18"),
    solid("FxBtnSheenBrush", a("#FFFFFF", 0.95)), solid("FxBtnInkBrush", "#06303A"),
    shadow("FxBtnShadow", "#08464F", 3, 1, 0.22), shadow("FxBtnHoverEffect", "#0A8A99", 12, 0, 0.5),
    solid("FxHoverWashBrush", a("#AEE9EF", 0.30)), solid("FxPressWashBrush", a("#0A8A99", 0.14)),
    lin("FxPrimaryBrush", [("#7EE8F5", 0), ("#2BB7CF", 0.48), ("#0B8FA6", 0.52), ("#33C0D6", 1)]),
    solid("FxPrimaryEdgeBrush", "#056573"), solid("FxPrimaryInkBrush", "#022A33"),
    shadow("FxPrimaryShadow", "#0A8A99", 10, 0, 0.45), shadow("FxPrimaryHoverEffect", "#3CE6DC", 16, 0, 0.6),
    solid("FxRowBrush", a("#FFFFFF", 0.45)), solid("FxRowEdgeBrush", a("#FFFFFF", 0.70)), corner("FxRowRadius", "12"),
    solid("FxRowHoverBrush", a("#FFFFFF", 0.80)),
    lin("FxSelectedBrush", [("#9FF1F8", 0), ("#3CC7DA", 1)]), solid("FxSelectedEdgeBrush", a("#FFFFFF", 0.85)),
    solid("FxSelectedInkBrush", "#022A33"), shadow("FxSelectedGlow", "#0A8A99", 12, 0, 0.45),
    solid("FxFieldBrush", a("#FFFFFF", 0.92)), solid("FxFieldEdgeBrush", "#8CC9CF"), corner("FxFieldRadius", "12"),
    solid("FxFieldInkBrush", "#06303A"), solid("FxFieldFocusBrush", "#0A8A99"), shadow("FxFieldFocusGlow", "#0A8A99", 10, 0, 0.4),
    solid("FxPopupBrush", a("#FFFFFF", 0.97)), solid("FxPopupEdgeBrush", "#8CC9CF"), corner("FxPopupRadius", "12"),
    shadow("FxPopupShadow", "#04466E", 18, 6, 0.3),
    solid("FxCheckBrush", a("#FFFFFF", 0.95)), solid("FxCheckEdgeBrush", "#7FC0C7"),
    lin("FxCheckOnBrush", [("#7EE8F5", 0), ("#2BB7CF", 0.48), ("#0B8FA6", 0.52), ("#33C0D6", 1)]),
    solid("FxCheckOnEdgeBrush", "#056573"), solid("FxCheckMarkBrush", "#FFFFFF"),
    lin("FxThumbBrush", [("#FFFFFF", 0), ("#D6F7F9", 0.5), ("#AEE9EF", 0.51), ("#C9F2F5", 1)], "0,0", "1,0"),
    solid("FxThumbEdgeBrush", "#7FC0C7"), thick("FxThumbBorderThickness", "1"),
    lin("ProgressFillBrush", [("#7EE8F5", 0), ("#2BB7CF", 0.48), ("#0B8FA6", 0.52), ("#33C0D6", 1)]),
    # Deep-sea LCD with a coral readout (BijouMusic frutiger-aqua --screen-*).
    lin("TimerScreenBrush", [("#04475E", 0), ("#022A3A", 0.6), ("#011A26", 1)]),
    rad("TimerScreenOverlayBrush", [(a("#5AE6F0", 0.25), 0), (a("#5AE6F0", 0.0), 1)], "0.5,0", 0.75, 0.9),
    glass("TimerScreenGlassBrush"), solid("TimerScreenEdgeBrush", a("#8CF0FA", 0.30)), thick("TimerScreenBorderThickness", "1"),
    corner("TimerScreenRadius", "14"), thick("TimerScreenPadding", "22,6,22,8"),
    solid("TimerTextBrush", "#FF8A6A"), solid("TimerScreenInkBrush", "#9FE8F0"),
    lin("PopoutScreenBrush", [("#04475E", 0), ("#011A26", 1)]),
    shadow("TimerGlow", "#FF8A6A", 14, 0, 0.55), font("TimerFont", "Candara"),
])

# ------------------------------------------------------------------ MS-DOS (Windows 95)
THEMES["Dos"] = dict(blurb="MS-DOS — Windows 95 plates over the clouds: bevels, navy title bar, square everything.", res=[
    *tokens("#C0C0C0", "#C0C0C0", "#FFFFFF", "#E8E8E8", "#000000", "#303030", "#4D4D4D", "#808080", "#A0A0A0",
            "#000080", "#808000", "#800080", "#C00000", "#008000", "Tahoma, MS Sans Serif, Verdana", radius=0),
    weight("HeadingFontWeight", "Bold"),
    color("TitleBarColor", "#000080"), color("TitleBarTextColor", "#FFFFFF"),
    image("WallpaperBrush", "dos.jpg"),
    solid("SidebarBrush", "#C0C0C0"), solid("SidebarEdgeBrush", "#00000000"),
    solid("ContentBrush", "#C0C0C0"), solid("ContentEdgeBrush", "#00000000"),
    corner("PanelRadius", "0"), corner("PanelTopRadius", "0"), thick("PanelInset", "3"),
    vis("PanelBevelVisibility", True),
    solid("PanelBevelOuterLightBrush", "#FFFFFF"), solid("PanelBevelOuterDarkBrush", "#000000"),
    solid("PanelBevelInnerLightBrush", "#DFDFDF"), solid("PanelBevelInnerDarkBrush", "#808080"),
    lin("SidebarHeaderBrush", [("#000080", 0), ("#1084D0", 1)], "0,0", "1,0"),
    solid("SidebarHeaderInkBrush", "#FFFFFF"), thick("SidebarHeaderBorderThickness", "0"),
    thick("SidebarHeaderPadding", "4,3,4,3"), thick("LogoMargin", "2,1,2,1"), double("LogoFontSize", 14),
    *bevel_set(),
    solid("FxBtnBrush", "#C0C0C0"), solid("FxBtnEdgeBrush", "#00000000"), thick("FxBtnBorderThickness", "0"),
    corner("FxBtnRadius", "0"), corner("FxTileRadius", "0"), solid("FxBtnInkBrush", "#000000"),
    solid("FxHoverWashBrush", "#00FFFFFF"), solid("FxPressWashBrush", "#00000000"), translate("FxPressTransform", 1),
    solid("FxPrimaryBrush", "#C0C0C0"), solid("FxPrimaryEdgeBrush", "#000000"), solid("FxPrimaryInkBrush", "#000000"),
    solid("FxRowBrush", "#00FFFFFF"), solid("FxRowEdgeBrush", "#00000000"), corner("FxRowRadius", "0"),
    solid("FxRowHoverBrush", a("#000080", 0.10)),
    solid("FxSelectedBrush", "#000080"), solid("FxSelectedEdgeBrush", "#000080"), solid("FxSelectedInkBrush", "#FFFFFF"),
    solid("FxFieldBrush", "#FFFFFF"), solid("FxFieldEdgeBrush", "#00000000"), thick("FxFieldBorderThickness", "0"),
    corner("FxFieldRadius", "0"), solid("FxFieldInkBrush", "#000000"), solid("FxFieldFocusBrush", "#00000000"),
    solid("FxPopupBrush", "#FFFFFF"), solid("FxPopupEdgeBrush", "#000000"), corner("FxPopupRadius", "0"),
    shadow("FxPopupShadow", "#000000", 0, 2, 0.5, 315),
    solid("FxCheckBrush", "#FFFFFF"), solid("FxCheckEdgeBrush", "#00000000"), corner("FxCheckRadius", "0"),
    solid("FxCheckOnBrush", "#FFFFFF"), solid("FxCheckOnEdgeBrush", "#00000000"), solid("FxCheckMarkBrush", "#000000"),
    solid("FxThumbBrush", "#C0C0C0"), corner("FxThumbRadius", "0"),
    checker("FxTrackBrush", "#C0C0C0", "#FFFFFF"),
    # The Windows 95 progress bar: navy blocks.
    blocks("ProgressFillBrush", "#000080", "#00000000"),
    # Timer: a black DOS box.
    solid("TimerScreenBrush", "#000000"), solid("TimerScreenEdgeBrush", "#808080"),
    thick("TimerScreenBorderThickness", "2"), thick("TimerScreenPadding", "18,4,18,6"),
    solid("TimerTextBrush", "#C0C0C0"), solid("TimerScreenInkBrush", "#808080"), solid("PopoutScreenBrush", "#000000"),
    font("TimerFont", "Lucida Console, Consolas"), weight("TimerFontWeight", "Normal"),
])

# ------------------------------------------------------------------ Vaporwave
THEMES["Vaporwave"] = dict(blurb="Vaporwave — pastel Win98 windows over a sunset, pink-purple title bars, neon grid.", res=[
    *tokens("#E8D9FF", "#E8D9FF", "#F6EDFF", "#FFF6FF", "#2A1A4A", "#4F3D78", "#6A5794", "#B99BE6", "#D9C8F3",
            "#C81D92", "#C96F00", "#8A3AD9", "#D6204E", "#0F9D74", "Trebuchet MS, Segoe UI", radius=4),
    weight("HeadingFontWeight", "Bold"),
    color("TitleBarColor", "#7A1FB8"), color("TitleBarTextColor", "#FFFFFF"),
    image("WallpaperBrush", "vaporwave.jpg", align_y="Bottom"),
    solid("SidebarBrush", a("#EAE0FF", 0.95)), solid("SidebarEdgeBrush", "#B967FF"),
    solid("ContentBrush", a("#F4EEFF", 0.93)), solid("ContentEdgeBrush", "#B967FF"),
    thick("SidebarBorderThickness", "2"), thick("ContentBorderThickness", "2"), thick("PanelInset", "2"),
    corner("PanelRadius", "4"), corner("PanelTopRadius", "2,2,0,0"),
    # Hard offset drop shadow, no blur — the retro-desktop way to float a window.
    shadow("PanelShadow", "#1A0A3D", 0, 5.66, 0.35, 315),
    graph("PanelTextureBrush", a("#4A2878", 0.07)),
    lin("SidebarHeaderBrush", [("#FF71CE", 0), ("#B967FF", 0.55), ("#6B4BD8", 1)], "0,0", "1,0"),
    solid("SidebarHeaderEdgeBrush", "#1A0A3D"), thick("SidebarHeaderBorderThickness", "0,0,0,2"),
    solid("SidebarHeaderInkBrush", "#FFFFFF"), vis("TitleGlitchVisibility", True),
    solid("FxBtnBrush", "#FDF3FF"), solid("FxBtnEdgeBrush", "#FF71CE"), thick("FxBtnBorderThickness", "1.5"),
    corner("FxBtnRadius", "3"), corner("FxTileRadius", "4"), solid("FxBtnInkBrush", "#2A1A4A"),
    shadow("FxBtnShadow", "#1A0A3D", 0, 2.83, 0.35, 315), shadow("FxBtnHoverEffect", "#FF71CE", 10, 0, 0.7),
    solid("FxHoverWashBrush", a("#FF71CE", 0.10)), solid("FxPressWashBrush", a("#B967FF", 0.18)), translate("FxPressTransform", 1),
    lin("FxPrimaryBrush", [("#FF71CE", 0), ("#B967FF", 1)], "0,0", "1,0"),
    solid("FxPrimaryEdgeBrush", "#B967FF"), solid("FxPrimaryInkBrush", "#FFFFFF"),
    shadow("FxPrimaryShadow", "#1A0A3D", 0, 2.83, 0.35, 315), shadow("FxPrimaryHoverEffect", "#FF4FB8", 12, 0, 0.6),
    solid("FxRowBrush", "#FDF7FF"), solid("FxRowEdgeBrush", "#D9C8F3"), corner("FxRowRadius", "3"),
    solid("FxRowHoverBrush", "#FFFFFF"),
    lin("FxSelectedBrush", [("#FF71CE", 0), ("#B967FF", 1)], "0,0", "1,0"), solid("FxSelectedEdgeBrush", "#B967FF"),
    solid("FxSelectedInkBrush", "#FFFFFF"),
    solid("FxFieldBrush", "#FFFAFE"), solid("FxFieldEdgeBrush", "#B99BE6"), thick("FxFieldBorderThickness", "1.5"),
    corner("FxFieldRadius", "3"), solid("FxFieldInkBrush", "#2A1A4A"), solid("FxFieldFocusBrush", "#FF71CE"),
    shadow("FxFieldFocusGlow", "#FF71CE", 8, 0, 0.5),
    solid("FxPopupBrush", "#FDF7FF"), solid("FxPopupEdgeBrush", "#B967FF"), corner("FxPopupRadius", "3"),
    shadow("FxPopupShadow", "#1A0A3D", 0, 4.24, 0.35, 315),
    solid("FxCheckBrush", "#FFFAFE"), solid("FxCheckEdgeBrush", "#B967FF"), corner("FxCheckRadius", "2"),
    lin("FxCheckOnBrush", [("#FF71CE", 0), ("#B967FF", 1)], "0,0", "1,1"), solid("FxCheckOnEdgeBrush", "#8A3AD9"),
    solid("FxCheckMarkBrush", "#FFFFFF"),
    lin("FxThumbBrush", [("#FF9CE0", 0), ("#B967FF", 1)]), corner("FxThumbRadius", "2"),
    lin("ProgressFillBrush", [("#FF71CE", 0), ("#B967FF", 0.6), ("#01CDFE", 1)], "0,0", "1,0"),
    # Timer: BijouMusic's purple CRT sunset under scanlines, neon-pink digits.
    image("TimerScreenBrush", "vapor-crt.jpg", align_y="Bottom"),
    scanlines("TimerScreenOverlayBrush", a("#000000", 0.32)),
    solid("TimerScreenEdgeBrush", "#1B0F33"), thick("TimerScreenBorderThickness", "2"),
    corner("TimerScreenRadius", "4"), thick("TimerScreenPadding", "26,8,26,10"),
    solid("TimerTextBrush", "#FF71CE"), solid("TimerScreenInkBrush", "#FFB8EE"), solid("PopoutScreenBrush", "#150426"),
    shadow("TimerGlow", "#FF4FD8", 16, 0, 0.8), font("TimerFont", "Trebuchet MS"), weight("TimerFontWeight", "Bold"),
])


# ------------------------------------------------------------------ Y2K Chrome / Gunmetal (shared hardware)
def y2k(dark):
    if not dark:
        ink, muted, faint = "#14171B", "#434A53", "#5D646D"
        brush = lin("SidebarBrush", [("#CDD1D6", 0), ("#E2E5E9", 0.45), ("#D0D4D9", 1)], "0,0", "1,0")
        content = lin("ContentBrush", [("#EEF0F2", 0), ("#E4E7EA", 1)])
        hair = hairlines("PanelTextureBrush", a("#FFFFFF", 0.22), a("#000000", 0.03))
        plate = lin("SidebarHeaderBrush", [("#F3F4F6", 0), ("#D9DDE2", 0.5), ("#C9CED4", 1)])
        plate_dots = dots("SidebarHeaderTextureBrush", a("#282C32", 0.22))
        key = lin("FxBtnBrush", [("#FDFDFE", 0), ("#E3E6E9", 0.48), ("#C9CED4", 0.52), ("#E2E5E8", 1)])
        key_edge, key_ink, key_sheen = "#7D848E", "#1D2126", a("#FFFFFF", 1.0)
        row_on, row_on_ink = "#FAFBFC", "#1D2126"
        field = "#FFFFFF"
        screws = [solid("FxScrewHeadBrush", "#F1F3F5"), solid("FxScrewRimBrush", "#7D848E"), solid("FxScrewSlotBrush", "#565C64")]
        bar = ("#D7DBE0", "#1D2126")
        tok = tokens("#E4E6E9", "#EEF0F2", "#FAFBFC", "#FFFFFF", ink, muted, faint, "#9EA5AE", "#C3C8CE",
                     "#A85206", "#8F6A0A", "#A8326A", "#B8312F", "#2A7A3F", "Bahnschrift, Segoe UI", radius=6)
        edge, wash = "#8A919A", a("#FFFFFF", 0.35)
    else:
        ink, muted, faint = "#E6E8EB", "#A3A9B1", "#7A818A"
        brush = lin("SidebarBrush", [("#2C3036", 0), ("#3A3E45", 0.45), ("#2D3137", 1)], "0,0", "1,0")
        content = lin("ContentBrush", [("#272A2F", 0), ("#212428", 1)])
        hair = hairlines("PanelTextureBrush", a("#FFFFFF", 0.05), a("#000000", 0.10))
        plate = lin("SidebarHeaderBrush", [("#454A52", 0), ("#30343A", 0.5), ("#26292E", 1)])
        plate_dots = dots("SidebarHeaderTextureBrush", a("#000000", 0.45))
        key = lin("FxBtnBrush", [("#5A616A", 0), ("#3D4249", 0.48), ("#272A2F", 0.52), ("#353940", 1)])
        key_edge, key_ink, key_sheen = "#0C0D0F", "#E6E8EB", a("#FFFFFF", 0.20)
        row_on, row_on_ink = "#33373E", "#E6E8EB"
        field = "#1B1D21"
        screws = [solid("FxScrewHeadBrush", "#8C939C"), solid("FxScrewRimBrush", "#15171A"), solid("FxScrewSlotBrush", "#2A2E33")]
        bar = ("#2C3036", "#E6E8EB")
        tok = tokens("#24272C", "#2A2E33", "#2E3238", "#383C43", ink, muted, faint, "#4A4F57", "#33373D",
                     "#FFB347", "#E0A030", "#E07AB0", "#FF6B5B", "#6BCF7F", "Bahnschrift, Segoe UI", radius=6)
        edge, wash = "#0B0C0E", a("#FFFFFF", 0.08)
    amber_key = lin("FxPrimaryBrush", [("#FFBF47", 0), ("#F29A17", 0.48), ("#D97C00", 0.52), ("#F0A133", 1)])
    return [
        *tok, weight("HeadingFontWeight", "Bold"),
        color("TitleBarColor", bar[0]), color("TitleBarTextColor", bar[1]),
        image("WallpaperBrush", "y2k.jpg"),
        brush, solid("SidebarEdgeBrush", edge), content, solid("ContentEdgeBrush", edge),
        shadow("PanelShadow", "#000000", 18, 6, 0.45 if dark else 0.35),
        hair, vis("ScrewsVisibility", True), *screws,
        plate, plate_dots, solid("SidebarHeaderEdgeBrush", edge), solid("SidebarHeaderInkBrush", ink),
        thick("SidebarHeaderPadding", "22,8,22,8"),
        # Chrome push-buttons with a mirror break at the middle and 1px of key travel.
        key, solid("FxBtnEdgeBrush", key_edge), corner("FxBtnRadius", "5"), corner("FxTileRadius", "8"),
        solid("FxBtnSheenBrush", key_sheen), solid("FxBtnInkBrush", key_ink),
        shadow("FxBtnShadow", "#000000", 2, 1, 0.5 if dark else 0.25),
        shadow("FxBtnHoverEffect", "#000000", 3, 1, 0.5 if dark else 0.3),
        solid("FxHoverWashBrush", wash), solid("FxPressWashBrush", a("#000000", 0.16)), translate("FxPressTransform", 1),
        # The latched/primary key is lit amber, with the power-LED glow.
        amber_key, solid("FxPrimaryEdgeBrush", "#8A4300"), solid("FxPrimaryInkBrush", "#1A0F00"),
        shadow("FxPrimaryShadow", "#FFA000", 10, 0, 0.45), shadow("FxPrimaryHoverEffect", "#FFA000", 14, 0, 0.65),
        solid("FxRowBrush", a("#FFFFFF", 0.0)), solid("FxRowEdgeBrush", "#00000000"), corner("FxRowRadius", "5"),
        solid("FxRowHoverBrush", a("#FFFFFF", 0.45 if not dark else 0.06)),
        # Selected row: lit, with the amber edge — like BijouMusic's track list.
        solid("FxSelectedBrush", row_on), solid("FxSelectedEdgeBrush", "#E8890C"), thick("FxSelectedBorderThickness", "3,1,1,1"),
        solid("FxSelectedInkBrush", row_on_ink),
        solid("FxFieldBrush", field), solid("FxFieldEdgeBrush", key_edge), corner("FxFieldRadius", "5"),
        solid("FxFieldInkBrush", ink), solid("FxFieldFocusBrush", "#E8890C"), shadow("FxFieldFocusGlow", "#FFA000", 8, 0, 0.35),
        solid("FxPopupBrush", field), solid("FxPopupEdgeBrush", key_edge), corner("FxPopupRadius", "6"),
        shadow("FxPopupShadow", "#000000", 16, 5, 0.4),
        solid("FxCheckBrush", field), solid("FxCheckEdgeBrush", key_edge), corner("FxCheckRadius", "3"),
        amber_key.replace('x:Key="FxPrimaryBrush"', 'x:Key="FxCheckOnBrush"'), solid("FxCheckOnEdgeBrush", "#8A4300"),
        solid("FxCheckMarkBrush", "#1A0F00"),
        key.replace('x:Key="FxBtnBrush"', 'x:Key="FxThumbBrush"').replace('StartPoint="0,0" EndPoint="0,1"', 'StartPoint="0,0" EndPoint="1,0"'),
        solid("FxThumbEdgeBrush", key_edge), thick("FxThumbBorderThickness", "1"), corner("FxThumbRadius", "3"),
        amber_key.replace('x:Key="FxPrimaryBrush"', 'x:Key="ProgressFillBrush"'),
        # Timer: the amber backlit LCD behind glass, dot-matrix grid over the digits.
        rad("TimerScreenBrush", [("#2E1A02", 0), ("#170C00", 0.7), ("#0B0600", 1)], "0.5,0.45", 0.5, 0.45),
        dotmatrix("TimerScreenOverlayBrush", a("#000000", 0.42)), glass("TimerScreenGlassBrush", 0.08, 0.03),
        solid("TimerScreenEdgeBrush", "#2B2F35"), thick("TimerScreenBorderThickness", "3"),
        corner("TimerScreenRadius", "6"), thick("TimerScreenPadding", "22,6,22,8"),
        solid("TimerTextBrush", "#FFB347"), solid("TimerScreenInkBrush", "#D18A2C"),
        rad("PopoutScreenBrush", [("#2E1A02", 0), ("#170C00", 0.7), ("#0B0600", 1)], "0.5,0.45", 0.5, 0.45),
        shadow("TimerGlow", "#FFA000", 10, 0, 0.6), font("TimerFont", "Consolas"), weight("TimerFontWeight", "Bold"),
    ]


THEMES["Y2kChrome"] = dict(blurb="Y2K Chrome — a Y2K media player: brushed aluminium, screws, chrome keys, amber LCD.", res=y2k(False))
THEMES["Y2kGunmetal"] = dict(blurb="Y2K Gunmetal — the same hardware in dark anodised aluminium (Bijou Footage).", res=y2k(True))

# ------------------------------------------------------------------ Studio (Bijou Footage's cuttingRoom)
THEMES["Studio"] = dict(blurb="Studio — a modern editing suite lit violet and blue from the edges (Bijou Footage).", res=[
    *tokens("#0D1020", "#141830", "#1A1E36", "#232847", "#E9EBF5", "#9AA0C0", "#6B7194", "#2A2F52", "#1C2038",
            "#A493FF", "#E0A85A", "#E07AB0", "#FF6B7A", "#5FD39A", "Segoe UI", radius=10),
    color("TitleBarColor", "#0B0E1C"), color("TitleBarTextColor", "#E9EBF5"),
    image("WallpaperBrush", "studio.jpg"),
    lin("SidebarBrush", [(a("#191D36", 0.94), 0), (a("#101326", 0.94), 1)]), solid("SidebarEdgeBrush", a("#96A0FF", 0.11)),
    lin("ContentBrush", [(a("#171B32", 0.92), 0), (a("#0F1224", 0.92), 1)]), solid("ContentEdgeBrush", a("#96A0FF", 0.11)),
    shadow("PanelShadow", "#000000", 40, 14, 0.55),
    lin("SidebarHeaderBrush", [(a("#FFFFFF", 0.035), 0), (a("#FFFFFF", 0.0), 1)]),
    solid("SidebarHeaderEdgeBrush", a("#96A0FF", 0.11)), solid("SidebarHeaderInkBrush", "#E9EBF5"),
    lin("FxBtnBrush", [("#252A46", 0), ("#1A1E36", 1)]), solid("FxBtnEdgeBrush", a("#FFFFFF", 0.08)),
    corner("FxBtnRadius", "8"), corner("FxTileRadius", "12"),
    solid("FxBtnSheenBrush", a("#FFFFFF", 0.10)), solid("FxBtnInkBrush", "#E9EBF5"),
    shadow("FxBtnShadow", "#000000", 5, 2, 0.45), shadow("FxBtnHoverEffect", "#000000", 8, 3, 0.55),
    solid("FxHoverWashBrush", a("#FFFFFF", 0.06)), solid("FxPressWashBrush", a("#000000", 0.25)), translate("FxPressTransform", 1),
    lin("FxPrimaryBrush", [("#927EFF", 0), ("#5A45E6", 1)]), solid("FxPrimaryEdgeBrush", a("#BEAFFF", 0.6)),
    solid("FxPrimaryInkBrush", "#FFFFFF"),
    shadow("FxPrimaryShadow", "#6E50FF", 18, 4, 0.45), shadow("FxPrimaryHoverEffect", "#6E50FF", 22, 4, 0.7),
    solid("FxRowBrush", a("#FFFFFF", 0.02)), solid("FxRowEdgeBrush", "#00000000"), corner("FxRowRadius", "8"),
    solid("FxRowHoverBrush", a("#FFFFFF", 0.05)),
    lin("FxSelectedBrush", [(a("#8269FF", 0.24), 0), (a("#5A46E6", 0.16), 1)]), solid("FxSelectedEdgeBrush", a("#A591FF", 0.65)),
    solid("FxSelectedInkBrush", "#FFFFFF"), shadow("FxSelectedGlow", "#6E50FF", 16, 0, 0.3),
    solid("FxFieldBrush", "#12152A"), solid("FxFieldEdgeBrush", "#2A2F52"), corner("FxFieldRadius", "8"),
    solid("FxFieldInkBrush", "#E9EBF5"), solid("FxFieldFocusBrush", "#927EFF"), shadow("FxFieldFocusGlow", "#6E50FF", 12, 0, 0.45),
    solid("FxPopupBrush", "#161A30"), solid("FxPopupEdgeBrush", "#2A2F52"), corner("FxPopupRadius", "10"),
    shadow("FxPopupShadow", "#000000", 24, 8, 0.6),
    solid("FxCheckBrush", "#12152A"), solid("FxCheckEdgeBrush", "#3A4070"),
    lin("FxCheckOnBrush", [("#927EFF", 0), ("#5A45E6", 1)]), solid("FxCheckOnEdgeBrush", "#A591FF"),
    solid("FxCheckMarkBrush", "#FFFFFF"),
    solid("FxThumbBrush", "#3A4070"),
    lin("ProgressFillBrush", [("#A493FF", 0), ("#5A45E6", 1)], "0,0", "1,0"),
    lin("TimerScreenBrush", [("#0D1024", 0), ("#070914", 1)]),
    rad("TimerScreenOverlayBrush", [(a("#7C5CFF", 0.22), 0), (a("#7C5CFF", 0.0), 1)], "0.15,0", 0.8, 1.1),
    solid("TimerScreenEdgeBrush", a("#A591FF", 0.35)), thick("TimerScreenBorderThickness", "1"),
    corner("TimerScreenRadius", "12"), thick("TimerScreenPadding", "22,6,22,8"),
    solid("TimerTextBrush", "#CFC7FF"), solid("TimerScreenInkBrush", "#8F88C8"),
    lin("PopoutScreenBrush", [("#0D1024", 0), ("#070914", 1)]),
    shadow("TimerGlow", "#7C5CFF", 18, 0, 0.7), font("TimerFont", "Consolas"),
])

# ------------------------------------------------------------------ Doodle Club (Bijou Doodle)
THEMES["DoodleClub"] = dict(blurb="Doodle Club — Bijou Doodle's blue doodle wallpaper, cream paper, ink outlines, felt-tip logo.", res=[
    *tokens("#FFFBE9", "#FFFBE9", "#FFFDF4", "#FFF0A8", "#202031", "#4A4A66", "#6E6E8A", "#27283B", "#D8D2B4",
            "#1F5FBF", "#C77A00", "#B92A65", "#C0392B", "#258026", "Comic Sans MS, Segoe UI", radius=8),
    weight("HeadingFontWeight", "Bold"),
    color("TitleBarColor", "#FFFBE9"), color("TitleBarTextColor", "#202031"),
    f'<ImageBrush x:Key="WallpaperBrush" ImageSource="{ASSET}doodle-tile.png" TileMode="Tile" Stretch="Fill" '
    f'Viewport="0,0,400,400" ViewportUnits="Absolute" RenderOptions.BitmapScalingMode="HighQuality"/>',
    thick("ShellPadding", "12"), thick("SidebarMargin", "0,0,12,0"),
    solid("SidebarBrush", "#FFFBE9"), solid("SidebarEdgeBrush", "#27283B"),
    solid("ContentBrush", "#FFFBE9"), solid("ContentEdgeBrush", "#27283B"),
    thick("SidebarBorderThickness", "2.5"), thick("ContentBorderThickness", "2.5"), thick("PanelInset", "2.5"),
    corner("PanelRadius", "12"), corner("PanelTopRadius", "10,10,0,0"),
    shadow("PanelShadow", "#172B50", 0, 3, 0.55, 270),
    solid("SidebarHeaderBrush", "#FFFBE9"), solid("SidebarHeaderEdgeBrush", "#27283B"),
    thick("SidebarHeaderBorderThickness", "0,0,0,2"), solid("SidebarHeaderInkBrush", "#1F5FBF"),
    vis("LogoStandardVisibility", False), vis("LogoDoodleVisibility", True),
    solid("FxBtnBrush", "#FFFBE9"), solid("FxBtnEdgeBrush", "#27283B"), thick("FxBtnBorderThickness", "1.5"),
    corner("FxBtnRadius", "6"), corner("FxTileRadius", "10"), solid("FxBtnInkBrush", "#202031"),
    solid("FxBtnSheenBrush", "#FFFFFF"), shadow("FxBtnShadow", "#CBC5A7", 0, 2, 1.0, 270),
    solid("FxHoverWashBrush", a("#FFE45C", 0.55)), solid("FxPressWashBrush", a("#FFD21E", 0.45)), translate("FxPressTransform", 1),
    # Marker-yellow primary, like Bijou Doodle's play button.
    solid("FxPrimaryBrush", "#FFE45C"), solid("FxPrimaryEdgeBrush", "#202031"), solid("FxPrimaryInkBrush", "#202031"),
    shadow("FxPrimaryShadow", "#D4AD26", 0, 3, 1.0, 270), shadow("FxPrimaryHoverEffect", "#D4AD26", 0, 3, 1.0, 270),
    solid("FxRowBrush", "#FFFDF4"), solid("FxRowEdgeBrush", "#27283B"), thick("FxRowBorderThickness", "1.5"),
    corner("FxRowRadius", "6"), solid("FxRowHoverBrush", "#FFF0A8"),
    solid("FxSelectedBrush", "#CFE3FF"), solid("FxSelectedEdgeBrush", "#27283B"), thick("FxSelectedBorderThickness", "2"),
    solid("FxSelectedInkBrush", "#13284D"),
    solid("FxFieldBrush", "#FFFFFF"), solid("FxFieldEdgeBrush", "#27283B"), thick("FxFieldBorderThickness", "1.5"),
    corner("FxFieldRadius", "6"), solid("FxFieldInkBrush", "#202031"), solid("FxFieldFocusBrush", "#1F5FBF"),
    solid("FxPopupBrush", "#FFFDF4"), solid("FxPopupEdgeBrush", "#27283B"), corner("FxPopupRadius", "8"),
    shadow("FxPopupShadow", "#172B50", 0, 3, 0.55, 270),
    solid("FxCheckBrush", "#FFFFFF"), solid("FxCheckEdgeBrush", "#27283B"), corner("FxCheckRadius", "4"),
    solid("FxCheckOnBrush", "#CFE3FF"), solid("FxCheckOnEdgeBrush", "#27283B"), solid("FxCheckMarkBrush", "#13284D"),
    solid("FxThumbBrush", "#8FB3E8"), solid("FxThumbEdgeBrush", "#27283B"), thick("FxThumbBorderThickness", "1.5"),
    corner("FxThumbRadius", "4"),
    solid("ProgressFillBrush", "#FFE45C"),
    solid("TimerScreenBrush", "#FFFFFF"), solid("TimerScreenEdgeBrush", "#27283B"), thick("TimerScreenBorderThickness", "2.5"),
    corner("TimerScreenRadius", "12"), thick("TimerScreenPadding", "22,2,22,6"),
    solid("TimerTextBrush", "#1F5FBF"), font("TimerFont", "Comic Sans MS"), weight("TimerFontWeight", "Bold"),
])

# ------------------------------------------------------------------ Fable (new art direction)
THEMES["Fable"] = dict(blurb="Fable — a storybook on aged parchment: sepia ink, wax-seal red, Palatino.", res=[
    *tokens("#E8D9AB", "#EFE2B8", a("#FBF4DC", 0.85), "#FFF8E4", "#3B2A17", "#6B4F30", "#8A6D47", "#A88A5A", "#D6C190",
            "#9C2B2B", "#8A5A10", "#7A3B52", "#8E1F1F", "#4A6329", "Palatino Linotype, Book Antiqua, Georgia", radius=4),
    weight("HeadingFontWeight", "Bold"),
    color("TitleBarColor", "#D6C08A"), color("TitleBarTextColor", "#3B2A17"),
    image("WallpaperBrush", "fable.jpg"),
    solid("SidebarBrush", a("#F2E6C4", 0.94)), solid("SidebarEdgeBrush", "#8F7248"),
    solid("ContentBrush", a("#F6ECCE", 0.92)), solid("ContentEdgeBrush", "#8F7248"),
    thick("SidebarBorderThickness", "1.5"), thick("ContentBorderThickness", "1.5"), thick("PanelInset", "1.5"),
    corner("PanelRadius", "6"), corner("PanelTopRadius", "5,5,0,0"),
    shadow("PanelShadow", "#3B2A17", 18, 6, 0.35),
    lin("SidebarHeaderBrush", [("#E4D09D", 0), ("#D6BE86", 1)]), solid("SidebarHeaderEdgeBrush", "#8F7248"),
    thick("SidebarHeaderBorderThickness", "0,0,0,2"), solid("SidebarHeaderInkBrush", "#3B2A17"),
    lin("FxBtnBrush", [("#FAF2D8", 0), ("#E9D9AA", 1)]), solid("FxBtnEdgeBrush", "#8F7248"),
    corner("FxBtnRadius", "4"), corner("FxTileRadius", "6"), solid("FxBtnInkBrush", "#3B2A17"),
    solid("FxBtnSheenBrush", a("#FFFFFF", 0.6)), shadow("FxBtnShadow", "#3B2A17", 2, 1, 0.25),
    shadow("FxBtnHoverEffect", "#C9A055", 10, 0, 0.6),
    solid("FxHoverWashBrush", a("#FFF6DA", 0.55)), solid("FxPressWashBrush", a("#8F7248", 0.18)), translate("FxPressTransform", 1),
    # Wax-seal red primary.
    lin("FxPrimaryBrush", [("#B54040", 0), ("#8A2222", 1)]), solid("FxPrimaryEdgeBrush", "#5E1515"),
    solid("FxPrimaryInkBrush", "#FBEFD0"), shadow("FxPrimaryShadow", "#3B2A17", 3, 1, 0.35),
    shadow("FxPrimaryHoverEffect", "#B54040", 10, 0, 0.5),
    solid("FxRowBrush", a("#FFFFFF", 0.0)), solid("FxRowEdgeBrush", "#00000000"), corner("FxRowRadius", "3"),
    solid("FxRowHoverBrush", a("#FFF6DA", 0.75)),
    solid("FxSelectedBrush", "#E2CC93"), solid("FxSelectedEdgeBrush", "#9C2B2B"), thick("FxSelectedBorderThickness", "3,0,0,0"),
    solid("FxSelectedInkBrush", "#3B2A17"),
    solid("FxFieldBrush", "#FDF8E8"), solid("FxFieldEdgeBrush", "#A88A5A"), corner("FxFieldRadius", "3"),
    solid("FxFieldInkBrush", "#3B2A17"), solid("FxFieldFocusBrush", "#9C2B2B"),
    solid("FxPopupBrush", "#FBF4DC"), solid("FxPopupEdgeBrush", "#8F7248"), corner("FxPopupRadius", "4"),
    shadow("FxPopupShadow", "#3B2A17", 14, 4, 0.35),
    solid("FxCheckBrush", "#FDF8E8"), solid("FxCheckEdgeBrush", "#8F7248"), corner("FxCheckRadius", "2"),
    solid("FxCheckOnBrush", "#9C2B2B"), solid("FxCheckOnEdgeBrush", "#5E1515"), solid("FxCheckMarkBrush", "#FBEFD0"),
    solid("FxThumbBrush", "#B49A6A"), corner("FxThumbRadius", "3"),
    lin("ProgressFillBrush", [("#B54040", 0), ("#8A2222", 1)]),
    solid("TimerTextBrush", "#8E2424"), font("TimerFont", "Palatino Linotype"), weight("TimerFontWeight", "Bold"),
    shadow("TimerGlow", "#FFF6DA", 0, 1, 0.9, 270),
])

# ------------------------------------------------------------------ Earthen (Fantasy key; new art direction)
THEMES["Fantasy"] = dict(blurb="Earthen — a candlelit tavern: dark oak, worn leather, gold trim.", res=[
    *tokens("#1A120B", "#241A10", a("#2C2013", 0.85), "#3A2A18", "#F0E0C0", "#C3A878", "#8A7154", "#5A452C", "#362A1A",
            "#E2BC52", "#E08A2E", "#D15A78", "#E0663F", "#8DB06E", "Constantia, Palatino Linotype, Georgia", radius=6),
    weight("HeadingFontWeight", "Bold"),
    color("TitleBarColor", "#1A120B"), color("TitleBarTextColor", "#E8C55A"),
    image("WallpaperBrush", "earthen.jpg"),
    solid("SidebarBrush", a("#221810", 0.94)), solid("SidebarEdgeBrush", "#8A6D3B"),
    solid("ContentBrush", a("#1E150D", 0.90)), solid("ContentEdgeBrush", "#8A6D3B"),
    corner("PanelRadius", "8"), corner("PanelTopRadius", "7,7,0,0"),
    shadow("PanelShadow", "#000000", 26, 8, 0.65),
    lin("SidebarHeaderBrush", [("#3A2A18", 0), ("#241A10", 1)]), solid("SidebarHeaderEdgeBrush", "#8A6D3B"),
    solid("SidebarHeaderInkBrush", "#E8C55A"),
    lin("FxBtnBrush", [("#4A3420", 0), ("#2C1F12", 1)]), solid("FxBtnEdgeBrush", "#6B5230"),
    corner("FxBtnRadius", "6"), corner("FxTileRadius", "8"), solid("FxBtnInkBrush", "#F0E0C0"),
    solid("FxBtnSheenBrush", a("#FFE8B0", 0.12)), shadow("FxBtnShadow", "#000000", 4, 2, 0.5),
    shadow("FxBtnHoverEffect", "#D4AF37", 12, 0, 0.5),
    solid("FxHoverWashBrush", a("#FFD27A", 0.08)), solid("FxPressWashBrush", a("#000000", 0.25)), translate("FxPressTransform", 1),
    # Polished gold primary.
    lin("FxPrimaryBrush", [("#F5DA88", 0), ("#D4AF37", 0.5), ("#A88224", 1)]), solid("FxPrimaryEdgeBrush", "#5E4520"),
    solid("FxPrimaryInkBrush", "#241A10"), shadow("FxPrimaryShadow", "#D4AF37", 10, 0, 0.4),
    shadow("FxPrimaryHoverEffect", "#FFC850", 16, 0, 0.65),
    solid("FxRowBrush", a("#FFFFFF", 0.0)), solid("FxRowEdgeBrush", "#00000000"), corner("FxRowRadius", "5"),
    solid("FxRowHoverBrush", a("#D4AF37", 0.10)),
    lin("FxSelectedBrush", [(a("#D4AF37", 0.28), 0), (a("#8A6D3B", 0.22), 1)]), solid("FxSelectedEdgeBrush", "#D4AF37"),
    solid("FxSelectedInkBrush", "#FFF0CC"), shadow("FxSelectedGlow", "#D4AF37", 10, 0, 0.3),
    solid("FxFieldBrush", "#150E08"), solid("FxFieldEdgeBrush", "#5A452C"), corner("FxFieldRadius", "5"),
    solid("FxFieldInkBrush", "#F0E0C0"), solid("FxFieldFocusBrush", "#D4AF37"), shadow("FxFieldFocusGlow", "#D4AF37", 8, 0, 0.35),
    solid("FxPopupBrush", "#1E150D"), solid("FxPopupEdgeBrush", "#8A6D3B"), corner("FxPopupRadius", "6"),
    shadow("FxPopupShadow", "#000000", 20, 6, 0.6),
    solid("FxCheckBrush", "#150E08"), solid("FxCheckEdgeBrush", "#8A6D3B"),
    lin("FxCheckOnBrush", [("#F5DA88", 0), ("#D4AF37", 0.5), ("#A88224", 1)]), solid("FxCheckOnEdgeBrush", "#5E4520"),
    solid("FxCheckMarkBrush", "#241A10"),
    lin("FxThumbBrush", [("#6B5230", 0), ("#4A3420", 1)], "0,0", "1,0"),
    lin("ProgressFillBrush", [("#F5DA88", 0), ("#D4AF37", 0.5), ("#A88224", 1)]),
    solid("TimerTextBrush", "#EBC862"), font("TimerFont", "Constantia"), weight("TimerFontWeight", "Bold"),
    shadow("TimerGlow", "#FFA030", 22, 0, 0.55),
])

# ------------------------------------------------------------------ MacBook Light (Light key; new art direction)
THEMES["Light"] = dict(blurb="MacBook Light — a soft macOS desktop: frosted white windows, hairlines, system blue.", res=[
    *tokens("#ECECEE", "#F5F5F7", "#FFFFFF", "#F5F5F7", "#1D1D1F", "#6E6E73", "#8E8E93", a("#000000", 0.12), "#E5E5EA",
            "#0A64C8", "#B26A00", "#D6174A", "#C41E1E", "#1E7A35", "Segoe UI", radius=8),
    color("TitleBarColor", "#ECECEE"), color("TitleBarTextColor", "#1D1D1F"),
    image("WallpaperBrush", "mac.jpg"),
    thick("ShellPadding", "10"), thick("SidebarMargin", "0,0,10,0"),
    solid("SidebarBrush", a("#F2F2F5", 0.86)), solid("SidebarEdgeBrush", a("#000000", 0.08)),
    solid("ContentBrush", a("#FFFFFF", 0.92)), solid("ContentEdgeBrush", a("#000000", 0.08)),
    shadow("PanelShadow", "#000000", 30, 10, 0.12),
    solid("SidebarHeaderEdgeBrush", "#00000000"), thick("SidebarHeaderBorderThickness", "0"),
    solid("FxBtnBrush", "#FFFFFF"), solid("FxBtnEdgeBrush", a("#000000", 0.12)), corner("FxBtnRadius", "6"),
    corner("FxTileRadius", "12"), solid("FxBtnInkBrush", "#1D1D1F"),
    shadow("FxBtnShadow", "#000000", 2, 1, 0.10), shadow("FxBtnHoverEffect", "#000000", 4, 1, 0.14),
    solid("FxHoverWashBrush", a("#000000", 0.03)), solid("FxPressWashBrush", a("#000000", 0.08)),
    lin("FxPrimaryBrush", [("#2F86E8", 0), ("#0A64C8", 1)]), solid("FxPrimaryEdgeBrush", "#0956AC"),
    solid("FxPrimaryInkBrush", "#FFFFFF"), shadow("FxPrimaryShadow", "#0A64C8", 3, 1, 0.25),
    shadow("FxPrimaryHoverEffect", "#0A64C8", 6, 1, 0.35),
    solid("FxRowBrush", a("#FFFFFF", 0.0)), solid("FxRowEdgeBrush", "#00000000"), corner("FxRowRadius", "6"),
    solid("FxRowHoverBrush", a("#000000", 0.05)),
    solid("FxSelectedBrush", "#0A64C8"), solid("FxSelectedEdgeBrush", "#0A64C8"), solid("FxSelectedInkBrush", "#FFFFFF"),
    solid("FxFieldBrush", "#FFFFFF"), solid("FxFieldEdgeBrush", a("#000000", 0.15)), corner("FxFieldRadius", "6"),
    solid("FxFieldInkBrush", "#1D1D1F"), solid("FxFieldFocusBrush", "#0A64C8"), shadow("FxFieldFocusGlow", "#0A64C8", 6, 0, 0.45),
    solid("FxPopupBrush", "#FFFFFF"), solid("FxPopupEdgeBrush", a("#000000", 0.10)), corner("FxPopupRadius", "8"),
    shadow("FxPopupShadow", "#000000", 20, 6, 0.18),
    solid("FxCheckBrush", "#FFFFFF"), solid("FxCheckEdgeBrush", a("#000000", 0.25)), corner("FxCheckRadius", "4"),
    solid("FxCheckOnBrush", "#0A64C8"), solid("FxCheckOnEdgeBrush", "#0A64C8"), solid("FxCheckMarkBrush", "#FFFFFF"),
    solid("FxThumbBrush", a("#000000", 0.35)),
    solid("ProgressFillBrush", "#0A64C8"),
    font("TimerFont", "Segoe UI"), weight("TimerFontWeight", "Light"),
])

# ------------------------------------------------------------------ Midnight (new art direction)
THEMES["Midnight"] = dict(blurb="Midnight — a quiet night sky with a teal aurora; dark glass, nothing loud.", res=[
    *tokens("#0A0B0D", "#0F1014", a("#16181E", 0.85), "#1C1F27", "#ECE9E2", "#9A9DA6", "#63656F", "#262A33", "#1A1B21",
            "#4FD1C5", "#F2A65A", "#D46FB0", "#E2665B", "#4ADE80", "Segoe UI", radius=8),
    color("TitleBarColor", "#07090E"), color("TitleBarTextColor", "#ECE9E2"),
    image("WallpaperBrush", "midnight.jpg"),
    solid("SidebarBrush", a("#0D0F14", 0.88)), solid("SidebarEdgeBrush", a("#4FD1C5", 0.14)),
    solid("ContentBrush", a("#0B0D12", 0.84)), solid("ContentEdgeBrush", a("#4FD1C5", 0.14)),
    shadow("PanelShadow", "#000000", 30, 10, 0.6),
    solid("SidebarHeaderEdgeBrush", a("#4FD1C5", 0.12)), solid("SidebarHeaderInkBrush", "#ECE9E2"),
    solid("FxBtnBrush", a("#1A1D24", 0.92)), solid("FxBtnEdgeBrush", "#2A2E38"), corner("FxBtnRadius", "8"),
    corner("FxTileRadius", "12"), solid("FxBtnInkBrush", "#ECE9E2"), solid("FxBtnSheenBrush", a("#FFFFFF", 0.05)),
    shadow("FxBtnHoverEffect", "#4FD1C5", 12, 0, 0.35),
    solid("FxHoverWashBrush", a("#4FD1C5", 0.07)), solid("FxPressWashBrush", a("#000000", 0.25)),
    solid("FxPrimaryBrush", "#4FD1C5"), solid("FxPrimaryEdgeBrush", "#4FD1C5"), solid("FxPrimaryInkBrush", "#06120F"),
    shadow("FxPrimaryShadow", "#4FD1C5", 10, 0, 0.3), shadow("FxPrimaryHoverEffect", "#4FD1C5", 16, 0, 0.55),
    solid("FxRowBrush", a("#FFFFFF", 0.0)), solid("FxRowEdgeBrush", "#00000000"), corner("FxRowRadius", "7"),
    solid("FxRowHoverBrush", a("#FFFFFF", 0.04)),
    solid("FxSelectedBrush", a("#4FD1C5", 0.14)), solid("FxSelectedEdgeBrush", a("#4FD1C5", 0.6)),
    solid("FxSelectedInkBrush", "#ECE9E2"),
    solid("FxFieldBrush", "#0F1116"), solid("FxFieldEdgeBrush", "#262A33"), corner("FxFieldRadius", "8"),
    solid("FxFieldInkBrush", "#ECE9E2"), solid("FxFieldFocusBrush", "#4FD1C5"), shadow("FxFieldFocusGlow", "#4FD1C5", 8, 0, 0.3),
    solid("FxPopupBrush", "#12141A"), solid("FxPopupEdgeBrush", "#262A33"), corner("FxPopupRadius", "8"),
    shadow("FxPopupShadow", "#000000", 22, 8, 0.6),
    solid("FxCheckBrush", "#0F1116"), solid("FxCheckEdgeBrush", "#3A3F4A"),
    solid("FxCheckOnBrush", "#4FD1C5"), solid("FxCheckOnEdgeBrush", "#4FD1C5"), solid("FxCheckMarkBrush", "#06120F"),
    solid("FxThumbBrush", "#3A3F4A"),
    lin("ProgressFillBrush", [("#4FD1C5", 0), ("#3C9EC8", 1)], "0,0", "1,0"),
    solid("TimerTextBrush", "#4FD1C5"), font("TimerFont", "Segoe UI"), weight("TimerFontWeight", "Light"),
    shadow("TimerGlow", "#4FD1C5", 20, 0, 0.45),
])


def write(name, spec):
    body = "\n    ".join(spec["res"])
    xml = f"""<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                     xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                     xmlns:sys="clr-namespace:System;assembly=mscorlib">

    <!-- {spec['blurb']}
         Pairs with Styles/ControlsFx.xaml. Generated by tools/themes/gen_fx_themes.py — edit the
         spec there and re-run rather than editing this file by hand. -->

    {body}
</ResourceDictionary>
"""
    with open(os.path.join(OUT, f"{name}.xaml"), "w", encoding="utf-8") as f:
        f.write(xml)


for name, spec in THEMES.items():
    write(name, spec)
    print(f"{name}: {len(spec['res'])} resources")
