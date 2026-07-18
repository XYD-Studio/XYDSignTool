using System;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Windows;
using Application = Autodesk.AutoCAD.ApplicationServices.Core.Application;

[assembly: ExtensionApplication(typeof(XYDSignTool.RibbonBuilder))]

namespace XYDSignTool
{
    public class RibbonBuilder : IExtensionApplication
    {
        public static System.Collections.Generic.Dictionary<string, double> PriceDictCache =
            new System.Collections.Generic.Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                {"PC板面板，不锈钢围边发光立体字", 50000.0}, {"单面灯箱", 5500.0}, {"双面灯箱", 6500.0},
                {"单面落地灯箱", 5500.0}, {"双面落地灯箱", 6500.0}, {"双面动静结合灯箱", 6500.0},
                {"单面动静结合灯箱", 5500.0}, {"发光字", 5500.0}, {"铝板折弯烤漆丝印", 2500.0},
                {"铝板氧化金", 2500.0}, {"贴膜", 1000.0}, {"耐磨地胶贴膜", 1000.0}, {"磨砂玻璃贴", 1000.0}
            };

        private bool _isLispLoaded = false;
        private bool _customLibrariesInitialized = false;

        public void Initialize()
        {
            try
            {
                // ★ 终极解法：永远挂载在空闲事件上，做成“守护进程”
                Application.Idle += Application_Idle_Watchdog;
            }
            catch (System.Exception ex)
            {
                Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage($"\n[XYD_ERROR] 守护进程启动失败: {ex.Message}\n");
            }

        }
        public void Terminate() { }

        // ==================== ★ 守护进程 (兼容天正的核心) ★ ====================
        private void Application_Idle_Watchdog(object sender, EventArgs e)
        {
            if (!_customLibrariesInitialized)
            {
                try { CustomBlockLibraryStore.RefreshSavedLibraries(); }
                catch { }
                _customLibrariesInitialized = true;
            }

            // 1. 静默加载 Lisp (只执行一次)
            if (!_isLispLoaded)
            {
                AutoLoadLispFiles();
                _isLispLoaded = true;
            }

            // 2. 检查菜单存活状态
            if (ComponentManager.Ribbon != null)
            {
                bool hasOurTab = false;
                foreach (RibbonTab tab in ComponentManager.Ribbon.Tabs)
                {
                    if (tab.Id == "XYD_TOOLKIT_TAB")
                    {
                        hasOurTab = true;
                        break;
                    }
                }

                // 如果发现菜单被天正或者工作空间切换给吃掉了，立刻补回去！
                if (!hasOurTab)
                {
                    BuildRibbon();
                }
            }
        }

        // ==================== 隐藏命令：手动强制召唤菜单 ====================
        [CommandMethod("XYD_UI")]
        public void ForceLoadUI()
        {
            BuildRibbon();
            Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage("\n[XYD] 界面已强制唤醒！\n");
        }

