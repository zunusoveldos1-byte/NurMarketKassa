using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NurMarketKassa.Configuration;
using NurMarketKassa.Services;

namespace NurMarketKassa.Views
{
    public partial class ServicesWindow : Window
    {
        public ServicesWindow()
        {
            InitializeComponent();

            if (UserPreferences.Instance.Fullscreen)
            {
                WindowStyle = WindowStyle.None;
                WindowState = WindowState.Maximized;
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e) { }

        private void Back_Click(object sender, RoutedEventArgs e) => Close();

        private void Window_ManipulationBoundaryFeedback(object sender, ManipulationBoundaryFeedbackEventArgs e) =>
            e.Handled = true;

        // Hover-эффекты для карточек
        private void Card_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is Border border)
            {
                border.BorderBrush = new SolidColorBrush(Color.FromRgb(30, 58, 95)); // акцентный цвет
                border.BorderThickness = new Thickness(1.5);
            }
        }

        private void Card_MouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is Border border)
            {
                border.BorderBrush = (Brush)FindResource("BrushBorder");
                border.BorderThickness = new Thickness(1);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}