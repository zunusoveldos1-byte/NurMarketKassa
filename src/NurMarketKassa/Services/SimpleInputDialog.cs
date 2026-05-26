// SimpleInputDialog.cs
using System.Windows;
using System.Windows.Controls;

namespace NurMarketKassa.Views
{
    public class SimpleInputDialog : Window
    {
        public string Result { get; private set; } = "";
        private readonly TextBox _textBox;

        public SimpleInputDialog(string title, string message, bool optional = false)
        {
            Title = title;
            Width = 350;
            Height = 200;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;

            var grid = new Grid { Margin = new Thickness(16) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var msgText = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
            _textBox = new TextBox { Height = 28 };
            var btnOk = new Button { Content = optional ? "Пропустить" : "OK", IsDefault = true, Width = 100, Height = 28, Margin = new Thickness(0, 8, 0, 0) };
            btnOk.Click += (s, e) =>
            {
                Result = _textBox.Text;
                DialogResult = true;
            };

            if (!optional)
            {
                var btnCancel = new Button { Content = "Отмена", IsCancel = true, Width = 100, Height = 28, Margin = new Thickness(8, 8, 0, 0) };
                grid.Children.Add(btnCancel);
                Grid.SetRow(btnCancel, 2);
            }

            grid.Children.Add(msgText);
            grid.Children.Add(_textBox);
            grid.Children.Add(btnOk);
            Grid.SetRow(msgText, 0);
            Grid.SetRow(_textBox, 1);
            Grid.SetRow(btnOk, 2);

            Content = grid;
        }
    }
}