        // ==================== 绘制 Ribbon 菜单 ====================
        private void BuildRibbon()
        {
            RibbonControl ribbon = ComponentManager.Ribbon;
            if (ribbon == null) return;

            // 防御机制：先清理同名残骸
            for (int i = ribbon.Tabs.Count - 1; i >= 0; i--)
            {
                if (ribbon.Tabs[i].Id == "XYD_TOOLKIT_TAB") ribbon.Tabs.RemoveAt(i);
            }

            RibbonTab tab = new RibbonTab { Title = "XYD 工具集", Id = "XYD_TOOLKIT_TAB" };
            ribbon.Tabs.Add(tab);

            // ------------------------------------------------------------
            // 面板 0：内置图库 
            // ------------------------------------------------------------
            RibbonPanelSource panelLib = CreatePanel(tab, "图库中心");

            RibbonSplitButton dropSign = CreateSplitButton("标识属性块", "block");
            dropSign.Items.Add(CreateButton("插入 A型 标识", "XYD_INS:XYD-SIGNBLOCK_A", RibbonItemSize.Large, "block"));
            dropSign.Items.Add(CreateButton("插入 B型 标识", "XYD_INS:XYD-SIGNBLOCK_B", RibbonItemSize.Large, "block"));
            dropSign.Items.Add(CreateButton("插入 C型 标识", "XYD_INS:XYD-SIGNBLOCK_C", RibbonItemSize.Large, "block"));
            dropSign.Items.Add(CreateButton("插入 D型(中英)", "XYD_INS:XYD-SIGNBLOCK_D", RibbonItemSize.Large, "block"));
            dropSign.Items.Add(CreateButton("插入 E型 标识", "XYD_INS:XYD-SIGNBLOCK_E", RibbonItemSize.Large, "block"));
            dropSign.Items.Add(CreateButton("插入 F型 标识", "XYD_INS:XYD-SIGNBLOCK_F", RibbonItemSize.Large, "block"));
            dropSign.Items.Add(CreateButton("插入 G型 标识", "XYD_INS:XYD-SIGNBLOCK_G", RibbonItemSize.Large, "block"));
            panelLib.Items.Add(dropSign);

            // ★ 修复：将下拉菜单主图标改为 frame
            RibbonSplitButton dropFrame = CreateSplitButton("图框属性块", "frame");
            // ★ 修复：将所有图框按钮的图标改为 frame
            dropFrame.Items.Add(CreateButton("A0 横向图框", "XYD_INS:XYD-TITLEBLOCK_A0", RibbonItemSize.Large, "frame"));
            dropFrame.Items.Add(CreateButton("A1 横向图框", "XYD_INS:XYD-TITLEBLOCK_A1", RibbonItemSize.Large, "frame"));
            dropFrame.Items.Add(CreateButton("A1+0.25 横向图框", "XYD_INS:XYD-TITLEBLOCK_A1+0.25", RibbonItemSize.Large, "frame"));
            dropFrame.Items.Add(CreateButton("A1+0.5 横向图框", "XYD_INS:XYD-TITLEBLOCK_A1+0.5", RibbonItemSize.Large, "frame"));
            dropFrame.Items.Add(CreateButton("A1+0.75 横向图框", "XYD_INS:XYD-TITLEBLOCK_A1+0.75", RibbonItemSize.Large, "frame"));
            dropFrame.Items.Add(CreateButton("A1+1 横向图框", "XYD_INS:XYD-TITLEBLOCK_A1+1", RibbonItemSize.Large, "frame"));
            dropFrame.Items.Add(CreateButton("A2 横向图框", "XYD_INS:XYD-TITLEBLOCK_A2", RibbonItemSize.Large, "frame"));
            dropFrame.Items.Add(CreateButton("A2 封面图框", "XYD_INS:XYD-TITLEBLOCK_A2封面", RibbonItemSize.Large, "frame"));
            dropFrame.Items.Add(CreateButton("A2+0.25 横向图框", "XYD_INS:XYD-TITLEBLOCK_A2+0.25", RibbonItemSize.Large, "frame"));
            dropFrame.Items.Add(CreateButton("A2+0.5 横向图框", "XYD_INS:XYD-TITLEBLOCK_A2+0.5", RibbonItemSize.Large, "frame"));
            dropFrame.Items.Add(CreateButton("A2+0.75 横向图框", "XYD_INS:XYD-TITLEBLOCK_A2+0.75", RibbonItemSize.Large, "frame"));
            dropFrame.Items.Add(CreateButton("A2+1 横向图框", "XYD_INS:XYD-TITLEBLOCK_A2+1", RibbonItemSize.Large, "frame"));
            dropFrame.Items.Add(CreateButton("A2+1.25 横向图框", "XYD_INS:XYD-TITLEBLOCK_A2+1.25", RibbonItemSize.Large, "frame"));
            dropFrame.Items.Add(CreateButton("A2+1.5 横向图框", "XYD_INS:XYD-TITLEBLOCK_A2+1.5", RibbonItemSize.Large, "frame"));
            dropFrame.Items.Add(CreateButton("A2+1.75 横向图框", "XYD_INS:XYD-TITLEBLOCK_A2+1.75", RibbonItemSize.Large, "frame"));
            dropFrame.Items.Add(CreateButton("A2+2 横向图框", "XYD_INS:XYD-TITLEBLOCK_A2+2", RibbonItemSize.Large, "frame"));
            dropFrame.Items.Add(CreateButton("A3 横向图框", "XYD_INS:XYD-TITLEBLOCK_A3", RibbonItemSize.Large, "frame"));
            dropFrame.Items.Add(CreateButton("A4 图框", "XYD_INS:XYD-TITLEBLOCK_A4", RibbonItemSize.Large, "frame"));
            panelLib.Items.Add(dropFrame);
            panelLib.Items.Add(CreateButton("添加自定义图块", "XYD_CUSTOMBLOCKS ", RibbonItemSize.Large, "block"));

            // ------------------------------------------------------------
            RibbonPanelSource panelSign = CreatePanel(tab, "标识系统算量");
            panelSign.Items.Add(CreateButton("算量及造价", "XYD_COST ", RibbonItemSize.Large, "calc"));
            panelSign.Items.Add(CreateButton("图内生成表格", "XYD_CAD ", RibbonItemSize.Large, "table"));

            // ★ 修复：普通清单绑定 dir，图纸属性编辑绑定 edit，JSON库绑定 json
            panelSign.Items.Add(CreateButton("导出普通清单", "XYD_XLS ", RibbonItemSize.Large, "dir"));
            panelSign.Items.Add(CreateButton("图纸属性编辑", "XYD_EDIT ", RibbonItemSize.Large, "edit"));
            panelSign.Items.Add(CreateButton("导出 JSON 库", "XYD_JSON ", RibbonItemSize.Large, "json"));

            // ------------------------------------------------------------
            RibbonPanelSource panelBatch = CreatePanel(tab, "出图与目录");
            // ★ 修复：绑定新做的批量出图、目录表相关图标
            panelBatch.Items.Add(CreateButton("批量出图", "XYD_BATCHPRINT ", RibbonItemSize.Large, "batchprint"));
            panelBatch.Items.Add(CreateButton("图纸目录中心", "XYD_DIRECTORY ", RibbonItemSize.Large, "table"));
            panelBatch.Items.Add(CreateButton("图框识别", "XYD_TITLEBLOCK_RULES ", RibbonItemSize.Large, "edit"));

            // 面板 3：统计辅助工具
            RibbonPanelSource panelStat = CreatePanel(tab, "统计与测量");
            panelStat.Items.Add(CreateButton("图块计数", "XYD_COUNTBLK ", RibbonItemSize.Large, "blockcount"));
            panelStat.Items.Add(CreateButton("动块长度", "XYD_DYNLEN ", RibbonItemSize.Large, "dynlen"));
            panelStat.Items.Add(CreateButton("规格统计", "XYD_DYNSPEC ", RibbonItemSize.Large, "spec"));
            panelStat.Items.Add(CreateButton("线段总长", "XYD_LINELEN ", RibbonItemSize.Large, "linelen"));
            panelStat.Items.Add(CreateButton("批量查字段", "XYD_FINDTEXT ", RibbonItemSize.Large, "findtext"));
            panelStat.Items.Add(CreateButton("批量旋转", "XYD_MRO ", RibbonItemSize.Large, "rotate"));

            RibbonPanelSource panelImage = CreatePanel(tab, "图片工具");
            panelImage.Items.Add(CreateButton("批量嵌图", "XYD_BATCHOLE ", RibbonItemSize.Large, "embedimage"));

            RibbonPanelSource panelHelp = CreatePanel(tab, "帮助");
            panelHelp.Items.Add(CreateButton("使用帮助", "XYD_HELP ", RibbonItemSize.Large, "help"));

            tab.IsActive = true;
        }

