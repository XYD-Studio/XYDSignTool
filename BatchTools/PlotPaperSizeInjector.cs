using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.PlottingServices;

namespace XYDSignTool
{
    public static class PlotPaperSizeInjector
    {
        public static bool InjectPaperSizesForPrinter(string printerName, out string message)
        {
            message = "";
            if (string.IsNullOrWhiteSpace(printerName))
            {
                message = "请先选择一个打印机设备。";
                return false;
            }

            string templateDir = GetBundledPmpTemplateDirectory();
            if (!Directory.Exists(templateDir))
            {
                message = $"未找到图纸尺寸模板目录：\n{templateDir}\n\n请先把导出的 .pmp 文件放到插件 Contents\\PMPFiles 目录。";
                return false;
            }

            string[] templates = Directory.GetFiles(templateDir, "*.pmp", SearchOption.TopDirectoryOnly);
            if (templates.Length == 0)
            {
                message = $"图纸尺寸模板目录中没有 .pmp 文件：\n{templateDir}\n\n请先从本机已配置好的 PC3 导出 PMP 文件。";
                return false;
            }

            string pmpDir = GetAutoCadPmpDirectory(printerName);
            if (string.IsNullOrWhiteSpace(pmpDir))
            {
                message = "无法定位 AutoCAD 的 PMP 文件目录。\n\n请确认当前打印机是 .pc3 设备，并至少打开过一次 AutoCAD 打印机特性。";
                return false;
            }

            Directory.CreateDirectory(pmpDir);
            string preferredTemplate = templates.FirstOrDefault(p => Path.GetFileName(p).Equals("XYD_PaperSizes.pmp", StringComparison.OrdinalIgnoreCase)) ?? templates[0];
            string targetName = GetTargetPmpFileName(printerName);
            string targetPath = Path.Combine(pmpDir, targetName);
            string pc3Path = GetPlotConfigPath(printerName);

            try
            {
                int copiedCount = 0;
                foreach (string template in templates)
                {
                    string dest = Path.Combine(pmpDir, Path.GetFileName(template));
                    if (!PathsEqual(template, dest))
                    {
                        File.Copy(template, dest, true);
                    }
                    copiedCount++;
                }

                string backupPath = null;
                if (File.Exists(targetPath))
                {
                    backupPath = targetPath + "." + DateTime.Now.ToString("yyyyMMddHHmmss") + ".bak";
                    File.Copy(targetPath, backupPath, true);
                }

                if (!PathsEqual(preferredTemplate, targetPath))
                {
                    WritePmpForTargetPrinter(preferredTemplate, targetPath);
                }

                string pc3BackupPath;
                string pc3Error;
                if (!TryAssociatePc3WithPmp(pc3Path, targetPath, out pc3BackupPath, out pc3Error))
                {
                    message =
                        $"图纸尺寸模板已复制，但当前打印机没有完成 PC3 关联，因此 AutoCAD 可能不会显示这些尺寸。\n\n" +
                        $"目标打印机：{printerName}\n" +
                        $"PMP 目录：{pmpDir}\n" +
                        $"目标 PMP：{targetPath}\n" +
                        $"PC3 关联失败：{pc3Error}\n\n" +
                        "请选择 AutoCAD 的 .pc3 打印设备，或先在 AutoCAD 打印机特性里为该打印机创建/保存一个 PC3 配置后再注入。";
                    return false;
                }

                PlotConfigManager.RefreshList(RefreshCode.All);

                message =
                    $"已注入图纸尺寸模板。\n\n" +
                    $"目标打印机：{printerName}\n" +
                    $"PMP 目录：{pmpDir}\n" +
                    $"目标 PMP：{targetPath}\n" +
                    $"PC3 文件：{pc3Path}\n" +
                    $"PC3 关联：已写入当前目标 PMP 路径\n" +
                    (string.IsNullOrEmpty(pc3BackupPath) ? "" : $"原 PC3 备份：{pc3BackupPath}\n") +
                    $"同步模板：{copiedCount} 个\n" +
                    (string.IsNullOrEmpty(backupPath) ? "" : $"原 PMP 备份：{backupPath}\n") +
                    "\n如果刷新后仍看不到新图纸尺寸，请关闭本窗口重新进入批量打印，或打开该 PC3 的打印机特性后点确定让 AutoCAD 重载配置。";
                return true;
            }
            catch (System.Exception ex)
            {
                message = $"注入图纸尺寸失败：\n{ex.Message}";
                return false;
            }
        }

