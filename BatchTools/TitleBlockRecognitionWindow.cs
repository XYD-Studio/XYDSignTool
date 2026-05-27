using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace XYDSignTool
{
    public class TitleBlockRecognitionWindow : Window
    {
        private readonly ObservableCollection<TitleBlockRecognitionRule> _rules;
        private DataGrid _grid;

        public TitleBlockRecognitionWindow()
        {
            TitleBlockRecognitionSettings settings = TitleBlockRecognitionSettings.Load();
            _rules = new ObservableCollection<TitleBlockRecognitionRule>(settings.Rules.Where(r => r != null));
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            Title = "用户图框识别设置";
            Width = 980;
            Height = 460;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ShowInTaskbar = false;

            Grid root = new Grid { Margin = new Thickness(12) };
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });

            TextBlock hint = new TextBlock
            {
                Text = "属性名可用逗号或分号分隔；图幅属性为空或取值为空时，可勾选从块名提取图幅。",
                Margin = new Thickness(0, 0, 0, 10),
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetRow(hint, 0);
            root.Children.Add(hint);

            _grid = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                ItemsSource = _rules
            };

            _grid.Columns.Add(new DataGridCheckBoxColumn { Header = "启用", Binding = new Binding("Enabled") { Mode = BindingMode.TwoWay }, Width = 52 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "块名前缀", Binding = new Binding("BlockNamePrefix") { Mode = BindingMode.TwoWay }, Width = 150 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "图名属性", Binding = new Binding("DrawTitleTags") { Mode = BindingMode.TwoWay }, Width = 160 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "图号属性", Binding = new Binding("DrawNumTags") { Mode = BindingMode.TwoWay }, Width = 160 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "比例属性", Binding = new Binding("DrawScaleTags") { Mode = BindingMode.TwoWay }, Width = 140 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "图幅属性", Binding = new Binding("PageSizeTags") { Mode = BindingMode.TwoWay }, Width = 140 });
            _grid.Columns.Add(new DataGridCheckBoxColumn { Header = "块名取图幅", Binding = new Binding("ExtractPageSizeFromBlockName") { Mode = BindingMode.TwoWay }, Width = 95 });

            Grid.SetRow(_grid, 1);
            root.Children.Add(_grid);

            StackPanel bottom = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0)
            };

            Button add = new Button { Content = "新增规则", Width = 90, Height = 28, Margin = new Thickness(0, 0, 8, 0) };
            add.Click += (s, e) => AddRule();
            Button delete = new Button { Content = "删除选中", Width = 90, Height = 28, Margin = new Thickness(0, 0, 8, 0) };
            delete.Click += (s, e) => DeleteSelectedRule();
            Button sample = new Button { Content = "示例模板", Width = 90, Height = 28, Margin = new Thickness(0, 0, 18, 0) };
            sample.Click += (s, e) => AddSampleRule();
            Button save = new Button { Content = "保存", Width = 90, Height = 28, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
            save.Click += (s, e) => SaveAndClose();
            Button cancel = new Button { Content = "取消", Width = 90, Height = 28, IsCancel = true };

            bottom.Children.Add(add);
            bottom.Children.Add(delete);
            bottom.Children.Add(sample);
            bottom.Children.Add(save);
            bottom.Children.Add(cancel);

            Grid.SetRow(bottom, 2);
            root.Children.Add(bottom);

            Content = root;
        }

        private void AddRule()
        {
            TitleBlockRecognitionRule rule = TitleBlockRecognitionSettings.CreateNewRule();
            _rules.Add(rule);
            _grid.SelectedItem = rule;
            _grid.ScrollIntoView(rule);
        }

        private void AddSampleRule()
        {
            TitleBlockRecognitionRule rule = new TitleBlockRecognitionRule
            {
                Enabled = true,
                BlockNamePrefix = "template_",
                DrawTitleTags = "图名,图纸名称,TITLE",
                DrawNumTags = "图号,编号,DRAWNUM",
                DrawScaleTags = "比例,SCALE",
                PageSizeTags = "",
                ExtractPageSizeFromBlockName = true
            };

            _rules.Add(rule);
            _grid.SelectedItem = rule;
            _grid.ScrollIntoView(rule);
        }

        private void DeleteSelectedRule()
        {
            TitleBlockRecognitionRule rule = _grid.SelectedItem as TitleBlockRecognitionRule;
            if (rule == null) return;
            _rules.Remove(rule);
        }

        private void SaveAndClose()
        {
            _grid.CommitEdit(DataGridEditingUnit.Cell, true);
            _grid.CommitEdit(DataGridEditingUnit.Row, true);

            TitleBlockRecognitionSettings settings = new TitleBlockRecognitionSettings();
            foreach (TitleBlockRecognitionRule rule in _rules)
            {
                if (rule != null && !string.IsNullOrWhiteSpace(rule.BlockNamePrefix))
                {
                    settings.Rules.Add(rule);
                }
            }

            settings.Save();
            try { DialogResult = true; } catch { }
            Close();
        }
    }
}
