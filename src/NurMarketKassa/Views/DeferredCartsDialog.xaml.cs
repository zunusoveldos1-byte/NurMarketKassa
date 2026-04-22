using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows;
using NurMarketKassa.Services;

namespace NurMarketKassa.Views;

public enum DeferredRestoreMode
{
    ReplaceCurrentCart,
    MergeIntoCurrentCart,
}

public partial class DeferredCartsDialog : Window
{
    public IReadOnlyList<DeferredCartEntry> EntriesToRestore { get; private set; } = [];
    public DeferredRestoreMode RestoreMode { get; private set; } = DeferredRestoreMode.ReplaceCurrentCart;

    public DeferredCartsDialog()
    {
        InitializeComponent();
        ReloadList();
    }

    private void ReloadList()
    {
        CartListBox.Items.Clear();
        var items = DeferredCartsStore.LoadAll().OrderByDescending(x => x.SavedAt).ToList();
        SummaryText.Text = items.Count == 0
            ? "Очередь пуста."
            : $"В очереди: {items.Count} чек(ов). Последний: {items[0].SavedAt.LocalDateTime:g}.";
        foreach (var e in items)
            CartListBox.Items.Add(new DeferredCartListRow(e));
    }

    private static int CountLines(string cartJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(cartJson) ? "{}" : cartJson);
            return CartDisplayHelper.EnumerateItems(doc.RootElement).Count();
        }
        catch
        {
            return 0;
        }
    }

    private void DeleteSelected_Click(object sender, RoutedEventArgs e)
    {
        var rows = CartListBox.SelectedItems.Cast<DeferredCartListRow>().ToList();
        if (rows.Count == 0)
        {
            MessageBox.Show(this, "Выберите строки в списке.", "Отложенные", MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        DeferredCartsStore.RemoveIds(rows.Select(r => r.Entry.Id));
        ReloadList();
    }

    private void MergeIntoCurrent_Click(object sender, RoutedEventArgs e)
    {
        AcceptSelection(DeferredRestoreMode.MergeIntoCurrentCart);
    }

    private void LoadAsSeparate_Click(object sender, RoutedEventArgs e)
    {
        AcceptSelection(DeferredRestoreMode.ReplaceCurrentCart);
    }

    private void AcceptSelection(DeferredRestoreMode mode)
    {
        var rows = CartListBox.SelectedItems.Cast<DeferredCartListRow>().ToList();
        if (rows.Count == 0)
        {
            MessageBox.Show(this, "Выберите одну или несколько корзин.", "Отложенные",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        RestoreMode = mode;
        EntriesToRestore = rows.Select(r => r.Entry).ToList();
        DialogResult = true;
    }

    private sealed class DeferredCartListRow
    {
        internal DeferredCartEntry Entry { get; }

        internal DeferredCartListRow(DeferredCartEntry entry) => Entry = entry;

        public override string ToString()
        {
            var n = CountLines(Entry.CartJson);
            return $"{Entry.Label} · {Entry.SavedAt.LocalDateTime:g} · {n} поз.";
        }
    }
}
