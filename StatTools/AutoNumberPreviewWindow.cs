using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace XYDSignTool
{
    public sealed class AutoNumberPreviewItem : INotifyPropertyChanged
    {
        private bool _isIncluded;
        private string _sequence;
        private string _newValue;
        private string _status;

        public AutoNumberCandidate Candidate { get; set; }
        public string BlockName { get { return Candidate == null ? "" : Candidate.EffectiveBlockName; } }
        public string AttributeTag { get; set; }
        public string OldValue { get; set; }
        public string BaseStatus { get; set; }
        public bool CanInclude { get; set; }
        public long AssignedNumber { get; set; }

        public bool IsIncluded
        {
            get { return _isIncluded; }
            set
            {
                if (_isIncluded == value || (!CanInclude && value)) return;
                _isIncluded = value;
                OnPropertyChanged("IsIncluded");
            }
        }

        public string Sequence { get { return _sequence; } set { _sequence = value; OnPropertyChanged("Sequence"); } }
        public string NewValue { get { return _newValue; } set { _newValue = value; OnPropertyChanged("NewValue"); } }
        public string Status { get { return _status; } set { _status = value; OnPropertyChanged("Status"); } }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string name)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(name));
        }
    }

    public sealed class AutoNumberPreviewWindow : Window
    {
        private readonly AutoNumberSettings _settings;
        private readonly TextBlock _summary;
        private bool _recalculating;

        public ObservableCollection<AutoNumberPreviewItem> Items { get; private set; }
        public List<AutoNumberPreviewItem> IncludedItems { get; private set; } = new List<AutoNumberPreviewItem>();

        public AutoNumberPreviewWindow(IEnumerable<AutoNumberCandidate> orderedCandidates, AutoNumberSettings settings)
        {
            _settings = settings.Clone();
            Items = BuildItems(orderedCandidates, settings);
            foreach (AutoNumberPreviewItem item in Items) item.PropertyChanged += ItemPropertyChanged;

            Title = "自动排号预览";
            Width = Math.Min(1050, SystemParameters.WorkArea.Width - 40);
            Height = Math.Min(680, SystemParameters.WorkArea.Height - 70);
            MinWidth = 760;
            MinHeight = 500;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ShowInTaskbar = false;
            Background = new SolidColorBrush(Color.FromRgb(245, 246, 248));

            Grid root = new Grid { Margin = new Thickness(14) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _summary = new TextBlock { Margin = new Thickness(0, 0, 0, 10), FontWeight = FontWeights.SemiBold };
            root.Children.Add(_summary);

            DataGrid grid = new DataGrid
            {
                ItemsSource = Items,
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                RowHeaderWidth = 0,
                GridLinesVisibility = DataGridGridLinesVisibility.All,
                Background = Brushes.White,
                AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(248, 249, 251))
            };
            grid.Columns.Add(new DataGridCheckBoxColumn { Header = "应用", Binding = new Binding("IsIncluded") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 58 });
            grid.Columns.Add(new DataGridTextColumn { Header = "顺序", Binding = new Binding("Sequence"), Width = 62, IsReadOnly = true });
            grid.Columns.Add(new DataGridTextColumn { Header = "有效块名", Binding = new Binding("BlockName"), Width = 190, IsReadOnly = true });
            grid.Columns.Add(new DataGridTextColumn { Header = "属性", Binding = new Binding("AttributeTag"), Width = 130, IsReadOnly = true });
            grid.Columns.Add(new DataGridTextColumn { Header = "旧值", Binding = new Binding("OldValue"), Width = 170, IsReadOnly = true });
            grid.Columns.Add(new DataGridTextColumn { Header = "新值", Binding = new Binding("NewValue"), Width = 170, IsReadOnly = true });
            grid.Columns.Add(new DataGridTextColumn { Header = "状态", Binding = new Binding("Status"), Width = new DataGridLength(1, DataGridLengthUnitType.Star), IsReadOnly = true });
            Grid.SetRow(grid, 1);
            root.Children.Add(grid);

            StackPanel bottom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            Button all = new Button { Content = "全选可用项", Width = 108, Height = 30, Margin = new Thickness(0, 0, 8, 0) };
            all.Click += (s, e) => SetAll(true);
            Button none = new Button { Content = "全不选", Width = 82, Height = 30, Margin = new Thickness(0, 0, 18, 0) };
            none.Click += (s, e) => SetAll(false);
            Button apply = new Button { Content = "确认并写入", Width = 112, Height = 30, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            apply.Click += (s, e) => Confirm();
            Button cancel = new Button { Content = "取消", Width = 82, Height = 30, IsCancel = true };
            bottom.Children.Add(all);
            bottom.Children.Add(none);
            bottom.Children.Add(apply);
            bottom.Children.Add(cancel);
            Grid.SetRow(bottom, 2);
            root.Children.Add(bottom);

            Content = root;
            Recalculate();
        }

        private static ObservableCollection<AutoNumberPreviewItem> BuildItems(
            IEnumerable<AutoNumberCandidate> orderedCandidates,
            AutoNumberSettings settings)
        {
            ObservableCollection<AutoNumberPreviewItem> result = new ObservableCollection<AutoNumberPreviewItem>();
            foreach (AutoNumberCandidate candidate in orderedCandidates ?? Enumerable.Empty<AutoNumberCandidate>())
            {
                string reason = AutoNumberingService.GetEligibilityReason(candidate, settings.AttributeTag);
                bool available = string.IsNullOrWhiteSpace(reason);
                result.Add(new AutoNumberPreviewItem
                {
                    Candidate = candidate,
                    AttributeTag = settings.AttributeTag,
                    OldValue = candidate.GetAttributeDisplayValue(settings.AttributeTag),
                    BaseStatus = reason,
                    CanInclude = available,
                    IsIncluded = available,
                    Status = available ? "待写入" : reason
                });
            }
            return result;
        }

        private void ItemPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "IsIncluded" && !_recalculating) Recalculate();
        }

        private void SetAll(bool selected)
        {
            _recalculating = true;
            foreach (AutoNumberPreviewItem item in Items)
                if (item.CanInclude) item.IsIncluded = selected;
            _recalculating = false;
            Recalculate();
        }

        private void Recalculate()
        {
            _recalculating = true;
            try
            {
                long next = AutoNumberingService.NormalizeNumber(_settings.StartNumber, _settings);
                int sequence = 0;
                foreach (AutoNumberPreviewItem item in Items)
                {
                    if (!item.CanInclude)
                    {
                        item.Sequence = "";
                        item.NewValue = "";
                        item.Status = item.BaseStatus;
                        continue;
                    }
                    if (!item.IsIncluded)
                    {
                        item.Sequence = "";
                        item.NewValue = "";
                        item.Status = "已排除";
                        continue;
                    }

                    sequence++;
                    item.AssignedNumber = next;
                    item.Sequence = sequence.ToString();
                    item.NewValue = AutoNumberingService.BuildValue(next, _settings);
                    item.Status = "待写入";
                    next = AutoNumberingService.GetFollowingNumber(next, _settings);
                }
                _summary.Text = "候选 " + Items.Count + " 个，将写入 " + sequence + " 个；取消勾选后后续编号会自动补齐。";
            }
            finally { _recalculating = false; }
        }

        private void Confirm()
        {
            Recalculate();
            IncludedItems = Items.Where(item => item.CanInclude && item.IsIncluded).ToList();
            if (IncludedItems.Count == 0)
            {
                MessageBox.Show("请至少保留一个可写入的图块。", "自动排号", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            DialogResult = true;
        }
    }
}