        public static string GetBundledPmpTemplateDirectory()
        {
            string dllDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            return Path.Combine(dllDir, "PMPFiles");
        }

        private static void WritePmpForTargetPrinter(string templatePath, string targetPath)
        {
            string text;
            Encoding utf8NoBom = new UTF8Encoding(false);
            using (StreamReader reader = new StreamReader(templatePath, Encoding.UTF8, true))
            {
                text = reader.ReadToEnd();
            }

            if (!text.StartsWith("PIAFILEVERSION_3.0,json", StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(templatePath, targetPath, true);
                return;
            }

            string escapedTargetPath = EscapeJsonString(targetPath);
            Regex pathRegex = new Regex("(\"user_defined_model_pathname\"\\s*:\\s*\")([^\"]*)(\")", RegexOptions.IgnoreCase);
            string updated = pathRegex.Replace(
                text,
                match => match.Groups[1].Value + escapedTargetPath + match.Groups[3].Value,
                1);

            File.WriteAllText(targetPath, updated, utf8NoBom);
        }

        private static bool TryAssociatePc3WithPmp(string pc3Path, string targetPmpPath, out string backupPath, out string error)
        {
            backupPath = null;
            error = null;

            if (string.IsNullOrWhiteSpace(pc3Path))
            {
                error = "无法找到当前打印机对应的 PC3 文件。";
                return false;
            }

            if (!File.Exists(pc3Path))
            {
                error = $"PC3 文件不存在：{pc3Path}";
                return false;
            }

            try
            {
                byte[] originalBytes = File.ReadAllBytes(pc3Path);
                int zlibStart = FindZlibStart(originalBytes);
                if (zlibStart < 12)
                {
                    error = "PC3 不是可识别的 AutoCAD 压缩配置格式。";
                    return false;
                }

                int compressedLength = BitConverter.ToInt32(originalBytes, zlibStart - 4);
                if (compressedLength <= 0 || zlibStart + compressedLength > originalBytes.Length)
                {
                    compressedLength = originalBytes.Length - zlibStart;
                }

                byte[] compressedBytes = new byte[compressedLength];
                Buffer.BlockCopy(originalBytes, zlibStart, compressedBytes, 0, compressedLength);
                byte[] uncompressedBytes = DecompressZlibBytes(compressedBytes);
                string text = Encoding.Default.GetString(uncompressedBytes);

                string updatedText = ReplacePc3LineValue(text, "user_defined_model_pathname", targetPmpPath);
                if (updatedText == null)
                {
                    error = "PC3 内缺少 user_defined_model_pathname 字段。";
                    return false;
                }

                if (updatedText == text)
                {
                    return true;
                }

                byte[] updatedUncompressedBytes = Encoding.Default.GetBytes(updatedText);
                byte[] updatedCompressedBytes = CompressZlibBytes(updatedUncompressedBytes);
                uint compressedChecksum = Adler32(updatedCompressedBytes);

                byte[] outputBytes = new byte[zlibStart + updatedCompressedBytes.Length];
                Buffer.BlockCopy(originalBytes, 0, outputBytes, 0, zlibStart - 12);
                WriteUInt32LittleEndian(outputBytes, zlibStart - 12, compressedChecksum);
                WriteInt32LittleEndian(outputBytes, zlibStart - 8, updatedUncompressedBytes.Length);
                WriteInt32LittleEndian(outputBytes, zlibStart - 4, updatedCompressedBytes.Length);
                Buffer.BlockCopy(updatedCompressedBytes, 0, outputBytes, zlibStart, updatedCompressedBytes.Length);

                backupPath = pc3Path + "." + DateTime.Now.ToString("yyyyMMddHHmmss") + ".bak";
                File.Copy(pc3Path, backupPath, true);
                File.WriteAllBytes(pc3Path, outputBytes);
                return true;
            }
            catch (System.Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static int FindZlibStart(byte[] bytes)
        {
            if (bytes == null) return -1;

            for (int i = 0; i < bytes.Length - 1; i++)
            {
                if (bytes[i] == 0x78 && (bytes[i + 1] == 0xDA || bytes[i + 1] == 0x9C))
                {
                    return i;
                }
            }

            return -1;
        }

        private static byte[] DecompressZlibBytes(byte[] zlibBytes)
        {
            if (zlibBytes == null || zlibBytes.Length < 6)
            {
                throw new InvalidOperationException("压缩数据长度无效。");
            }

            using (MemoryStream source = new MemoryStream(zlibBytes, 2, zlibBytes.Length - 6))
            using (DeflateStream deflate = new DeflateStream(source, CompressionMode.Decompress))
            using (MemoryStream output = new MemoryStream())
            {
                deflate.CopyTo(output);
                return output.ToArray();
            }
        }

        private static byte[] CompressZlibBytes(byte[] uncompressedBytes)
        {
            byte[] deflatedBytes;
            using (MemoryStream raw = new MemoryStream())
            {
                using (DeflateStream deflate = new DeflateStream(raw, CompressionLevel.Optimal, true))
                {
                    deflate.Write(uncompressedBytes, 0, uncompressedBytes.Length);
                }
                deflatedBytes = raw.ToArray();
            }

            uint adler = Adler32(uncompressedBytes);
            using (MemoryStream zlib = new MemoryStream())
            {
                zlib.WriteByte(0x78);
                zlib.WriteByte(0xDA);
                zlib.Write(deflatedBytes, 0, deflatedBytes.Length);
                zlib.WriteByte((byte)((adler >> 24) & 0xFF));
                zlib.WriteByte((byte)((adler >> 16) & 0xFF));
                zlib.WriteByte((byte)((adler >> 8) & 0xFF));
                zlib.WriteByte((byte)(adler & 0xFF));
                return zlib.ToArray();
            }
        }

        private static string ReplacePc3LineValue(string text, string key, string value)
        {
            bool replaced = false;
            Regex regex = new Regex("(^\\s*" + Regex.Escape(key) + "=\")[^\\r\\n]*", RegexOptions.Multiline);
            string updated = regex.Replace(
                text,
                match =>
                {
                    replaced = true;
                    return match.Groups[1].Value + value;
                },
                1);

            return replaced ? updated : null;
        }

        private static uint Adler32(byte[] data)
        {
            const uint mod = 65521;
            uint a = 1;
            uint b = 0;

            foreach (byte value in data)
            {
                a = (a + value) % mod;
                b = (b + a) % mod;
            }

            return (b << 16) | a;
        }

        private static void WriteUInt32LittleEndian(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)(value & 0xFF);
            buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
            buffer[offset + 2] = (byte)((value >> 16) & 0xFF);
            buffer[offset + 3] = (byte)((value >> 24) & 0xFF);
        }

        private static void WriteInt32LittleEndian(byte[] buffer, int offset, int value)
        {
            WriteUInt32LittleEndian(buffer, offset, unchecked((uint)value));
        }

        private static string EscapeJsonString(string value)
        {
            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"");
        }

        private static string GetAutoCadPmpDirectory(string printerName)
        {
            string descPaths = ReadAutoCadPreferencePath("PrinterDescPath", "PrinterDescDir");
            string pmpDir = PickPathFromList(descPaths);
            if (!string.IsNullOrWhiteSpace(pmpDir)) return pmpDir;

            pmpDir = GetPmpDirectoryNearPlotConfig(printerName);
            if (!string.IsNullOrWhiteSpace(pmpDir)) return pmpDir;

            return FindKnownPmpDirectoryUnderAppData();
        }

        private static string GetPmpDirectoryNearPlotConfig(string printerName)
        {
            string pc3Path = GetPlotConfigPath(printerName);
            if (string.IsNullOrWhiteSpace(pc3Path)) return null;

            try
            {
                string pc3Dir = Path.GetDirectoryName(pc3Path);
                if (string.IsNullOrWhiteSpace(pc3Dir)) return null;

                string[] candidates =
                {
                    Path.Combine(pc3Dir, "PMP Files"),
                    Path.Combine(pc3Dir, "PMPFiles"),
                    Path.Combine(pc3Dir, "PMP")
                };

                foreach (string candidate in candidates)
                {
                    if (Directory.Exists(candidate)) return candidate;
                }

                return candidates[0];
            }
            catch
            {
                return null;
            }
        }

        private static string GetTargetPmpFileName(string printerName)
        {
            string name = printerName.Trim();
            string pc3Path = GetPlotConfigPath(printerName);
            if (!string.IsNullOrWhiteSpace(pc3Path))
            {
                name = Path.GetFileNameWithoutExtension(pc3Path);
            }

            name = Path.GetFileNameWithoutExtension(name);
            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(invalid, '_');
            }

            return name + ".pmp";
        }

