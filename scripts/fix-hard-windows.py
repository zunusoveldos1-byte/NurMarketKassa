#!/usr/bin/env python3
"""Targeted fixes for known broken AXAML windows only."""
from __future__ import annotations

import re
from pathlib import Path

FILES = [
    Path("src/NurMarketKassa.Avalonia/Views.axaml/Main/FinanceWindow.axaml"),
    Path("src/NurMarketKassa.Avalonia/Views.axaml/Main/SalesWindow.axaml"),
    Path("src/NurMarketKassa.Avalonia/Views.axaml/Main/PosSettingsWindow.axaml"),
    Path("src/NurMarketKassa.Avalonia/Views.axaml/Dialogs/CashOperationsDialog.axaml"),
    Path("src/NurMarketKassa.Avalonia/Views.axaml/Dialogs/FrmKeyboard.axaml"),
    Path("src/NurMarketKassa.Avalonia/Views.axaml/Dialogs/ReturnSaleDialog.axaml"),
    Path("src/NurMarketKassa.Avalonia/Views.axaml/Dialogs/FinanceDateRangeDialog.axaml"),
    Path("src/NurMarketKassa.Avalonia/Views.axaml/Login/LoginWindow.axaml"),
]

TEMPLATE_SETTER = re.compile(
    r'<Setter\s+Property="Template"\s*>\s*<Setter\.Value>\s*<ControlTemplate[\s\S]*?</ControlTemplate>\s*</Setter\.Value>\s*</Setter>',
    re.MULTILINE,
)

DROP_TYPES = ("TextBox", "DatePicker", "ComboBox", "TabControl", "PasswordBox")


def drop_templates(text: str) -> str:
    def repl(m: re.Match) -> str:
        block = m.group(0)
        for t in DROP_TYPES:
            if f'TargetType="{t}"' in block:
                return ""
        return block

    return TEMPLATE_SETTER.sub(repl, text)


def clean(text: str) -> str:
    text = text.replace("{TemplateBinding Control.", "{TemplateBinding ")
    text = text.replace('Property="Control.', 'Property="')
    text = text.replace("<TabPanel ", "<ItemsPresenter ")
    text = text.replace("</TabPanel>", "</ItemsPresenter>")
    text = text.replace('Property="Border.CornerRadius"', 'Property="CornerRadius"')
    text = text.replace('Property="Border.Padding"', 'Property="Padding"')
    text = text.replace('Property="DataGrid.RowHeight"', 'Property="RowHeight"')
    text = text.replace('Property="DataGrid.GridLinesVisibility"', 'Property="GridLinesVisibility"')
    text = drop_templates(text)
    text = re.sub(r'\s*<Setter\s+Property="UIElement\.[^"]+"\s+Value="[^"]*"\s*/>\s*', "\n", text)
    text = re.sub(r'\s*<Setter\s+Property="Effect"\s+Value="[^"]*"\s*/>\s*', "\n", text)
    text = re.sub(r'\s*<Setter\s+Property="ScrollViewer\.[^"]+"\s+Value="[^"]*"\s*/>\s*', "\n", text)
    text = re.sub(r'\s*<Setter\s+Property="ItemContainerStyle"\s*>[\s\S]*?</Setter>\s*', "\n", text)
    text = re.sub(r'\sItemContainerStyle\s*=\s*"[^"]*"', "", text)
    text = re.sub(r'\sSelectedDateChanged\s*=\s*"[^"]*"', "", text)
    text = re.sub(r'\sVerticalScrollBarVisibility\s*=\s*"[^"]*"', "", text)
    text = re.sub(r'\sHorizontalScrollBarVisibility\s*=\s*"[^"]*"', "", text)
    text = re.sub(r'\sActualWidth\s*=\s*"[^"]*"', "", text)
    text = re.sub(r'\sMinWidth\s*=\s*"\{TemplateBinding ActualWidth\}"', "", text)
    return text


for path in FILES:
    if not path.is_file():
        print("missing", path)
        continue
    orig = path.read_text(encoding="utf-8")
    new = clean(orig)
    if new != orig:
        path.write_text(new, encoding="utf-8", newline="\n")
        print("updated", path)
    else:
        print("unchanged", path)
