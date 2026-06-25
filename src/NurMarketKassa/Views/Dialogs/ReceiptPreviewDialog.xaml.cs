using System.IO;
using System.Windows;

namespace NurMarketKassa.Views.Dialogs;

public partial class ReceiptPreviewDialog : Window
{
    private readonly string _path;

    public bool IsPrintRequested { get; private set; }

    public ReceiptPreviewDialog(string title, string pdfPath)
    {
        InitializeComponent();
        TxtHeader.Text = title;
        _path = pdfPath;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!File.Exists(_path))
            return;

        PdfBrowser.Navigate(new Uri(Path.GetFullPath(_path)));
    }

    private void BtnPrintToPort_Click(object sender, RoutedEventArgs e)
    {
        IsPrintRequested = true;
        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
