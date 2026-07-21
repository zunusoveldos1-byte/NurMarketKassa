from pathlib import Path
import re
ROOT = Path(r"C:\Users\User\Desktop\NurCrmPosKassa-master\NurCrmPosKassa-master\src\NurMarketKassa.Avalonia\Views.axaml")
TARGETS = ["FinanceWindow.axaml","SalesWindow.axaml","PosSettingsWindow.axaml","Dialogs/ReturnSaleDialog.axaml","Dialogs/CashOperationsDialog.axaml","Dialogs/FrmKeyboard.axaml","Dialogs/FinanceDateRangeDialog.axaml"]
def fix(text):
    text = re.sub(r"\s*<DataGrid(?:Text|Bound)Column\.ElementStyle>.*?</DataGrid(?:Text|Bound)Column\.ElementStyle>", "", text, flags=re.DOTALL)
    for a in [r'\s+CanUserAddRows="[^"]*"', r'\s+CanUserDeleteRows="[^"]*"', r'\s+CanUserResizeRows="[^"]*"', r'\s+EnableRowVirtualization="[^"]*"', r'\s+EnableColumnVirtualization="[^"]*"', r'\s+ScrollViewer\.CanContentScroll="[^"]*"', r'\s+ScrollViewer\.PanningMode="[^"]*"', r'\s+Stylus\.IsFlicksEnabled="[^"]*"', r'\s+IsManipulationEnabled="[^"]*"', r'\s+ManipulationBoundaryFeedback="[^"]*"', r'\s+PopupAnimation="[^"]*"', r'\s+ElementStyle="\{StaticResource [^}]+\}"']:
        text = re.sub(a, "", text)
    text = re.sub(r"\s*<Setter Property=\"CellStyle\">.*?</Setter>", "", text, flags=re.DOTALL)
    text = re.sub(r"\s*<Setter Property=\"RowStyle\">.*?</Setter>", "", text, flags=re.DOTALL)
    text = re.sub(r'Visibility="Collapsed"', 'IsVisible="False"', text)
    text = re.sub(r"\s*<EventSetter[^>]*>.*?</EventSetter>\s*", "", text, flags=re.DOTALL)
    text = re.sub(r"\s*<Border\.Effect>\s*</Border\.Effect>\s*", "\n", text)
    if "<BoolToIsVisibleConverter x:Key=" in text and "xmlns:conv" not in text[:900]:
        text = text.replace("<Window ", '<Window xmlns:conv="using:NurMarketKassa.AvaloniaHost.Converters" ', 1)
        text = text.replace("<BoolToIsVisibleConverter", "<conv:BoolToIsVisibleConverter")
    return text
for rel in TARGETS:
    p = ROOT / rel
    if p.exists():
        o = p.read_text(encoding="utf-8")
        u = fix(o)
        if u != o:
            p.write_text(u, encoding="utf-8", newline="\n")
            print("fixed", rel)
