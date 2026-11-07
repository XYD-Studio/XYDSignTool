using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;
using Autodesk.AutoCAD.ApplicationServices;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace XYDSignTool
{
    public class AttributeSource
    {
        public bool Selected { get; set; }
        public string Path { get; set; }
        public Document Document { get; set; }
        public string Kind { get { return Document != null ? "已打开（当前内存）" : System.IO.Path.GetExtension(Path).ToLowerInvariant() == ".json" ? "JSON（仅导出）" : "外部 DWG"; } }
    }
    public class AttributeSourceWindow : Window
    {
        public ObservableCollection<AttributeSource> Sources { get; } = new ObservableCollection<AttributeSource>();
        public bool Accepted { get; private set; }
        public string Scope { get { return (string)_scope.SelectedItem; } }
        private readonly ComboBox _scope;
        public AttributeSourceWindow(bool standard)
        {
            Title = "选择提取来源"; Width = 820; Height = 440; WindowStartupLocation = WindowStartupLocation.CenterScreen; ShowInTaskbar = false;
            foreach (Document d in Application.DocumentManager) Sources.Add(new AttributeSource { Document = d, Path = d.Name, Selected = d == Application.DocumentManager.MdiActiveDocument });
            var root = new DockPanel { Margin = new Thickness(12) }; var top = new WrapPanel(); DockPanel.SetDock(top, Dock.Top);
            MappingEditorWindow.AddButton(top, "添加多个 DWG", () => AddFiles(false));
            if (standard) MappingEditorWindow.AddButton(top, "合并 JSON 数据库", () => AddFiles(true));
            MappingEditorWindow.AddButton(top, "全选", () => { foreach (var s in Sources) s.Selected = true; ((DataGrid)root.Children[root.Children.Count - 1]).Items.Refresh(); });
            top.Children.Add(new TextBlock { Text = "空间：", VerticalAlignment = VerticalAlignment.Center });
            _scope = new ComboBox { ItemsSource = new[] { "全部", "模型", "布局" }, SelectedIndex = standard ? 1 : 0, IsEnabled = !standard, Width = 100, Margin = new Thickness(4) }; top.Children.Add(_scope); root.Children.Add(top);
            var bottom = new WrapPanel(); DockPanel.SetDock(bottom, Dock.Bottom);
            MappingEditorWindow.AddButton(bottom, "提取", () => { var grid = (DataGrid)root.Children[root.Children.Count - 1]; grid.CommitEdit(DataGridEditingUnit.Cell, true); grid.CommitEdit(DataGridEditingUnit.Row, true); Accepted = Sources.Any(s => s.Selected); if (Accepted) Close(); });
            MappingEditorWindow.AddButton(bottom, "取消", Close); root.Children.Add(bottom);
            var data = new DataGrid { AutoGenerateColumns = false, CanUserAddRows = false, ItemsSource = Sources };
            data.Columns.Add(new DataGridCheckBoxColumn { Header = "提取", Binding = new Binding("Selected"), Width = 60 });
            data.Columns.Add(MappingEditorWindow.TextColumn("文件", "Path", 530, true)); data.Columns.Add(MappingEditorWindow.TextColumn("来源", "Kind", 170, true)); root.Children.Add(data); Content = root;
        }
        private void AddFiles(bool json)
        {
            var dialog = new OpenFileDialog { Filter = json ? "JSON (*.json)|*.json" : "DWG (*.dwg)|*.dwg", Multiselect = true };
            if (dialog.ShowDialog() != true) return;
            foreach (string path in dialog.FileNames)
            {
                var existing = Sources.FirstOrDefault(s => s.Document != null ? AttributeMappingService.DocumentMatchesPath(s.Document, path) : AttributeMappingService.SamePath(s.Path, path));
                if (existing != null) existing.Selected = true; else Sources.Add(new AttributeSource { Path = path, Selected = true });
            }
            var root = (DockPanel)Content; ((DataGrid)root.Children[root.Children.Count - 1]).Items.Refresh();
        }
    }
}
