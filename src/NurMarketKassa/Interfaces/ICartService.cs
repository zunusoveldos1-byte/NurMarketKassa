using System.Collections.Generic;
using System.Text.Json;
using NurMarketKassa.Models.Pos;

namespace NurMarketKassa.Interfaces;

/// <summary>Строго типизированная позиция корзины (неизменяемый снимок строки чека).</summary>
public sealed record CartItem(
    string? Id,
    string? ProductId,
    string Name,
    double Quantity,
    decimal UnitPrice,
    decimal LineDiscount,
    decimal LineTotal,
    bool MustWeigh);

/// <summary>
/// Изолированная, потокобезопасная логика POS-корзины.
/// Заменяет статическое глобальное состояние <c>App.Cart</c>; регистрируется как Singleton в DI.
/// </summary>
public interface ICartService
{
    // --- Идентификация и состояние сессии ---

    /// <summary>Идентификатор серверной корзины (null для черновика/локального офлайн-чека).</summary>
    string? CartId { get; }

    /// <summary>Корзина существует локально без серверной синхронизации (офлайн-продажа).</summary>
    bool IsLocalOffline { get; }

    /// <summary>Локальный черновик чека до материализации на сервере (после откладывания).</summary>
    bool IsStaging { get; }

    /// <summary>Есть данные корзины для отображения.</summary>
    bool HasCart { get; }

    /// <summary>Можно выполнить GET /pos/carts/{id}/ (или локальная корзина в офлайне).</summary>
    bool CanRefresh { get; }

    /// <summary>Текущий JSON-снимок корзины (источник данных для серверно-ориентированных операций).</summary>
    JsonElement Root { get; }

    /// <summary>Потокобезопасная копия сырого JSON текущей корзины.</summary>
    string GetRawText();

    // --- Строго типизированная проекция содержимого ---

    /// <summary>Текущий неизменяемый список позиций корзины.</summary>
    IReadOnlyList<CartItem> Items { get; }

    /// <summary>Количество строк (позиций) в корзине.</summary>
    int LineCount { get; }

    /// <summary>Суммарное количество единиц товара во всех позициях.</summary>
    double TotalQuantity { get; }

    /// <summary>Итоговая сумма к оплате с учётом скидок.</summary>
    decimal TotalAmount { get; }

    /// <summary>Суммарная применённая скидка (по строкам + на весь чек).</summary>
    decimal TotalDiscount { get; }

    // --- Управление жизненным циклом корзины ---

    /// <summary>Заменяет содержимое корзины снимком из ответа сервера.</summary>
    void SetCart(JsonElement root);

    /// <summary>Заменяет содержимое корзины локальным офлайн-снимком (сырой JSON).</summary>
    void SetLocalOfflineCart(string cartJson);

    /// <summary>Полностью очищает корзину.</summary>
    void Clear();

    // --- Типизированные операции над позициями (локальное/снимочное редактирование) ---

    /// <summary>Добавляет товар каталога в корзину (или увеличивает количество существующей строки).</summary>
    void AddItem(CatalogProductTileVm product, double quantity);

    /// <summary>Изменяет количество в указанной строке корзины.</summary>
    void UpdateQuantity(string itemId, double quantity);

    /// <summary>Удаляет строку корзины по её идентификатору.</summary>
    void RemoveItem(string itemId);
}
