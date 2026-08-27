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
    public sealed class AutoNumberShiftPreviewItem
    {
        public AutoNumberCandidate Candidate { get; set; }
        public string ChangeKind { get; set; }
        public string AttributeTag { get; set; }
        public string OldValue { get; set; }
        public string NewValue { get; set; }
        public long AssignedNumber { get; set; }
        public string BlockName { get { return Candidate == null ? "" : Candidate.EffectiveBlockName; } }
    }

    public sealed class AutoNumberShiftPreviewWindow : Window
    {
        public ObservableCollection<AutoNumberShiftPreviewItem> Items { get; private set; }

        public AutoNumberShiftPreviewWindow(IEnumerable<AutoNumberShiftPreviewItem> items)
        {
            Items = new ObservableCollection<AutoNumberShiftPreviewItem>(
                items ?? Enumerable.Empty<AutoNumberShiftPreviewItem>());

            Title = "插号并顺延预览";
            Width = Math.Min(980, SystemParameters.WorkArea.Width - 40);
            Height = Math.Min(680, SystemParameters.WorkArea.Height - 70);
            MinWidth = 720;
            MinHeight = 480;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ShowInTaskbar = false;
            Background = new SolidColorBrush(Color.FromRgb(245, 246, 248));

            Grid root = new Grid { Margin = new Thickness(14) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            int inserted = Items.Count(item => string.Equals(item.ChangeKind, "新增编号", StringComparison.Ordinal));
            TextBlock summary = new TextBlock
            {
                Text = "将为 " + inserted + " 个漏号图块补号，并顺延 " + (Items.Count - inserted) +
                       " 个后续图块。确认后作为一次 CAD 撤销步骤。",
                Margin = new Thickness(0, 0, 0, 10),
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            };
            root.Children.Add(summary);

            DataGrid grid = new DataGrid
            {
                ItemsSource = Items,
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                IsReadOnly = true,
                RowHeaderWidth = 0,
                GridLinesVisibility = DataGridGridLinesVisibility.All,
                Background = Brushes.White,
                AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(248, 249, 251))
            };
            grid.Columns.Add(new DataGridTextColumn { Header = "操作", Binding = new Binding("ChangeKind"), Width = 90 });
            grid.Columns.Add(new DataGridTextColumn { Header = "有效块名", Binding = new Binding("BlockName"), Width = 210 });
            grid.Columns.Add(new DataGridTextColumn { Header = "属性", Binding = new Binding("AttributeTag"), Width = 130 });
            grid.Columns.Add(new DataGridTextColumn { Header = "旧值", Binding = new Binding("OldValue"), Width = 190 });
            grid.Columns.Add(new DataGridTextColumn { Header = "新值", Binding = new Binding("NewValue"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            Grid.SetRow(grid, 1);
            root.Children.Add(grid);

            StackPanel buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0)
            };
            Button apply = new Button
            {
                Content = "确认插号并顺延",
                Width = 148,
                Height = 31,
                IsDefault = true,
                Margin = new Thickness(0, 0, 8, 0)
            };
            apply.Click += (s, e) => DialogResult = true;
            Button cancel = new Button { Content = "取消", Width = 82, Height = 31, IsCancel = true };
            buttons.Children.Add(apply);
            buttons.Children.Add(cancel);
            Grid.SetRow(buttons, 2);
            root.Children.Add(buttons);

            Content = root;
        }
    }
}
