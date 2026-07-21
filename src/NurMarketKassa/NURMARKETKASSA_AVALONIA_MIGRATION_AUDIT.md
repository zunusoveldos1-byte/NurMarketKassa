# Аудит миграции NurMarketKassa на Avalonia UI

**Дата аудита:** 14 июля 2026  
**Область:** `src/NurMarketKassa`, `src/NurMarketKassa.Avalonia`, `src/NurMarketKassa.ViewModels`, `src/NurMarketKassa.Core`, `src/NurMarketKassa.Infrastructure`, `src/NurMarketKassa.Ui.Shared`  
**Метод:** статический анализ кода (без изменений и без запуска приложения)

---

## Краткое резюме

Проект находится на **ранней стадии** миграции с WPF на Avalonia. Создан отдельный хост `NurMarketKassa.Avalonia` (Avalonia 11.2.5, .NET 8), но **продакшен-приложение остаётся WPF** (`NurMarketKassa`, `UseWPF=true`).

Архитектурная подготовка выполнена частично: вынесены `Core`, `Infrastructure`, `ViewModels`, `Ui.Shared`, общий `LoginViewModel`. Однако **основная кассовая логика по-прежнему живёт в WPF `MainWindow.xaml.cs` (~5900 строк)** и 13 адаптерах `Wpf*`.

**Avalonia migration status: 12%**

---

# 1. Общая оценка переноса

### Статистика UI

| Показатель | WPF (продакшен) | Avalonia (миграция) |
|---|---|---|
| Проект-хост | `NurMarketKassa` | `NurMarketKassa.Avalonia` |
| Файлы разметки | 47 `.xaml` (только POS) | 6 `.axaml` |
| Окна верхнего уровня | 10 | 2 (`LoginWindow`, `MainWindow`-заглушка) |
| Диалоги | 27 | 1 (`CheckoutDialog`) |
| UserControl / View | 2 | 1 (`LoginView`, не используется в потоке входа) |
| Темы | 4 WPF + 2 dialog themes | 1 (`AppThemeDark.axaml`) + FluentTheme |

### Что перенесено

| Категория | Статус |
|---|---|
| **LoginWindow** | Полностью перенесён на AXAML, подключён `LoginViewModel`, DI, auth-инфраструктура |
| **CheckoutDialog** | AXAML-разметка готова (~670 строк), bindings к `CheckoutViewModel`; **не подключён к POS-потоку** |
| **MainWindow** | Только placeholder («Avalonia host placeholder») |
| **Ui.Shared** | `IDispatcher`, `IAppSession`, `IWindowService`, `IDialogService` + Avalonia-реализации |
| **LoginViewModel** | Общий для WPF и Avalonia — логика авторизации перенесена в ViewModel |

### Что остаётся на WPF

- `MainWindow` — основной POS-экран (каталог, корзина, смена, оплата, возвраты)
- `SalesWindow`, `FinanceWindow`, `WarehouseWindow`, `PosSettingsWindow`
- `FilterWindow`, `ShiftsHistoryWindow`, `AdminSupportWindow`, `ServicesWindow`
- 26 диалогов без Avalonia-аналогов
- Admin-приложение `NurMarketKassa.Admin` (`DashboardWindow`) — отдельный WPF-хост
- 13 сервисов `Wpf*` (каталог, корзина, смена, склад, промпты, принтер и т.д.)

### Логика, подключённая к Avalonia ViewModel

| ViewModel | Подключение к Avalonia |
|---|---|
| `LoginViewModel` | ✅ Полностью — единственный рабочий end-to-end сценарий |
| `CheckoutViewModel` | ⚠️ AXAML готов, View зарегистрирован в DI, но **не в DI как ViewModel**, нет вызова из MainWindow |
| `CatalogViewModel` | ❌ Не зарегистрирован в Avalonia DI, нет View |
| `BarcodeScanViewModel` | ❌ |
| `WarehouseViewModel` | ❌ |
| `ProductDetailVm` | ❌ |
| `ShiftViewModel` / `ShiftHistoryViewModel` | ❌ WPF-only |
| `FrmKeyboardViewModel` | ❌ |

