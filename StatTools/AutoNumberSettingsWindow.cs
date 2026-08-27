using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace XYDSignTool
{
    public sealed class AutoNumberSettingsWindow : Window
    {
        private readonly List<AutoNumberAttributeOption> _attributeOptions;
        private readonly Dictionary<string, AutoNumberSettings> _savedStates;
        private readonly Dictionary<string, string> _sampleValues;
        private readonly bool _fullyAutomatic;
        private readonly bool _lockAttribute;
        private readonly bool _insertAndShift;

        private ComboBox _attributeCombo;
        private TextBox _sampleText;
        private ComboBox _segmentCombo;
        private TextBox _prefixText;
        private TextBox _startText;
        private TextBox _suffixText;
        private TextBox _widthText;
        private TextBox _incrementText;
        private ComboBox _skipModeCombo;
        private TextBox _skipDigitsText;
        private ComboBox _sortModeCombo;
        private TextBox _toleranceText;
        private TextBlock _previewText;
        private bool _updating;
        private string _activeAttributeTag = "";

        public AutoNumberSettings AcceptedSettings { get; private set; }
        public event Action PickSampleRequested;

        public string SelectedAttributeTag
        {
            get
            {
                AutoNumberAttributeOption selected = _attributeCombo == null ? null : _attributeCombo.SelectedItem as AutoNumberAttributeOption;
                string typed = ((_attributeCombo == null ? "" : _attributeCombo.Text) ?? "").Trim();
                if (selected != null && (typed.Length == 0 ||
                    string.Equals(typed, selected.Tag, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(typed, selected.ToString(), StringComparison.OrdinalIgnoreCase)))
                    return selected.Tag;
                return typed;
            }
        }

        public AutoNumberSettingsWindow(
            IEnumerable<AutoNumberAttributeOption> attributeOptions,
            IDictionary<string, AutoNumberSettings> savedStates,
            IDictionary<string, string> sampleValues,
            bool fullyAutomatic,
            string initialAttributeTag = null,
            AutoNumberSettings initialSettings = null,
            bool lockAttribute = false,
            bool insertAndShift = false)
        {
            _attributeOptions = (attributeOptions ?? Enumerable.Empty<AutoNumberAttributeOption>()).ToList();
            _savedStates = new Dictionary<string, AutoNumberSettings>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, AutoNumberSettings> pair in savedStates ?? new Dictionary<string, AutoNumberSettings>())
                _savedStates[pair.Key] = pair.Value.Clone();
            _sampleValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> pair in sampleValues ?? new Dictionary<string, string>())
                _sampleValues[pair.Key] = pair.Value ?? "";
            _fullyAutomatic = fullyAutomatic;
            _lockAttribute = lockAttribute;
            _insertAndShift = insertAndShift;

            BuildWindow();
            InitializeSelection(initialAttributeTag, initialSettings);
        }

        public void SetSampleValue(string value)
        {
            _sampleText.Text = value ?? "";
            BuildSegmentOptions();
            if (_insertAndShift) ApplySampleNumber(false, false);
        }

        private void BuildWindow()
        {
            Title = _insertAndShift ? "自动排号 - 插号并顺延" :
                (_fullyAutomatic ? "自动排号 - 全自动设置" : "自动排号 - 半自动设置");
            Width = 690;
            Height = _fullyAutomatic ? 680 : 610;
            MinWidth = 620;
            MinHeight = _fullyAutomatic ? 620 : 550;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ShowInTaskbar = false;
            Background = new SolidColorBrush(Color.FromRgb(245, 246, 248));

            Grid root = new Grid { Margin = new Thickness(16) };
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            ScrollViewer scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            StackPanel form = new StackPanel();
            scroll.Content = form;

            form.Children.Add(CreateSectionTitle("目标属性"));
            _attributeCombo = new ComboBox
            {
                ItemsSource = _attributeOptions,
                IsEditable = true,
                IsTextSearchEnabled = true,
                Height = 29,
                IsEnabled = !_lockAttribute,
                ItemTemplate = CreateAttributeTemplate()
            };
            TextSearch.SetTextPath(_attributeCombo, "Tag");
            _attributeCombo.SelectionChanged += (s, e) => ApplySelectedAttributeContext();
            _attributeCombo.LostKeyboardFocus += (s, e) => ApplyTypedAttributeContext();
            form.Children.Add(CreateLabeledControl("属性标签：", _attributeCombo,
                "括号中显示包含该属性的图块数/候选图块总数，也可直接输入标签。"));

            Grid sampleGrid = new Grid();
            sampleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            sampleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _sampleText = new TextBox { Height = 29, IsReadOnly = true, Background = Brushes.White };
            Button pickSample = new Button { Content = "图中取样", Width = 90, Height = 29, Margin = new Thickness(8, 0, 0, 0) };
            pickSample.Click += (s, e) =>
            {
                Action handler = PickSampleRequested;
                if (handler != null) handler();
            };
            sampleGrid.Children.Add(_sampleText);
            Grid.SetColumn(pickSample, 1);
            sampleGrid.Children.Add(pickSample);
            form.Children.Add(CreateLabeledControl("样本属性值：", sampleGrid,
                _insertAndShift ? "样本应是漏号位置后面第一个已有编号，它会作为顺延起点。" :
                "可从已有编号中解析前缀、数字段和后缀。"));

            _segmentCombo = new ComboBox { Height = 29 };
            Button continueButton = new Button
            {
                Content = _insertAndShift ? "以样本号作为插入号" : "从样本的下一号继续",
                Width = 156,
                Height = 29,
                Margin = new Thickness(8, 0, 0, 0)
            };
            continueButton.Click += (s, e) => ApplySampleNumber(!_insertAndShift, true);
            Grid segmentGrid = new Grid();
            segmentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            segmentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            segmentGrid.Children.Add(_segmentCombo);
            Grid.SetColumn(continueButton, 1);
            segmentGrid.Children.Add(continueButton);
            form.Children.Add(CreateLabeledControl("数字段：", segmentGrid, "多个数字段时默认选择最后一段。"));

            form.Children.Add(CreateSectionTitle("编号规则"));
            Grid ruleGrid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            for (int i = 0; i < 4; i++) ruleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = i % 2 == 0 ? new GridLength(86) : new GridLength(1, GridUnitType.Star) });
            _prefixText = CreateTextBox();
            _startText = CreateTextBox();
            _suffixText = CreateTextBox();
            _widthText = CreateTextBox();
            _incrementText = CreateTextBox();
            AddRuleField(ruleGrid, 0, 0, "前缀：", _prefixText);
            AddRuleField(ruleGrid, 0, 2, "起始数字：", _startText);
            AddRuleField(ruleGrid, 1, 0, "后缀：", _suffixText);
            AddRuleField(ruleGrid, 1, 2, "数字位数：", _widthText);
            AddRuleField(ruleGrid, 2, 0, "递增步长：", _incrementText);
            form.Children.Add(ruleGrid);

            form.Children.Add(CreateSectionTitle("跳号规则"));
            _skipModeCombo = new ComboBox { Height = 29 };
            AddEnumItem(_skipModeCombo, "不跳号", AutoNumberSkipMode.None);
            AddEnumItem(_skipModeCombo, "尾号命中时跳过", AutoNumberSkipMode.EndingDigit);
            AddEnumItem(_skipModeCombo, "任意一位命中时跳过", AutoNumberSkipMode.AnyDigit);
            _skipDigitsText = CreateTextBox();
            Grid skipGrid = new Grid();
            skipGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            skipGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(135) });
            _skipModeCombo.Margin = new Thickness(0, 0, 8, 0);
            skipGrid.Children.Add(_skipModeCombo);
            Grid.SetColumn(_skipDigitsText, 1);
            skipGrid.Children.Add(_skipDigitsText);
            form.Children.Add(CreateLabeledControl("模式 / 禁用数字：", skipGrid,
                "禁用数字只检查补零后的中间数字部分，例如输入 4 或 47。"));

            if (_fullyAutomatic)
            {
                form.Children.Add(CreateSectionTitle("空间排序"));
                _sortModeCombo = new ComboBox { Height = 29 };
                AddEnumItem(_sortModeCombo, "横向普通：左→右，上→下", AutoNumberSortMode.Horizontal);
                AddEnumItem(_sortModeCombo, "横向蛇形：逐行反向", AutoNumberSortMode.HorizontalSnake);
                AddEnumItem(_sortModeCombo, "纵向普通：上→下，左→右", AutoNumberSortMode.Vertical);
                AddEnumItem(_sortModeCombo, "纵向蛇形：逐列反向", AutoNumberSortMode.VerticalSnake);
                form.Children.Add(CreateLabeledControl("排序方式：", _sortModeCombo, "上下左右按当前 UCS 判断。"));
                _toleranceText = CreateTextBox();
                form.Children.Add(CreateLabeledControl("行/列容差：", _toleranceText,
                    "输入 0 表示根据图块尺寸自动计算；大于 0 时使用当前绘图单位。"));
            }

            _previewText = new TextBlock
            {
                Margin = new Thickness(0, 12, 0, 0),
                Foreground = new SolidColorBrush(Color.FromRgb(38, 94, 145)),
                TextWrapping = TextWrapping.Wrap,
                FontWeight = FontWeights.SemiBold
            };
            form.Children.Add(_previewText);
            foreach (TextBox textBox in new[] { _prefixText, _startText, _suffixText, _widthText, _incrementText })
                textBox.TextChanged += (s, e) => UpdatePreview();
            _skipModeCombo.SelectionChanged += (s, e) => UpdatePreview();
            _skipDigitsText.TextChanged += (s, e) => UpdatePreview();

            Grid.SetRow(scroll, 0);
            root.Children.Add(scroll);

            StackPanel buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0)
            };
            Button confirm = new Button
            {
                Content = _insertAndShift || _fullyAutomatic ? "生成预览" : "开始点选排号",
                Width = 132,
                Height = 31,
                IsDefault = true,
                Margin = new Thickness(0, 0, 8, 0)
            };
            confirm.Click += (s, e) => Confirm();
            Button cancel = new Button { Content = "取消", Width = 82, Height = 31, IsCancel = true };
            buttons.Children.Add(confirm);
            buttons.Children.Add(cancel);
            Grid.SetRow(buttons, 1);
            root.Children.Add(buttons);

            Content = root;
        }

        private void InitializeSelection(string initialTag, AutoNumberSettings initialSettings)
        {
            AutoNumberAttributeOption selected = null;
            if (!string.IsNullOrWhiteSpace(initialTag))
                selected = _attributeOptions.FirstOrDefault(option => string.Equals(option.Tag, initialTag, StringComparison.OrdinalIgnoreCase));
            if (selected == null) selected = _attributeOptions.FirstOrDefault();

            _updating = true;
            if (selected != null) _attributeCombo.SelectedItem = selected;
            else _attributeCombo.Text = initialTag ?? "";
            _updating = false;

            if (initialSettings != null)
            {
                string sample;
                _sampleText.Text = _sampleValues.TryGetValue(initialTag ?? "", out sample) ? sample : "";
                _activeAttributeTag = SelectedAttributeTag;
                ApplySettings(initialSettings);
                BuildSegmentOptions();
                if (_insertAndShift) ApplySampleNumber(false, false);
                UpdatePreview();
            }
            else ApplySelectedAttributeContext();
        }

        private void ApplySelectedAttributeContext()
        {
            if (_updating) return;
            AutoNumberAttributeOption selected = _attributeCombo.SelectedItem as AutoNumberAttributeOption;
            ApplyAttributeContext(selected == null ? SelectedAttributeTag : selected.Tag);
        }

        private void ApplyTypedAttributeContext()
        {
            ApplyAttributeContext(SelectedAttributeTag);
        }

        private void ApplyAttributeContext(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return;
            if (string.Equals(tag.Trim(), _activeAttributeTag, StringComparison.OrdinalIgnoreCase)) return;
            _activeAttributeTag = tag.Trim();
            _updating = true;
            try
            {
                string sample;
                _sampleText.Text = _sampleValues.TryGetValue(tag, out sample) ? sample : "";
                AutoNumberSettings saved;
                ApplySettings(_savedStates.TryGetValue(tag, out saved) ? saved : new AutoNumberSettings { AttributeTag = tag });
                BuildSegmentOptions();
                if (_insertAndShift) ApplySampleNumber(false, false);
            }
            finally { _updating = false; }
            UpdatePreview();
        }

        private void ApplySettings(AutoNumberSettings settings)
        {
            _prefixText.Text = settings.Prefix ?? "";
            _suffixText.Text = settings.Suffix ?? "";
            _startText.Text = Math.Max(0, settings.StartNumber).ToString(CultureInfo.InvariantCulture);
            _widthText.Text = Math.Max(1, settings.DigitWidth).ToString(CultureInfo.InvariantCulture);
            _incrementText.Text = Math.Max(1, settings.Increment).ToString(CultureInfo.InvariantCulture);
            SelectEnumItem(_skipModeCombo, settings.SkipMode);
            _skipDigitsText.Text = string.IsNullOrWhiteSpace(settings.SkipDigits) ? "4" : settings.SkipDigits;
            if (_fullyAutomatic)
            {
                SelectEnumItem(_sortModeCombo, settings.SortMode);
                _toleranceText.Text = Math.Max(0, settings.ManualTolerance).ToString("0.###", CultureInfo.CurrentCulture);
            }
        }

        private void BuildSegmentOptions()
        {
            List<AutoNumberSegmentOption> segments = AutoNumberingService.ParseNumberSegments(_sampleText.Text);
            _segmentCombo.ItemsSource = segments;
            _segmentCombo.SelectedIndex = segments.Count == 0 ? -1 : segments.Count - 1;
        }

        private void ApplySampleNumber(bool useFollowingNumber, bool showErrors)
        {
            AutoNumberSegmentOption segment = _segmentCombo.SelectedItem as AutoNumberSegmentOption;
            if (segment == null)
            {
                if (showErrors)
                    MessageBox.Show("样本属性值中没有可用的连续数字段。", "自动排号", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            long increment;
            if (!long.TryParse(_incrementText.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out increment) || increment <= 0)
            {
                if (showErrors)
                    MessageBox.Show("请先输入大于 0 的递增步长。", "自动排号", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                _prefixText.Text = segment.Prefix;
                _suffixText.Text = segment.Suffix;
                _widthText.Text = segment.DigitWidth.ToString(CultureInfo.InvariantCulture);
                AutoNumberSettings temporary;
                if (!TryBuildSettings(out temporary, false)) return;
                long number = useFollowingNumber
                    ? AutoNumberingService.GetFollowingNumber(segment.Number, temporary)
                    : segment.Number;
                _startText.Text = number.ToString(CultureInfo.InvariantCulture);
                UpdatePreview();
            }
            catch (System.Exception ex)
            {
                if (showErrors)
                    MessageBox.Show("无法从样本解析编号：" + ex.Message, "自动排号", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void Confirm()
        {
            AutoNumberSettings settings;
            if (!TryBuildSettings(out settings, true)) return;
            AcceptedSettings = settings;
            DialogResult = true;
        }

        private bool TryBuildSettings(out AutoNumberSettings settings, bool showErrors)
        {
            settings = null;
            string tag = SelectedAttributeTag;
            long start = 0;
            long increment = 1;
            int width = 1;
            double tolerance = 0;
            string error = "";
            if (string.IsNullOrWhiteSpace(tag)) error = "请选择或输入属性标签。";
            else if (!long.TryParse(_startText.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out start) || start < 0) error = "起始数字必须是非负整数。";
            else if (!int.TryParse(_widthText.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out width) || width < 1 || width > 19) error = "数字位数必须介于 1 至 19。";
            else if (!long.TryParse(_incrementText.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out increment) || increment <= 0) error = "递增步长必须大于 0。";
            else if (_fullyAutomatic && !TryParseNonNegativeDouble(_toleranceText.Text, out tolerance)) error = "行/列容差必须是大于或等于 0 的数字。";

            AutoNumberSkipMode skipMode = GetEnumValue(_skipModeCombo, AutoNumberSkipMode.None);
            string skipDigits = AutoNumberingService.NormalizeSkipDigits(_skipDigitsText.Text);
            if (string.IsNullOrEmpty(error) && skipMode != AutoNumberSkipMode.None && skipDigits.Length == 0)
                error = "启用跳号时请至少输入一个禁用数字。";

            if (!string.IsNullOrEmpty(error))
            {
                if (showErrors) MessageBox.Show(error, "自动排号", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            settings = new AutoNumberSettings
            {
                AttributeTag = tag.Trim(),
                Prefix = _prefixText.Text ?? "",
                Suffix = _suffixText.Text ?? "",
                StartNumber = start,
                DigitWidth = width,
                Increment = increment,
                SkipMode = skipMode,
                SkipDigits = skipDigits,
                SortMode = _fullyAutomatic ? GetEnumValue(_sortModeCombo, AutoNumberSortMode.HorizontalSnake) : AutoNumberSortMode.HorizontalSnake,
                ManualTolerance = tolerance
            };
            try { settings.StartNumber = AutoNumberingService.NormalizeNumber(settings.StartNumber, settings); }
            catch (System.Exception ex)
            {
                if (showErrors) MessageBox.Show(ex.Message, "自动排号", MessageBoxButton.OK, MessageBoxImage.Warning);
                settings = null;
                return false;
            }
            return true;
        }

        private void UpdatePreview()
        {
            if (_previewText == null || _updating) return;
            AutoNumberSettings settings;
            if (!TryBuildSettings(out settings, false))
            {
                _previewText.Text = "";
                return;
            }
            try
            {
                long number = AutoNumberingService.NormalizeNumber(settings.StartNumber, settings);
                _previewText.Text = (_insertAndShift ? "插入起始号预览：" : "首个编号预览：") +
                    AutoNumberingService.BuildValue(number, settings);
            }
            catch { _previewText.Text = ""; }
        }

        private static Border CreateLabeledControl(string label, UIElement control, string hint)
        {
            StackPanel panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
            panel.Children.Add(control);
            if (!string.IsNullOrWhiteSpace(hint))
                panel.Children.Add(new TextBlock { Text = hint, Foreground = Brushes.Gray, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
            return new Border { Child = panel, Margin = new Thickness(0, 0, 0, 11) };
        }

        private static DataTemplate CreateAttributeTemplate()
        {
            FrameworkElementFactory panel = new FrameworkElementFactory(typeof(DockPanel));
            panel.SetValue(DockPanel.LastChildFillProperty, true);
            FrameworkElementFactory coverage = new FrameworkElementFactory(typeof(TextBlock));
            coverage.SetBinding(TextBlock.TextProperty, new Binding("CoverageText"));
            coverage.SetValue(DockPanel.DockProperty, Dock.Right);
            coverage.SetValue(TextBlock.ForegroundProperty, Brushes.Gray);
            coverage.SetValue(TextBlock.MarginProperty, new Thickness(12, 0, 4, 0));
            panel.AppendChild(coverage);

            FrameworkElementFactory tag = new FrameworkElementFactory(typeof(TextBlock));
            tag.SetBinding(TextBlock.TextProperty, new Binding("Tag"));
            tag.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            panel.AppendChild(tag);
            return new DataTemplate { VisualTree = panel };
        }

        private static TextBlock CreateSectionTitle(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(36, 63, 87)),
                Margin = new Thickness(0, 4, 0, 9)
            };
        }

        private static TextBox CreateTextBox()
        {
            return new TextBox { Height = 27, Margin = new Thickness(0, 0, 8, 7) };
        }

        private static void AddRuleField(Grid grid, int row, int column, string label, UIElement control)
        {
            while (grid.RowDefinitions.Count <= row) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            TextBlock text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 7) };
            Grid.SetRow(text, row);
            Grid.SetColumn(text, column);
            grid.Children.Add(text);
            Grid.SetRow(control, row);
            Grid.SetColumn(control, column + 1);
            grid.Children.Add(control);
        }

        private static void AddEnumItem(ComboBox combo, string text, object value)
        {
            combo.Items.Add(new ComboBoxItem { Content = text, Tag = value });
            if (combo.SelectedIndex < 0) combo.SelectedIndex = 0;
        }

        private static void SelectEnumItem(ComboBox combo, object value)
        {
            if (combo == null) return;
            foreach (ComboBoxItem item in combo.Items.OfType<ComboBoxItem>())
            {
                if (Equals(item.Tag, value))
                {
                    combo.SelectedItem = item;
                    return;
                }
            }
            combo.SelectedIndex = 0;
        }

        private static T GetEnumValue<T>(ComboBox combo, T fallback)
        {
            ComboBoxItem item = combo == null ? null : combo.SelectedItem as ComboBoxItem;
            return item != null && item.Tag is T ? (T)item.Tag : fallback;
        }

        private static bool TryParseNonNegativeDouble(string text, out double value)
        {
            return (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) ||
                    double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) &&
                   value >= 0 && !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
