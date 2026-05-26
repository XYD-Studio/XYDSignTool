using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Win32;

namespace XYDSignTool
{
    /// <summary>
    /// 单个工艺的价格绑定模型 (支持属性更新通知)
    /// </summary>
    public class PriceItem : INotifyPropertyChanged
    {
        private double _price;
        public string Tech { get; set; }

        public double Price
        {
            get { return _price; }
            set
            {
                _price = value;
                OnPropertyChanged("Price"); // 核心：通知 WPF 界面实时刷新这个格子
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    /// <summary>
    /// 综合单价管理窗口 (纯 C# WPF 编写)
    /// </summary>
    public class PriceManagerWindow : Window
    {
        private ListView _listView;
        private TextBlock _txtSelectedTech;
        private TextBox _edPrice;
        private Button _btnUpdate;
        private Button _btnLoadCsv;
        private Button _btnSaveCsv;
        private Button _btnOk;
        private Button _btnCancel;

        // 绑定到表格的数据源
        public ObservableCollection<PriceItem> PriceItems { get; private set; }
        public bool IsConfirmed { get; private set; }

        public PriceManagerWindow(Dictionary<string, double> currentPrices)
        {
            PriceItems = new ObservableCollection<PriceItem>();
            foreach (var kvp in currentPrices)
            {
                PriceItems.Add(new PriceItem { Tech = kvp.Key, Price = kvp.Value });
            }

            IsConfirmed = false;
            InitializeComponent();
        }

        // ==================== WPF 纯代码绘制界面 ====================
        private void InitializeComponent()
        {
            this.Title = "综合单价管理器 - XYD";
            this.Width = 500;
            this.Height = 500;
            this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            this.ResizeMode = ResizeMode.NoResize;
            this.Background = new SolidColorBrush(Color.FromRgb(245, 245, 245));
            this.ShowInTaskbar = false;

            // 主 Grid 布局
            Grid mainGrid = new Grid { Margin = new Thickness(15) };
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 表格区
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 修改编辑区
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 导入导出及确定取消区

            // 1. 实例化核心表格 (ListView + GridView 组合)
            _listView = new ListView { Margin = new Thickness(0, 0, 0, 15) };
            _listView.SelectionChanged += ListView_SelectionChanged;

            GridView gridView = new GridView();
            // 列 1
            GridViewColumn colTech = new GridViewColumn
            {
                Header = "标识工艺名称",
                Width = 280,
                DisplayMemberBinding = new Binding("Tech")
            };
            // 列 2
            GridViewColumn colPrice = new GridViewColumn
            {
                Header = "综合单价 (元)",
                Width = 140,
                DisplayMemberBinding = new Binding("Price") { StringFormat = "F2" } // 保持 2 位小数
            };
            gridView.Columns.Add(colTech);
            gridView.Columns.Add(colPrice);
            _listView.View = gridView;
            _listView.ItemsSource = PriceItems;

            Grid.SetRow(_listView, 0);
            mainGrid.Children.Add(_listView);

            // 2. 实例化中部修改编辑区 (Boxed Row 视觉效果)
            Border editBorder = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(220, 220, 220)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 15)
            };
            Grid editGrid = new Grid();
            editGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // 工艺名
            editGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 单价输入
            editGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 按钮

            _txtSelectedTech = new TextBlock
            {
                Text = "请从上方列表选择工艺...",
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.Gray,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(_txtSelectedTech, 0);
            editGrid.Children.Add(_txtSelectedTech);

            StackPanel priceInputPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 10, 0) };
            priceInputPanel.Children.Add(new TextBlock { Text = "单价: ", VerticalAlignment = VerticalAlignment.Center });
            _edPrice = new TextBox { Width = 80, Height = 24, VerticalContentAlignment = VerticalAlignment.Center, IsEnabled = false };
            priceInputPanel.Children.Add(_edPrice);
            Grid.SetColumn(priceInputPanel, 1);
            editGrid.Children.Add(priceInputPanel);

            _btnUpdate = new Button { Content = "更新", Width = 60, Height = 24, IsEnabled = false };
            _btnUpdate.Click += BtnUpdate_Click;
            Grid.SetColumn(_btnUpdate, 2);
            editGrid.Children.Add(_btnUpdate);

            editBorder.Child = editGrid;
            Grid.SetRow(editBorder, 1);
            mainGrid.Children.Add(editBorder);

            // 3. 实例化底部控制区 (导入、导出、确定、取消)
            Grid bottomGrid = new Grid();
            bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 外部功能
            bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // 弹簧占位
            bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 提交功能

            // 3.1 外部 CSV 功能组
            StackPanel csvPanel = new StackPanel { Orientation = Orientation.Horizontal };
            _btnLoadCsv = new Button { Content = "导入外部CSV库", Width = 110, Height = 28, Margin = new Thickness(0, 0, 10, 0) };
            _btnLoadCsv.Click += BtnLoadCsv_Click;
            _btnSaveCsv = new Button { Content = "导出单价为CSV", Width = 110, Height = 28 };
            _btnSaveCsv.Click += BtnSaveCsv_Click;
            csvPanel.Children.Add(_btnLoadCsv);
            csvPanel.Children.Add(_btnSaveCsv);
            Grid.SetColumn(csvPanel, 0);
            bottomGrid.Children.Add(csvPanel);

            // 3.2 提交功能组
            StackPanel submitPanel = new StackPanel { Orientation = Orientation.Horizontal };
            _btnOk = new Button { Content = "确定计算", Width = 80, Height = 28, Margin = new Thickness(0, 0, 10, 0), IsDefault = true };
            _btnOk.Click += (s, e) => { IsConfirmed = true; this.Close(); };
            _btnCancel = new Button { Content = "取消", Width = 80, Height = 28, IsCancel = true };
            _btnCancel.Click += (s, e) => { this.Close(); };
            submitPanel.Children.Add(_btnOk);
            submitPanel.Children.Add(_btnCancel);
            Grid.SetColumn(submitPanel, 2);
            bottomGrid.Children.Add(submitPanel);

            Grid.SetRow(bottomGrid, 2);
            mainGrid.Children.Add(bottomGrid);

            this.Content = mainGrid;
        }

