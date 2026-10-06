using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

using dosymep.Bim4Everyone.SimpleServices;
using dosymep.SimpleServices;
using dosymep.WpfUI.Core;

using Juru.RevitPlugins.ClashNavigator.Models;
using Juru.RevitPlugins.ClashNavigator.Models.Clashes;
using Juru.RevitPlugins.ClashNavigator.ViewModels.Navigator;

using Visibility = System.Windows.Visibility;

namespace Juru.RevitPlugins.ClashNavigator.Views.Navigator
{
    public partial class NavigatorWindow : WpfUIPlatformWindow
    {
        private Window _imagePreviewWindow;
        private Image _imagePreviewImage;
        private bool _columnSettingsApplied;
        private bool _startupLoadRequested;

        public NavigatorWindow(
            ILoggerService loggerService,
            ISerializationService serializationService,
            ILanguageService languageService, ILocalizationService localizationService,
            IUIThemeService uiThemeService, IUIThemeUpdaterService themeUpdaterService)
            : base(loggerService, serializationService, languageService, localizationService,
                uiThemeService, themeUpdaterService)
        {
            InitializeComponent();
        }

        // Used by the legacy ClashDetective.CheckViewModel.ShowClashes() path, which constructs this
        // window directly instead of through the Ninject kernel. Theme/localization services stay
        // unavailable on this path (same as before WpfUIPlatformWindow was introduced).
        public NavigatorWindow()
        {
            InitializeComponent();
        }

        public override string PluginName => "Навигатор коллизий";
        public override string ProjectConfigName => nameof(NavigatorWindow);

