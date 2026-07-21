#!/usr/bin/env python3
"""Migrate WPF Style/TargetType syntax to Avalonia Selector/Classes."""
from __future__ import annotations

import re
from pathlib import Path

ROOT = Path(r"C:\Users\User\Desktop\NurCrmPosKassa-master\NurCrmPosKassa-master\src\NurMarketKassa.Avalonia")


def strip_type_prefix(type_name: str) -> str:
    type_name = type_name.strip()
    if type_name.startswith("{x:Type "):
        type_name = type_name[len("{x:Type ") :].rstrip("}")
    if ":" in type_name:
        type_name = type_name.split(":", 1)[1]
    return type_name.strip()


def protect_control_templates(text: str) -> tuple[str, list[str]]:
    templates: list[str] = []

    def repl(m: re.Match[str]) -> str:
        templates.append(m.group(0))
        return f"__CONTROL_TEMPLATE_{len(templates) - 1}__"

    protected = re.sub(
        r"<ControlTemplate\b[^>]*>.*?</ControlTemplate>",
        repl,
        text,
        flags=re.DOTALL | re.IGNORECASE,
    )
    return protected, templates


def restore_control_templates(text: str, templates: list[str]) -> str:
    for i, tpl in enumerate(templates):
        text = text.replace(f"__CONTROL_TEMPLATE_{i}__", tpl)
    return text


def convert_style_tags(text: str) -> str:
    def style_repl(m: re.Match[str]) -> str:
        attrs = m.group(1)
        key_m = re.search(r'\bx:Key="([^"]+)"', attrs)
        tt_m = re.search(r'\bTargetType="([^"]+)"', attrs)
        if not tt_m:
            return m.group(0)
        type_name = strip_type_prefix(tt_m.group(1))
        new_attrs = re.sub(r'\s*TargetType="[^"]*"', "", attrs)
        if key_m:
            key = key_m.group(1)
            selector = f"{type_name}.{key}"
        else:
            selector = type_name
        if re.search(r'\bSelector="', new_attrs):
            new_attrs = re.sub(r'\s*TargetType="[^"]*"', "", new_attrs)
            return f"<Style{new_attrs}>"
        new_attrs = re.sub(r'\s*TargetType="[^"]*"', "", new_attrs)
        return f'<Style Selector="{selector}"{new_attrs}>'

    return re.sub(r"<Style(\s[^>]*)>", style_repl, text, flags=re.IGNORECASE)


def style_to_classes(text: str) -> str:
    def repl(m: re.Match[str]) -> str:
        key = m.group(1)
        return f'Classes="{key}"'

    text = re.sub(
        r'\bStyle="\{(?:Static|Dynamic)Resource\s+([^}]+)\}"',
        repl,
        text,
    )
    return text


def merge_classes_on_tags(text: str) -> str:
    def merge_tag(m: re.Match[str]) -> str:
        tag = m.group(0)
        classes = re.findall(r'\bClasses="([^"]*)"', tag)
        if len(classes) <= 1:
            return tag
        merged: list[str] = []
        seen: set[str] = set()
        for cls_attr in classes:
            for cls in cls_attr.split():
                if cls and cls not in seen:
                    seen.add(cls)
                    merged.append(cls)
        tag = re.sub(r'\s*Classes="[^"]*"', "", tag)
        insert_at = tag.rfind(">")
        if insert_at == -1:
            return tag
        return tag[:insert_at] + f' Classes="{" ".join(merged)}"' + tag[insert_at:]

    return re.sub(r"<[^>?\s][^>]*>", merge_tag, text)


def remove_drop_shadow_effects(text: str) -> str:
    text = re.sub(r"\s*Effect=\"\{[^\"]+\}\"", "", text)
    text = re.sub(r"<DropShadowEffect\b[^>]*/>\s*", "", text, flags=re.IGNORECASE)
    text = re.sub(
        r"<DropShadowEffect\b[^>]*>.*?</DropShadowEffect>\s*",
        "",
        text,
        flags=re.DOTALL | re.IGNORECASE,
    )
    return text


def process(text: str) -> str:
    text = remove_drop_shadow_effects(text)
    protected, templates = protect_control_templates(text)
    protected = convert_style_tags(protected)
    text = restore_control_templates(protected, templates)
    text = style_to_classes(text)
    text = merge_classes_on_tags(text)
    return text


def should_process(path: Path) -> bool:
    if not path.is_file():
        return False
    if "_wpf_port_backup" in path.parts:
        return False
    if path.name.endswith(".bak"):
        return False
    return True


def main() -> None:
    updated = 0
    for path in sorted(ROOT.rglob("*.axaml")):
        if not should_process(path):
            continue
        original = path.read_text(encoding="utf-8", errors="replace")
        changed = process(original)
        if changed != original:
            path.write_text(changed, encoding="utf-8", newline="\n")
            updated += 1
            print(path.relative_to(ROOT))
    print(f"Updated {updated} file(s)")


if __name__ == "__main__":
    main()