        // ==================== 交互控制逻辑 ====================

        private void ListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            PriceItem selected = _listView.SelectedItem as PriceItem;
            if (selected != null)
            {
                _txtSelectedTech.Text = selected.Tech;
                _txtSelectedTech.Foreground = Brushes.Black;
                _edPrice.Text = selected.Price.ToString("F2");
                _edPrice.IsEnabled = true;
                _btnUpdate.IsEnabled = true;
            }
        }

        private void BtnUpdate_Click(object sender, RoutedEventArgs e)
        {
            PriceItem selected = _listView.SelectedItem as PriceItem;
            if (selected != null && double.TryParse(_edPrice.Text, out double newPrice))
            {
                selected.Price = newPrice; // 会瞬间触发 WPF 列表格子的动态更新！
            }
        }

        // 导出 CSV 库 (100% 还原你之前的单价导出结构)
        private void BtnSaveCsv_Click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog sfd = new SaveFileDialog
            {
                Title = "保存工艺单价库",
                Filter = "CSV格式 (*.csv)|*.csv",
                FileName = "XYD工艺造价库.csv"
            };

            if (sfd.ShowDialog() == true)
            {
                try
                {
                    using (StreamWriter sw = new StreamWriter(sfd.FileName, false, System.Text.Encoding.UTF8))
                    {
                        sw.WriteLine("工艺名称,综合单价");
                        foreach (var item in PriceItems)
                        {
                            sw.WriteLine($"{item.Tech},{item.Price.ToString("F2")}");
                        }
                    }
                    MessageBox.Show("工艺单价库导出成功！可以直接双击用 Excel 打开编辑。", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"导出失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        // 导入 CSV 库 (兼容追加与更新逻辑)
        private void BtnLoadCsv_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog
            {
                Title = "导入工艺单价库",
                Filter = "CSV格式 (*.csv)|*.csv"
            };

            if (ofd.ShowDialog() == true)
            {
                try
                {
                    using (StreamReader sr = new StreamReader(ofd.FileName, System.Text.Encoding.UTF8))
                    {
                        sr.ReadLine(); // 跳过第一行表头
                        string line;
                        while ((line = sr.ReadLine()) != null)
                        {
                            string[] parts = line.Split(',');
                            if (parts.Length >= 2 && double.TryParse(parts[1], out double price))
                            {
                                string techName = parts[0].Trim();
                                // 查找当前列表是否已存在该工艺
                                PriceItem existItem = null;
                                foreach (var item in PriceItems)
                                {
                                    if (item.Tech.Equals(techName, StringComparison.OrdinalIgnoreCase))
                                    {
                                        existItem = item;
                                        break;
                                    }
                                }

                                if (existItem != null)
                                    existItem.Price = price; // 更新
                                else
                                    PriceItems.Add(new PriceItem { Tech = techName, Price = price }); // 追加
                            }
                        }
                    }
                    MessageBox.Show("外部单价库导入合并成功！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"导入失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}