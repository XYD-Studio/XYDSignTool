using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace XYDSignTool
{
    /// <summary>
    /// 大类排序可视化窗口 (纯 C# WPF 编写)
    /// </summary>
    public class AreaSorterWindow : Window
    {
        private ListBox _listBox;
        private Button _btnUp;
        private Button _btnDown;
        private Button _btnOk;
        private Button _btnCancel;

        // 存储排好序的区域列表
        public ObservableCollection<string> Areas { get; private set; }
        // 标记用户是否点击了确定
        public bool IsConfirmed { get; private set; }

        public AreaSorterWindow(List<string> rawAreas)
        {
            // 核心算法复刻：在窗口打开前，先对区域进行智能预排序
            rawAreas.Sort((a, b) => GetHeuristicScore(a).CompareTo(GetHeuristicScore(b)));

            // 绑定到 WPF 的动态观察集合
            Areas = new ObservableCollection<string>(rawAreas);
            IsConfirmed = false;

            // 绘制界面
            InitializeComponent();
        }

        // 预置启发式排序权重
        private int GetHeuristicScore(string name)
        {
            string u = name.ToUpper();
            if (u.Contains("站名")) return 10;
            if (u.Contains("站房")) return 20;
            if (u.Contains("地道")) return 30;
            if (u.Contains("天桥")) return 40;
            if (u.Contains("站台")) return 50;
            return 90;
        }

        // ==================== WPF 纯代码绘制界面 ====================
        private void InitializeComponent()
        {
            // 1. 设置主窗体属性
            this.Title = "调整大类出表顺序 - XYD";
            this.Width = 350;
            this.Height = 400;
            this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            this.ResizeMode = ResizeMode.NoResize;
            this.Background = new SolidColorBrush(Color.FromRgb(245, 245, 245)); // 浅灰色底纹
            this.ShowInTaskbar = false; // 不在 Windows 任务栏显示单独标签

            // 2. 创建主网格布局 (Grid)
            Grid mainGrid = new Grid();
            mainGrid.Margin = new Thickness(15);
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 列表区占满
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 按钮区自适应

            // 3. 创建上半部分布局 (列表 + 上下移动按钮)
            Grid upperGrid = new Grid();
            upperGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            upperGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // 3.1 实例化列表框 (ListBox)
            _listBox = new ListBox();
            _listBox.ItemsSource = Areas;
            _listBox.Margin = new Thickness(0, 0, 15, 0);
            _listBox.SelectedIndex = 0;
            _listBox.SelectionChanged += (s, e) => UpdateButtonStates();
            Grid.SetColumn(_listBox, 0);
            upperGrid.Children.Add(_listBox);

            // 3.2 实例化右侧上下移动控制面板 (StackPanel)
            StackPanel btnPanel = new StackPanel();
            btnPanel.Width = 80;
            btnPanel.VerticalAlignment = VerticalAlignment.Top;

            _btnUp = new Button { Content = "▲ 上移", Height = 35, Margin = new Thickness(0, 0, 0, 12) };
            _btnUp.Click += BtnUp_Click;

            _btnDown = new Button { Content = "▼ 下移", Height = 35, Margin = new Thickness(0, 0, 0, 12) };
            _btnDown.Click += BtnDown_Click;

            btnPanel.Children.Add(_btnUp);
            btnPanel.Children.Add(_btnDown);
            Grid.SetColumn(btnPanel, 1);
            upperGrid.Children.Add(btnPanel);

            Grid.SetRow(upperGrid, 0);
            mainGrid.Children.Add(upperGrid);

            // 4. 创建底部确定/取消面板 (StackPanel)
            StackPanel bottomPanel = new StackPanel();
            bottomPanel.Orientation = Orientation.Horizontal;
            bottomPanel.HorizontalAlignment = HorizontalAlignment.Right;
            bottomPanel.Margin = new Thickness(0, 15, 0, 0);

            _btnOk = new Button { Content = "确定", Width = 80, Height = 28, Margin = new Thickness(0, 0, 12, 0), IsDefault = true };
            _btnOk.Click += (s, e) => { IsConfirmed = true; this.Close(); };

            _btnCancel = new Button { Content = "取消", Width = 80, Height = 28, IsCancel = true };
            _btnCancel.Click += (s, e) => { this.Close(); };

            bottomPanel.Children.Add(_btnOk);
            bottomPanel.Children.Add(_btnCancel);
            Grid.SetRow(bottomPanel, 1);
            mainGrid.Children.Add(bottomPanel);

            // 5. 将主布局塞入窗体
            this.Content = mainGrid;
            UpdateButtonStates();
        }

        // ==================== 按钮交互逻辑 ====================

        private void BtnUp_Click(object sender, RoutedEventArgs e)
        {
            int index = _listBox.SelectedIndex;
            if (index > 0)
            {
                Areas.Move(index, index - 1); // 数据源置换，WPF 界面自动更新
                _listBox.SelectedIndex = index - 1;
            }
        }

        private void BtnDown_Click(object sender, RoutedEventArgs e)
        {
            int index = _listBox.SelectedIndex;
            if (index < Areas.Count - 1)
            {
                Areas.Move(index, index + 1); // 数据源置换
                _listBox.SelectedIndex = index + 1;
            }
        }

        // 动态控制按钮状态，比如顶端的行不能再点"上移"
        private void UpdateButtonStates()
        {
            int index = _listBox.SelectedIndex;
            _btnUp.IsEnabled = index > 0;
            _btnDown.IsEnabled = index >= 0 && index < Areas.Count - 1;
        }
    }
}