using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace XYDSignTool
{
    public enum TextReplaceMatchMode
    {
        Fuzzy,
        Exact
    }

    public enum TextReplaceScope
    {
        CurrentSpace,
        EntireDrawing,
        ManualSelection
    }

    public class TextReplaceRule : INotifyPropertyChanged
    {
        private string _searchText;
        private string _replacementText;

        public string SearchText
        {
            get { return _searchText; }
            set
            {
                if (_searchText == value) return;
                _searchText = value;
                OnPropertyChanged("SearchText");
            }
        }

        public string ReplacementText
        {
            get { return _replacementText; }
            set
            {
                if (_replacementText == value) return;
                _replacementText = value;
                OnPropertyChanged("ReplacementText");
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public static class TextReplacementEngine
    {
        private sealed class IndexedRule
        {
            public int Index { get; set; }
            public string SearchText { get; set; }
            public string ReplacementText { get; set; }
        }

        public static string Apply(string source, IEnumerable<TextReplaceRule> rules, TextReplaceMatchMode mode)
        {
            source = source ?? "";
            List<IndexedRule> validRules = NormalizeRules(rules);
            if (validRules.Count == 0) return source;

            if (mode == TextReplaceMatchMode.Exact)
            {
                IndexedRule exact = validRules.FirstOrDefault(rule =>
                    string.Equals(source, rule.SearchText, StringComparison.OrdinalIgnoreCase));
                return exact == null ? source : exact.ReplacementText;
            }

            List<IndexedRule> ordered = validRules
                .OrderByDescending(rule => rule.SearchText.Length)
                .ThenBy(rule => rule.Index)
                .ToList();
            StringBuilder result = new StringBuilder(source.Length);
            int position = 0;
            while (position < source.Length)
            {
                IndexedRule matched = ordered.FirstOrDefault(rule =>
                    position + rule.SearchText.Length <= source.Length &&
                    string.Compare(source, position, rule.SearchText, 0, rule.SearchText.Length,
                        StringComparison.OrdinalIgnoreCase) == 0);
                if (matched == null)
                {
                    result.Append(source[position]);
                    position++;
                    continue;
                }

                result.Append(matched.ReplacementText);
                position += matched.SearchText.Length;
            }
            return result.ToString();
        }

        public static string ApplyToMTextContents(
            string contents,
            IEnumerable<TextReplaceRule> rules,
            TextReplaceMatchMode mode)
        {
            contents = contents ?? "";
            if (mode == TextReplaceMatchMode.Exact)
            {
                string visibleText = GetMTextPlainText(contents);
                TextReplaceRule exactRule = (rules ?? Enumerable.Empty<TextReplaceRule>())
                    .FirstOrDefault(rule => rule != null && !string.IsNullOrWhiteSpace(rule.SearchText) &&
                        string.Equals(visibleText, rule.SearchText.Trim(), StringComparison.OrdinalIgnoreCase));
                if (exactRule == null) return contents;

                TextReplaceRule normalizedRule = new TextReplaceRule
                {
                    SearchText = exactRule.SearchText.Trim(),
                    ReplacementText = exactRule.ReplacementText ?? ""
                };
                string formattedReplacement = ApplyToMTextContents(
                    contents,
                    new[] { normalizedRule },
                    TextReplaceMatchMode.Fuzzy);
                return string.Equals(formattedReplacement, contents, StringComparison.Ordinal)
                    ? normalizedRule.ReplacementText
                    : formattedReplacement;
            }

            StringBuilder result = new StringBuilder(contents.Length);
            StringBuilder literal = new StringBuilder();
            Action flushLiteral = () =>
            {
                if (literal.Length == 0) return;
                result.Append(Apply(literal.ToString(), rules, mode));
                literal.Clear();
            };

            int index = 0;
            while (index < contents.Length)
            {
                char current = contents[index];
                if (current == '{' || current == '}')
                {
                    flushLiteral();
                    result.Append(current);
                    index++;
                    continue;
                }

                if (current != '\\' || index + 1 >= contents.Length)
                {
                    literal.Append(current);
                    index++;
                    continue;
                }

                char command = contents[index + 1];
                if (command == '\\' || command == '{' || command == '}')
                {
                    literal.Append(contents, index, 2);
                    index += 2;
                    continue;
                }

                flushLiteral();
                int commandLength = GetMTextCommandLength(contents, index);
                result.Append(contents, index, commandLength);
                index += commandLength;
            }

            flushLiteral();
            return result.ToString();
        }

        private static List<IndexedRule> NormalizeRules(IEnumerable<TextReplaceRule> rules)
        {
            return (rules ?? Enumerable.Empty<TextReplaceRule>())
                .Select((rule, index) => new IndexedRule
                {
                    Index = index,
                    SearchText = rule == null ? "" : (rule.SearchText ?? "").Trim(),
                    ReplacementText = rule == null ? "" : rule.ReplacementText ?? ""
                })
                .Where(rule => rule.SearchText.Length > 0)
                .ToList();
        }

        private static int GetMTextCommandLength(string contents, int start)
        {
            char command = contents[start + 1];
            if ("PpLlOoKkXx~".IndexOf(command) >= 0) return 2;
            if ((command == 'U' || command == 'u') && start + 6 < contents.Length && contents[start + 2] == '+')
                return 7;

            int semicolon = contents.IndexOf(';', start + 2);
            return semicolon >= 0 ? semicolon - start + 1 : Math.Min(2, contents.Length - start);
        }

        private static string GetMTextPlainText(string contents)
        {
            StringBuilder result = new StringBuilder();
            int index = 0;
            while (index < contents.Length)
            {
                char current = contents[index];
                if (current == '{' || current == '}')
                {
                    index++;
                    continue;
                }
                if (current != '\\' || index + 1 >= contents.Length)
                {
                    result.Append(current);
                    index++;
                    continue;
                }

                char command = contents[index + 1];
                if (command == '\\' || command == '{' || command == '}')
                {
                    result.Append(command);
                    index += 2;
                    continue;
                }
                if (command == 'P' || command == 'p') result.Append('\n');
                else if (command == '~') result.Append(' ');
                index += GetMTextCommandLength(contents, index);
            }
            return result.ToString();
        }
    }

    public class TextReplaceWindow : Window
    {
        private readonly ObservableCollection<TextReplaceRule> _items = new ObservableCollection<TextReplaceRule>();
        private readonly DataGrid _grid;
        private readonly RadioButton _fuzzyRadio;
        private readonly RadioButton _currentSpaceRadio;
        private readonly RadioButton _entireDrawingRadio;

        public List<TextReplaceRule> Rules { get; private set; } = new List<TextReplaceRule>();
        public TextReplaceMatchMode MatchMode { get; private set; }
        public TextReplaceScope Scope { get; private set; }

        public TextReplaceWindow(bool hasPreselection)
        {
            Title = "批量文本替换";
            Width = 760;
            Height = 560;
            MinWidth = 640;
            MinHeight = 460;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ShowInTaskbar = false;

            Grid root = new Grid { Margin = new Thickness(14) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            StackPanel toolbar = new StackPanel { Orientation = Orientation.Horizontal };
            Button addButton = CreateButton("新增规则", 90);
            addButton.Click += (s, e) => AddRule();
            Button deleteButton = CreateButton("删除选中", 90);
            deleteButton.Click += (s, e) => DeleteSelectedRules();
            Button clearButton = CreateButton("清空", 72);
            clearButton.Click += (s, e) => _items.Clear();
            toolbar.Children.Add(addButton);
            toolbar.Children.Add(deleteButton);
            toolbar.Children.Add(clearButton);
            root.Children.Add(toolbar);

            _grid = new DataGrid
            {
                ItemsSource = _items,
                AutoGenerateColumns = false,
                CanUserAddRows = true,
                CanUserDeleteRows = true,
                SelectionMode = DataGridSelectionMode.Extended,
                Margin = new Thickness(0, 10, 0, 10)
            };
            _grid.Columns.Add(new DataGridTextColumn
            {
                Header = "查找内容",
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                Binding = new Binding("SearchText") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }
            });
            _grid.Columns.Add(new DataGridTextColumn
            {
                Header = "替换为（可留空）",
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                Binding = new Binding("ReplacementText") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }
            });
            Grid.SetRow(_grid, 1);
            root.Children.Add(_grid);

            StackPanel options = new StackPanel { Orientation = Orientation.Horizontal };
            GroupBox modeGroup = new GroupBox { Header = "匹配方式", Padding = new Thickness(10, 5, 10, 7) };
            StackPanel modePanel = new StackPanel { Orientation = Orientation.Horizontal };
            _fuzzyRadio = new RadioButton { Content = "模糊匹配", IsChecked = true, Margin = new Thickness(0, 0, 16, 0) };
            modePanel.Children.Add(_fuzzyRadio);
            modePanel.Children.Add(new RadioButton { Content = "严格匹配" });
            modeGroup.Content = modePanel;

            GroupBox scopeGroup = new GroupBox
            {
                Header = "处理范围",
                Padding = new Thickness(10, 5, 10, 7),
                Margin = new Thickness(14, 0, 0, 0)
            };
            StackPanel scopePanel = new StackPanel { Orientation = Orientation.Horizontal };
            _currentSpaceRadio = new RadioButton { Content = "当前空间", IsChecked = !hasPreselection, Margin = new Thickness(0, 0, 16, 0) };
            _entireDrawingRadio = new RadioButton { Content = "整张 DWG", Margin = new Thickness(0, 0, 16, 0) };
            RadioButton manualRadio = new RadioButton { Content = hasPreselection ? "预选对象" : "手动选择", IsChecked = hasPreselection };
            scopePanel.Children.Add(_currentSpaceRadio);
            scopePanel.Children.Add(_entireDrawingRadio);
            scopePanel.Children.Add(manualRadio);
            scopeGroup.Content = scopePanel;
            options.Children.Add(modeGroup);
            options.Children.Add(scopeGroup);
            Grid.SetRow(options, 2);
            root.Children.Add(options);

            TextBlock hint = new TextBlock
            {
                Text = "所有规则对原始文字同步生效，不会把某条规则的结果再次交给下一条规则。",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 0)
            };
            Grid.SetRow(hint, 3);
            root.Children.Add(hint);

            StackPanel bottom = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0)
            };
            Button applyButton = CreateButton("执行替换", 96);
            applyButton.IsDefault = true;
            applyButton.Click += (s, e) => Confirm();
            Button cancelButton = CreateButton("取消", 82);
            cancelButton.IsCancel = true;
            bottom.Children.Add(applyButton);
            bottom.Children.Add(cancelButton);
            Grid.SetRow(bottom, 4);
            root.Children.Add(bottom);

            Content = root;
            AddRule();
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

        private void AddRule()
        {
            TextReplaceRule rule = new TextReplaceRule();
            _items.Add(rule);
            _grid.SelectedItem = rule;
            _grid.ScrollIntoView(rule);
        }

        private void DeleteSelectedRules()
        {
            List<TextReplaceRule> selected = _grid.SelectedItems.OfType<TextReplaceRule>().ToList();
            foreach (TextReplaceRule rule in selected) _items.Remove(rule);
        }

        private void Confirm()
        {
            _grid.CommitEdit(DataGridEditingUnit.Cell, true);
            _grid.CommitEdit(DataGridEditingUnit.Row, true);

            List<TextReplaceRule> valid = _items
                .Where(rule => rule != null && !string.IsNullOrWhiteSpace(rule.SearchText))
                .Select(rule => new TextReplaceRule
                {
                    SearchText = rule.SearchText.Trim(),
                    ReplacementText = rule.ReplacementText ?? ""
                })
                .ToList();
            if (valid.Count == 0)
            {
                MessageBox.Show("请至少填写一条替换规则。", "批量文本替换", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string duplicate = valid
                .GroupBy(rule => rule.SearchText, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(duplicate))
            {
                MessageBox.Show("查找内容不能重复：" + duplicate, "批量文本替换", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Rules = valid;
            MatchMode = _fuzzyRadio.IsChecked == true ? TextReplaceMatchMode.Fuzzy : TextReplaceMatchMode.Exact;
            Scope = _currentSpaceRadio.IsChecked == true
                ? TextReplaceScope.CurrentSpace
                : _entireDrawingRadio.IsChecked == true
                    ? TextReplaceScope.EntireDrawing
                    : TextReplaceScope.ManualSelection;
            DialogResult = true;
        }
    }
}
