using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Win32;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace XYDSignTool
{
    public class AttributeCenterWindow : Window
    {
        private readonly AttributeMappingProfile _profile;
        private readonly ObservableCollection<AttributeRecord> _rows;
        private readonly bool _standard, _cost;
        private readonly List<string> _areaOrder;
        private bool _pricesConfirmed;
        private readonly DataGrid _grid;
        private readonly Dictionary<DataGridColumn, MappingColumn> _columnMap = new Dictionary<DataGridColumn, MappingColumn>();
        private readonly TextBox _search, _fill, _old;
        private readonly ComboBox _target, _range;
        private readonly CheckBox _signatures, _summary;
        private readonly TextBlock _status;
        public AttributeCenterWindow(AttributeMappingProfile profile, List<AttributeRecord> rows, bool standard = false, bool cost = false)
        {
            _profile = profile; _rows = new ObservableCollection<AttributeRecord>(rows); _standard = standard; _cost = cost;
            _areaOrder = rows.OrderBy(r => r.Category).Select(r => r.Area ?? "").Distinct().ToList();
            _pricesConfirmed = cost;
            Title = standard ? "标识清单中心" : profile.TitleBlocks ? "高级图框编辑" : "自定义映射清单中心";
            Width = Math.Min(1400, SystemParameters.WorkArea.Width - 40); Height = Math.Min(780, SystemParameters.WorkArea.Height - 60);
            WindowStartupLocation = WindowStartupLocation.CenterScreen; ShowInTaskbar = false;
            Background = Brushes.White;
            var root = new DockPanel { Margin = new Thickness(12), Background = Brushes.White }; var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top);
            var cols = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            MappingEditorWindow.AddButton(cols, "全选列", () => SetVisible(true)); MappingEditorWindow.AddButton(cols, "全不选", () => SetVisible(false));
            foreach (var c in profile.Columns.OrderBy(c => c.Order))
            {
                var checkbox = new CheckBox { Content = c.Name, Margin = new Thickness(5), VerticalAlignment = VerticalAlignment.Center };
                checkbox.SetBinding(CheckBox.IsCheckedProperty, new Binding("Visible") { Source = c, Mode = BindingMode.TwoWay });
                checkbox.Checked += (s, e) => RefreshVisibility(); checkbox.Unchecked += (s, e) => RefreshVisibility(); cols.Children.Add(checkbox);
            }
            top.Children.Add(cols);
            var tools = new WrapPanel();
            _target = new ComboBox { Width = 140, ItemsSource = profile.Columns, DisplayMemberPath = "Name", Margin = new Thickness(3), SelectedIndex = 0 }; tools.Children.Add(_target);
            tools.Children.Add(new TextBlock { Text = "筛选包含：", VerticalAlignment = VerticalAlignment.Center });
            _search = new TextBox { Width = 130, Margin = new Thickness(3) }; tools.Children.Add(_search);
            MappingEditorWindow.AddButton(tools, "筛选", () => Filter()); MappingEditorWindow.AddButton(tools, "清除筛选", () => { _search.Text = ""; Filter(); });
            _range = new ComboBox { ItemsSource = new[] { "选中行", "筛选结果", "全部行" }, SelectedIndex = 0, Width = 100, Margin = new Thickness(3) }; tools.Children.Add(_range);
            tools.Children.Add(new TextBlock { Text = "新值：", VerticalAlignment = VerticalAlignment.Center });
            _fill = new TextBox { Width = 140, Margin = new Thickness(3) }; tools.Children.Add(_fill);
            MappingEditorWindow.AddButton(tools, "批量填入", () => Fill(false));
            tools.Children.Add(new TextBlock { Text = "原值：", VerticalAlignment = VerticalAlignment.Center });
            _old = new TextBox { Width = 120, Margin = new Thickness(3) }; tools.Children.Add(_old);
            MappingEditorWindow.AddButton(tools, "同值替换", () => Fill(true)); top.Children.Add(tools);
            var options = new WrapPanel();
            _summary = new CheckBox { Content = "汇总导出（分组列在映射设置中选择）", Margin = new Thickness(5), IsEnabled = !standard }; options.Children.Add(_summary);
            _signatures = new CheckBox { Content = "增加 / 同步电子签", Margin = new Thickness(5), Visibility = profile.TitleBlocks ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed, IsChecked = false }; options.Children.Add(_signatures);
            if (!standard) MappingEditorWindow.AddButton(options, "编辑映射 / 追加样本", EditMapping);
            if (!standard) MappingEditorWindow.AddButton(options, "汇总预览", PreviewSummary);
            if (standard) MappingEditorWindow.AddButton(options, "单价设置", ConfigurePrices);
            options.Children.Add(new TextBlock { Text = "拖动表头调整输出顺序。未点击回写时，表格编辑不会修改 DWG。", Margin = new Thickness(6), Foreground = Brushes.Gray });
            top.Children.Add(options); root.Children.Add(top);
            var bottom = new WrapPanel(); DockPanel.SetDock(bottom, Dock.Bottom);
            MappingEditorWindow.AddButton(bottom, "回写图块属性", () => Write(false));
            if (profile.TitleBlocks) MappingEditorWindow.AddButton(bottom, "移除生成签名", () => Write(true));
            MappingEditorWindow.AddButton(bottom, "图内生成表格", GenerateCad);
            MappingEditorWindow.AddButton(bottom, "导出普通清单", () => Export("Excel", false));
            if (standard) MappingEditorWindow.AddButton(bottom, "导出造价清单", () => Export("Excel", true));
            MappingEditorWindow.AddButton(bottom, "导出清单 JSON", () => Export("JSON", false));
            if (!standard) MappingEditorWindow.AddButton(bottom, "保存模板及列设置", () => { Commit(); SaveOrder(); MappingProfileStore.Save(profile); _status.Text = "模板已保存。"; });
            MappingEditorWindow.AddButton(bottom, "退出", Close);
            _status = new TextBlock { Text = "明细：" + rows.Count + " 行。" + (profile.TitleBlocks ? "电子签默认关闭；应用图框设置时，会签姓名文字仍隐藏。" : "只读计算列、字段属性和没有来源的导入行不能回写。"), Margin = new Thickness(3), TextWrapping = TextWrapping.Wrap };
            var lower = new StackPanel(); DockPanel.SetDock(lower, Dock.Bottom); lower.Children.Add(bottom); lower.Children.Add(_status); root.Children.Add(lower);
            _grid = new DataGrid { AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, CanUserReorderColumns = true,
                ItemsSource = _rows, SelectionMode = DataGridSelectionMode.Extended, SelectionUnit = DataGridSelectionUnit.FullRow, FrozenColumnCount = 2, EnableRowVirtualization = true };
            _grid.Columns.Add(MappingEditorWindow.TextColumn("来源图纸", "SourceDisplay", 160, true)); _grid.Columns.Add(MappingEditorWindow.TextColumn("空间", "Layout", 90, true));
            foreach (var c in profile.Columns.OrderBy(c => c.Order))
            {
                var column = MappingEditorWindow.TextColumn(c.Name, "Cells[" + c.Id + "].Value", 140);
                column.CanUserSort = false; _grid.Columns.Add(column); _columnMap[column] = c;
            }
            _grid.Columns.Add(MappingEditorWindow.TextColumn("状态 / 限制", "Status", 210, true));
            _grid.BeginningEdit += (s, e) =>
            {
                MappingColumn c; AttributeCell cell;
                if (_columnMap.TryGetValue(e.Column, out c) && (!(e.Row.Item as AttributeRecord).Cells.TryGetValue(c.Id, out cell) || cell.ReadOnly))
                { e.Cancel = true; _status.Text = cell == null ? "该图块没有此属性。" : "不能编辑：" + (cell.Reason ?? "只读计算或导入数据"); }
            };
            root.Children.Add(_grid); Content = root; RefreshVisibility();
        }
        private void Commit()
        {
            if (!_grid.CommitEdit(DataGridEditingUnit.Cell, true) || !_grid.CommitEdit(DataGridEditingUnit.Row, true)) throw new InvalidOperationException("请先修正表格中的无效输入。");
            foreach (var r in _rows) StandardSignAdapter.RefreshCalculated(r);
        }
        private void ConfigurePrices()
        {
            Commit(); var techs = _rows.Where(r => r.Sign != null && r.Value("IsCount") != "否").Select(r => r.Value("Tech").Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (techs.Any(string.IsNullOrWhiteSpace)) throw new InvalidOperationException("有空工艺，请先填写。");
            var prices = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (string tech in techs) { double price; prices[tech] = RibbonBuilder.PriceDictCache.TryGetValue(tech, out price) ? price : 0; }
            var window = new PriceManagerWindow(prices, techs); Application.ShowModalWindow(window);
            if (!window.IsConfirmed) throw new InvalidOperationException("已取消单价设置，未导出造价。");
            foreach (var item in window.PriceItems) RibbonBuilder.PriceDictCache[item.Tech.Trim()] = item.Price;
            foreach (var row in _rows.Where(r => r.Sign != null))
            {
                double price; row.Sign.BasePrice = RibbonBuilder.PriceDictCache.TryGetValue(row.Value("Tech").Trim(), out price) ? price : 0;
                row.Cells["BasePrice"].Value = row.Sign.BasePrice.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            _pricesConfirmed = true; Commit();
        }
        private void SaveOrder() { foreach (var pair in _columnMap) pair.Value.Order = pair.Key.DisplayIndex; }
        private void SetVisible(bool visible) { foreach (var c in _profile.Columns) c.Visible = visible; RefreshVisibility(); }
        private void RefreshVisibility() { if (_grid == null) return; foreach (var pair in _columnMap) pair.Key.Visibility = pair.Value.Visible ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed; }
        private List<MappingColumn> SelectedColumns()
        {
            SaveOrder(); var columns = _profile.Columns.Where(c => c.Visible).OrderBy(c => c.Order).ToList();
            if (columns.Count == 0) throw new InvalidOperationException("请至少勾选一列。");
            if (_summary.IsChecked == true && string.IsNullOrWhiteSpace(_profile.QuantityColumnId)) columns.Add(new MappingColumn { Id = "__COUNT", Name = "实例数量" });
            return columns;
        }
        private List<AttributeRecord> OutputRows()
        {
            Commit(); var rows = CollectionViewSource.GetDefaultView(_rows).Cast<AttributeRecord>().ToList();
            if (_standard) rows = StandardSignAdapter.SortRows(rows.Where(r => r.Value("IsCount") != "否"), _areaOrder);
            return _summary.IsChecked == true ? AttributeMappingService.Summarize(rows, _profile) : rows;
        }
        private void Filter()
        {
            Commit(); var column = _target.SelectedItem as MappingColumn; string text = _search.Text;
            CollectionViewSource.GetDefaultView(_rows).Filter = item => string.IsNullOrEmpty(text) || (column != null && ((AttributeRecord)item).Value(column.Id).IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0);
        }
        private void Fill(bool replace)
        {
            Commit(); var column = _target.SelectedItem as MappingColumn; if (column == null) return;
            IEnumerable<AttributeRecord> scope = _range.SelectedIndex == 0 ? _grid.SelectedItems.Cast<AttributeRecord>() : _range.SelectedIndex == 1 ? CollectionViewSource.GetDefaultView(_rows).Cast<AttributeRecord>() : _rows;
            int changed = 0, skipped = 0;
            foreach (var row in scope.ToList())
            {
                AttributeCell cell;
                if (!row.Cells.TryGetValue(column.Id, out cell) || cell.ReadOnly) { skipped++; continue; }
                if (!replace || cell.Value == _old.Text) { cell.Value = _fill.Text; changed++; }
            }
            _status.Text = "已填入 " + changed + " 行；只读或缺失属性跳过 " + skipped + " 行。尚未回写。";
        }
        private void Write(bool remove)
        {
            Commit(); Hide();
            try
            {
                var result = AttributeMappingWriter.Write(_rows, _profile, !remove && _signatures.IsChecked == true, remove);
                _status.Text = result.ToString(); Application.ShowAlertDialog(result.ToString()); _grid.Items.Refresh();
            }
            finally { Show(); Activate(); }
        }
        private void Export(string kind, bool cost)
        {
            if (cost && (!_pricesConfirmed || _rows.Any(r => r.Sign != null && !RibbonBuilder.PriceDictCache.ContainsKey(r.Value("Tech").Trim())))) ConfigurePrices();
            var rows = OutputRows(); var columns = SelectedColumns();
            if (cost && rows.Any(r => r.Sign != null && string.IsNullOrWhiteSpace(r.Value("Tech")))) throw new InvalidOperationException("造价清单有空工艺，请先填写。");
            var dialog = new SaveFileDialog { Filter = kind == "JSON" ? "JSON (*.json)|*.json" : "Excel 清单 (*.xls)|*.xls", FileName = Title + (cost ? "_含造价" : "") + "_" + DateTime.Now.ToString("yyMMdd") };
            if (dialog.ShowDialog() != true) return;
            if (kind == "JSON") AttributeReportExporter.Json(dialog.FileName, rows, columns); else AttributeReportExporter.Excel(dialog.FileName, rows, columns, cost, _standard);
            _status.Text = "已导出：" + dialog.FileName;
        }
        private void GenerateCad()
        {
            var rows = OutputRows(); var columns = SelectedColumns(); var doc = Application.DocumentManager.MdiActiveDocument; Hide();
            try
            {
                var picked = doc.Editor.GetPoint("\n指定清单表格左上角："); if (picked.Status != PromptStatus.OK) return;
                using (var loc = doc.LockDocument()) AttributeReportExporter.Cad(doc.Database, picked.Value, rows, columns, Title, _standard);
            }
            finally { Show(); Activate(); }
        }
        private void PreviewSummary()
        {
            Commit(); var rows = AttributeMappingService.Summarize(CollectionViewSource.GetDefaultView(_rows).Cast<AttributeRecord>(), _profile);
            var grid = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, ItemsSource = rows, CanUserAddRows = false };
            foreach (var c in _profile.Columns.Where(c => c.Visible).OrderBy(c => c.Order)) grid.Columns.Add(MappingEditorWindow.TextColumn(c.Name, "Cells[" + c.Id + "].Value", 160, true));
            if (string.IsNullOrWhiteSpace(_profile.QuantityColumnId)) grid.Columns.Add(MappingEditorWindow.TextColumn("实例数量", "Cells[__COUNT].Value", 100, true));
            new Window { Title = "汇总预览（只读，编辑请返回明细）", Width = Width - 60, Height = 500, Content = grid, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner }.ShowDialog();
        }
        private void EditMapping()
        {
            Commit();
            if (_rows.Any(r => r.Cells.Values.Any(c => c.Dirty && !c.ReadOnly)) && MessageBox.Show("重新设置映射后需要重新提取。尚未回写的表格修改将丢弃，是否继续？", "重新提取", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            Hide();
            try
            {
                var editor = new MappingEditorWindow(_profile.TitleBlocks, _profile); Application.ShowModalWindow(editor);
                if (editor.Accepted) { Close(); AttributeCenterCommands.OpenAdvanced(editor.Profile); }
            }
            finally { if (IsLoaded) { Show(); Activate(); } }
        }
    }
}
