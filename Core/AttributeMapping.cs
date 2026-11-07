using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using Autodesk.AutoCAD.DatabaseServices;

namespace XYDSignTool
{
    public class MappingColumn : INotifyPropertyChanged
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        private string _name = "";
        public string Name { get { return _name; } set { _name = value; Changed("Name"); } }
        private bool _visible = true;
        public bool Visible { get { return _visible; } set { _visible = value; Changed("Visible"); } }
        public int Order { get; set; }
        public bool Group { get; set; }
        public string Semantic { get; set; } = "";
        public event PropertyChangedEventHandler PropertyChanged;
        private void Changed(string name) { PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name)); }
    }
    public class MappingField
    {
        public string Tag { get; set; }
        public string ColumnId { get; set; }
        public bool ReadOnly { get; set; }
    }
    public class MappingRule
    {
        public string BlockName { get; set; }
        public List<MappingField> Fields { get; set; } = new List<MappingField>();
        public List<SignaturePosition> Signatures { get; set; } = new List<SignaturePosition>();
    }
    public class SignaturePosition
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Role { get; set; } = "设计";
        public string NameTag { get; set; } = "";
        public bool Countersign { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public double Width { get; set; } = 20;
        public double Height { get; set; } = 8;
    }
    public class SignaturePerson
    {
        public string Name { get; set; }
        public string BlockName { get; set; }
    }
    public class AttributeMappingProfile
    {
        public int SchemaVersion { get; set; } = 1;
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "未保存模板";
        public bool TitleBlocks { get; set; }
        public List<MappingColumn> Columns { get; set; } = new List<MappingColumn>();
        public List<MappingRule> Rules { get; set; } = new List<MappingRule>();
        public string QuantityColumnId { get; set; } = "";
        public string SignatureLibraryPath { get; set; } = "";
        public List<SignaturePerson> People { get; set; } = new List<SignaturePerson>();
        public void Validate()
        {
            if (SchemaVersion != 1) throw new InvalidOperationException("不支持的模板版本。");
            if (Columns.Any(c => string.IsNullOrWhiteSpace(c.Name)) || Columns.GroupBy(c => c.Id).Any(g => g.Count() > 1))
                throw new InvalidOperationException("列名不能为空，列标识不能重复。");
            if (Columns.GroupBy(c => c.Name.Trim(), StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
                throw new InvalidOperationException("列名重复，请合并映射到已有列，或使用不同列名。");
            if (Rules.GroupBy(r => r.BlockName, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
                throw new InvalidOperationException("同一有效块名只允许一组映射。");
            foreach (var rule in Rules)
            {
                if (string.IsNullOrWhiteSpace(rule.BlockName) || rule.Fields.Any(f => string.IsNullOrWhiteSpace(f.Tag))) throw new InvalidOperationException("块名和属性标签不能为空。");
                if (rule.Fields.GroupBy(f => f.Tag, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
                    throw new InvalidOperationException("同一块属性不能重复映射。");
                if (rule.Fields.GroupBy(f => f.ColumnId).Any(g => g.Count() > 1))
                    throw new InvalidOperationException("同一块的不同属性不能同时写入同一列。");
                if (rule.Fields.Any(f => !Columns.Any(c => c.Id == f.ColumnId)))
                    throw new InvalidOperationException("映射引用了不存在的列。");
                if (rule.Signatures.Any(s => string.IsNullOrWhiteSpace(s.NameTag) || s.Width <= 0 || s.Height <= 0 || new[] { s.X, s.Y, s.Z, s.Width, s.Height }.Any(n => double.IsNaN(n) || double.IsInfinity(n))))
                    throw new InvalidOperationException("签名位置需要姓名属性及有效区域。");
                if (rule.Signatures.GroupBy(s => s.Id).Any(g => g.Count() > 1)) throw new InvalidOperationException("同一图框的签名位置标识不能重复。");
                if (rule.Signatures.GroupBy(s => s.NameTag, StringComparer.OrdinalIgnoreCase).Any(g => g.Select(s => s.Countersign).Distinct().Count() > 1))
                    throw new InvalidOperationException("图签和会签需使用独立姓名属性，以便分别控制文字显示。");
            }
        }
    }
    public static class MappingProfileStore
    {
        public static AttributeMappingProfile CurrentTitleProfile { get; set; }
        public static string DirectoryPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XYDSignTool", "MappingProfiles"); } }
        public static IEnumerable<AttributeMappingProfile> LoadAll(bool titleBlocks)
        {
            if (!Directory.Exists(DirectoryPath)) yield break;
            foreach (string file in Directory.GetFiles(DirectoryPath, "*.xml"))
            {
                AttributeMappingProfile p;
                using (var stream = File.OpenRead(file)) p = (AttributeMappingProfile)new XmlSerializer(typeof(AttributeMappingProfile)).Deserialize(stream);
                p.Validate();
                if (p.TitleBlocks == titleBlocks) yield return p;
            }
        }
        public static void Save(AttributeMappingProfile p)
        {
            p.Validate(); Directory.CreateDirectory(DirectoryPath);
            string file = Path.Combine(DirectoryPath, Guid.Parse(p.Id).ToString("N") + ".xml");
            string temporary = file + ".tmp";
            using (var stream = File.Create(temporary)) new XmlSerializer(typeof(AttributeMappingProfile)).Serialize(stream, p);
            if (File.Exists(file)) File.Replace(temporary, file, file + ".bak"); else File.Move(temporary, file);
            if (p.TitleBlocks) { CurrentTitleProfile = p; File.WriteAllText(Path.Combine(DirectoryPath, "ActiveTitleMapping.txt"), p.Id); }
        }
        public static AttributeMappingProfile ActiveTitleProfile()
        {
            string active = Path.Combine(DirectoryPath, "ActiveTitleMapping.txt");
            if (!File.Exists(active)) return null;
            string id = Guid.Parse(File.ReadAllText(active).Trim()).ToString("N");
            string file = Path.Combine(DirectoryPath, id + ".xml"); if (!File.Exists(file)) return null;
            using (var stream = File.OpenRead(file)) { var p = (AttributeMappingProfile)new XmlSerializer(typeof(AttributeMappingProfile)).Deserialize(stream); p.Validate(); return p; }
        }
        public static void Delete(AttributeMappingProfile p) { File.Delete(Path.Combine(DirectoryPath, Guid.Parse(p.Id).ToString("N") + ".xml")); }
    }
    public class AttributeCell : INotifyPropertyChanged
    {
        private string _value;
        public string Value { get { return _value; } set { _value = value ?? ""; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Value")); } }
        public string Original { get; set; }
        public string Tag { get; set; }
        public bool ReadOnly { get; set; }
        public string Reason { get; set; }
        public bool Dirty { get { return !string.Equals(Value, Original, StringComparison.Ordinal); } }
        public event PropertyChangedEventHandler PropertyChanged;
    }
    public class AttributeRecord
    {
        public ObjectId ObjectId { get; set; }
        public Database OpenDatabase { get; set; }
        public string Fingerprint { get; set; }
        public string FilePath { get; set; }
        public string Handle { get; set; }
        public string Layout { get; set; }
        public string BlockName { get; set; }
        public string Mode { get; set; } = "";
        public bool Imported { get; set; }
        public int Category { get; set; }
        public string Area { get; set; }
        public SignItem Sign { get; set; }
        public string SourceDisplay { get { return Imported ? "JSON（不能回写）" : Path.GetFileName(FilePath); } }
        public string Status { get; set; }
        public Dictionary<string, AttributeCell> Cells { get; set; } = new Dictionary<string, AttributeCell>();
        public Dictionary<string, string> OriginalAttributes { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, bool> OriginalVisibility { get; set; } = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> Restrictions { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public string Value(string columnId) { AttributeCell c; return Cells.TryGetValue(columnId, out c) ? c.Value : ""; }
    }
    public class AttributeSample
    {
        public string BlockName { get; set; }
        public string Tag { get; set; }
        public string Prompt { get; set; }
        public string Example { get; set; }
        public bool ReadOnly { get; set; }
    }
}
