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
    public enum FindSameCriterionKind
    {
        ObjectType,
        Layer,
        BlockName,
        Linetype,
        Color,
        LineWeight,
        Transparency,
        TextStyle,
        DimensionStyle,
        HatchPattern
    }

    public enum FindSameScope
    {
        CurrentSpace,
        EntireDrawing,
        ManualSelection
    }

    public class FindSameCriterion : INotifyPropertyChanged
    {
        private bool _isSelected;

        public FindSameCriterionKind Kind { get; set; }
        public string Name { get; set; }
        public string Value { get; set; }

        public bool IsSelected
        {
            get { return _isSelected; }
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                PropertyChangedEventHandler handler = PropertyChanged;
                if (handler != null) handler(this, new PropertyChangedEventArgs("IsSelected"));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    public class FindSameWindow : Window
    {
        private readonly ObservableCollection<FindSameCriterion> _criteria;
        private readonly RadioButton _currentSpaceRadio;
        private readonly RadioButton _entireDrawingRadio;

        public List<FindSameCriterionKind> SelectedCriteria { get; private set; } = new List<FindSameCriterionKind>();
        public FindSameScope Scope { get; private set; }

        public FindSameWindow(IEnumerable<FindSameCriterion> criteria)
        {
            _criteria = new ObservableCollection<FindSameCriterion>(criteria ?? Enumerable.Empty<FindSameCriterion>());
            Title = "查找相同";
            Width = 620;
            Height = 570;
            MinWidth = 520;
            MinHeight = 450;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ShowInTaskbar = false;

            Grid root = new Grid { Margin = new Thickness(14) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            TextBlock hint = new TextBlock
            {
                Text = "勾选需要比较的属性。选择多项时，目标对象必须同时满足全部条件。",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            };
            root.Children.Add(hint);

            ListView list = new ListView { ItemsSource = _criteria };
            GridView view = new GridView();
            view.Columns.Add(new GridViewColumn { Header = "使用", Width = 64, CellTemplate = CreateCheckBoxTemplate() });
            view.Columns.Add(new GridViewColumn { Header = "属性", Width = 150, DisplayMemberBinding = new Binding("Name") });
            view.Columns.Add(new GridViewColumn { Header = "样本值", Width = 340, DisplayMemberBinding = new Binding("Value") });
            list.View = view;
            Grid.SetRow(list, 1);
            root.Children.Add(list);

            GroupBox scopeGroup = new GroupBox
            {
                Header = "查找范围",
                Padding = new Thickness(10, 7, 10, 9),
                Margin = new Thickness(0, 10, 0, 0)
            };
            StackPanel scopePanel = new StackPanel { Orientation = Orientation.Horizontal };
            _currentSpaceRadio = new RadioButton { Content = "当前空间", Margin = new Thickness(0, 0, 20, 0) };
            _entireDrawingRadio = new RadioButton { Content = "整张 DWG", IsChecked = true, Margin = new Thickness(0, 0, 20, 0) };
            scopePanel.Children.Add(_currentSpaceRadio);
            scopePanel.Children.Add(_entireDrawingRadio);
            scopePanel.Children.Add(new RadioButton { Content = "手动选择范围" });
            scopeGroup.Content = scopePanel;
            Grid.SetRow(scopeGroup, 2);
            root.Children.Add(scopeGroup);

            TextBlock rangeHint = new TextBlock
            {
                Text = "整张 DWG 会扫描模型空间和所有布局；当前画面只能高亮当前空间中的匹配对象。",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 9, 0, 0)
            };
            Grid.SetRow(rangeHint, 3);
            root.Children.Add(rangeHint);

            StackPanel bottom = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0)
            };
            Button selectButton = CreateButton("选中相同对象", 116);
            selectButton.IsDefault = true;
            selectButton.Click += (s, e) => Confirm();
            Button cancelButton = CreateButton("取消", 82);
            cancelButton.IsCancel = true;
            bottom.Children.Add(selectButton);
            bottom.Children.Add(cancelButton);
            Grid.SetRow(bottom, 4);
            root.Children.Add(bottom);

            Content = root;
        }

        private static DataTemplate CreateCheckBoxTemplate()
        {
            FrameworkElementFactory factory = new FrameworkElementFactory(typeof(CheckBox));
            factory.SetValue(CheckBox.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            factory.SetBinding(CheckBox.IsCheckedProperty, new Binding("IsSelected") { Mode = BindingMode.TwoWay });
            return new DataTemplate { VisualTree = factory };
        }

        private static Button CreateButton(string text, double width)
        {
            return new Button
            {
                Content = text,
                Width = width,
                Height = 29,
                Margin = new Thickness(0, 0, 8, 0)
            };
        }

        private void Confirm()
        {
            SelectedCriteria = _criteria.Where(item => item.IsSelected).Select(item => item.Kind).ToList();
            if (SelectedCriteria.Count == 0)
            {
                MessageBox.Show("请至少勾选一个比较属性。", "查找相同", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Scope = _currentSpaceRadio.IsChecked == true
                ? FindSameScope.CurrentSpace
                : _entireDrawingRadio.IsChecked == true
                    ? FindSameScope.EntireDrawing
                    : FindSameScope.ManualSelection;
            DialogResult = true;
        }
    }
}
