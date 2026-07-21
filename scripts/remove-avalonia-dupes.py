#!/usr/bin/env python3
"""Delete Avalonia-vendored copies of Core/ViewModels/Ui.Shared business code."""
from __future__ import annotations

import shutil
from pathlib import Path

ROOT = Path("src/NurMarketKassa.Avalonia")

# Entire folders that duplicate Core / ViewModels
DELETE_DIRS = [
    ROOT / "Contracts",
    ROOT / "Application",
    ROOT / "Domain",
    ROOT / "Catalog",
    ROOT / "Scanning",
    ROOT / "Main",  # MainWindowViewModel etc. live in ViewModels project
]

# Root-level duplicate VM / toolkit files
DELETE_FILES = [
    ROOT / "LoginViewModel.cs",
    ROOT / "ViewModelBase.cs",
    ROOT / "BarcodeScanViewModel.cs",
    ROOT / "CheckoutViewModel.cs",
    ROOT / "ProductDetailVm.cs",
    ROOT / "RelayCommand.cs",
    ROOT / "RelayCommandOfT.cs",
    ROOT / "AsyncRelayCommand.cs",
    ROOT / "IDialogService.cs",
    ROOT / "IAppSession.cs",
    ROOT / "IDispatcher.cs",
    ROOT / "IWindowService.cs",
]

# Avalonia ViewModels that duplicate WPF-side ones — keep UI-only ones if unique later
# ShiftHistoryViewModel / WarehouseViewModel / FrmKeyboardViewModel may be Avalonia-only UI VMs — keep for now

deleted = []
for d in DELETE_DIRS:
    if d.exists():
        shutil.rmtree(d)
        deleted.append(f"DIR {d}")

for f in DELETE_FILES:
    if f.is_file():
        f.unlink()
        deleted.append(f"FILE {f}")

print("deleted", len(deleted))
for x in deleted:
    print(" ", x)
