"""Generates the Mac app's themes (BijouHub.Mac/Themes/*.axaml) from the Windows themes.

    python tools/themes/gen_mac_themes.py

Reads each Themes/*.xaml (the source of truth) for its palette, fonts, radii, wallpaper and
the "fx" materials (glass panels, glossy buttons), maps Windows-only fonts to ones a Mac has,
and writes an Avalonia ResourceDictionary with the keys BijouHub.Mac's styles use. Rerun after
changing a Windows theme; don't hand-edit the output.
"""
import os
import re
import shutil
import xml.etree.ElementTree as ET

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SRC = os.path.join(ROOT, "Themes")
OUT = os.path.join(ROOT, "BijouHub.Mac", "Themes")
ASSETS_SRC = os.path.join(ROOT, "Assets", "Themes")
ASSETS_OUT = os.path.join(ROOT, "BijouHub.Mac", "Assets", "Themes")

X = "{http://schemas.microsoft.com/winfx/2006/xaml}"
P = "{http://schemas.microsoft.com/winfx/2006/xaml/presentation}"

# Kept in step with Services/ThemeService.LightThemes on Windows.
LIGHT = {"Light", "Aero", "Aqua", "Dos", "Vaporwave", "Y2kChrome", "DoodleClub", "Fable"}

INTER = "fonts:Inter#Inter"
FONT_MAP = {
    "Segoe UI": INTER,
    "Bahnschrift": "Avenir Next Condensed",
    "Corbel": "Avenir Next",
    "Candara": "Avenir Next",
    "Constantia": "Palatino",
    "Palatino Linotype": "Palatino",
    "Book Antiqua": "Palatino",
    "Georgia": "Georgia",
    "Comic Sans MS": "Comic Sans MS",
    "Tahoma": "Tahoma",
    "MS Sans Serif": "Tahoma",
    "Verdana": "Verdana",
    "Trebuchet MS": "Trebuchet MS",
    "Consolas": "Menlo",
    "Lucida Console": "Monaco",
}
BUNDLED = {
    "IBM Plex Mono": "avares://BijouHub.Mac/Assets/Fonts#IBM Plex Mono",
    "Rajdhani SemiBold": "avares://BijouHub.Mac/Assets/Fonts#Rajdhani SemiBold",
}

TOKENS = ["Bg", "Panel", "Card", "CardHover", "Text", "MutedText", "FaintText", "Border", "SoftBorder",
          "Accent", "Hazard", "Pink", "Danger", "Success"]


def mac_font(value):
    if "pack://" in value:  # a bundled font; its URI has commas of its own
        return f'{BUNDLED.get(value.split("#", 1)[1], INTER)}, {INTER}'
    families = []
    for part in [p.strip() for p in value.split(",")]:
        if part.startswith("pack://"):
            name = part.split("#", 1)[1]
            families.append(BUNDLED.get(name, INTER))
        else:
            families.append(FONT_MAP.get(part, part))
    if INTER not in families:
        families.append(INTER)
    seen = []
    for f in families:
        if f not in seen:
            seen.append(f)
    return ", ".join(seen)


def luminance(hex_color):
    h = hex_color.lstrip("#")[-6:]
    r, g, b = (int(h[i:i + 2], 16) / 255 for i in (0, 2, 4))
    lin = lambda c: c / 12.92 if c <= 0.03928 else ((c + 0.055) / 1.055) ** 2.4
    return 0.2126 * lin(r) + 0.7152 * lin(g) + 0.0722 * lin(b)


def brush_xml(el, key):
    """Re-emits a WPF SolidColorBrush / LinearGradientBrush as Avalonia XAML."""
    tag = el.tag.replace(P, "")
    if tag == "SolidColorBrush":
        return f'    <SolidColorBrush x:Key="{key}" Color="{el.get("Color")}"/>'
    if tag == "LinearGradientBrush":
        sp = el.get("StartPoint", "0,0").split(",")
        ep = el.get("EndPoint", "0,1").split(",")
        rel = lambda v: f"{float(v) * 100:g}%"
        stops = "".join(f'<GradientStop Color="{s.get("Color")}" Offset="{s.get("Offset", "0")}"/>' for s in el.iter(P + "GradientStop"))
        return (f'    <LinearGradientBrush x:Key="{key}" StartPoint="{rel(sp[0])},{rel(sp[1])}" EndPoint="{rel(ep[0])},{rel(ep[1])}">'
                f"{stops}</LinearGradientBrush>")
    return None


