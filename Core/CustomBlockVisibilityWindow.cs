using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace XYDSignTool
{
    public class CustomBlockVisibilityItem : INotifyPropertyChanged
    {
        private bool _isVisible;

        public string BlockName { get; set; }

        public bool IsVisible
        {
            get { return _isVisible; }
            set
            {
                if (_isVisible == value) return;
                _isVisible = value;
                PropertyChangedEventHandler handler = PropertyChanged;
                if (handler != null) handler(this, new PropertyChangedEventArgs("IsVisible"));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    public class CustomBlockVisibilityWindow : Window
    {
        private readonly ObservableCollection<CustomBlockVisibilityItem> _items;
        private readonly ListView _list;
        private readonly TextBox _searchBox;

        public bool Saved { get; private set; }
        public bool ShowAllAutomatically { get; private set; }
        public List<string> SelectedBlockNames { get; private set; } = new List<string>();

        public CustomBlockVisibilityWindow(CustomBlockLibraryEntry entry)
        {
            HashSet<string> visible = new HashSet<string>(
                CustomBlockLibraryStore.GetVisibleBlockNames(entry),
                StringComparer.OrdinalIgnoreCase);
            _items = new ObservableCollection<CustomBlockVisibilityItem>(
                (entry.BlockNames ?? new List<string>()).Select(name => new CustomBlockVisibilityItem
                {
                    BlockName = name,
                    IsVisible = visible.Contains(name)
                }));

            Title = "设置显示图块 - " + entry.DisplayName;
            Width = 620;
            Height = 580;
            MinWidth = 500;
            MinHeight = 420;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ShowInTaskbar = false;

            Grid root = new Grid { Margin = new Thickness(14) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            TextBlock hint = new TextBlock
            {
                Text = "只有勾选的图块会出现在插入列表中。图块内部的依赖块可以取消勾选，不影响主图块正常插入。",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            };
            Grid.SetRow(hint, 0);
            root.Children.Add(hint);

            Grid search = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            search.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            search.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            search.Children.Add(new TextBlock { Text = "搜索：", VerticalAlignment = VerticalAlignment.Center });
            _searchBox = new TextBox { Height = 27, VerticalContentAlignment = VerticalAlignment.Center };
            _searchBox.TextChanged += (s, e) => ReloadItems();
            Grid.SetColumn(_searchBox, 1);
            search.Children.Add(_searchBox);
            Grid.SetRow(search, 1);
            root.Children.Add(search);

            _list = new ListView();
            GridView view = new GridView();
            view.Columns.Add(new GridViewColumn
            {
                Header = "显示",
                Width = 64,
                CellTemplate = CreateCheckBoxTemplate()
            });
            view.Columns.Add(new GridViewColumn
            {
                Header = "图块名称",
                Width = 470,
                DisplayMemberBinding = new Binding("BlockName")
            });
            _list.View = view;
            Grid.SetRow(_list, 2);
            root.Children.Add(_list);

            StackPanel actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 10, 0, 0)
            };
            Button selectAll = CreateButton("全选", 72);
            selectAll.Click += (s, e) => SetFilteredItems(true);
            Button selectNone = CreateButton("全不选", 72);
            selectNone.Click += (s, e) => SetFilteredItems(false);
            Button invert = CreateButton("反选", 72);
            invert.Click += (s, e) => InvertFilteredItems();
            Button automatic = CreateButton("全部自动显示", 120);
            automatic.Click += (s, e) => SaveAutomatic();
            actions.Children.Add(selectAll);
            actions.Children.Add(selectNone);
            actions.Children.Add(invert);
            actions.Children.Add(automatic);
            Grid.SetRow(actions, 3);
            root.Children.Add(actions);

            StackPanel bottom = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0)
            };
            Button save = CreateButton("保存", 86);
            save.IsDefault = true;
            save.Click += (s, e) => SaveSelection();
            Button cancel = CreateButton("取消", 86);
            cancel.IsCancel = true;
            bottom.Children.Add(save);
            bottom.Children.Add(cancel);
            Grid.SetRow(bottom, 4);
            root.Children.Add(bottom);

            Content = root;
            ReloadItems();
        }

        private static DataTemplate CreateCheckBoxTemplate()
        {
            FrameworkElementFactory factory = new FrameworkElementFactory(typeof(CheckBox));
            factory.SetValue(CheckBox.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            factory.SetBinding(CheckBox.IsCheckedProperty, new Binding("IsVisible") { Mode = BindingMode.TwoWay });
            return new DataTemplate { VisualTree = factory };
        }

        private static Button CreateButton(string text, double width)
        {
            return new Button
            {
                Content = text,
                Width = width,
                Height = 28,
                Margin = new Thickness(0, 0, 8, 0)
            };
        }

        private void ReloadItems()
        {
            string filter = (_searchBox == null ? "" : _searchBox.Text ?? "").Trim();
            _list.ItemsSource = _items.Where(item => string.IsNullOrWhiteSpace(filter) ||
                item.BlockName.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0).ToList();
        }

        private IEnumerable<CustomBlockVisibilityItem> GetFilteredItems()
        {
            return (_list.ItemsSource as IEnumerable<CustomBlockVisibilityItem>) ?? Enumerable.Empty<CustomBlockVisibilityItem>();
        }

        private void SetFilteredItems(bool value)
        {
            foreach (CustomBlockVisibilityItem item in GetFilteredItems()) item.IsVisible = value;
        }

        private void InvertFilteredItems()
        {
            foreach (CustomBlockVisibilityItem item in GetFilteredItems()) item.IsVisible = !item.IsVisible;
        }

        private void SaveSelection()
        {
            SelectedBlockNames = _items.Where(item => item.IsVisible).Select(item => item.BlockName).ToList();
            Saved = true;
            ShowAllAutomatically = false;
            DialogResult = true;
        }

        private void SaveAutomatic()
        {
            Saved = true;
            ShowAllAutomatically = true;
            SelectedBlockNames = new List<string>();
            DialogResult = true;
        }
    }
}
