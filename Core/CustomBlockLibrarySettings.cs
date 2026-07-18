using Autodesk.AutoCAD.DatabaseServices;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Xml.Serialization;

namespace XYDSignTool
{
    [Serializable]
    public class CustomBlockLibraryCatalog
    {
        public List<CustomBlockLibraryEntry> Libraries { get; set; } = new List<CustomBlockLibraryEntry>();
    }

    [Serializable]
    public class CustomBlockLibraryEntry : INotifyPropertyChanged
    {
        public string FilePath { get; set; }
        public long LastWriteTimeUtcTicks { get; set; }
        public List<string> BlockNames { get; set; } = new List<string>();

        [XmlIgnore]
        public bool IsAvailable { get; set; }

        private string _status;

        [XmlIgnore]
        public string Status
        {
            get { return _status ?? "尚未扫描"; }
            set
            {
                _status = value;
                OnPropertyChanged("Status");
                OnPropertyChanged("DisplayLabel");
            }
        }

        [XmlIgnore]
        public string DisplayName
        {
            get
            {
                string name = Path.GetFileName(FilePath);
                return string.IsNullOrWhiteSpace(name) ? (FilePath ?? "未命名图库") : name;
            }
        }

        [XmlIgnore]
        public string DisplayLabel
        {
            get { return DisplayName + "  [" + Status + "]"; }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class CustomBlockDescriptor
    {
        public string BlockName { get; set; }
        public string SourcePath { get; set; }
        public string SourceName { get { return Path.GetFileName(SourcePath); } }
    }

    public static class CustomBlockLibraryStore
    {
        public static string ConfigPath
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "XYDSignTool");
                return Path.Combine(dir, "CustomBlockLibraries.xml");
            }
        }

        public static CustomBlockLibraryCatalog Load()
        {
            CustomBlockLibraryCatalog catalog = new CustomBlockLibraryCatalog();
            try
            {
                if (File.Exists(ConfigPath))
                {
                    XmlSerializer serializer = new XmlSerializer(typeof(CustomBlockLibraryCatalog));
                    using (FileStream stream = File.OpenRead(ConfigPath))
                    {
                        catalog = serializer.Deserialize(stream) as CustomBlockLibraryCatalog ?? catalog;
                    }
                }
            }
            catch { }

            if (catalog.Libraries == null) catalog.Libraries = new List<CustomBlockLibraryEntry>();
            catalog.Libraries = catalog.Libraries
                .Where(entry => entry != null && !string.IsNullOrWhiteSpace(entry.FilePath))
                .GroupBy(entry => NormalizePath(entry.FilePath), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();

            foreach (CustomBlockLibraryEntry entry in catalog.Libraries)
            {
                entry.FilePath = NormalizePath(entry.FilePath);
                if (entry.BlockNames == null) entry.BlockNames = new List<string>();
            }
            return catalog;
        }

        public static void Save(CustomBlockLibraryCatalog catalog)
        {
            if (catalog == null) return;
            string dir = Path.GetDirectoryName(ConfigPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            XmlSerializer serializer = new XmlSerializer(typeof(CustomBlockLibraryCatalog));
            using (FileStream stream = File.Create(ConfigPath))
            {
                serializer.Serialize(stream, catalog);
            }
        }

        public static void RefreshSavedLibraries()
        {
            CustomBlockLibraryCatalog catalog = Load();
            bool changed = RefreshAll(catalog, false);
            if (changed) Save(catalog);
        }

        public static bool RefreshAll(CustomBlockLibraryCatalog catalog, bool force)
        {
            if (catalog == null || catalog.Libraries == null) return false;
            bool changed = false;
            foreach (CustomBlockLibraryEntry entry in catalog.Libraries)
            {
                if (RefreshEntry(entry, force)) changed = true;
            }
            return changed;
        }

        public static bool RefreshEntry(CustomBlockLibraryEntry entry, bool force)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.FilePath)) return false;
            entry.FilePath = NormalizePath(entry.FilePath);
            if (entry.BlockNames == null) entry.BlockNames = new List<string>();

            if (!File.Exists(entry.FilePath))
            {
                entry.IsAvailable = false;
                entry.Status = "文件不存在";
                return false;
            }

            long modifiedTicks;
            try { modifiedTicks = File.GetLastWriteTimeUtc(entry.FilePath).Ticks; }
            catch
            {
                entry.IsAvailable = false;
                entry.Status = "无法读取文件状态";
                return false;
            }

            if (!force && entry.LastWriteTimeUtcTicks == modifiedTicks && entry.LastWriteTimeUtcTicks != 0)
            {
                entry.IsAvailable = true;
                entry.Status = entry.BlockNames.Count == 0 ? "没有可插入图块" : entry.BlockNames.Count + " 个图块";
                return false;
            }

            string error;
            List<string> names = ScanInsertableBlockNames(entry.FilePath, out error);
            if (names == null)
            {
                bool changed = entry.LastWriteTimeUtcTicks != 0;
                entry.LastWriteTimeUtcTicks = 0;
                entry.IsAvailable = false;
                entry.Status = string.IsNullOrWhiteSpace(error) ? "读取失败" : "读取失败：" + error;
                return changed;
            }

            entry.BlockNames = names;
            entry.LastWriteTimeUtcTicks = modifiedTicks;
            entry.IsAvailable = true;
            entry.Status = names.Count == 0 ? "没有可插入图块" : names.Count + " 个图块";
            return true;
        }

        public static List<string> ScanInsertableBlockNames(string dwgPath, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(dwgPath) || !File.Exists(dwgPath))
            {
                error = "DWG 文件不存在。";
                return null;
            }

            try
            {
                HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using (Database db = new Database(false, true))
                {
                    db.ReadDwgFile(dwgPath, FileShare.Read, true, "");
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        BlockTable blockTable = tr.GetObject(db.BlockTableId, OpenMode.ForRead, false) as BlockTable;
                        if (blockTable == null)
                        {
                            error = "DWG 中没有有效的块表。";
                            return null;
                        }

                        foreach (ObjectId id in blockTable)
                        {
                            try
                            {
                                BlockTableRecord block = tr.GetObject(id, OpenMode.ForRead, false) as BlockTableRecord;
                                if (!IsInsertableBlock(block)) continue;
                                names.Add(block.Name);
                            }
                            catch { }
                        }
                        tr.Commit();
                    }
                }

                return names.OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase).ToList();
            }
            catch (System.Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        public static bool ContainsPath(CustomBlockLibraryCatalog catalog, string path)
        {
            if (catalog == null || catalog.Libraries == null || string.IsNullOrWhiteSpace(path)) return false;
            string normalized = NormalizePath(path);
            return catalog.Libraries.Any(entry => entry != null &&
                string.Equals(NormalizePath(entry.FilePath), normalized, StringComparison.OrdinalIgnoreCase));
        }

        public static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "";
            try { return Path.GetFullPath(path.Trim()); }
            catch { return path.Trim(); }
        }

        private static bool IsInsertableBlock(BlockTableRecord block)
        {
            if (block == null) return false;
            string name;
            try { name = block.Name; }
            catch { return false; }

            if (string.IsNullOrWhiteSpace(name) || name.StartsWith("*", StringComparison.Ordinal) || name.Contains("|")) return false;
            try { if (block.IsLayout || block.IsAnonymous || block.IsFromExternalReference || block.IsFromOverlayReference) return false; }
            catch { return false; }
            try { if (block.IsDependent) return false; }
            catch { }
            return true;
        }
    }
}
