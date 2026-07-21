#!/usr/bin/env python3
"""Sanitize seven Avalonia AXAML files: strip WPF-only DataGrid/style APIs."""
from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1] / "src" / "NurMarketKassa.Avalonia" / "Views.axaml"

TARGETS = [
    ROOT / "FinanceWindow.axaml",
    ROOT / "SalesWindow.axaml",
    ROOT / "PosSettingsWindow.axaml",
    ROOT / "Dialogs" / "ReturnSaleDialog.axaml",
    ROOT / "Dialogs" / "CashOperationsDialog.axaml",
    ROOT / "Dialogs" / "FrmKeyboard.axaml",
    ROOT / "Dialogs" / "FinanceDateRangeDialog.axaml",
]

FORBIDDEN = [
    "CanUserAddRows",
    "EventSetter",
    "SelectiveScrollingGrid",
    "ElementStyle",
    "BasedOn=",
]

DATAGRID_TEMPLATE_STYLE = re.compile(
    r"\s*<Style\s+Selector=\"DataGrid(?:Cell|Row)\.[^\"]*\"[^>]*>.*?</Style>\s*",
    re.DOTALL | re.IGNORECASE,
)

COLUMN_HEADER_STYLE_SETTER = re.compile(
    r"\s*<Setter\s+Property=\"(?:DataGrid\.)?ColumnHeaderStyle\"[^>]*>\s*"
    r"<Setter\.Value>.*?</Setter\.Value>\s*</Setter>\s*",
    re.DOTALL | re.IGNORECASE,
)

GRIDVIEW_HEADER_STYLE = re.compile(
    r"\s*<Style\s+Selector=\"GridViewColumnHeader\"[^>]*>.*?</Style>\s*",
    re.DOTALL | re.IGNORECASE,
)

LISTVIEW_GRIDVIEW = re.compile(
    r"<ListView\s+x:Name=\"SalesListView\"(?P<attrs>[^>]*)>\s*"
    r"<ListView\.View>\s*<GridView>.*?</GridView>\s*</ListView\.View>\s*</ListView>",
    re.DOTALL | re.IGNORECASE,
)

DATAGRID_REPLACEMENT = (
    '<DataGrid x:Name="SalesListView"{attrs}\n'
    '                          AutoGenerateColumns="False"\n'
    '                          IsReadOnly="True">\n'
    '                                    <DataGrid.Columns>\n'
    '                                        <DataGridTextColumn Header="\u041d\u043e\u043c\u0435\u0440 \u0447\u0435\u043a\u0430"\n'
    '                                            Binding="{Binding SaleId}" />\n'
    '                                        <DataGridTextColumn Header="\u0414\u0430\u0442\u0430"\n'
    '                                            Binding="{Binding SaleDate, StringFormat=\'dd.MM.yyyy HH:mm\'}" />\n'
    '                                        <DataGridTextColumn Header="\u0421\u0443\u043c\u043c\u0430"\n'
    '                                            Binding="{Binding TotalAmount, StringFormat=\'{}{0:F2} \u0441\u043e\u043c\'}" />\n'
    '                                        <DataGridTemplateColumn Header="" Width="120">\n'
    '                                            <DataGridTemplateColumn.CellTemplate>\n'
    '                                                <DataTemplate>\n'
    '                                                    <Button Content="\u0412\u044b\u0431\u0440\u0430\u0442\u044c"\n'
    '                                                Classes="TouchPrimaryButton"\n'
    '                                                MinWidth="100"\n'
    '                                                Padding="10,8,10,8"\n'
    '                                                Tag="{Binding SaleId}"\n'
    '                                                Click="SelectSale_Click" />\n'
    '                                                </DataTemplate>\n'
    '                                            </DataGridTemplateColumn.CellTemplate>\n'
    '                                        </DataGridTemplateColumn>\n'
    '                                    </DataGrid.Columns>\n'
    '                                </DataGrid>'
)


def note(report: list[str], label: str) -> None:
    if label not in report:
        report.append(label)


def strip_lines_matching(text: str, pattern: str, label: str, report: list[str]) -> str:
    rx = re.compile(pattern, re.IGNORECASE | re.MULTILINE)
    if rx.search(text):
        note(report, label)
        return rx.sub("", text)
    return text


def fix_selector_xtype(text: str, report: list[str]) -> str:
    def fix_dotted(m: re.Match[str]) -> str:
        note(report, f"Selector {m.group(1)}.{{x:Type}} -> {m.group(1)}")
        return f'Selector="{m.group(1)}"'

    text = re.sub(
        r'Selector="([A-Za-z0-9_]+)\.\{x:Type\s+([^}]+)\}"',
        fix_dotted,
        text,
    )

    def fix_bare(m: re.Match[str]) -> str:
        type_name = m.group(1).strip()
        if type_name.startswith("{x:Type "):
            type_name = type_name[len("{x:Type ") :].rstrip("}")
        if ":" in type_name:
            type_name = type_name.split(":", 1)[1]
        note(report, f"Selector x:Type -> {type_name}")
        return f'Selector="{type_name}"'

    text = re.sub(r'Selector="\{x:Type\s+([^}]+)\}"', fix_bare, text)
    text = re.sub(
        r'Selector="([A-Za-z0-9_]+)\."',
        lambda m: (note(report, f'Selector {m.group(1)}. -> {m.group(1)}'), f'Selector="{m.group(1)}"')[1],
        text,
    )
    return text


