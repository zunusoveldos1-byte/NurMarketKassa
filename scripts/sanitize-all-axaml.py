from pathlib import Path
import re
root = Path(r"C:\Users\User\Desktop\NurCrmPosKassa-master\NurCrmPosKassa-master\src\NurMarketKassa.Avalonia\Views.axaml")

def strip_block(text, tag):
    pat = re.compile(rf"<{tag}(\s[^>]*)?>", re.I)
    close = re.compile(rf"</{tag}\s*>", re.I)
    while True:
        m = pat.search(text)
        if not m: break
        start, pos, depth = m.start(), m.end(), 1
        while depth and pos < len(text):
            nm, cm = pat.search(text, pos), close.search(text, pos)
            if not cm:
                text = text[:start] + text[m.end():]; break
            if nm and nm.start() < cm.start():
                depth += 1; pos = nm.end()
            else:
                depth -= 1; pos = cm.end()
                if depth == 0:
                    text = text[:start] + text[pos:]
        else:
            break
    return text

def fix(text):
    for tag in ("EventSetter","SelectiveScrollingGrid"):
        text = strip_block(text, tag)
        text = re.sub(rf"<{tag}[^/]*/>", "", text, flags=re.I)
    # Remove WPF-only DataGrid attrs
    for a in [
        r'\s+CanUserAddRows="[^"]*"', r'\s+CanUserDeleteRows="[^"]*"',
        r'\s+ColumnHeaderStyle="[^"]*"', r'\s+CellStyle="[^"]*"', r'\s+RowStyle="[^"]*"',
        r'\s+ElementStyle="[^"]*"', r'\s+EditingElementStyle="[^"]*"',
        r'\s+EnableRowVirtualization="[^"]*"', r'\s+EnableColumnVirtualization="[^"]*"',
        r'\s+ScrollViewer\.[A-Za-z]+="[^"]*"', r'\s+IsManipulationEnabled="[^"]*"',
        r'\s+Stylus\.[A-Za-z]+="[^"]*"', r'\s+StaysOpen="[^"]*"',
        r'\s+BasedOn="[^"]*"', r'\s+HorizontalContentAlignment="[^"]*"',
        r'\s+Focusable="[^"]*"', r'\s+Width="Auto"', r'\s+Height="Auto"',
        r'\s+FontSize="Auto"',
    ]:
        text = re.sub(a, "", text)
    # ToolTip -> ToolTip.Tip (Avalonia)
    text = re.sub(r'\sToolTip="', ' ToolTip.Tip="', text)
    text = text.replace("FrameworkElement", "Control")
    text = text.replace("DatePickerTextBox", "TextBox")
    text = text.replace("ListView", "DataGrid")
    text = text.replace("GridView", "DataGrid")
    # Broken selectors Type. or Type.{x:Type
    text = re.sub(r'Selector="([^".]+)\.(?:\{[^}]+\}|"?)', r'Selector="\1"', text)
    text = re.sub(r'Selector="([^"]+)\."', r'Selector="\1"', text)
    text = re.sub(r'Visibility="Collapsed"', 'IsVisible="False"', text)
    text = re.sub(r'Visibility="Hidden"', 'IsVisible="False"', text)
    text = re.sub(r'Visibility="Visible"', 'IsVisible="True"', text)
    return text

n=0
for p in root.rglob("*.axaml"):
    if not p.is_file(): continue
    o = p.read_text(encoding="utf-8", errors="replace")
    u = fix(o)
    if u != o:
        p.write_text(u, encoding="utf-8", newline="\n"); n += 1; print(p.relative_to(root))
print(f"sanitized {n}")