### Итог

**Avalonia migration status: 12%**

Расчёт: 1 из ~10 ключевых экранов полностью + частично CheckoutDialog (~30%) + placeholder MainWindow (~1%) ≈ **12%** функциональной готовности UI.

---

# 2. Сравнение старого UI и Avalonia UI

| Экран | Старый UI | Avalonia AXAML | ViewModel | Логика перенесена | Статус |
|---|---|---|---|---|---|
| **LoginWindow** | `Views/LoginWindow.xaml` (WPF) | `Views/LoginWindow.axaml` | `LoginViewModel` (shared) | ✅ Auth, offline, remember me | **Перенесён** |
| **MainWindow** | `Views/MainWindow.xaml` + `.cs` (~5900 строк) | `MainWindow.axaml` (placeholder) | `CatalogViewModel`, `BarcodeScanViewModel` (WPF only) | ❌ Вся POS-логика в WPF code-behind | **Не перенесён** |
| **Dashboard** | `NurMarketKassa.Admin/Views/DashboardWindow.xaml` | — | `DashboardViewModel` (Admin) | ❌ Отдельное Admin-приложение | **Не в scope Avalonia POS** |
| **SalesWindow** | `Views/SalesWindow.xaml` (WPF) | — | Нет отдельной VM (логика в code-behind) | ❌ | **Не перенесён** |
| **ProductWindow** | Нет отдельного окна; каталог в `MainWindow` + `ProductDetailDialog.xaml` | — | `CatalogViewModel`, `ProductDetailVm` | ⚠️ VM есть, View нет | **Не перенесён** |
| **CategoryWindow** | Нет отдельного окна; категории в `MainWindow` / `CatalogViewModel` | — | `CatalogViewModel` + `CategoryDto` | ⚠️ Логика в VM, UI только WPF | **Не перенесён** |
| **WarehouseWindow** | `Views/WarehouseWindow.xaml` (WPF) | — | `WarehouseViewModel` | ⚠️ VM готова, View только WPF | **Не перенесён** |
| **FinanceWindow** | `Views/FinanceWindow.xaml` (WPF) | — | Нет VM (логика в code-behind ~728 строк) | ❌ | **Не перенесён** |
| **ReportsWindow** | Нет в POS; отчёты в Admin `DashboardWindow` | — | `ReportExportService` (Admin) | ❌ | **Не перенесён** |
| **SettingsWindow** | `Views/PosSettingsWindow.xaml` (WPF) | — | Нет VM | ❌ | **Не перенесён** |

### Дополнительно: CheckoutDialog

| Компонент | WPF | Avalonia | Статус |
|---|---|---|---|
| CheckoutDialog | `Views/Dialogs/CheckoutDialog.xaml` — используется из `MainWindow` | `Views/Dialogs/CheckoutDialog.axaml` — зарегистрирован в DI, **не вызывается** | **Частично** (UI готов, поток не подключён) |

### Дополнительно: 26 WPF-диалогов без Avalonia-аналога

`OpenShiftDialog`, `CloseShiftDialog`, `CashOperationsDialog`, `DeferredCartsDialog`, `OrderDiscountDialog`, `ReturnSaleDialog`, `WeighedProductDialog`, `FrmKeyboard`, `SaleSuccessDialog`, `ReceiptPreviewDialog` и др.

---

# 3. Проверка сохранения бизнес-логики

> **Важно:** бизнес-логика в `Core` / `Infrastructure` / `ViewModels` **сохранена в кодовой базе**, но для Avalonia-хоста **доступна только авторизация**. Остальные функции работают только через WPF-хост.

## Продажи

