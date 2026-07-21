# NurMarketKassa — Avalonia Migration TODO

**Дата:** 14 июля 2026  
**Источник:** `NURMARKETKASSA_AVALONIA_MIGRATION_AUDIT.md`

---

## Осталось перенести

### P0 — без этого нельзя считать перенос завершённым

- [ ] **MainWindow (POS)** — перенести `Views/MainWindow.xaml` (~2239 строк) на AXAML; извлечь ~5900 строк логики из `MainWindow.xaml.cs` в ViewModels/Services
- [ ] **Регистрация полного DI** в `NurMarketKassa.Avalonia/App.axaml.cs` — портировать сервисы из WPF `App.xaml.cs` (Cart, Catalog, Shift, Stock, Hardware, MediatR, Audit)
- [ ] **Avalonia-адаптеры** вместо 13 `Wpf*` сервисов (`WpfPosCartGateway`, `WpfCatalogCacheService`, `WpfShiftOpenCoordinator`, `WpfUserPrompts`, `WpfWeightInputPrompt`, stock gateways и др.)
- [ ] **CheckoutDialog** — подключить к POS-потоку: регистрация `CheckoutViewModel` в DI, вызов через `IWindowService.ShowDialogAsync` из MainWindow
- [ ] **OpenShiftDialog / CloseShiftDialog** — AXAML + интеграция с `ICashShiftService`
- [ ] **Каталог и корзина** — Avalonia View для `CatalogViewModel` + отображение корзины, commands add/remove/qty
- [ ] **Штрихкод** — подключить `BarcodeScanViewModel` + `IBarcodeInputService` в Avalonia

### P1 — важно для полноценной кассы

- [ ] **WarehouseWindow** — AXAML + `WarehouseViewModel` (VM уже готова)
- [ ] **FinanceWindow** — создать `FinanceViewModel`, перенести логику из code-behind (~728 строк)
- [ ] **SalesWindow** — создать `SalesViewModel`, перенести логику из code-behind (~380 строк)
- [ ] **PosSettingsWindow** — настройки принтера, весов, темы, автозапуска
- [ ] **ProductDetailDialog** — AXAML для `ProductDetailVm`
- [ ] **OrderDiscountDialog**, **WeighedProductDialog**, **NoStockDialog** — критичные для продаж
- [ ] **ReturnSaleDialog**, **DeferredCartsDialog** — возвраты и отложенные корзины
- [ ] **SaleSuccessDialog**, **ReceiptPreviewDialog**, **PaymentConfirmationDialog**
- [ ] **CashOperationsDialog**, **CashHistoryDialog**, **NewOperationDialog**
- [ ] **FrmKeyboard** — виртуальная клавиатура (`FrmKeyboardViewModel`)
- [ ] **X-отчёт / Z-отчёт** — UI + `CashShiftService.GenerateXReportAsync` / `GenerateZReportAsync`
- [ ] **ShiftsHistoryWindow**, **ShiftHistoryView**, **ShiftSummaryView**
- [ ] **FilterWindow**, **AdminSupportWindow**, **ServicesWindow**
- [ ] **Тема Light** — портировать `AppThemeLight.xaml`
- [ ] **Удалить static `App.*` bridge** — полный переход на `IAppSession` и DI

### P2 — улучшения

- [ ] **LoginView.axaml** — удалить дубликат или использовать внутри `LoginWindow` вместо inline-разметки
- [ ] **AdminSupport** в Avalonia Login — полноценный `AdminSupportWindow.axaml` вместо inline `Window`
- [ ] **AvaloniaDialogService** — заменить программные окна на AXAML-шаблоны (`PosConfirmDialog`, `PosAlertDialog`)
- [ ] **Адаптивный layout** — visual states для Login и MainWindow на разных разрешениях
- [ ] **TouchKeyboard** — портировать touch-режим
- [ ] **Admin app** (`NurMarketKassa.Admin`) — опциональная миграция `DashboardWindow`
- [ ] **Удалить мёртвый код** — `WpfLoginViewModel`, дубликат `FrmKeyboardViewModel`
- [ ] **E2E / UI тесты** для Avalonia-хоста

---

## Требует переподключения логики

### P0