        private static string GetPlotConfigPath(string printerName)
        {
            string fullPath = null;
            try
            {
                using (PlotConfig config = PlotConfigManager.SetCurrentConfig(printerName))
                {
                    if (config != null && !string.IsNullOrWhiteSpace(config.FullPath))
                    {
                        fullPath = config.FullPath;
                    }
                }
            }
            catch { }

            if (string.IsNullOrWhiteSpace(fullPath)) return null;
            if (Path.IsPathRooted(fullPath) && File.Exists(fullPath)) return fullPath;

            string fileName = Path.GetFileName(fullPath);
            if (string.IsNullOrWhiteSpace(fileName)) fileName = Path.GetFileName(printerName);
            if (string.IsNullOrWhiteSpace(fileName)) return fullPath;

            string configPaths = ReadAutoCadPreferencePath("PrinterConfigPath", "PrinterConfigDir");
            foreach (string dir in SplitPathList(configPaths))
            {
                string candidate = Path.Combine(dir, fileName);
                if (File.Exists(candidate)) return candidate;
            }

            return fullPath;
        }

        private static string ReadAutoCadPreferencePath(params string[] propertyNames)
        {
            try
            {
                dynamic acadApp = Autodesk.AutoCAD.ApplicationServices.Application.AcadApplication;
                dynamic files = acadApp.Preferences.Files;

                foreach (string propertyName in propertyNames)
                {
                    string value = ReadAutoCadFilesProperty(files, propertyName);
                    if (!string.IsNullOrWhiteSpace(value)) return value;
                }
            }
            catch { }

            return null;
        }

