using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace XYDSignTool
{
    /// <summary>
    /// 图纸属性网格编辑器窗口 (纯 C# WPF 编写)
    /// </summary>
    public class SignEditorWindow : Window
    {
        private DataGrid _dataGrid;
        private Button _btnOk;
        private Button _btnCancel;

        public ObservableCollection<SignItem> Items { get; private set; }
        public bool IsConfirmed { get; private set; }

        public SignEditorWindow(List<SignItem> rawItems)
        {
            Items = new ObservableCollection<SignItem>(rawItems);
            IsConfirmed = false;
            InitializeComponent();
        }

        // ==================== WPF 纯代码绘制精美网格 ====================
        private void InitializeComponent()
        {
            this.Title = "标识图纸属性编辑器 - XYD (双击格子即可编辑，仅限当前图纸)";
            this.Width = 980;
            this.Height = 550;
            this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            this.Background = new SolidColorBrush(Color.FromRgb(245, 245, 245));
            this.ShowInTaskbar = false;

            Grid mainGrid = new Grid { Margin = new Thickness(15) };
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 数据表格区
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 按钮区

            // 1. 实例化 DataGrid 核心表格
            _dataGrid = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                ItemsSource = Items,
                GridLinesVisibility = DataGridGridLinesVisibility.All,
                Background = Brushes.White,
                AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(248, 248, 248)),
                HorizontalGridLinesBrush = new SolidColorBrush(Color.FromRgb(225, 225, 225)),
                VerticalGridLinesBrush = new SolidColorBrush(Color.FromRgb(225, 225, 225)),
                RowHeaderWidth = 0 // 隐藏左侧空白列头
            };

            // 2. 动态创建每一列，并绑定到 SignItem 对应的属性 (TwoWay 代表双向修改)
            _dataGrid.Columns.Add(new DataGridTextColumn { Header = "标识区域 (--XYDAREA)", Binding = new Binding("Area") { Mode = BindingMode.TwoWay }, Width = 140 });
            _dataGrid.Columns.Add(new DataGridTextColumn { Header = "编号 (XYD-NUMBER)", Binding = new Binding("No") { Mode = BindingMode.TwoWay }, Width = 110 });
            _dataGrid.Columns.Add(new DataGridTextColumn { Header = "标识名称 (--XYDNAME)", Binding = new Binding("Name") { Mode = BindingMode.TwoWay }, Width = 180 });
            _dataGrid.Columns.Add(new DataGridTextColumn { Header = "安装方式 (XYD-TYPE)", Binding = new Binding("InstallType") { Mode = BindingMode.TwoWay }, Width = 120 });
            _dataGrid.Columns.Add(new DataGridTextColumn { Header = "宽", Binding = new Binding("Width") { Mode = BindingMode.TwoWay }, Width = 50 });
            _dataGrid.Columns.Add(new DataGridTextColumn { Header = "高", Binding = new Binding("Height") { Mode = BindingMode.TwoWay }, Width = 50 });
            _dataGrid.Columns.Add(new DataGridTextColumn { Header = "厚", Binding = new Binding("Thickness") { Mode = BindingMode.TwoWay }, Width = 40 });
            _dataGrid.Columns.Add(new DataGridTextColumn { Header = "重量", Binding = new Binding("Weight") { Mode = BindingMode.TwoWay }, Width = 60 });
            _dataGrid.Columns.Add(new DataGridTextColumn { Header = "数量", Binding = new Binding("Qty") { Mode = BindingMode.TwoWay }, Width = 40 });
            _dataGrid.Columns.Add(new DataGridTextColumn { Header = "工艺 (XYD-TECH)", Binding = new Binding("Tech") { Mode = BindingMode.TwoWay }, Width = 150 });

            Grid.SetRow(_dataGrid, 0);
            mainGrid.Children.Add(_dataGrid);

            // 3. 创建底部按钮
            StackPanel btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 15, 0, 0) };
            _btnOk = new Button { Content = "保存并回写图纸属性", Width = 150, Height = 28, Margin = new Thickness(0, 0, 12, 0), IsDefault = true };
            _btnOk.Click += (s, e) => { IsConfirmed = true; this.Close(); };
            _btnCancel = new Button { Content = "取消编辑", Width = 80, Height = 28, IsCancel = true };
            _btnCancel.Click += (s, e) => { this.Close(); };

            btnPanel.Children.Add(_btnOk);
            btnPanel.Children.Add(_btnCancel);
            Grid.SetRow(btnPanel, 1);
            mainGrid.Children.Add(btnPanel);

            this.Content = mainGrid;
        }
    }
}