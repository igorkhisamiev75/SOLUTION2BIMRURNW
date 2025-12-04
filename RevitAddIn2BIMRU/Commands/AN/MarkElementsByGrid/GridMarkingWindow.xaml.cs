using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace RevitAddIn2BIMRU.Commands.AN
{
    public partial class GridMarkingWindow : Window
    {
        public GridMarkingSettings Settings { get; private set; }
        private int _selectedElementsCount;

        public GridMarkingWindow(int selectedElementsCount)
        {
            try
            {
                InitializeComponent();
                _selectedElementsCount = selectedElementsCount;
                Settings = new GridMarkingSettings();
                UpdateInfoText();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при создании окна: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateInfoText()
        {
            try
            {
                string xDirection = rbXLeftToRight?.IsChecked == true ? "слева направо" : "справа налево";
                string yDirection = rbYBottomToTop?.IsChecked == true ? "снизу вверх" : "сверху вниз";
                string mode = rbByRows?.IsChecked == true ? "по строкам" : "непрерывно по X";

                string orderDescription = (rbByRows?.IsChecked == true)
                    ? $"Сначала {yDirection}, в каждой строке {xDirection}"
                    : $"Сначала {xDirection}, в каждом столбце {yDirection}";

                txtInfo.Text = $"Выбрано элементов: {_selectedElementsCount}\n" +
                              $"Параметр: {txtParameterName?.Text ?? "ADSK_Позиция"}\n" +
                              $"Начальный номер: {txtStartNumber?.Text ?? "1"}\n" +
                              $"Режим: {mode}\n" +
                              $"Порядок: {orderDescription}\n" +
                              $"Точность: максимальная (0.001 мм)";
            }
            catch (Exception ex)
            {
                txtInfo.Text = $"Ошибка обновления информации: {ex.Message}";
            }
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Получаем имя параметра из текстового поля
                string parameterName = txtParameterName?.Text?.Trim();

                if (string.IsNullOrEmpty(parameterName))
                {
                    MessageBox.Show("Введите имя параметра для маркировки", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                Settings.ParameterName = parameterName;

                // Получаем стартовый номер
                string startNumberText = txtStartNumber?.Text ?? "1";
                if (!int.TryParse(startNumberText, out int startNumber) || startNumber < 1)
                {
                    MessageBox.Show("Введите корректный стартовый номер (целое число больше 0)", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                Settings.StartNumber = startNumber;

                // Направление по X
                Settings.XDirection = (rbXLeftToRight?.IsChecked == true) ?
                    MarkElementsByGrid.SortDirection.Ascending :
                    MarkElementsByGrid.SortDirection.Descending;

                // Направление по Y
                Settings.YDirection = (rbYBottomToTop?.IsChecked == true) ?
                    MarkElementsByGrid.SortDirection.Ascending :
                    MarkElementsByGrid.SortDirection.Descending;

                // Режим маркировки
                Settings.MarkByRows = (rbByRows?.IsChecked == true);

                this.DialogResult = true;
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при сохранении настроек: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                this.DialogResult = false;
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при закрытии окна: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                this.Close();
            }
        }

        private void TxtStartNumber_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            try
            {
                // Разрешаем только цифры
                e.Handled = !char.IsDigit(e.Text, 0);
            }
            catch { }
        }

        private void TxtStartNumber_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                UpdateInfoText();
            }
            catch { }
        }

        private void TxtParameterName_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                UpdateInfoText();
            }
            catch { }
        }

        private void RadioButton_Checked(object sender, RoutedEventArgs e)
        {
            try
            {
                UpdateInfoText();
            }
            catch { }
        }
    }
}