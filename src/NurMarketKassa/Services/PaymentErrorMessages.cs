namespace NurMarketKassa.Services;

internal static class PaymentErrorMessages
{
    public const string DiscountFailure =
        "Не удалось выполнить оплату. Проверьте параметры скидки.";

    public const string GenericFailure =
        "Не удалось выполнить оплату. Попробуйте ещё раз или обратитесь к администратору.";

    public static bool LooksLikeDiscountError(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return false;

        return message.Contains("order_discount", StringComparison.OrdinalIgnoreCase)
               || message.Contains("non_field_errors", StringComparison.OrdinalIgnoreCase)
               || message.Contains("фиксированную скидку", StringComparison.OrdinalIgnoreCase)
               || message.Contains("скидку в процентах", StringComparison.OrdinalIgnoreCase);
    }

    public static string ForCashier(Exception ex) =>
        ex is ApiException api
            ? LooksLikeDiscountError(api.Message)
                ? DiscountFailure
                : string.IsNullOrWhiteSpace(api.Message) ? GenericFailure : api.Message
            : GenericFailure;

    public static void Log(string context, Exception ex) =>
        PosLogger.Log($"{context}: {ex.Message}", "PAYMENT ERROR");
}
