using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace XYDSignTool
{
    public class CustomBlockLibraryWindow : Window
    {
        private readonly CustomBlockLibraryCatalog _catalog;
        private ListBox _libraryList;
        private ListView _blockList;
        private TextBox _searchBox;
        private TextBlock _statusText;
        private Button _insertButton;
        private bool _initialPickerShown;

        public CustomBlockDescriptor SelectedBlock { get; private set; }

        public CustomBlockLibraryWindow()
        {
            _catalog = CustomBlockLibraryStore.Load();
            if (CustomBlockLibraryStore.RefreshAll(_catalog, false)) CustomBlockLibraryStore.Save(_catalog);
            InitializeComponent();
            Loaded += CustomBlockLibraryWindow_Loaded;
        }

        private void InitializeComponent()
        {
            Title = "自定义图块库";
            Width = 920;
            Height = 580;
            MinWidth = 760;
            MinHeight = 460;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ShowInTaskbar = false;
            Background = new SolidColorBrush(Color.FromRgb(245, 245, 245));

            Grid root = new Grid { Margin = new Thickness(14) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            StackPanel toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            Button addButton = CreateToolbarButton("添加 DWG", 92);
            addButton.Click += (s, e) => AddLibraries();
            Button removeButton = CreateToolbarButton("移除图库", 92);
            removeButton.Click += (s, e) => RemoveSelectedLibrary();
            Button relinkButton = CreateToolbarButton("重新定位", 92);
            relinkButton.Click += (s, e) => RelinkSelectedLibrary();
            Button refreshButton = CreateToolbarButton("刷新", 76);
            refreshButton.Click += (s, e) => RefreshSelectedLibrary();
            toolbar.Children.Add(addButton);
            toolbar.Children.Add(removeButton);
            toolbar.Children.Add(relinkButton);
            toolbar.Children.Add(refreshButton);
            Grid.SetRow(toolbar, 0);
            root.Children.Add(toolbar);

            Grid body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            GroupBox libraryGroup = new GroupBox { Header = "图库文件", Padding = new Thickness(6) };
            _libraryList = new ListBox
            {
                DisplayMemberPath = "DisplayLabel",
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            _libraryList.SelectionChanged += (s, e) => ReloadBlockList();
            libraryGroup.Content = _libraryList;
            Grid.SetColumn(libraryGroup, 0);
            body.Children.Add(libraryGroup);

            GroupBox blockGroup = new GroupBox { Header = "可插入图块", Padding = new Thickness(6) };
            DockPanel blockPanel = new DockPanel();
            Grid searchGrid = new Grid { Margin = new Thickness(0, 0, 0, 7) };
            searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            TextBlock searchLabel = new TextBlock { Text = "搜索：", VerticalAlignment = VerticalAlignment.Center };
            _searchBox = new TextBox { Height = 26, VerticalContentAlignment = VerticalAlignment.Center };
            _searchBox.TextChanged += (s, e) => ReloadBlockList();
            Grid.SetColumn(searchLabel, 0);
            Grid.SetColumn(_searchBox, 1);
            searchGrid.Children.Add(searchLabel);
            searchGrid.Children.Add(_searchBox);
            DockPanel.SetDock(searchGrid, Dock.Top);
            blockPanel.Children.Add(searchGrid);

            _blockList = new ListView();
            GridView blockView = new GridView();
            blockView.Columns.Add(new GridViewColumn
            {
                Header = "图块名称",
                Width = 320,
                DisplayMemberBinding = new Binding("BlockName")
            });
            blockView.Columns.Add(new GridViewColumn
            {
                Header = "来源 DWG",
                Width = 210,
                DisplayMemberBinding = new Binding("SourceName")
            });
            _blockList.View = blockView;
            _blockList.SelectionChanged += (s, e) => UpdateInsertButtonState();
            _blockList.MouseDoubleClick += BlockList_MouseDoubleClick;
            blockPanel.Children.Add(_blockList);
            blockGroup.Content = blockPanel;
            Grid.SetColumn(blockGroup, 2);
            body.Children.Add(blockGroup);

            Grid.SetRow(body, 1);
            root.Children.Add(body);

            _statusText = new TextBlock
            {
                Margin = new Thickness(0, 10, 0, 0),
                Foreground = Brushes.DimGray,
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetRow(_statusText, 2);
            root.Children.Add(_statusText);

            StackPanel bottom = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0)
            };
            _insertButton = new Button
            {
                Content = "插入选中图块",
                Width = 126,
                Height = 30,
                Margin = new Thickness(0, 0, 10, 0),
                IsDefault = true,
                IsEnabled = false
            };
            _insertButton.Click += (s, e) => ConfirmSelection();
            Button closeButton = new Button { Content = "关闭", Width = 80, Height = 30, IsCancel = true };
            bottom.Children.Add(_insertButton);
            bottom.Children.Add(closeButton);
            Grid.SetRow(bottom, 3);
            root.Children.Add(bottom);

            Content = root;
            ResetLibraryItems(null);
        }

        private Button CreateToolbarButton(string text, double width)
        {
            return new Button
            {
                Content = text,
                Width = width,
                Height = 28,
                Margin = new Thickness(0, 0, 8, 0)
            };
        }

        private void CustomBlockLibraryWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (_initialPickerShown || _catalog.Libraries.Count > 0) return;
            _initialPickerShown = true;
            AddLibraries();
        }

        private void AddLibraries()
        {
            OpenFileDialog dialog = new OpenFileDialog
            {
                Title = "选择自定义图块图库 DWG",
                Filter = "AutoCAD 图纸 (*.dwg)|*.dwg",
                Multiselect = true,
                CheckFileExists = true
            };
            if (dialog.ShowDialog() != true) return;

            CustomBlockLibraryEntry selectEntry = null;
            foreach (string selectedPath in dialog.FileNames)
            {
                string path = CustomBlockLibraryStore.NormalizePath(selectedPath);
                CustomBlockLibraryEntry existing = _catalog.Libraries.FirstOrDefault(item =>
                    string.Equals(CustomBlockLibraryStore.NormalizePath(item.FilePath), path, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    CustomBlockLibraryStore.RefreshEntry(existing, true);
                    selectEntry = existing;
                    continue;
                }

                CustomBlockLibraryEntry newEntry = new CustomBlockLibraryEntry { FilePath = path };
                CustomBlockLibraryStore.RefreshEntry(newEntry, true);
                _catalog.Libraries.Add(newEntry);
                selectEntry = newEntry;
            }

            CustomBlockLibraryStore.Save(_catalog);
            ResetLibraryItems(selectEntry);
        }

        private void RemoveSelectedLibrary()
        {
            CustomBlockLibraryEntry entry = _libraryList.SelectedItem as CustomBlockLibraryEntry;
            if (entry == null) return;

            MessageBoxResult result = MessageBox.Show(
                "确定从插件中移除这个图库吗？\n\n" + entry.FilePath + "\n\n源 DWG 文件不会被删除。",
                "移除自定义图库",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            int oldIndex = _catalog.Libraries.IndexOf(entry);
            _catalog.Libraries.Remove(entry);
            CustomBlockLibraryStore.Save(_catalog);
            CustomBlockLibraryEntry next = _catalog.Libraries.Count == 0
                ? null
                : _catalog.Libraries[Math.Min(oldIndex, _catalog.Libraries.Count - 1)];
            ResetLibraryItems(next);
        }

        private void RelinkSelectedLibrary()
        {
            CustomBlockLibraryEntry entry = _libraryList.SelectedItem as CustomBlockLibraryEntry;
            if (entry == null) return;

            OpenFileDialog dialog = new OpenFileDialog
            {
                Title = "重新定位图库 " + entry.DisplayName,
                Filter = "AutoCAD 图纸 (*.dwg)|*.dwg",
                Multiselect = false,
                CheckFileExists = true,
                FileName = entry.DisplayName
            };
            try
            {
                string directory = Path.GetDirectoryName(entry.FilePath);
                if (Directory.Exists(directory)) dialog.InitialDirectory = directory;
            }
            catch { }

            if (dialog.ShowDialog() != true) return;
            string newPath = CustomBlockLibraryStore.NormalizePath(dialog.FileName);
            CustomBlockLibraryEntry duplicate = _catalog.Libraries.FirstOrDefault(item => item != entry &&
                string.Equals(CustomBlockLibraryStore.NormalizePath(item.FilePath), newPath, StringComparison.OrdinalIgnoreCase));
            if (duplicate != null)
            {
                MessageBox.Show("该 DWG 已经在图库列表中。", "重新定位", MessageBoxButton.OK, MessageBoxImage.Information);
                ResetLibraryItems(duplicate);
                return;
            }

            entry.FilePath = newPath;
            entry.LastWriteTimeUtcTicks = 0;
            CustomBlockLibraryStore.RefreshEntry(entry, true);
            CustomBlockLibraryStore.Save(_catalog);
            ResetLibraryItems(entry);
        }

        private void RefreshSelectedLibrary()
        {
            CustomBlockLibraryEntry entry = _libraryList.SelectedItem as CustomBlockLibraryEntry;
            if (entry == null)
            {
                if (CustomBlockLibraryStore.RefreshAll(_catalog, true)) CustomBlockLibraryStore.Save(_catalog);
                ResetLibraryItems(_catalog.Libraries.FirstOrDefault());
                return;
            }

            CustomBlockLibraryStore.RefreshEntry(entry, true);
            CustomBlockLibraryStore.Save(_catalog);
            ResetLibraryItems(entry);
        }

        private void ResetLibraryItems(CustomBlockLibraryEntry selectedEntry)
        {
            _libraryList.ItemsSource = null;
            _libraryList.ItemsSource = _catalog.Libraries;
            if (selectedEntry != null) _libraryList.SelectedItem = selectedEntry;
            else if (_catalog.Libraries.Count > 0) _libraryList.SelectedIndex = 0;
            else ReloadBlockList();
        }

        private void ReloadBlockList()
        {
            CustomBlockLibraryEntry entry = _libraryList.SelectedItem as CustomBlockLibraryEntry;
            string filter = (_searchBox == null ? "" : _searchBox.Text ?? "").Trim();
            List<CustomBlockDescriptor> blocks = new List<CustomBlockDescriptor>();

            if (entry != null && entry.BlockNames != null)
            {
                blocks = entry.BlockNames
                    .Where(name => string.IsNullOrWhiteSpace(filter) || name.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0)
                    .Select(name => new CustomBlockDescriptor
                    {
                        BlockName = name,
                        SourcePath = entry.FilePath
                    })
                    .ToList();
            }

            _blockList.ItemsSource = blocks;
            if (entry == null)
            {
                _statusText.Text = "请添加一个或多个 DWG 图库文件。";
                _statusText.Foreground = Brushes.DimGray;
            }
            else
            {
                _statusText.Text = entry.FilePath + "\n" + entry.Status;
                _statusText.Foreground = entry.IsAvailable ? Brushes.DimGray : Brushes.Firebrick;
            }
            UpdateInsertButtonState();
        }

        private void UpdateInsertButtonState()
        {
            CustomBlockLibraryEntry entry = _libraryList.SelectedItem as CustomBlockLibraryEntry;
            _insertButton.IsEnabled = entry != null && entry.IsAvailable && _blockList.SelectedItem is CustomBlockDescriptor;
        }

        private void BlockList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (_blockList.SelectedItem is CustomBlockDescriptor) ConfirmSelection();
        }

        private void ConfirmSelection()
        {
            CustomBlockLibraryEntry entry = _libraryList.SelectedItem as CustomBlockLibraryEntry;
            CustomBlockDescriptor block = _blockList.SelectedItem as CustomBlockDescriptor;
            if (entry == null || block == null || !entry.IsAvailable) return;

            SelectedBlock = block;
            try { DialogResult = true; }
            catch { Close(); }
        }
    }
}
