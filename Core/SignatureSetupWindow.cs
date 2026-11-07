using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Xml.Serialization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Win32;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace XYDSignTool
{
    public class SignatureSlotRow : SignaturePosition { public string BlockName { get; set; } public List<string> TagChoices { get; set; } = new List<string>(); }
    public class SignatureSetupWindow : Window
    {
        private readonly AttributeMappingProfile _profile;
        private readonly AttributeMappingProfile _destination;
        private readonly ObservableCollection<SignatureSlotRow> _slots = new ObservableCollection<SignatureSlotRow>();
        private readonly ObservableCollection<SignaturePerson> _people;
        private readonly TextBox _library;
        private readonly DataGrid _slotGrid, _peopleGrid;
        private readonly TextBlock _hint;
        public SignatureSetupWindow(AttributeMappingProfile profile)
        {
            _destination = profile;
            var serializer = new XmlSerializer(typeof(AttributeMappingProfile));
            using (var stream = new MemoryStream()) { serializer.Serialize(stream, profile); stream.Position = 0; _profile = (AttributeMappingProfile)serializer.Deserialize(stream); }
            _people = new ObservableCollection<SignaturePerson>(_profile.People);
            foreach (var rule in _profile.Rules) foreach (var s in rule.Signatures)
                _slots.Add(new SignatureSlotRow { BlockName = rule.BlockName, Id = s.Id, Role = s.Role, NameTag = s.NameTag, Countersign = s.Countersign, X = s.X, Y = s.Y, Z = s.Z, Width = s.Width, Height = s.Height, TagChoices = rule.Fields.Select(f => f.Tag).Concat(rule.Signatures.Select(p => p.NameTag)).Distinct(StringComparer.OrdinalIgnoreCase).ToList() });
            Title = "电子签设置：一人多位置 / 图签保留姓名 / 会签隐藏姓名";
            Width = Math.Min(1100, SystemParameters.WorkArea.Width - 40); Height = Math.Min(700, SystemParameters.WorkArea.Height - 60);
            WindowStartupLocation = WindowStartupLocation.CenterScreen; ShowInTaskbar = false;
            Background = Brushes.White;
            var root = new DockPanel { Margin = new Thickness(12), Background = Brushes.White }; var top = new WrapPanel(); DockPanel.SetDock(top, Dock.Top);
            _library = new TextBox { Text = profile.SignatureLibraryPath, Width = 550, Margin = new Thickness(4) }; top.Children.Add(_library);
            MappingEditorWindow.AddButton(top, "选择本地签名库", ChooseLibrary);
            MappingEditorWindow.AddButton(top, "添加签名位置", AddPosition);
            MappingEditorWindow.AddButton(top, "删除选中位置", () => { var slot = _slotGrid.SelectedItem as SignatureSlotRow; if (slot != null) _slots.Remove(slot); });
            root.Children.Add(top);
            var bottom = new WrapPanel(); DockPanel.SetDock(bottom, Dock.Bottom);
            MappingEditorWindow.AddButton(bottom, "预览选中人员签名", Preview);
            MappingEditorWindow.AddButton(bottom, "应用本次设置", () => { ApplySettings(); Close(); });
            MappingEditorWindow.AddButton(bottom, "应用并保存模板", () => { ApplySettings(); MappingProfileStore.Save(profile); Close(); });
            root.Children.Add(bottom);
            var main = new Grid(); main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            _hint = new TextBlock { Text = "先选择签名 DWG，姓名与块名相同会自动匹配，可手动修改映射。添加位置时选图框，再点签名区域两角。角色支持设计、校对、审核、项目经理、负责人、会签及自定义名称。会签必须使用独立姓名属性；其文字无论是否添加电子签都隐藏。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 8) };
            main.Children.Add(_hint);
            _slotGrid = new DataGrid { AutoGenerateColumns = false, CanUserAddRows = false, ItemsSource = _slots };
            _slotGrid.Columns.Add(MappingEditorWindow.TextColumn("图框有效块名", "BlockName", 200, true));
            var roleFactory = new FrameworkElementFactory(typeof(ComboBox));
            roleFactory.SetValue(ComboBox.ItemsSourceProperty, new[] { "设计", "校对", "审核", "项目经理", "负责人", "会签" });
            roleFactory.SetValue(ComboBox.IsEditableProperty, true);
            roleFactory.SetBinding(ComboBox.TextProperty, new Binding("Role") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            _slotGrid.Columns.Add(new DataGridTemplateColumn { Header = "角色（可自定义）", Width = 130, CellTemplate = new DataTemplate { VisualTree = roleFactory } });
            var tagFactory = new FrameworkElementFactory(typeof(ComboBox));
            tagFactory.SetBinding(ComboBox.ItemsSourceProperty, new Binding("TagChoices"));
            tagFactory.SetBinding(ComboBox.TextProperty, new Binding("NameTag") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            tagFactory.SetValue(ComboBox.IsEditableProperty, true);
            var tagTemplate = new DataTemplate { VisualTree = tagFactory };
            _slotGrid.Columns.Add(new DataGridTemplateColumn { Header = "姓名属性标签", Width = 170, CellTemplate = tagTemplate });
            _slotGrid.Columns.Add(new DataGridCheckBoxColumn { Header = "会签（始终隐藏姓名）", Binding = new Binding("Countersign"), Width = 150 });
            foreach (string key in new[] { "X", "Y", "Z", "Width", "Height" }) _slotGrid.Columns.Add(MappingEditorWindow.TextColumn(key, key, 70));
            Grid.SetRow(_slotGrid, 1); main.Children.Add(_slotGrid);
            _peopleGrid = new DataGrid { AutoGenerateColumns = false, CanUserAddRows = true, CanUserDeleteRows = true, ItemsSource = _people, Margin = new Thickness(0, 10, 0, 0) };
            _peopleGrid.Columns.Add(MappingEditorWindow.TextColumn("姓名（匹配属性值）", "Name", 250));
            _peopleGrid.Columns.Add(MappingEditorWindow.TextColumn("对应签名块名", "BlockName", 350));
            Grid.SetRow(_peopleGrid, 2); main.Children.Add(_peopleGrid); root.Children.Add(main); Content = root;
        }
        private void ChooseLibrary()
        {
            var dialog = new OpenFileDialog { Filter = "签名图块库 (*.dwg)|*.dwg" };
            if (dialog.ShowDialog() != true) return;
            var entries = ElectronicSignatureService.Catalog(dialog.FileName);
            _library.Text = dialog.FileName; _people.Clear(); foreach (var entry in entries) _people.Add(entry);
        }
        private void AddPosition()
        {
            var doc = Application.DocumentManager.MdiActiveDocument; Hide();
            try
            {
                var opt = new PromptEntityOptions("\n请选择图框属性块样本："); opt.SetRejectMessage("请选择属性块。"); opt.AddAllowedClass(typeof(BlockReference), true);
                var picked = doc.Editor.GetEntity(opt); if (picked.Status != PromptStatus.OK) return;
                Matrix3d inverse; string name;
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    var block = (BlockReference)tr.GetObject(picked.ObjectId, OpenMode.ForRead); inverse = block.BlockTransform.Inverse(); name = AttributeMappingService.EffectiveName(block, tr);
                }
                var samples = AttributeMappingService.Samples(doc.Database, new[] { picked.ObjectId });
                if (!samples.Any(s => !s.ReadOnly)) throw new InvalidOperationException("图框没有可用的姓名属性。");
                var a = doc.Editor.GetPoint("\n点选本签名区域第一角："); if (a.Status != PromptStatus.OK) return;
                var b = doc.Editor.GetPoint(new PromptPointOptions("\n点选签名区域另一角：") { UseBasePoint = true, BasePoint = a.Value }); if (b.Status != PromptStatus.OK) return;
                // Editor.GetPoint returns WCS coordinates even with an active UCS.
                var p1 = a.Value.TransformBy(inverse); var p2 = b.Value.TransformBy(inverse);
                if (Math.Abs(p1.X - p2.X) < 1e-8 || Math.Abs(p1.Y - p2.Y) < 1e-8) throw new InvalidOperationException("签名区域宽高必须大于零。");
                var row = new SignatureSlotRow { BlockName = name, X = (p1.X + p2.X) / 2, Y = (p1.Y + p2.Y) / 2, Z = (p1.Z + p2.Z) / 2, Width = Math.Abs(p1.X - p2.X), Height = Math.Abs(p1.Y - p2.Y), NameTag = samples.First(s => !s.ReadOnly).Tag, TagChoices = samples.Where(s => !s.ReadOnly).Select(s => s.Tag).ToList() };
                _slots.Add(row); _slotGrid.SelectedItem = row;
                _hint.Text = "样本属性标签：" + string.Join("、", samples.Where(s => !s.ReadOnly).Select(s => s.Tag)) + "。请在位置表中设置角色、姓名标签和是否会签；可重复添加同一姓名的位置。";
            }
            finally { Show(); Activate(); }
        }
        private void ApplySettings()
        {
            Sync(); _destination.SignatureLibraryPath = _profile.SignatureLibraryPath; _destination.People = _profile.People; _destination.Rules = _profile.Rules;
        }
        private void Sync()
        {
            _slotGrid.CommitEdit(DataGridEditingUnit.Cell, true); _slotGrid.CommitEdit(DataGridEditingUnit.Row, true);
            _peopleGrid.CommitEdit(DataGridEditingUnit.Cell, true); _peopleGrid.CommitEdit(DataGridEditingUnit.Row, true);
            if (_slots.GroupBy(s => s.BlockName + "|" + s.NameTag, StringComparer.OrdinalIgnoreCase).Any(g => g.Select(s => s.Countersign).Distinct().Count() > 1)) throw new InvalidOperationException("图签和会签需使用不同姓名属性。");
            if (_people.Any(s => string.IsNullOrWhiteSpace(s.Name) || string.IsNullOrWhiteSpace(s.BlockName))) throw new InvalidOperationException("人员姓名及签名块名不能为空。");
            if (_people.GroupBy(s => s.Name.Trim(), StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1)) throw new InvalidOperationException("人员姓名重复，请保留唯一匹配。");
            if (_slots.Any(s => string.IsNullOrWhiteSpace(s.NameTag) || s.Width <= 0 || s.Height <= 0)) throw new InvalidOperationException("请设置姓名属性及有效签名区域。");
            _profile.SignatureLibraryPath = _library.Text.Trim(); _profile.People = _people.ToList();
            foreach (var rule in _profile.Rules) rule.Signatures.Clear();
            foreach (var s in _slots)
            {
                var rule = _profile.Rules.FirstOrDefault(r => string.Equals(r.BlockName, s.BlockName, StringComparison.OrdinalIgnoreCase));
                if (rule == null) { rule = new MappingRule { BlockName = s.BlockName }; _profile.Rules.Add(rule); }
                rule.Signatures.Add(new SignaturePosition { Id = s.Id, Role = s.Role, NameTag = s.NameTag, Countersign = s.Countersign, X = s.X, Y = s.Y, Z = s.Z, Width = s.Width, Height = s.Height });
            }
            _profile.Validate();
        }
        private void Preview()
        {
            Sync(); var person = _peopleGrid.SelectedItem as SignaturePerson; if (person == null) throw new InvalidOperationException("请先选中一位人员。");
            var slot = _slotGrid.SelectedItem as SignatureSlotRow ?? _slots.FirstOrDefault();
            if (slot == null) throw new InvalidOperationException("请先配置一个签名位置。");
            using (var prepared = ElectronicSignatureService.PrepareOne(_profile, person, slot))
            {
                var canvas = new Canvas { Width = 600, Height = 260, Background = Brushes.White };
                var bounds = prepared.Bounds; double w = bounds.MaxPoint.X - bounds.MinPoint.X, h = bounds.MaxPoint.Y - bounds.MinPoint.Y;
                double scale = Math.Min(560 / Math.Max(w, 1e-8), 220 / Math.Max(h, 1e-8));
                foreach (var entity in prepared.Geometry)
                {
                    var curve = entity as Curve; if (curve == null) continue;
                    var line = new System.Windows.Shapes.Polyline { Stroke = Brushes.Black, StrokeThickness = 1.4 };
                    for (int i = 0; i <= 100; i++)
                    {
                        var point = curve.GetPointAtParameter(curve.StartParam + (curve.EndParam - curve.StartParam) * i / 100);
                        line.Points.Add(new System.Windows.Point(20 + (point.X - bounds.MinPoint.X) * scale, 240 - (point.Y - bounds.MinPoint.Y) * scale));
                    }
                    canvas.Children.Add(line);
                }
                new Window { Title = person.Name + " / " + slot.Role + "，区域 " + slot.Width + " × " + slot.Height, Content = canvas, SizeToContent = SizeToContent.WidthAndHeight, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner }.ShowDialog();
            }
        }
    }
}