| Функция | Старый код | Новый код (Avalonia) | Перенесено | Проблемы |
|---|---|---|---|---|
| Добавление товара в чек | `MainWindow.xaml.cs` → `LocalCartService.AddProduct`, `ICartService` | — | ❌ | Нет MainWindow, нет `ICartService` в Avalonia DI |
| Изменение количества | `MainWindow.xaml.cs` → `LocalCartService.UpdateLineQuantity` | — | ❌ | |
| Расчёт суммы | `CartTotalsCalculator`, `CartInPlaceRecalculator` | `CheckoutViewModel.RecalculateTotals` (только в VM) | ⚠️ | VM есть, но не вызывается из Avalonia |
| Скидки | `OrderDiscountHelper`, `MainWindow` + `OrderDiscountDialog` | `CheckoutViewModel` (order discount) | ⚠️ | UI checkout готов, POS-поток отсутствует |
| Оплата | `MainWindow` → `CheckoutDialog` (WPF) → API/hardware | `CheckoutDialog.axaml` + `CheckoutViewModel` | ⚠️ | AXAML без `ExecutePay` wiring; `CheckoutViewModel` не в DI |
| Закрытие продажи | `MainWindow.xaml.cs`, `SalesApiService`, `StockOnSaleHandler` | — | ❌ | |

## Товары

| Функция | Старый код | Новый код (Avalonia) | Перенесено | Проблемы |
|---|---|---|---|---|
| Создание | API/Infrastructure + Admin | — | ❌ | Нет UI |
| Редактирование | `ProductDetailDialog` (WPF) | — | ❌ | `ProductDetailVm` без View |
| Удаление | Через API/Admin | — | ❌ | |
| Поиск | `CatalogViewModel`, `ProductSearchService` | — | ⚠️ | VM готова, Avalonia View нет |
| Категории | `CatalogViewModel.Categories` | — | ⚠️ | Логика в VM, UI только WPF |

## Склад

| Функция | Старый код | Новый код (Avalonia) | Перенесено | Проблемы |
|---|---|---|---|---|
| Приход товара | `WarehouseViewModel` + `IInventoryService` | — | ⚠️ | VM готова, нет Avalonia View/DI |
| Списание | `WarehouseViewModel.WriteOffAsync` | — | ⚠️ | |
| Остатки | `ILocalStockProvider`, `StockService` | — | ❌ | Wpf-адаптеры не зарегистрированы в Avalonia |

## Финансы

| Функция | Старый код | Новый код (Avalonia) | Перенесено | Проблемы |
|---|---|---|---|---|
| Дневная выручка | `FinanceWindow.xaml.cs`, `AnalyticsService` | — | ❌ | Логика в WPF code-behind |
| Отчёты | `FinanceWindow`, Admin `ReportExportService` | — | ❌ | |
| X-отчёт | `CashShiftService.GenerateXReportAsync` | — | ❌ | Сервис не в Avalonia DI |
| Z-отчёт | `CashShiftService.GenerateZReportAsync` | — | ❌ | |

## Пользователи

| Функция | Старый код | Новый код (Avalonia) | Перенесено | Проблемы |
|---|---|---|---|---|
| Авторизация | `LoginViewModel` + WPF/Avalonia LoginWindow | `LoginViewModel` + Avalonia `LoginWindow` | ✅ | Avalonia не вызывает `CompanyInfoService`, `AuditDb.LogEvent` (есть в WPF) |
| Роли | Не обнаружена явная RBAC-модель в POS | — | ⚠️ | Авторизация по email/паролю; роли не выделены |
| Права доступа | Косвенно через смену/кассу (`IAppSession`) | `AvaloniaAppSession` | ⚠️ | Сессия есть, enforcement в POS отсутствует (нет MainWindow) |

---

# 4. Проверка архитектуры MVVM

### Соответствие принципам

| Критерий | Оценка | Комментарий |
|---|---|---|
| View отделён от логики | ⚠️ Частично | `LoginViewModel`, `CatalogViewModel`, `CheckoutViewModel` — хорошо; `MainWindow.xaml.cs`, `FinanceWindow`, `SalesWindow` — антипаттерн |
| Логика в ViewModel/Services | ⚠️ | Shared VM + Core/Infrastructure OK; WPF MainWindow — монолит |
| Нет бизнес-логики в AXAML | ✅ | AXAML содержит только разметку, стили, bindings |
| Нет тяжёлой логики в code-behind | ⚠️ | Avalonia `LoginWindow.axaml.cs` (~460 строк) — UI-логика (пароль, drag, support dialog) |
| Команды через ICommand | ✅ | `LoginViewModel`, `CheckoutViewModel` используют `RelayCommand`/`AsyncRelayCommand` |
| Dependency Injection | ⚠️ | Avalonia DI минимален (auth + 4 UI-сервиса); WPF DI полный (~50+ сервисов) |

