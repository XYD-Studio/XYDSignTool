using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Serialization;

namespace XYDSignTool
{
    public class TitleBlockRecognitionRule
    {
        public bool Enabled { get; set; } = true;
        public string BlockNamePrefix { get; set; } = "";
        public string DrawTitleTags { get; set; } = "";
        public string DrawNumTags { get; set; } = "";
        public string DrawScaleTags { get; set; } = "";
        public string PageSizeTags { get; set; } = "";
        public bool ExtractPageSizeFromBlockName { get; set; }
    }

    public class TitleBlockRecognitionSettings
    {
        public List<TitleBlockRecognitionRule> Rules { get; set; } = new List<TitleBlockRecognitionRule>();

        public static string ConfigPath
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "XYDSignTool");
                return Path.Combine(dir, "TitleBlockRecognitionSettings.xml");
            }
        }

        public static TitleBlockRecognitionSettings Load()
        {
            try
            {
                if (!File.Exists(ConfigPath)) return new TitleBlockRecognitionSettings();

                XmlSerializer serializer = new XmlSerializer(typeof(TitleBlockRecognitionSettings));
                using (FileStream stream = File.OpenRead(ConfigPath))
                {
                    TitleBlockRecognitionSettings settings = serializer.Deserialize(stream) as TitleBlockRecognitionSettings;
                    return settings ?? new TitleBlockRecognitionSettings();
                }
            }
            catch
            {
                return new TitleBlockRecognitionSettings();
            }
        }

        public void Save()
        {
            string dir = Path.GetDirectoryName(ConfigPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            XmlSerializer serializer = new XmlSerializer(typeof(TitleBlockRecognitionSettings));
            using (FileStream stream = File.Create(ConfigPath))
            {
                serializer.Serialize(stream, this);
            }
        }

        public static List<TitleBlockRecognitionRule> GetActiveRules()
        {
            List<TitleBlockRecognitionRule> rules = GetBuiltInRules();
            TitleBlockRecognitionSettings settings = Load();

            foreach (TitleBlockRecognitionRule rule in settings.Rules)
            {
                if (rule != null && rule.Enabled && !string.IsNullOrWhiteSpace(rule.BlockNamePrefix))
                {
                    rules.Add(rule);
                }
            }

            return rules;
        }

        public static List<TitleBlockRecognitionRule> GetBuiltInRules()
        {
            return new List<TitleBlockRecognitionRule>
            {
                new TitleBlockRecognitionRule
                {
                    Enabled = true,
                    BlockNamePrefix = "XYD-TITLEBLOCK,MYTITLEBLOCK,TEMPLATE_,建筑图签,A0,A1,A2,A3,A4",
                    DrawTitleTags = "DRAWTITLE,TITLE,图名,图纸名称,DR_TITLE,NAME",
                    DrawNumTags = "DRAWNUM,DRAWNO,图号,编号,DR_NUM,PROJECT_NO",
                    DrawScaleTags = "DRAWSCALE,SCALE,比例,SC",
                    PageSizeTags = "PAGESIZE,PAPER,图幅,纸张,FORMAT,SIZE",
                    ExtractPageSizeFromBlockName = true
                }
            };
        }

        public static bool MatchesBlockName(TitleBlockRecognitionRule rule, string blockName)
        {
            if (rule == null || string.IsNullOrWhiteSpace(blockName)) return false;

            string upperBlockName = blockName.Trim().ToUpperInvariant();
            foreach (string prefix in SplitNames(rule.BlockNamePrefix))
            {
                string upperPrefix = prefix.ToUpperInvariant();
                if (upperBlockName.StartsWith(upperPrefix, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }

        public static string[] SplitNames(string names)
        {
            if (string.IsNullOrWhiteSpace(names)) return new string[0];

            return names
                .Split(new[] { ',', '，', ';', '；', '|', '、', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(v => v.Trim())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToArray();
        }

        public static TitleBlockRecognitionRule CreateNewRule()
        {
            return new TitleBlockRecognitionRule
            {
                Enabled = true,
                BlockNamePrefix = "template_",
                DrawTitleTags = "图名,TITLE,DRAWTITLE",
                DrawNumTags = "图号,DRAWNUM,DRAWNO",
                DrawScaleTags = "比例,SCALE,DRAWSCALE",
                PageSizeTags = "图幅,PAGESIZE,PAPER",
                ExtractPageSizeFromBlockName = true
            };
        }

        public static string ExtractPageSizeFromBlockName(string blockName)
        {
            if (string.IsNullOrWhiteSpace(blockName)) return "";

            MatchCollection matches = Regex.Matches(
                blockName,
                @"(?i)(?<![A-Z0-9])A\s*([0-4])\s*(?:[\+＋]\s*([0-9]+(?:[\.．][0-9]+)?))?(?![A-Z0-9])");

            if (matches.Count == 0) return "";

            Match match = matches[matches.Count - 1];
            string size = "A" + match.Groups[1].Value;
            string add = match.Groups[2].Value;
            if (!string.IsNullOrWhiteSpace(add))
            {
                add = add.Replace('．', '.');
                decimal numeric;
                if (decimal.TryParse(add, NumberStyles.Number, CultureInfo.InvariantCulture, out numeric))
                {
                    add = numeric.ToString("0.####", CultureInfo.InvariantCulture);
                }
                size += "+" + add;
            }

            return size;
        }
    }
}
