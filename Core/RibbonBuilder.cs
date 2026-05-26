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
            dropFrame.Items.Add(CreateButton("A2+0.25 横向图框", "XYD_INS:XYD-TITLEBLOCK_A2+0.25", RibbonItemSize.Large, "frame"));
            dropFrame.Items.Add(CreateButton("A2+0.5 横向图框", "XYD_INS:XYD-TITLEBLOCK_A2+0.5", RibbonItemSize.Large, "frame"));
            dropFrame.Items.Add(CreateButton("A2+0.75 横向图框", "XYD_INS:XYD-TITLEBLOCK_A2+0.75", RibbonItemSize.Large, "frame"));
            dropFrame.Items.Add(CreateButton("A2+1 横向图框", "XYD_INS:XYD-TITLEBLOCK_A2+1", RibbonItemSize.Large, "frame"));
            dropFrame.Items.Add(CreateButton("A2+1.25 横向图框", "XYD_INS:XYD-TITLEBLOCK_A2+1.25", RibbonItemSize.Large, "frame"));
            dropFrame.Items.Add(CreateButton("A2+1.5 横向图框", "XYD_INS:XYD-TITLEBLOCK_A2+1.5", RibbonItemSize.Large, "frame"));
            dropFrame.Items.Add(CreateButton("A2+1.75 横向图框", "XYD_INS:XYD-TITLEBLOCK_A2+1.75", RibbonItemSize.Large, "frame"));
            dropFrame.Items.Add(CreateButton("A2+2 横向图框", "XYD_INS:XYD-TITLEBLOCK_A2+2", RibbonItemSize.Large, "frame"));
            dropFrame.Items.Add(CreateButton("A3 横向图框", "XYD_INS:XYD-TITLEBLOCK_A3", RibbonItemSize.Large, "frame"));
            panelLib.Items.Add(dropFrame);

            // ------------------------------------------------------------
            RibbonPanelSource panelSign = CreatePanel(tab, "标识系统算量");
            panelSign.Items.Add(CreateButton("算量及造价", "XYD_COST ", RibbonItemSize.Large, "calc"));
            panelSign.Items.Add(CreateButton("图内生成表格", "XYD_CAD ", RibbonItemSize.Large, "table"));

            // ★ 修复：普通清单绑定 dir，图纸属性编辑绑定 edit，JSON库绑定 json
            panelSign.Items.Add(CreateButton("导出普通清单", "XYD_XLS ", RibbonItemSize.Large, "dir"));
            panelSign.Items.Add(CreateButton("图纸属性编辑", "XYD_EDIT ", RibbonItemSize.Large, "edit"));
            panelSign.Items.Add(CreateButton("导出 JSON 库", "XYD_JSON ", RibbonItemSize.Large, "json"));

            // ------------------------------------------------------------
            RibbonPanelSource panelBatch = CreatePanel(tab, "批量工具");
            // ★ 修复：绑定新做的批量出图、目录表相关图标
            panelBatch.Items.Add(CreateButton("批量出图", "XYD_BATCHPRINT ", RibbonItemSize.Large, "batchprint"));
            panelBatch.Items.Add(CreateButton("图纸目录中心", "XYD_DIRECTORY ", RibbonItemSize.Large, "table"));
            panelBatch.Items.Add(CreateButton("批量查字段", "XYD_FINDTEXT ", RibbonItemSize.Large, "default"));

            // 面板 3：统计辅助工具
            RibbonPanelSource panelStat = CreatePanel(tab, "统计与测量");
            panelStat.Items.Add(CreateButton("图块统计", "XYD_COUNTBLK ", RibbonItemSize.Large, "default"));
            panelStat.Items.Add(CreateButton("动块长度", "XYD_DYNLEN ", RibbonItemSize.Large, "default"));
            panelStat.Items.Add(CreateButton("线段总长", "XYD_LINELEN ", RibbonItemSize.Large, "default"));

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
            return btn;
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
                        foreach (string file in files) doc.SendStringToExecute($"(load \"{file.Replace("\\", "/")}\" \"\")\n", false, false, false);
                    }
                }
            }
            catch { }
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