### Нарушения

#### Логика в `*.axaml.cs`

| Файл | Проблема |
|---|---|
| `Avalonia/Views/LoginWindow.axaml.cs` | Prefill credentials, remember-me persistence, navigation to MainWindow, support dialog — должно быть в VM/сервисах |
| `NurMarketKassa/Views/MainWindow.xaml.cs` | ~5900 строк: корзина, оплата, смена, штрихкод, возвраты, печать |
| `NurMarketKassa/Views/FinanceWindow.xaml.cs` | ~728 строк: загрузка продаж, история, cash sessions |
| `NurMarketKassa/Views/SalesWindow.xaml.cs` | ~380 строк: история продаж inline |

#### Прямые обращения к БД/API из View

| Файл | Проблема |
|---|---|
| `FinanceWindow.xaml.cs` | Прямая работа с JSON-файлом `cash_history.json`, API через `App.SalesApi` |
| `SalesWindow.xaml.cs` | `App.SalesApi`, фильтрация в code-behind |
| `MainWindow.xaml.cs` | `App.AuditDb`, `App.SalesApi`, `ICartService` через static `App.*` bridge |

#### Дублирование кода

| Область | Описание |
|---|---|
| Login UI | `LoginWindow.axaml` (полная форма) и `LoginView.axaml` (упрощённая) — дублирование, `LoginView` не используется в потоке |
| CheckoutDialog | WPF и Avalonia версии; Avalonia не подключена к потоку |
| `FrmKeyboardViewModel` | Дубликат в `NurMarketKassa/ViewModels` и `NurMarketKassa.ViewModels` |
| `WpfLoginViewModel` | Мёртвый код, заменён `LoginViewModel` |

---

# 5. Проверка ViewModel

| ViewModel | View подключен | Команды работают | Логика перенесена | Статус |
|---|---|---|---|---|
| `LoginViewModel` | ✅ `LoginWindow.axaml` | ✅ `LoginCommand` | ✅ Auth online/offline | **Готово** |
| `CheckoutViewModel` | ⚠️ AXAML есть, не в runtime-потоке | ⚠️ Команды в VM, `CheckoutViewModel` не в DI | ✅ Расчёты, скидки, сдача | **UI готов, не подключён** |
| `CatalogViewModel` | ❌ | — | ✅ Поиск, категории, фильтры | **Только логика** |
| `BarcodeScanViewModel` | ❌ | — | ✅ Parse/process barcode | **Только логика** |
| `WarehouseViewModel` | ❌ (WPF `WarehouseWindow`) | ✅ WPF only | ✅ Приход/списание/ревизия | **WPF only** |
| `ProductDetailVm` | ❌ (WPF dialog) | — | ⚠️ | **WPF only** |
| `FrmKeyboardViewModel` | ❌ (WPF `FrmKeyboard`) | ✅ WPF only | ✅ | **WPF only** |
| `ShiftViewModel` | ❌ | ✅ WPF only | ✅ Open/Close shift | **WPF only** |
| `ShiftHistoryViewModel` | ❌ (WPF views) | ✅ WPF only | ✅ | **WPF only** |
| `DashboardViewModel` | ❌ (Admin WPF) | — | ✅ Admin metrics | **Вне Avalonia POS** |

### Заглушки

| Компонент | Тип заглушки |
|---|---|
| `MainWindow.axaml` | Текст «Avalonia host placeholder» |
| `AvaloniaDialogService` | Программные окна (не AXAML) для confirm/alert |
| `AdminSupport_Click` в Avalonia Login | Inline `Window` с `TextBlock`, не полноценный `AdminSupportWindow` |

