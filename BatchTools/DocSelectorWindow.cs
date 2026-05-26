using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace XYDSignTool
{
    public class DocSelectorWindow : Window
    {
        private ListBox _listBox;
        public ObservableCollection<string> Docs { get; private set; }
        public List<string> SelectedDocs { get; private set; }
        public bool IsConfirmed { get; private set; }

        public DocSelectorWindow(List<string> openDocs)
        {
            Docs = new ObservableCollection<string>(openDocs);
            SelectedDocs = new List<string>();
            IsConfirmed = false;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Title = "请勾选要批量处理的图纸";
            this.Width = 400; this.Height = 350;
            this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            this.Background = new SolidColorBrush(Color.FromRgb(245, 245, 245));
            this.ShowInTaskbar = false;

            Grid mainGrid = new Grid { Margin = new Thickness(15) };
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _listBox = new ListBox
            {
                ItemsSource = Docs,
                SelectionMode = SelectionMode.Multiple, // 允许按住 Ctrl 或拖动多选
                Margin = new Thickness(0, 0, 0, 15)
            };
            Grid.SetRow(_listBox, 0);
            mainGrid.Children.Add(_listBox);

            Grid bottomGrid = new Grid();
            bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel leftPanel = new StackPanel { Orientation = Orientation.Horizontal };
            Button btnAll = new Button { Content = "全选", Width = 60, Margin = new Thickness(0, 0, 10, 0) };
            btnAll.Click += (s, e) => { _listBox.SelectAll(); };
            Button btnNone = new Button { Content = "全不选", Width = 60 };
            btnNone.Click += (s, e) => { _listBox.UnselectAll(); };
            leftPanel.Children.Add(btnAll); leftPanel.Children.Add(btnNone);
            Grid.SetColumn(leftPanel, 0); bottomGrid.Children.Add(leftPanel);

            StackPanel rightPanel = new StackPanel { Orientation = Orientation.Horizontal };
            Button btnOk = new Button { Content = "确定", Width = 80, Margin = new Thickness(0, 0, 10, 0), IsDefault = true };
            btnOk.Click += BtnOk_Click;
            Button btnCancel = new Button { Content = "取消", Width = 80, IsCancel = true };
            btnCancel.Click += (s, e) => { this.Close(); };
            rightPanel.Children.Add(btnOk); rightPanel.Children.Add(btnCancel);
            Grid.SetColumn(rightPanel, 2); bottomGrid.Children.Add(rightPanel);

            Grid.SetRow(bottomGrid, 1);
            mainGrid.Children.Add(bottomGrid);
            this.Content = mainGrid;
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _listBox.SelectedItems)
            {
                SelectedDocs.Add(item.ToString());
            }
            IsConfirmed = true;
            this.Close();
        }
    }
}