        private RibbonPanelSource CreatePanel(RibbonTab tab, string title)
        {
            RibbonPanelSource panelSource = new RibbonPanelSource { Title = title };
            tab.Panels.Add(new RibbonPanel { Source = panelSource });
            return panelSource;
        }

        private RibbonSplitButton CreateSplitButton(string text, string iconType)
        {
            RibbonSplitButton splitBtn = new RibbonSplitButton
            {
                Text = text,
                ShowText = true,
                ShowImage = true,
                Size = RibbonItemSize.Large,
                Orientation = System.Windows.Controls.Orientation.Vertical,
                ListStyle = RibbonSplitButtonListStyle.List
            };

            if (!string.IsNullOrEmpty(iconType))
            {
                var imgSource = IconFactory.GetVectorIcon(iconType);
                splitBtn.Image = imgSource;
                splitBtn.LargeImage = imgSource;
            }
            return splitBtn;
        }

        private RibbonButton CreateButton(string text, string command, RibbonItemSize size, string iconType)
        {
            RibbonButton btn = new RibbonButton { Text = text, ShowText = true, ShowImage = true, Size = size };
            btn.Orientation = size == RibbonItemSize.Large ? System.Windows.Controls.Orientation.Vertical : System.Windows.Controls.Orientation.Horizontal;

            if (!string.IsNullOrEmpty(iconType))
            {
                var imgSource = IconFactory.GetVectorIcon(iconType);
                btn.Image = imgSource;
                btn.LargeImage = imgSource;
            }

            btn.CommandParameter = command;
            btn.CommandHandler = new RibbonCommandHandler();
            ApplyCommandToolTip(btn, text, command);
            return btn;
        }

        private void ApplyCommandToolTip(RibbonButton button, string title, string command)
        {
            if (button == null || string.IsNullOrWhiteSpace(command)) return;
            if (command.TrimStart().StartsWith("XYD_INS:", StringComparison.OrdinalIgnoreCase)) return;

            string displayCommand = command.Trim();
            string description = GetCommandDescription(displayCommand);
            string shortcut = GetCommandShortcut(displayCommand);
            RibbonToolTip toolTip = new RibbonToolTip
            {
                Title = title,
                Content = $"{description}\n命令: {shortcut}",
                Command = shortcut,
                Shortcut = shortcut,
                IsHelpEnabled = false
            };

            button.ToolTip = toolTip;
            button.Description = description;
            button.HelpTopic = shortcut;
        }