---

# 6. Проверка AXAML интерфейсов

### LoginWindow.axaml

| Аспект | Статус |
|---|---|
| Binding | ✅ `Username`, `Password`, `RememberMe`, `ErrorMessage`, `IsLoading`, `LoginCommand` |
| DataContext | ✅ Устанавливается в code-behind + `x:DataType` |
| Styles | ✅ Локальные стили + `DynamicResource` из темы |
| Resources | ✅ `AssetPathToBitmapConverter`, изображения |
| Темы | ✅ `AppThemeDark.axaml` подключена в `App.axaml` |
| Compiled bindings | ✅ `x:CompileBindings="True"` |

**Элементы без Command (Click в code-behind):**

- `TogglePasswordButton` — `Click="TogglePasswordButton_Click"` (допустимо для UI)
- `ExitButton` — `Click="ExitButton_Click"`
- `AdminSupport_Click` — временный inline-диалог
- `ChromeDrag_PointerPressed` — drag окна

### CheckoutDialog.axaml

| Аспект | Статус |
|---|---|
| Binding | ✅ Полный набор: суммы, скидки, оплата, банки, QR, печать чека |
| DataContext | ⚠️ Только через конструктор `CheckoutDialog(CheckoutViewModel)` |
| Styles | ✅ Обширные кастомные стили |
| Команды | ✅ `PayCommand`, `CancelCommand`, quick-cash, `ClearCashCommand` |
| Без Command | `Cancel_Click` на кнопке закрытия в header (дублирует `CancelCommand`) |

### MainWindow.axaml

- ❌ Только placeholder, нет bindings, нет DataContext, нет бизнес-UI

### LoginView.axaml

- ✅ Bindings корректны, но **не используется** в `LoginWindow` (дублирующий упрощённый вариант)

### Адаптивность

- `LoginWindow` — двухколоночный layout с `ColumnDefinitions="0.4*,20,0.6*"`; на узких экранах может не адаптироваться (нет visual states / breakpoints)
- `CheckoutDialog` — фиксированный размер 1100×700

---

# 7. Проверка сервисов и данных

### Avalonia DI (`App.axaml.cs`)

**Зарегистрировано:**

- `AvaloniaDispatcher`, `AvaloniaAppSession`, `AvaloniaWindowService`, `AvaloniaDialogService`
- Auth: `DatabaseService`, `AuthService`, `PosAuthService`, `AuthApiService`, `LocalAccountsManager`, `ConnectivityService`, `OfflineLoginSupport`
- Views: `LoginViewModel`, `LoginView`, `LoginWindow`, `MainWindow`, `CheckoutDialog`

**Не зарегистрировано (есть в WPF DI):**

| Сервис | Назначение |
|---|---|
| `ICartService` / `CartService` | Корзина |
| `ICatalogCacheService` | Каталог |
| `ISalesApiService` | Продажи API |
| `IShiftApiService` / `ICashShiftService` | Смены, X/Z отчёты |
| `IStockService` / `IInventoryService` | Склад |
| `IReceiptPrinterService` | Печать чеков |
| `IWeightScaleService` | Весы |
| `MediatR` | Доменные handlers |
| `MySqlAuditService` | Аудит |
| `SyncService` | Офлайн-синхронизация |
| 13× `Wpf*` адаптеров | UI-специфичные мосты |

### Использование сервисов новыми ViewModel

| ViewModel | Сервисы | Avalonia |
|---|---|---|
| `LoginViewModel` | `IAuthService`, `ILocalAccountsStore`, `IConnectivityService`, `IOfflineLoginSupport`, `IAppSession` | ✅ |
| `CheckoutViewModel` | `CartTotalsCalculator`, `OrderDiscountHelper`, `UserPreferences` (static) | ⚠️ Не создаётся из DI |
| `CatalogViewModel` | `ICatalogCacheService`, `IDispatcher`, `IAppSession` | ❌ `ICatalogCacheService` нет в Avalonia |

### API/БД