        private void ButtonCancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        // Ширину и видимость колонок восстанавливаем на Loaded: DataContext на устаревшем пути
        // (CheckViewModel.ShowClashes) назначается уже после конструктора окна.
        private void ClashesGrid_Loaded(object sender, RoutedEventArgs e)
        {
            // Заголовки переводятся кодом: DataGridColumn не входит в дерево элементов,
            // и поиск ресурса по ключу из разметки для него не разрешается.
            ColumnLocalization.Apply(ClashesGrid, LocalizationService);

            if (_columnSettingsApplied || DataContext is not NavigatorViewModel viewModel)
            {
                return;
            }

            _columnSettingsApplied = true;
            var settings = viewModel.GetColumnSettings();
            foreach (var column in ClashesGrid.Columns)
            {
                if (GetColumnKey(column) is not string key || !settings.TryGetValue(key, out var columnSettings))
                {
                    continue;
                }

                if (columnSettings.Width > 0)
                {
                    column.Width = new DataGridLength(columnSettings.Width, columnSettings.IsStarWidth
                        ? DataGridLengthUnitType.Star
                        : DataGridLengthUnitType.Pixel);
                }

                column.Visibility = columnSettings.IsVisible ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        // Закрытие фильтра щелчком вне него применяет отмеченные значения: без этого галочки
        // оставались снятыми, а таблица - неотфильтрованной, и фильтр выглядел неработающим.
        private void FilterPopup_Closed(object sender, EventArgs e)
        {
            if (sender is Popup popup
                && popup.DataContext is ColumnFilterViewModel filter
                && filter.ApplyCommand.CanExecute(null))
            {
                filter.ApplyCommand.Execute(null);
            }
        }

        // Статус коллизии пишем в модель здесь, а не двусторонней привязкой SelectedValue:
        // в шаблоне ячейки DataGrid запись в источник не доходила, и выбранный статус оставался
        // только в самом списке - счётчик изменений и колонка "Кем изменён" его не видели.
        private void StatusBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is not ComboBox box
                || box.DataContext is not IClashViewModel clash
                || box.SelectedValue is not ClashStatus status
                || clash.ClashStatus == status)
            {
                return;
            }

            clash.ClashStatus = status;

            // Если модель выбор не приняла, возвращаем список к её значению: пусть расхождение
            // будет видно сразу, а не оборачивается молчаливым "статус изменён, но никуда не идёт".
            if (clash.ClashStatus != status)
            {
                box.SelectedValue = clash.ClashStatus;
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (_startupLoadRequested || DataContext is not NavigatorViewModel viewModel)
            {
                return;
            }

            _startupLoadRequested = true;

            // Загрузка с сервера синхронная и на большом отчёте заметно долгая, поэтому запускаем её
            // фоновым приоритетом: окно успевает отрисоваться до того, как поток UI будет занят.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                Mouse.OverrideCursor = Cursors.Wait;
                try
                {
                    if (viewModel.LoadFromServerOnStartupCommand.CanExecute(null))
                    {
                        viewModel.LoadFromServerOnStartupCommand.Execute(null);
                    }
                }
                finally
                {
                    Mouse.OverrideCursor = null;
                }
            }), DispatcherPriority.Background);
        }

        // Изменённые статусы уходят на сервер по кнопке сохранения, поэтому перед закрытием
        // предлагаем сохранить: иначе правки пользователя потерялись бы молча.
        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (DataContext is not NavigatorViewModel viewModel)
            {
                return;
            }

            if (viewModel.AskForSaveCommand.CanExecute(null))
            {
                viewModel.AskForSaveCommand.Execute(null);
            }
        }

        /// <summary>
        /// F3 - следующая коллизия, Shift+F3 - предыдущая: выделяет строку и показывает
        /// коллизию в Ревите. Коллизии, которые нельзя показать (элементы не найдены), пропускаются.
        /// </summary>
        private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key != System.Windows.Input.Key.F3 || DataContext is not NavigatorViewModel viewModel)
            {
                return;
            }

            int step = (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Shift) != 0 ? -1 : 1;
            var items = ClashesGrid.Items;
            int index = ClashesGrid.SelectedIndex + step;
            while (index >= 0 && index < items.Count
                   && !(items[index] is ViewModels.Navigator.ClashViewModel { IsResolved: true }))
            {
                index += step;
            }
            if (index < 0 || index >= items.Count)
            {
                return;
            }

            e.Handled = true;
            ClashesGrid.SelectedIndex = index;
            ClashesGrid.ScrollIntoView(items[index]);
            if (viewModel.SelectClashCommand.CanExecute(items[index]))
            {
                viewModel.SelectClashCommand.Execute(items[index]);
            }
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            if (DataContext is not NavigatorViewModel viewModel)
            {
                return;
            }

            viewModel.ClearHighlight();

            var saved = viewModel.GetColumnSettings();
            var settings = new Dictionary<string, ClashColumnSettings>();
            foreach (var column in ClashesGrid.Columns)
            {
                if (GetColumnKey(column) is not string key)
                {
                    continue;
                }

                bool isStarWidth = column.Width.IsStar;
                double width = isStarWidth ? column.Width.Value : column.ActualWidth;
                if (width <= 0 && saved.TryGetValue(key, out var previous))
                {
                    // У скрытой колонки ActualWidth равен нулю - сохраняем ширину с прошлого раза.
                    isStarWidth = previous.IsStarWidth;
                    width = previous.Width;
                }

                settings[key] = new ClashColumnSettings
                {
                    Width = width,
                    IsStarWidth = isStarWidth,
                    IsVisible = column.Visibility == Visibility.Visible
                };
            }

            try
            {
                viewModel.SaveColumnSettings(settings);
            }
            catch (Exception)
            {
                // Настройки колонок - удобство отображения: ошибка записи файла не должна ломать закрытие окна.
            }
        }

        // Ключ - SortMemberPath, а не заголовок: заголовки переводятся, и настройки колонок,
        // записанные на одном языке, к тем же колонкам на другом уже не подошли бы.
        private static string GetColumnKey(DataGridColumn column)
        {
            string key = column.SortMemberPath;
            return string.IsNullOrWhiteSpace(key) ? null : key;
        }

        private void ClashImage_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Image image || image.Source is null)
            {
                return;
            }

            if (_imagePreviewWindow is null)
            {
                _imagePreviewImage = new Image
                {
                    Stretch = Stretch.Uniform,
                    Margin = new Thickness(10)
                };
                _imagePreviewWindow = new Window
                {
                    Title = "Снимок коллизии",
                    Content = _imagePreviewImage,
                    Width = 900,
                    Height = 700,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    Owner = this
                };
                _imagePreviewWindow.Closed += (_, _) => _imagePreviewWindow = null;
            }

            _imagePreviewImage.Source = image.Source;

            if (_imagePreviewWindow.IsVisible)
            {
                _imagePreviewWindow.Activate();
            }
            else
            {
                _imagePreviewWindow.Show();
            }
        }
    }
}