        private string GetCommandDescription(string command)
        {
            switch ((command ?? "").Trim().ToUpperInvariant())
            {
                case "XYD_COST":
                    return "统计标识属性块并生成算量及造价清单。";
                case "XYD_CAD":
                    return "在当前图纸中生成标识统计表格。";
                case "XYD_XLS":
                    return "导出当前图纸的普通标识清单。";
                case "XYD_EDIT":
                    return "批量查看并编辑当前图纸中的标识属性。";
                case "XYD_JSON":
                    return "导出标识数据 JSON，用于后续复用或外部处理。";
                case "XYD_BATCHPRINT":
                    return "按图框识别结果批量输出 PDF。";
                case "XYD_BATCHOLE":
                    return "调用外挂 LSP 批量导入图片，结束后自动恢复 CAD 对话框设置。";
                case "XYD_DIRECTORY":
                    return "提取图框信息，生成、导入或导出图纸目录。";
                case "XYD_TITLEBLOCK_RULES":
                    return "配置用户自定义图框块名前缀和属性字段映射。";
                case "XYD_CUSTOMBLOCKS":
                    return "管理多个自定义 DWG 图库并插入其中的命名图块。";
                case "XYD_COUNTBLK":
                    return "选择一个图块样本，统计同名图块使用次数。";
                case "XYD_DYNLEN":
                    return "选择动态图块样本，汇总指定动态参数的数值。";
                case "XYD_DYNSPEC":
                    return "选择动态图块样本，按指定动态参数分类计数。";
                case "XYD_LINELEN":
                    return "统计预选或框选的线段、圆弧、多段线等曲线总长度。";
                case "XYD_FINDTEXT":
                    return "查找文本和块属性字段，统计次数并自动选中匹配对象。";
                case "XYD_MRO":
                    return "将预选或框选对象分别绕各自包围盒中心旋转。";
                case "XYD_HELP":
                    return "查看 XYD 工具集介绍、详细使用教程、定制开发联系方式和版权声明。";
                default:
                    return "执行 XYD 工具命令。";
            }
        }

        private string GetCommandShortcut(string command)
        {
            switch ((command ?? "").Trim().ToUpperInvariant())
            {
                case "XYD_BATCHOLE":
                    return "XYD_BATCHOLE / BatchOLE";
                case "XYD_COUNTBLK":
                    return "XYD_COUNTBLK / TJTK";
                case "XYD_DYNLEN":
                    return "XYD_DYNLEN / TJCD";
                case "XYD_DYNSPEC":
                    return "XYD_DYNSPEC / TJGG";
                case "XYD_LINELEN":
                    return "XYD_LINELEN / ZZ";
                case "XYD_FINDTEXT":
                    return "XYD_FINDTEXT / MCOUNT";
                case "XYD_MRO":
                    return "XYD_MRO / MRO";
                default:
                    return command.Trim();
            }
        }

        private void AutoLoadLispFiles()
        {
            try
            {
                string dir = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                string lispDir = System.IO.Path.Combine(dir, "Lisp");
                var doc = Application.DocumentManager.MdiActiveDocument;
                if (System.IO.Directory.Exists(lispDir))
                {
                    string[] files = System.IO.Directory.GetFiles(lispDir, "*.lsp");
                    if (files.Length > 0 && doc != null)
                    {
                        foreach (string file in files)
                        {
                            if (IsRewrittenLegacyLisp(file)) continue;
                            doc.SendStringToExecute($"(load \"{file.Replace("\\", "/")}\" \"\")\n", false, false, false);
                        }
                    }
                }
            }
            catch { }
        }

        private bool IsRewrittenLegacyLisp(string file)
        {
            string name = System.IO.Path.GetFileName(file);
            return name.Equals("MRO批量旋转.lsp", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("XYD_COUNTBLK.lsp", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("XYD_FINDTEXT.lsp", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("统计线段长度.lsp", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("BatchOLE_V3.1批量导入图片.lsp", StringComparison.OrdinalIgnoreCase);
        }
    }

    // ==================== ★ 命令拦截器 ====================
    public class RibbonCommandHandler : System.Windows.Input.ICommand
    {
        public event EventHandler CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object parameter) { return true; }

        public void Execute(object parameter)
        {
            if (parameter is RibbonButton btn && btn.CommandParameter != null)
            {
                string cmd = (string)btn.CommandParameter;

                if (cmd.StartsWith("XYD_INS:"))
                {
                    string blockName = cmd.Substring(8).Trim();
                    BlockManager.InsertBlockFromLibrary(blockName);
                }
                else
                {
                    Application.DocumentManager.MdiActiveDocument.SendStringToExecute(cmd, true, false, false);
                }
            }
        }
    }

}