        private static string ReadAutoCadFilesProperty(dynamic files, string propertyName)
        {
            try
            {
                switch (propertyName)
                {
                    case "PrinterDescPath":
                        return Convert.ToString(files.PrinterDescPath);
                    case "PrinterDescDir":
                        return Convert.ToString(files.PrinterDescDir);
                    case "PrinterConfigPath":
                        return Convert.ToString(files.PrinterConfigPath);
                    case "PrinterConfigDir":
                        return Convert.ToString(files.PrinterConfigDir);
                    default:
                        return null;
                }
            }
            catch
            {
                return null;
            }
        }

        private static string PickPathFromList(string paths)
        {
            string firstUsable = null;
            foreach (string dir in SplitPathList(paths))
            {
                if (firstUsable == null) firstUsable = dir;
                if (Directory.Exists(dir)) return dir;
            }

            return firstUsable;
        }

        private static string[] SplitPathList(string paths)
        {
            if (string.IsNullOrWhiteSpace(paths)) return new string[0];

            return paths
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => Environment.ExpandEnvironmentVariables(p.Trim().Trim('"')))
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToArray();
        }

        private static string FindKnownPmpDirectoryUnderAppData()
        {
            try
            {
                string autodeskDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Autodesk");

                if (!Directory.Exists(autodeskDir)) return null;

                string[] dirs = Directory.GetDirectories(autodeskDir, "PMP Files", SearchOption.AllDirectories);
                return dirs.FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        private static bool PathsEqual(string left, string right)
        {
            return string.Equals(
                Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
    }
}
