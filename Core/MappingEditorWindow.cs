using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.IO;
using System.Xml.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace XYDSignTool
{
    public class MappingSampleRow
    {
        public bool Selected { get; set; }
        public string BlockName { get; set; }
        public string Tag { get; set; }
        public string Prompt { get; set; }
        public string Example { get; set; }
        public bool ReadOnly { get; set; }
        public string ColumnName { get; set; }
        public string ColumnId { get; set; }
        public string OriginalColumnName { get; set; }
    }
    public class MappingEditorWindow : Window
    {
        public AttributeMappingProfile Profile { get; private set; }
        public bool Accepted { get; private set; }
        private readonly ObservableCollection<MappingSampleRow> _samples = new ObservableCollection<MappingSampleRow>();
        private DataGrid _grid, _columns;
        private TextBox _name;
        private ComboBox _quantity;
        public MappingEditorWindow(bool titleBlocks, AttributeMappingProfile initial = null)
        {
            if (initial == null) Profile = new AttributeMappingProfile { TitleBlocks = titleBlocks };
            else
            {
                var serializer = new XmlSerializer(typeof(AttributeMappingProfile));
                using (var stream = new MemoryStream()) { serializer.Serialize(stream, initial); stream.Position = 0; Profile = (AttributeMappingProfile)serializer.Deserialize(stream); }
            }
            Title = titleBlocks ? "高级图框映射与签名设置" : "自定义清单映射";
            Width = Math.Min(1150, SystemParameters.WorkArea.Width - 40); Height = Math.Min(720, SystemParameters.WorkArea.Height - 60);
            WindowStartupLocation = WindowStartupLocation.CenterScreen; ShowInTaskbar = false;
            Background = System.Windows.Media.Brushes.White;
            var root = new DockPanel { Margin = new Thickness(12), Background = System.Windows.Media.Brushes.White };
            var top = new WrapPanel(); DockPanel.SetDock(top, Dock.Top);
            _name = new TextBox { Width = 190, Text = Profile.Name, Margin = new Thickness(4) }; top.Children.Add(_name);
            AddButton(top, "新建", () => { Profile = new AttributeMappingProfile { TitleBlocks = titleBlocks }; LoadRows(); });
            AddButton(top, "添加属性块样本", AddSamples);
            AddButton(top, "加载模板", LoadProfile);
            AddButton(top, "保存", () => Save(false)); AddButton(top, "另存", () => Save(true));
            AddButton(top, "删除模板", () => { MappingProfileStore.Delete(Profile); MessageBox.Show("已删除当前模板文件，本次设置仍可使用。"); });
            if (titleBlocks) AddButton(top, "电子签设置", ConfigureSignatures);
            root.Children.Add(top);
            var bottom = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right }; DockPanel.SetDock(bottom, Dock.Bottom);
            AddButton(bottom, "使用本次映射并提取", () => { Sync(); if (Profile.Rules.Count == 0) throw new InvalidOperationException("请先添加并勾选属性。"); Accepted = true; Close(); });
            AddButton(bottom, "取消", Close); root.Children.Add(bottom);
            var panel = new DockPanel();
            var settings = new StackPanel(); DockPanel.SetDock(settings, Dock.Bottom);
            settings.Children.Add(new TextBlock { Text = "列设置：同名目标列会合并；分组用于汇总。目录用途可设为图名、图号、比例、图幅、版本、日期、备注。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 4) });
            _columns = new DataGrid { AutoGenerateColumns = false, CanUserAddRows = false, Height = 160 };
            _columns.Columns.Add(TextColumn("列名", "Name", 180));
            _columns.Columns.Add(new DataGridCheckBoxColumn { Header = "显示/导出", Binding = new Binding("Visible"), Width = 90 });
            _columns.Columns.Add(new DataGridCheckBoxColumn { Header = "汇总分组", Binding = new Binding("Group"), Width = 90 });
            _columns.Columns.Add(new DataGridComboBoxColumn { Header = "目录用途", ItemsSource = new[] { "", "图名", "图号", "比例", "图幅", "版本", "日期", "备注" }, SelectedItemBinding = new Binding("Semantic"), Width = 100 });
            settings.Children.Add(_columns);
            var q = new WrapPanel(); q.Children.Add(new TextBlock { Text = "汇总数量列（不选则计数）：", VerticalAlignment = VerticalAlignment.Center });
            _quantity = new ComboBox { Width = 160, DisplayMemberPath = "Name", SelectedValuePath = "Id", Margin = new Thickness(5) }; q.Children.Add(_quantity);
            AddButton(q, "使用实例计数", () => { _quantity.SelectedItem = null; Profile.QuantityColumnId = ""; });
            AddButton(q, "更新列设置", Sync); settings.Children.Add(q); panel.Children.Add(settings);
            _grid = new DataGrid { AutoGenerateColumns = false, CanUserAddRows = false, ItemsSource = _samples };
            _grid.Columns.Add(new DataGridCheckBoxColumn { Header = "提取", Binding = new Binding("Selected"), Width = 50 });
            _grid.Columns.Add(TextColumn("有效块名", "BlockName", 200, true)); _grid.Columns.Add(TextColumn("属性标签", "Tag", 160, true));
            _grid.Columns.Add(TextColumn("说明", "Prompt", 120, true)); _grid.Columns.Add(TextColumn("示例值", "Example", 140, true));
            _grid.Columns.Add(TextColumn("清单列名（同名合并）", "ColumnName", 180));
            _grid.Columns.Add(new DataGridCheckBoxColumn { Header = "只读", Binding = new Binding("ReadOnly"), IsReadOnly = true, Width = 50 });
            panel.Children.Add(_grid); root.Children.Add(panel); Content = root; LoadRows();
        }
        public static DataGridTextColumn TextColumn(string title, string path, double width, bool readOnly = false)
        { return new DataGridTextColumn { Header = title, Binding = new Binding(path) { Mode = readOnly ? BindingMode.OneWay : BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = width, IsReadOnly = readOnly }; }
        public static void AddButton(Panel panel, string text, Action action)
        {
            var button = new Button { Content = text, Padding = new Thickness(9, 5, 9, 5), Margin = new Thickness(3) };
            button.Click += (s, e) => { try { action(); } catch (System.Exception ex) { MessageBox.Show(ex.Message, "XYD", MessageBoxButton.OK, MessageBoxImage.Warning); } };
            panel.Children.Add(button);
        }
        private void LoadRows()
        {
            _samples.Clear(); _name.Text = Profile.Name;
            foreach (var r in Profile.Rules) foreach (var f in r.Fields)
            {
                var c = Profile.Columns.First(v => v.Id == f.ColumnId);
                _samples.Add(new MappingSampleRow { Selected = true, BlockName = r.BlockName, Tag = f.Tag, ReadOnly = f.ReadOnly, ColumnId = c.Id, ColumnName = c.Name, OriginalColumnName = c.Name });
            }
            RefreshColumns();
        }
        private void RefreshColumns()
        { _columns.ItemsSource = Profile.Columns; _columns.Items.Refresh(); _quantity.ItemsSource = Profile.Columns; _quantity.SelectedValue = Profile.QuantityColumnId; }
        private void Sync()
        {
            _grid.CommitEdit(DataGridEditingUnit.Cell, true); _grid.CommitEdit(DataGridEditingUnit.Row, true);
            _columns.CommitEdit(DataGridEditingUnit.Cell, true); _columns.CommitEdit(DataGridEditingUnit.Row, true);
            Profile.Name = _name.Text.Trim();
            Profile.QuantityColumnId = _quantity.SelectedValue as string ?? "";
            foreach (var rule in Profile.Rules) rule.Fields.Clear();
            foreach (var row in _samples.Where(r => r.Selected))
            {
                if (string.IsNullOrWhiteSpace(row.ColumnName)) throw new InvalidOperationException("请填写列名：" + row.Tag);
                var col = row.ColumnName == row.OriginalColumnName ? Profile.Columns.FirstOrDefault(c => c.Id == row.ColumnId) : null;
                col = col ?? Profile.Columns.FirstOrDefault(c => string.Equals(c.Name, row.ColumnName.Trim(), StringComparison.OrdinalIgnoreCase));
                if (col == null) { col = new MappingColumn { Name = row.ColumnName.Trim(), Order = Profile.Columns.Count }; Profile.Columns.Add(col); }
                row.ColumnId = col.Id; row.ColumnName = col.Name; row.OriginalColumnName = col.Name;
                var rule = Profile.Rules.FirstOrDefault(r => string.Equals(r.BlockName, row.BlockName, StringComparison.OrdinalIgnoreCase));
                if (rule == null) { rule = new MappingRule { BlockName = row.BlockName }; Profile.Rules.Add(rule); }
                rule.Fields.Add(new MappingField { Tag = row.Tag, ColumnId = col.Id, ReadOnly = row.ReadOnly });
            }
            // Keep signature-only rules; discard orphan output columns.
            Profile.Columns.RemoveAll(c => !Profile.Rules.Any(r => r.Fields.Any(f => f.ColumnId == c.Id)));
            if (!Profile.Columns.Any(c => c.Id == Profile.QuantityColumnId)) Profile.QuantityColumnId = "";
            Profile.Rules.RemoveAll(r => r.Fields.Count == 0 && r.Signatures.Count == 0);
            Profile.Validate(); RefreshColumns(); _grid.Items.Refresh();
        }
        private void AddSamples()
        {
            Sync(); var doc = Application.DocumentManager.MdiActiveDocument;
            Hide();
            try
            {
                var result = doc.Editor.GetSelection(new PromptSelectionOptions { MessageForAdding = "\n选择一个或多个属性块样本：" }, new SelectionFilter(new[] { new TypedValue(0, "INSERT") }));
                if (result.Status != PromptStatus.OK) return;
                foreach (var sample in AttributeMappingService.Samples(doc.Database, result.Value.GetObjectIds()))
                {
                    var existing = _samples.FirstOrDefault(r => string.Equals(r.BlockName, sample.BlockName, StringComparison.OrdinalIgnoreCase) && string.Equals(r.Tag, sample.Tag, StringComparison.OrdinalIgnoreCase));
                    if (existing != null) { existing.Prompt = sample.Prompt; existing.Example = sample.Example; existing.ReadOnly = sample.ReadOnly; }
                    else _samples.Add(new MappingSampleRow { BlockName = sample.BlockName, Tag = sample.Tag, Prompt = sample.Prompt, Example = sample.Example, ReadOnly = sample.ReadOnly, ColumnName = sample.Tag });
                }
                _grid.Items.Refresh();
            }
            finally { Show(); Activate(); }
        }
        private void Save(bool asNew)
        {
            Sync(); if (asNew) Profile.Id = Guid.NewGuid().ToString("N");
            MappingProfileStore.Save(Profile); MessageBox.Show("模板已保存：" + Profile.Name);
        }
        private void LoadProfile()
        {
            var profiles = MappingProfileStore.LoadAll(Profile.TitleBlocks).ToList();
            var dialog = new Window { Title = "加载映射模板", Width = 420, Height = 360, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var panel = new DockPanel { Margin = new Thickness(12) }; var list = new ListBox { ItemsSource = profiles, DisplayMemberPath = "Name" };
            var buttons = new WrapPanel(); DockPanel.SetDock(buttons, Dock.Bottom);
            AddButton(buttons, "加载", () => { if (list.SelectedItem == null) return; Profile = (AttributeMappingProfile)list.SelectedItem; LoadRows(); dialog.Close(); });
            panel.Children.Add(buttons); panel.Children.Add(list); dialog.Content = panel; dialog.ShowDialog();
        }
        private void ConfigureSignatures()
        {
            Sync(); Hide(); try { var window = new SignatureSetupWindow(Profile); Application.ShowModalWindow(window); } finally { Show(); Activate(); }
            RefreshColumns();
        }
    }
}
