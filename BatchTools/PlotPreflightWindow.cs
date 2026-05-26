using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace XYDSignTool
{
    public class MediaInfo
    {
        public string LocalName { get; set; }
        public string CanonicalName { get; set; }
    }

    public class PaperMappingItem : INotifyPropertyChanged
    {
        public string RequestedSize { get; set; }

        private ObservableCollection<MediaInfo> _availableMedia;
        public ObservableCollection<MediaInfo> AvailableMedia
        {
            get { return _availableMedia; }
            set { _availableMedia = value; OnPropertyChanged("AvailableMedia"); }
        }

        private string _selectedCanonicalName;
        public string SelectedCanonicalName
        {
            get { return _selectedCanonicalName; }
            set { _selectedCanonicalName = value; OnPropertyChanged("SelectedCanonicalName"); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class PlotPreflightWindow : Window
    {
        private ComboBox _cboPrinters;
        private ComboBox _cboStyles; // ★ 新增打印样式下拉框
        private ListView _listView;
        private TextBlock _txtWarn;
        private Button _btnOk;
        private Button _btnCancel;
        private Button _btnInjectPaperSizes;
        private Button _btnRefreshPaperSizes;

        public ObservableCollection<string> Printers { get; private set; }
        public ObservableCollection<string> PlotStyles { get; private set; }
        public ObservableCollection<PaperMappingItem> MappingItems { get; private set; }

        public string FinalPrinterName { get; private set; }
        public string FinalStyleSheet { get; private set; } // ★ 抛出最终选择的打印样式
        public Dictionary<string, string> FinalMapping { get; private set; }
        public bool IsConfirmed { get; private set; }

        public PlotPreflightWindow(List<string> printers, string defaultPrinter, List<string> plotStyles, string defaultStyle, List<string> paperSizes)
        {
            Printers = new ObservableCollection<string>(printers);
            PlotStyles = new ObservableCollection<string>(plotStyles);
            MappingItems = new ObservableCollection<PaperMappingItem>();
            foreach (var size in paperSizes)
            {
                MappingItems.Add(new PaperMappingItem { RequestedSize = size });
            }

            IsConfirmed = false;
            FinalMapping = new Dictionary<string, string>();

            InitializeComponent();

            if (Printers.Contains(defaultPrinter)) _cboPrinters.SelectedItem = defaultPrinter;
            else if (Printers.Count > 0) _cboPrinters.SelectedIndex = 0;

            if (PlotStyles.Contains(defaultStyle)) _cboStyles.SelectedItem = defaultStyle;
            else if (PlotStyles.Count > 0) _cboStyles.SelectedIndex = 0;
        }

        private void InitializeComponent()
        {
            this.Title = "打印设置与容错检查";
            this.Width = 700; this.Height = 520;
            this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            this.ResizeMode = ResizeMode.NoResize;
            this.Background = new SolidColorBrush(Color.FromRgb(245, 245, 245));
            this.ShowInTaskbar = false;

            Grid mainGrid = new Grid { Margin = new Thickness(15) };
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) }); // 警告信息
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) }); // 打印机与样式选择
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 映射表格
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) }); // 按钮区

            // 1. 警告提示 (如果没有缺失则隐藏)
            _txtWarn = new TextBlock
            {
                Text = "请选择用户自己的打印机和打印样式。若缺少图纸尺寸，可点击一键注入图纸尺寸。",
                Foreground = Brushes.DimGray,
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 15)
            };
            Grid.SetRow(_txtWarn, 0); mainGrid.Children.Add(_txtWarn);

            // 2. 选择区 (打印机 + 样式)
            StackPanel optionPanel = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 0, 0, 10) };

            StackPanel printerPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            printerPanel.Children.Add(new TextBlock { Text = "打印机设备 (.pc3)：", Width = 130, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.Bold });
            _cboPrinters = new ComboBox { Width = 260, ItemsSource = Printers, VerticalContentAlignment = VerticalAlignment.Center };
            _cboPrinters.SelectionChanged += CboPrinters_SelectionChanged;
            printerPanel.Children.Add(_cboPrinters);
            _btnInjectPaperSizes = new Button { Content = "一键注入图纸尺寸", Width = 130, Height = 26, Margin = new Thickness(10, 0, 0, 0) };
            _btnInjectPaperSizes.Click += BtnInjectPaperSizes_Click;
            printerPanel.Children.Add(_btnInjectPaperSizes);
            _btnRefreshPaperSizes = new Button { Content = "刷新纸张", Width = 90, Height = 26, Margin = new Thickness(8, 0, 0, 0) };
            _btnRefreshPaperSizes.Click += BtnRefreshPaperSizes_Click;
            printerPanel.Children.Add(_btnRefreshPaperSizes);
            optionPanel.Children.Add(printerPanel);

            StackPanel stylePanel = new StackPanel { Orientation = Orientation.Horizontal };
            stylePanel.Children.Add(new TextBlock { Text = "打印样式表 (.ctb)：", Width = 130, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.Bold });
            _cboStyles = new ComboBox { Width = 360, ItemsSource = PlotStyles, VerticalContentAlignment = VerticalAlignment.Center };
            stylePanel.Children.Add(_cboStyles);
            optionPanel.Children.Add(stylePanel);

            Grid.SetRow(optionPanel, 1); mainGrid.Children.Add(optionPanel);

            // 3. 映射列表区
            _listView = new ListView();
            GridView gridView = new GridView();
            gridView.Columns.Add(new GridViewColumn { Header = "图框图幅", Width = 180, DisplayMemberBinding = new Binding("RequestedSize") });

            FrameworkElementFactory cbFactory = new FrameworkElementFactory(typeof(ComboBox));
            cbFactory.SetBinding(ComboBox.ItemsSourceProperty, new Binding("AvailableMedia"));
            cbFactory.SetValue(ComboBox.DisplayMemberPathProperty, "LocalName");
            cbFactory.SetValue(ComboBox.SelectedValuePathProperty, "CanonicalName");
            cbFactory.SetBinding(ComboBox.SelectedValueProperty, new Binding("SelectedCanonicalName") { Mode = BindingMode.TwoWay });
            cbFactory.SetValue(ComboBox.VerticalContentAlignmentProperty, VerticalAlignment.Center);
            cbFactory.SetValue(ComboBox.MarginProperty, new Thickness(2));

            DataTemplate dt = new DataTemplate { VisualTree = cbFactory };
            gridView.Columns.Add(new GridViewColumn { Header = "打印机纸张", Width = 430, CellTemplate = dt });

            _listView.View = gridView;
            _listView.ItemsSource = MappingItems;

            Grid.SetRow(_listView, 2); mainGrid.Children.Add(_listView);

            // 4. 底部按钮
            StackPanel bottomPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 15, 0, 0) };
            _btnOk = new Button { Content = "应用并开始打印", Width = 130, Height = 30, Margin = new Thickness(0, 0, 10, 0), IsDefault = true };
            _btnOk.Click += BtnOk_Click;
            _btnCancel = new Button { Content = "取消", Width = 80, Height = 30, IsCancel = true };
            _btnCancel.Click += (s, e) => this.Close();
            bottomPanel.Children.Add(_btnOk); bottomPanel.Children.Add(_btnCancel);
            Grid.SetRow(bottomPanel, 3); mainGrid.Children.Add(bottomPanel);

            this.Content = mainGrid;
        }

        private void CboPrinters_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_cboPrinters.SelectedItem == null) return;
            string printer = _cboPrinters.SelectedItem.ToString();
            RefreshPaperMapping(printer);
        }

        private int RefreshPaperMapping(string printer)
        {
            var mediaList = NativePlotEngine.GetMediaInfoList(printer);
            var obsMedia = new ObservableCollection<MediaInfo>(mediaList);
            int missingCount = 0;

            foreach (var item in MappingItems)
            {
                item.AvailableMedia = obsMedia;
                item.SelectedCanonicalName = NativePlotEngine.TryMatchMedia(item.RequestedSize, mediaList);
                if (string.IsNullOrEmpty(item.SelectedCanonicalName)) missingCount++;
            }

            _txtWarn.Text = missingCount == 0
                ? "当前打印机已匹配全部图纸尺寸，请确认打印样式后开始打印。"
                : $"当前打印机缺少 {missingCount} 个图纸尺寸。可点击一键注入图纸尺寸，或手动指定替代纸张。";
            _txtWarn.Foreground = missingCount == 0 ? Brushes.Green : Brushes.Red;
            return missingCount;
        }

        private void BtnRefreshPaperSizes_Click(object sender, RoutedEventArgs e)
        {
            if (_cboPrinters.SelectedItem == null) return;
            RefreshPaperMapping(_cboPrinters.SelectedItem.ToString());
        }

        private void BtnInjectPaperSizes_Click(object sender, RoutedEventArgs e)
        {
            if (_cboPrinters.SelectedItem == null)
            {
                MessageBox.Show("请先选择一个打印机设备。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string printer = _cboPrinters.SelectedItem.ToString();
            MessageBoxResult confirm = MessageBox.Show(
                $"将把插件内置的 PMP 图纸尺寸模板注入到当前打印机：\n\n{printer}\n\n如果该打印机已有同名 PMP，程序会先自动备份再覆盖。是否继续？",
                "一键注入图纸尺寸",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;

            string message;
            bool ok = PlotPaperSizeInjector.InjectPaperSizesForPrinter(printer, out message);
            if (ok)
            {
                int missingCount = RefreshPaperMapping(printer);
                if (missingCount > 0)
                {
                    message += $"\n\n已自动刷新当前列表，但 AutoCAD 仍报告缺少 {missingCount} 个图纸尺寸。可以再点一次“刷新纸张”；若仍看不到，请关闭本窗口重新进入批量打印，或打开该 PC3 的打印机特性后点确定让 AutoCAD 重载 PMP。";
                }
            }
            MessageBox.Show(message, ok ? "注入完成" : "注入失败", MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            FinalPrinterName = _cboPrinters.SelectedItem?.ToString();
            FinalStyleSheet = _cboStyles.SelectedItem?.ToString(); // ★ 获取样式表
            FinalMapping.Clear();

            if (string.IsNullOrEmpty(FinalPrinterName))
            {
                MessageBox.Show("请选择打印机设备。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(FinalStyleSheet))
            {
                MessageBox.Show("请选择打印样式表。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            foreach (var item in MappingItems)
            {
                if (string.IsNullOrEmpty(item.SelectedCanonicalName))
                {
                    MessageBox.Show($"请为缺失尺寸 '{item.RequestedSize}' 指定替代纸张！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                FinalMapping[item.RequestedSize] = item.SelectedCanonicalName;
            }
            IsConfirmed = true;
            this.Close();
        }
    }
}