- [ ] **`CheckoutViewModel`** — добавить в DI (`services.AddTransient<CheckoutViewModel>()`); создавать с `CartTotalsCalculator.CartTotals` из `ICartService`
- [ ] **`CatalogViewModel`** — зарегистрировать в DI; реализовать `ICatalogCacheService` для Avalonia (сейчас только `WpfCatalogCacheService`)
- [ ] **`LoginWindow.axaml.cs`** — перенести post-login шаги из WPF: `CompanyInfoService.RefreshAsync`, `AuditDb.LogEvent`, `AccountCatalogIsolation`, catalog warm-up
- [ ] **`AvaloniaAppSession`** — синхронизировать с auth result (`PosCashboxId` и др. — сейчас не все поля WPF `App.*` покрыты)
- [ ] **`AvaloniaWindowService`** — расширить `CreateWindow` для всех ViewModel (сейчас только `CheckoutViewModel`)
- [ ] **MainWindow после login** — подключить `CatalogViewModel`, `BarcodeScanViewModel`, `ICartService` вместо placeholder

### P1

- [ ] **`WarehouseViewModel`** — зарегистрировать `IInventoryService`, `ILocalStockProvider`, `IStockCatalogUpdater`, `IUserPrompts` (Avalonia-реализации)
- [ ] **`ShiftViewModel`** — перенести из WPF-only; убрать зависимость от `Application.Current.MainWindow`
- [ ] **`ExecutePay` в CheckoutViewModel** — убедиться, что Avalonia-поток вызывает `SalesApiService` и hardware так же, как WPF `CheckoutDialog.xaml.cs`
- [ ] **MediatR handlers** — зарегистрировать в Avalonia (`ProcessBarcodeHandler`, `StockOnSaleHandler`, `SaleFinalizedNotification`)
- [ ] **Offline sync** — `SyncService`, `CatalogBackgroundSyncService` в Avalonia startup

### P2

- [ ] **`CheckoutViewModel`** — заменить static `UserPreferences.Instance` на injected `IOptions` / сервис
- [ ] **Finance/Sales windows** — вынести `App.SalesApi`, `App.CurrentUserId` в injected services

---

## Требует исправления Binding

### P0

- [ ] **`CheckoutDialog.axaml`** — header close button: заменить `Click="Cancel_Click"` на `Command="{Binding CancelCommand}"` для единообразия
- [ ] **`CheckoutDialog`** — убедиться, что `DataContext` устанавливается через DI, а не только parameterless ctor

### P1

- [ ] **`LoginWindow.axaml`** — `EmailValidIcon` управляется из code-behind (`UpdateEmailValidIcon`); перенести в VM как `IsEmailValid` + binding `IsVisible`
- [ ] **`LoginWindow.axaml`** — `PasswordBox` / `VisiblePasswordBox` дублируют binding `Password`; унифицировать через одно свойство + converter
- [ ] **`LoginView.axaml`** — не подключён к navigation; либо удалить, либо интегрировать

### P2

- [ ] **Compiled bindings** — добавить `x:DataType` во все будущие AXAML
- [ ] **DynamicResource** — проверить все `Brush*` ресурсы при добавлении Light theme

---

## Требует улучшения UI

### P1

- [ ] **LoginWindow** — адаптивность: на узких экранах скрывать левую бренд-панель (сейчас фиксированные `0.4* / 0.6*`)
- [ ] **CheckoutDialog** — responsive: фиксированный 1100×700 не подходит для малых экранов
- [ ] **MainWindow** — спроектировать layout POS (каталог | корзина | toolbar) по аналогии с WPF
- [ ] **Темы** — портировать `PosDialogTheme.xaml`, `KeyboardIcons.xaml`
- [ ] **Иконки MDL2** — проверить fallback на платформах без Segoe MDL2 Assets

### P2

- [ ] **Анимации** — WPF Login имеет border focus animation; Avalonia — instant switch
- [ ] **BoxShadow / Opacity** — визуальное сравнение WPF vs Avalonia Login
- [ ] **Fullscreen mode** — WPF Login поддерживает `UserPreferences.Instance.Fullscreen`; Avalonia — всегда maximized

---

## Рекомендуемый порядок работ

```
Фаза 1 (P0): DI + Avalonia-адаптеры + MainWindow skeleton + Catalog/Cart VM binding
Фаза 2 (P0): Checkout end-to-end + Shift open/close
Фаза 3 (P1): Warehouse, Finance, Sales, Settings
Фаза 4 (P1): Оставшиеся 20+ диалогов
Фаза 5 (P2): Polish, themes, touch, admin, cleanup
```

**Ориентировочный объём:** ~90% UI-работы впереди.