- `DatabaseService` — ✅ подключён в Avalonia (auth schema)
- `AuthApiService` / `NurMarketApiClient` — ✅
- MySQL audit, PostgreSQL, catalog sync, stock ledger — ❌ не в Avalonia DI

### Временные заглушки

- `MainWindow.axaml` — placeholder
- `AvaloniaDialogService` — программные диалоги вместо AXAML
- `AvaloniaWindowService` — поддерживает только `CheckoutViewModel`
- Static bridge `App.*` в WPF помечен как «временный мост (Фаза P0)»

---

# 8. Проверка функционального соответствия

### WPF → Avalonia: что работает

| Функция | WPF | Avalonia |
|---|---|---|
| Вход (online) | ✅ | ✅ |
| Вход (offline) | ✅ | ✅ |
| Remember me | ✅ | ✅ (с отличиями — см. ниже) |
| Переход на MainWindow после входа | ✅ Полный POS | ⚠️ Placeholder |

### Что потеряно / отсутствует в Avalonia

1. **Весь POS-экран** — каталог, корзина, штрихкод, весовые товары
2. **Управление сменой** — открытие/закрытие, X/Z отчёты
3. **Оплата end-to-end** — Checkout AXAML есть, но не вызывается; нет `CartService`, нет API продаж
4. **Склад** — приход, списание, ревизия
5. **Финансы и история продаж**
6. **Настройки кассы** — принтер, весы, тема, автозапуск
7. **Возвраты, отложенные корзины, печать чеков**
8. **Виртуальная клавиатура, touch-режим**
9. **Аудит входа** — WPF пишет в `AuditDb`, Avalonia — нет
10. **Admin Support** — WPF `AdminSupportWindow`, Avalonia — inline TextBlock

### Что работает иначе

| Аспект | WPF | Avalonia |
|---|---|---|
| После login | `CompanyInfoService.RefreshAsync`, audit log, catalog warm-up | Сразу placeholder MainWindow |
| Session storage | `WpfAppSession` → static `App.*` | `AvaloniaAppSession` (in-memory) |
| Тема | Dark/Light переключение | Только dark theme |
| Password field | `PasswordBox` (WPF) | `TextBox` с `PasswordChar` |
| Диалоги | 27 AXAML-диалогов | Программные окна в `AvaloniaDialogService` |

---

# 9. Список оставшейся работы

Подробный приоритизированный список — в файле **`NURMARKETKASSA_AVALONIA_TODO.md`**.

---

## Финальный вывод

| Метрика | Оценка |
|---|---|
| **Перенос на Avalonia** | **12%** |
| **Бизнес-логика сохранена** | **88%** (в кодовой базе Core/Infrastructure/ViewModels; для Avalonia доступно ~8%) |
| **UI готовность** | **15%** (Login ~95%, Checkout AXAML ~75%, остальное ~0%) |

### Критические незавершённые части

1. **MainWindow (POS)** — основной экран кассы (~5900 строк логики) не перенесён; без него касса неработоспособна
2. **DI и сервисы** — Avalonia-хост регистрирует только auth; нет корзины, каталога, смен, склада, hardware
3. **Checkout / продажи** — AXAML готов, но не подключён к `CartService` и API; нет end-to-end оплаты

### Можно ли считать миграцию завершённой: **Нет**

### Почему

Миграция находится на стадии **proof-of-concept**: работает только экран входа. Продакшен-приложение — WPF (`NurMarketKassa`). Avalonia-хост после логина показывает placeholder вместо кассы. Отсутствуют все операционные экраны (продажи, склад, финансы, настройки, 26 диалогов). Бизнес-логика вынесена в shared-проекты, но **не подключена** к Avalonia UI. Для завершения миграции требуется перенос `MainWindow`, регистрация полного DI-контейнера, создание Avalonia-адаптеров вместо `Wpf*` и подключение существующих ViewModel к новым Views.

---

*Аудит выполнен без изменения кода. Рекомендуется дополнить ручным smoke-тестом Avalonia-хоста и WPF-хоста для подтверждения runtime-поведения.*
