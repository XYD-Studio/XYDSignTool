using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace XYDSignTool
{
    public class DirColumnDef
    {
        public string Header { get; set; }
        public string BindingPath { get; set; }
        public double DefaultWidth { get; set; }
    }

    public class DirectoryEditorWindow : Window
    {
        private DataGrid _dataGrid;
        private CheckBox _chkScale, _chkVersion, _chkDate, _chkRemarks;
        private DataGridTextColumn _colScale, _colVersion, _colDate, _colRemarks;

        public ObservableCollection<TitleBlockModel> Items { get; private set; }
        public List<DirColumnDef> FinalColumns { get; private set; }
        public bool IsConfirmed { get; private set; }
        public string ActionName { get; private set; }
        public event Action GenerateInDrawingRequested;
        public event Action ExportExcelRequested;
        public event Action ExportJsonRequested;
        public event Action ImportJsonRequested;

        public DirectoryEditorWindow(List<TitleBlockModel> data, string actionName)
        {
            Items = new ObservableCollection<TitleBlockModel>(data);
            ActionName = actionName;
            IsConfirmed = false;
            FinalColumns = new List<DirColumnDef>();
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Title = "图纸目录排版编辑器 - " + ActionName;
            this.Width = 1120; this.Height = 550;
            this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            this.Background = new SolidColorBrush(Color.FromRgb(245, 245, 245));
            this.ShowInTaskbar = false;

            Grid mainGrid = new Grid { Margin = new Thickness(15) };
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // 1. 顶部控制面板
            StackPanel topPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            topPanel.Children.Add(new TextBlock { Text = "可选字段：", VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 10, 0) });

            _chkScale = new CheckBox { Content = "比例", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            _chkVersion = new CheckBox { Content = "版本", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            _chkDate = new CheckBox { Content = "日期", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            _chkRemarks = new CheckBox { Content = "备注", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 30, 0) };

            _chkScale.Checked += (s, e) => _colScale.Visibility = Visibility.Visible;
            _chkScale.Unchecked += (s, e) => _colScale.Visibility = Visibility.Collapsed;
            _chkVersion.Checked += (s, e) => _colVersion.Visibility = Visibility.Visible;
            _chkVersion.Unchecked += (s, e) => _colVersion.Visibility = Visibility.Collapsed;
            _chkDate.Checked += (s, e) => _colDate.Visibility = Visibility.Visible;
            _chkDate.Unchecked += (s, e) => _colDate.Visibility = Visibility.Collapsed;
            _chkRemarks.Checked += (s, e) => _colRemarks.Visibility = Visibility.Visible;
            _chkRemarks.Unchecked += (s, e) => _colRemarks.Visibility = Visibility.Collapsed;

            topPanel.Children.Add(_chkScale); topPanel.Children.Add(_chkVersion);
            topPanel.Children.Add(_chkDate); topPanel.Children.Add(_chkRemarks);

            // ==========================================
            // ★ 批量填入小工具 (新增填写备注)
            // ==========================================
            TextBox txtBatchFill = new TextBox { Width = 100, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) };
            Button btnFillDate = new Button { Content = "填日期", Padding = new Thickness(5, 2, 5, 2), Margin = new Thickness(0, 0, 5, 0) };
            Button btnFillVersion = new Button { Content = "填版本", Padding = new Thickness(5, 2, 5, 2), Margin = new Thickness(0, 0, 5, 0) };
            Button btnFillRemarks = new Button { Content = "填备注", Padding = new Thickness(5, 2, 5, 2), Margin = new Thickness(0, 0, 5, 0) }; // ★ 新增按钮

            btnFillDate.Click += (s, e) => { foreach (var item in Items) item.Date = txtBatchFill.Text; };
            btnFillVersion.Click += (s, e) => { foreach (var item in Items) item.Version = txtBatchFill.Text; };
            btnFillRemarks.Click += (s, e) => { foreach (var item in Items) item.Remarks = txtBatchFill.Text; }; // ★ 新增逻辑

            topPanel.Children.Add(new TextBlock { Text = " 批量填入值：", VerticalAlignment = VerticalAlignment.Center });
            topPanel.Children.Add(txtBatchFill);
            topPanel.Children.Add(btnFillDate);
            topPanel.Children.Add(btnFillVersion);
            topPanel.Children.Add(btnFillRemarks);

            Grid.SetRow(topPanel, 0); mainGrid.Children.Add(topPanel);

            // 2. 核心网格 
            _dataGrid = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                CanUserReorderColumns = true,
                ItemsSource = Items,
                GridLinesVisibility = DataGridGridLinesVisibility.All,
                Background = Brushes.White,
                AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(248, 248, 248)),
                RowHeaderWidth = 0
            };

            _dataGrid.Columns.Add(new DataGridTextColumn { Header = "图号", Binding = new Binding("DrawNum") { Mode = BindingMode.TwoWay }, Width = 140 });
            _dataGrid.Columns.Add(new DataGridTextColumn { Header = "图纸名称", Binding = new Binding("DrawTitle") { Mode = BindingMode.TwoWay }, Width = 280 });
            _dataGrid.Columns.Add(new DataGridTextColumn { Header = "图幅", Binding = new Binding("PageSize") { Mode = BindingMode.TwoWay }, Width = 80 });

            _colScale = new DataGridTextColumn { Header = "比例", Binding = new Binding("DrawScale") { Mode = BindingMode.TwoWay }, Width = 80, Visibility = Visibility.Collapsed };
            _colVersion = new DataGridTextColumn { Header = "版本", Binding = new Binding("Version") { Mode = BindingMode.TwoWay }, Width = 80, Visibility = Visibility.Collapsed };
            _colDate = new DataGridTextColumn { Header = "日期", Binding = new Binding("Date") { Mode = BindingMode.TwoWay }, Width = 120, Visibility = Visibility.Collapsed };
            _colRemarks = new DataGridTextColumn { Header = "备注", Binding = new Binding("Remarks") { Mode = BindingMode.TwoWay }, Width = 150, Visibility = Visibility.Collapsed };

            _dataGrid.Columns.Add(_colScale); _dataGrid.Columns.Add(_colVersion);
            _dataGrid.Columns.Add(_colDate); _dataGrid.Columns.Add(_colRemarks);

            Grid.SetRow(_dataGrid, 1); mainGrid.Children.Add(_dataGrid);

            // 3. 底部按钮
            StackPanel bottomPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 15, 0, 0) };
            bottomPanel.Children.Add(new TextBlock { Text = "提示：按住表头左右拖动，即可调整出表的列顺序。", Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 20, 0) });
            Button btnImportJson = new Button { Content = "导入 JSON", Width = 100, Height = 30, Margin = new Thickness(0, 0, 10, 0) };
            btnImportJson.Click += (s, e) => ImportJsonRequested?.Invoke();
            Button btnCad = new Button { Content = "在图纸中生成", Width = 120, Height = 30, Margin = new Thickness(0, 0, 10, 0), IsDefault = true };
            btnCad.Click += (s, e) => { if (BuildFinalColumns()) GenerateInDrawingRequested?.Invoke(); };
            Button btnExcel = new Button { Content = "导出 Excel", Width = 100, Height = 30, Margin = new Thickness(0, 0, 10, 0) };
            btnExcel.Click += (s, e) => { if (BuildFinalColumns()) ExportExcelRequested?.Invoke(); };
            Button btnJson = new Button { Content = "导出 JSON", Width = 100, Height = 30, Margin = new Thickness(0, 0, 10, 0) };
            btnJson.Click += (s, e) => { if (BuildFinalColumns()) ExportJsonRequested?.Invoke(); };
            Button btnExit = new Button { Content = "退出", Width = 80, Height = 30, IsCancel = true };
            btnExit.Click += (s, e) => this.Close();

            bottomPanel.Children.Add(btnImportJson); bottomPanel.Children.Add(btnCad); bottomPanel.Children.Add(btnExcel); bottomPanel.Children.Add(btnJson); bottomPanel.Children.Add(btnExit);
            Grid.SetRow(bottomPanel, 2); mainGrid.Children.Add(bottomPanel);

            this.Content = mainGrid;
        }

        public void ApplyImportedItems(List<TitleBlockModel> importedItems, List<string> importedFields)
        {
            foreach (TitleBlockModel item in importedItems)
            {
                Items.Add(item);
            }

            bool hasScale = importedFields.Contains("DrawScale");
            bool hasVersion = importedFields.Contains("Version");
            bool hasDate = importedFields.Contains("Date");
            bool hasRemarks = importedFields.Contains("Remarks");

            _chkScale.IsChecked = _chkScale.IsChecked == true || hasScale;
            _chkVersion.IsChecked = _chkVersion.IsChecked == true || hasVersion;
            _chkDate.IsChecked = _chkDate.IsChecked == true || hasDate;
            _chkRemarks.IsChecked = _chkRemarks.IsChecked == true || hasRemarks;
            _dataGrid.Items.Refresh();
        }

        private bool BuildFinalColumns()
        {
            _dataGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            _dataGrid.CommitEdit(DataGridEditingUnit.Row, true);
            FinalColumns.Clear();

            var visibleCols = _dataGrid.Columns
                .Where(c => c.Visibility == Visibility.Visible)
                .OrderBy(c => c.DisplayIndex)
                .ToList();

            FinalColumns.Add(new DirColumnDef { Header = "序号", BindingPath = "INDEX", DefaultWidth = 12.0 });

            foreach (var col in visibleCols)
            {
                string header = col.Header.ToString();
                string path = ((Binding)((DataGridTextColumn)col).Binding).Path.Path;

                double cadWidth = 15.0;
                if (header == "图纸名称") cadWidth = 60.0;
                else if (header == "图号") cadWidth = 25.0;
                else if (header == "日期") cadWidth = 20.0;
                else if (header == "备注") cadWidth = 30.0;

                FinalColumns.Add(new DirColumnDef { Header = header, BindingPath = path, DefaultWidth = cadWidth });
            }

            IsConfirmed = true;
            return true;
        }
    }
}