def sanitize(text: str) -> tuple[str, list[str]]:
    report: list[str] = []

    if DATAGRID_TEMPLATE_STYLE.search(text):
        note(report, "DataGridCell/DataGridRow custom ControlTemplate Style blocks")
        text = DATAGRID_TEMPLATE_STYLE.sub("\n", text)

    if COLUMN_HEADER_STYLE_SETTER.search(text):
        note(report, "ColumnHeaderStyle setter blocks")
        text = COLUMN_HEADER_STYLE_SETTER.sub("\n", text)

    if GRIDVIEW_HEADER_STYLE.search(text):
        note(report, "GridViewColumnHeader Style block (EventSetter)")
        text = GRIDVIEW_HEADER_STYLE.sub("\n", text)

    if LISTVIEW_GRIDVIEW.search(text):
        note(report, "ListView+GridView -> DataGrid")
        text = LISTVIEW_GRIDVIEW.sub(
            lambda m: DATAGRID_REPLACEMENT.replace("{attrs}", m.group("attrs")),
            text,
        )

    text = strip_lines_matching(text, r"^\s*<EventSetter\b[^>]*/>\s*$", "EventSetter elements", report)
    text = strip_lines_matching(text, r"^\s*<EventSetter\b[^>]*>.*?</EventSetter>\s*$", "EventSetter elements", report)

    for prop in ("CanUserAddRows", "CanUserDeleteRows"):
        text = strip_lines_matching(
            text,
            rf'^\s*<Setter\s+Property="{prop}"\s+Value="[^"]*"\s*/>\s*$',
            f"{prop} setters",
            report,
        )

    for prop in ("ElementStyle", "CellStyle", "RowStyle"):
        text = strip_lines_matching(text, rf'\s*{prop}="[^"]*"', f"{prop} attributes", report)
        text = strip_lines_matching(text, rf'^\s*<Setter\s+Property="{prop}"[^>]*/>\s*$', f"{prop} setters", report)

    if re.search(r"\bBasedOn=", text):
        note(report, "BasedOn= attributes on Style")
        text = re.sub(r'\s+BasedOn="\{[^"]*\}"', "", text)

    if re.search(r"<ListView\b", text, re.IGNORECASE):
        note(report, "ListView -> ListBox")
        text = re.sub(r"<ListView\b", "<ListBox", text, flags=re.IGNORECASE)
        text = re.sub(r"</ListView>", "</ListBox>", text, flags=re.IGNORECASE)
        text = re.sub(r"<ListView\.", "<ListBox.", text)
        if re.search(r"<GridView\b", text, re.IGNORECASE):
            note(report, "GridView blocks removed")
            text = re.sub(
                r"\s*<ListBox\.View>\s*<GridView>.*?</GridView>\s*</ListBox\.View>\s*",
                "\n",
                text,
                flags=re.DOTALL | re.IGNORECASE,
            )

    if re.search(r"\bVisibility=", text):
        note(report, "Visibility= -> IsVisible=")
        text = re.sub(r"\bVisibility=", "IsVisible=", text)

    if re.search(r'(?:Width|FontSize|Height|MinWidth|MaxWidth)="Auto"', text):
        note(report, 'Width/FontSize/Height="Auto" attributes removed')
        text = re.sub(r'\s+(?:Width|FontSize|Height|MinWidth|MaxWidth)="Auto"', "", text)

    if re.search(r"<SelectiveScrollingGrid\b", text, re.IGNORECASE):
        note(report, "SelectiveScrollingGrid elements")
        text = re.sub(
            r"<SelectiveScrollingGrid\b[^>]*>.*?</SelectiveScrollingGrid>",
            "",
            text,
            flags=re.DOTALL | re.IGNORECASE,
        )

    wpf_datagrid_props = [
        r'^\s*<Setter\s+Property="EnableRowVirtualization"[^>]*/>\s*$',
        r'^\s*<Setter\s+Property="EnableColumnVirtualization"[^>]*/>\s*$',
        r'^\s*<Setter\s+Property="ScrollViewer\.CanContentScroll"[^>]*/>\s*$',
        r'^\s*<Setter\s+Property="ScrollViewer\.PanningMode"[^>]*/>\s*$',
        r'^\s*<Setter\s+Property="IsManipulationEnabled"[^>]*/>\s*$',
        r'^\s*<Setter\s+Property="Stylus\.IsFlicksEnabled"[^>]*/>\s*$',
    ]
    for pat in wpf_datagrid_props:
        text = strip_lines_matching(text, pat, "WPF DataGrid virtualization/scroll setters", report)

    text = fix_selector_xtype(text, report)
    text = re.sub(r"\n{3,}", "\n\n", text)
    return text, report


def verify(text: str, path: Path) -> list[str]:
    return [f"{path.name}: still contains {token!r}" for token in FORBIDDEN if token in text]


def main() -> int:
    all_errors: list[str] = []
    for path in TARGETS:
        if not path.is_file():
            print(f"MISSING: {path}")
            all_errors.append(f"missing file: {path}")
            continue
        original = path.read_text(encoding="utf-8", errors="replace")
        updated, changes = sanitize(original)
        if updated != original:
            path.write_text(updated, encoding="utf-8", newline="\n")
        rel = path.relative_to(ROOT)
        print(f"\n{rel}:")
        if changes:
            for item in changes:
                print(f"  - {item}")
        else:
            print("  - (no changes)")
        all_errors.extend(verify(updated, path))

    if all_errors:
        print("\nVERIFICATION FAILED:")
        for err in all_errors:
            print(f"  {err}")
        return 1

    print("\nVERIFICATION OK: forbidden tokens absent from all 7 files.")
    return 0


if __name__ == "__main__":
    sys.exit(main())