def convert(path):
    name = os.path.splitext(os.path.basename(path))[0]
    root = ET.parse(path).getroot()
    by_key = {el.get(X + "Key"): el for el in root.iter() if el.get(X + "Key")}
    color = lambda k: by_key[k + "Color"].text.strip()

    lines = [
        '<ResourceDictionary xmlns="https://github.com/avaloniaui"',
        '                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">',
        f"    <!-- Generated from Themes/{name}.xaml by tools/themes/gen_mac_themes.py — edit the Windows theme and rerun. -->",
        f'    <x:String x:Key="ThemeVariantName">{"Light" if name in LIGHT else "Dark"}</x:String>',
    ]
    for t in TOKENS:
        c = color(t)
        lines.append(f'    <Color x:Key="{t}Color">{c}</Color>')
        lines.append(f'    <SolidColorBrush x:Key="{t}Brush" Color="{c}"/>')

    heading = by_key.get("HeadingFont")
    body = by_key.get("BodyFont")
    timer = by_key.get("TimerFont")
    lines.append(f'    <FontFamily x:Key="HeadingFont">{mac_font(heading.text if heading is not None else "Segoe UI")}</FontFamily>')
    lines.append(f'    <FontFamily x:Key="BodyFont">{mac_font(body.text if body is not None else "Segoe UI")}</FontFamily>')
    lines.append(f'    <FontFamily x:Key="TimerFont">{mac_font(timer.text if timer is not None else "Segoe UI")}</FontFamily>')

    radius = by_key.get("CornerRadiusBase")
    r = radius.text.strip() if radius is not None else "8"
    lines.append(f'    <CornerRadius x:Key="Radius">{r}</CornerRadius>')
    panel_radius = by_key.get("PanelRadius")
    lines.append(f'    <CornerRadius x:Key="PanelRadius">{panel_radius.text.strip() if panel_radius is not None else "0"}</CornerRadius>')

    # Wallpaper: the fx themes' image (copied into the Mac app's assets), else the plain background.
    wall = by_key.get("WallpaperBrush")
    is_fx = wall is not None and wall.tag == P + "ImageBrush"
    if is_fx:
        file = wall.get("ImageSource").rsplit("/", 1)[1]
        source = f"avares://BijouHub.Mac/Assets/Themes/{file}"
        if wall.get("TileMode") == "Tile":
            size = wall.get("Viewport", "0,0,400,400").split(",")[2]
            lines.append(f'    <ImageBrush x:Key="WallpaperBrush" Source="{source}" TileMode="Tile" Stretch="Fill" '
                         f'DestinationRect="0,0,{size},{size}"/>')
        else:
            align = wall.get("AlignmentY", "Center")
            lines.append(f'    <ImageBrush x:Key="WallpaperBrush" Source="{source}" Stretch="UniformToFill" AlignmentY="{align}"/>')
    else:
        lines.append(f'    <SolidColorBrush x:Key="WallpaperBrush" Color="{color("Bg")}"/>')

    # Panels: fx themes float translucent glass panels over the wallpaper; flat ones sit edge to edge.
    def emit(key, src_key, fallback):
        el = by_key.get(src_key) if src_key else None
        out = brush_xml(el, key) if el is not None else None
        lines.append(out or f'    <SolidColorBrush x:Key="{key}" Color="{fallback}"/>')

    emit("SidebarBrush", "SidebarBrush", color("Panel"))
    emit("ContentBrush", "ContentBrush", "#00000000")
    emit("PanelEdgeBrush", "SidebarEdgeBrush", color("Border"))
    lines.append(f'    <Thickness x:Key="ShellPadding">{"8" if is_fx else "0"}</Thickness>')
    lines.append(f'    <Thickness x:Key="PanelGap">{"0,0,8,0" if is_fx else "0"}</Thickness>')
    lines.append(f'    <Thickness x:Key="PanelEdge">{"1" if is_fx else "0,0,1,0"}</Thickness>')
    lines.append(f'    <BoxShadows x:Key="PanelShadow">{"0 8 28 0 #66000000" if is_fx else "0 0 0 0 #00000000"}</BoxShadows>')

    # Buttons: fx themes' glossy materials, or calm cards and a solid accent.
    accent = color("Accent")
    accent_ink = "#0B0D12" if luminance(accent) > 0.42 else "#FFFFFF"
    emit("ButtonBrush", "FxBtnBrush" if is_fx else None, color("Card"))
    emit("ButtonEdgeBrush", "FxBtnEdgeBrush" if is_fx else None, color("Border"))
    emit("ButtonInkBrush", "FxBtnInkBrush" if is_fx else None, color("Text"))
    emit("PrimaryBrush", "FxPrimaryBrush" if is_fx else None, accent)
    emit("PrimaryEdgeBrush", "FxPrimaryEdgeBrush" if is_fx else None, accent)
    emit("PrimaryInkBrush", "FxPrimaryInkBrush" if is_fx else None, accent_ink)

    # Avalonia's Fluent controls read these keys, so text boxes, dropdowns, menus, tooltips and
    # list rows all wear the theme without custom templates.
    def alias(new_key, src_key):
        for line in lines:
            if f'x:Key="{src_key}"' in line:
                lines.append(line.replace(f'x:Key="{src_key}"', f'x:Key="{new_key}"', 1))
                return
        raise KeyError(src_key)

    fluent = {
        "ButtonBackground": "ButtonBrush", "ButtonBackgroundPointerOver": "ButtonBrush", "ButtonBackgroundPressed": "ButtonBrush",
        "ButtonBackgroundDisabled": "ButtonBrush", "ButtonForeground": "ButtonInkBrush", "ButtonForegroundPointerOver": "ButtonInkBrush",
        "ButtonForegroundPressed": "ButtonInkBrush", "ButtonForegroundDisabled": "FaintTextBrush", "ButtonBorderBrush": "ButtonEdgeBrush",
        "ButtonBorderBrushPointerOver": "AccentBrush", "ButtonBorderBrushPressed": "AccentBrush", "ButtonBorderBrushDisabled": "SoftBorderBrush",
        "TextControlForeground": "TextBrush", "TextControlForegroundPointerOver": "TextBrush", "TextControlForegroundFocused": "TextBrush",
        "TextControlBackground": "CardBrush", "TextControlBackgroundPointerOver": "CardBrush", "TextControlBackgroundFocused": "CardBrush",
        "TextControlBorderBrush": "BorderBrush", "TextControlBorderBrushPointerOver": "BorderBrush", "TextControlBorderBrushFocused": "AccentBrush",
        "TextControlPlaceholderForeground": "FaintTextBrush", "TextControlPlaceholderForegroundPointerOver": "FaintTextBrush",
        "TextControlPlaceholderForegroundFocused": "FaintTextBrush",
        "ComboBoxBackground": "CardBrush", "ComboBoxBackgroundPointerOver": "CardHoverBrush", "ComboBoxBackgroundPressed": "CardHoverBrush",
        "ComboBoxBackgroundFocused": "CardBrush", "ComboBoxBorderBrush": "BorderBrush", "ComboBoxBorderBrushPointerOver": "BorderBrush",
        "ComboBoxBorderBrushPressed": "AccentBrush", "ComboBoxForeground": "TextBrush", "ComboBoxPlaceHolderForeground": "FaintTextBrush",
        "ComboBoxDropDownGlyphForeground": "MutedTextBrush", "ComboBoxDropDownBackground": "PanelBrush", "ComboBoxDropDownBorderBrush": "BorderBrush",
        "ComboBoxItemForeground": "TextBrush", "ComboBoxItemForegroundSelected": "TextBrush", "ComboBoxItemBackgroundPointerOver": "CardHoverBrush",
        "ComboBoxItemBackgroundSelected": "CardHoverBrush", "ComboBoxItemBackgroundSelectedPointerOver": "CardHoverBrush",
        "MenuFlyoutPresenterBackground": "PanelBrush", "MenuFlyoutPresenterBorderBrush": "BorderBrush", "MenuFlyoutItemForeground": "TextBrush",
        "MenuFlyoutItemBackgroundPointerOver": "CardHoverBrush", "MenuFlyoutSubItemForeground": "TextBrush",
        "ToolTipBackground": "PanelBrush", "ToolTipForeground": "TextBrush", "ToolTipBorderBrush": "BorderBrush",
        "ListBoxItemBackgroundPointerOver": "CardHoverBrush", "ListBoxItemBackgroundSelected": "CardHoverBrush",
        "ListBoxItemBackgroundSelectedPointerOver": "CardHoverBrush", "ListBoxItemBackgroundPressed": "CardHoverBrush",
        "ListBoxItemForeground": "TextBrush", "ListBoxItemForegroundSelected": "TextBrush", "ListBoxItemForegroundPointerOver": "TextBrush",
        "CheckBoxForegroundUnchecked": "TextBrush", "CheckBoxForegroundChecked": "TextBrush", "CheckBoxForegroundUncheckedPointerOver": "TextBrush",
        "CheckBoxForegroundCheckedPointerOver": "TextBrush", "CheckBoxCheckBackgroundStrokeUnchecked": "BorderBrush",
        "CheckBoxCheckBackgroundStrokeUncheckedPointerOver": "AccentBrush",
    }
    for key, src in fluent.items():
        alias(key, src)
    lines.append(f'    <CornerRadius x:Key="ControlCornerRadius">{min(int(float(r.split(",")[0])), 10)}</CornerRadius>')
    lines.append(f'    <CornerRadius x:Key="OverlayCornerRadius">{min(int(float(r.split(",")[0])), 10)}</CornerRadius>')

    # Fluent's own controls (checkboxes, selection, focus) read these.
    for suffix in ["", "Light1", "Light2", "Light3", "Dark1", "Dark2", "Dark3"]:
        lines.append(f'    <Color x:Key="SystemAccentColor{suffix}">{accent}</Color>')

    prefix = by_key.get("SectionPrefix")
    # SectionPrefix ("// " in NervClassic) is a Windows-only flourish; the Mac layout has no use for it.
    lines.append("</ResourceDictionary>")

    with open(os.path.join(OUT, name + ".axaml"), "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")
    return name, is_fx


def main():
    os.makedirs(OUT, exist_ok=True)
    os.makedirs(ASSETS_OUT, exist_ok=True)
    for file in os.listdir(ASSETS_SRC):
        if re.search(r"\.(jpg|png)$", file):
            shutil.copyfile(os.path.join(ASSETS_SRC, file), os.path.join(ASSETS_OUT, file))
    for file in sorted(os.listdir(SRC)):
        if file.endswith(".xaml"):
            name, fx = convert(os.path.join(SRC, file))
            print(f"{name:12} {'fx' if fx else 'flat'}")


if __name__ == "__main__":
    main()
