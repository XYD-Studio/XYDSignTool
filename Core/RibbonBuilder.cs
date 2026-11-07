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
        private static RibbonBuilder _initializedInstance;
        private bool _idleRegistered;
        private bool _buildingRibbon;
        private bool _activateWhenReady;
        private int _ribbonFailures;
        private DateTime _nextRibbonCheckUtc = DateTime.MinValue;

        public void Initialize()
        {
            try
            {
                _initializedInstance = this;
                RegisterIdle();
            }
            catch (System.Exception ex)
            {
                Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage($"\n[XYD_ERROR] 守护进程启动失败: {ex.Message}\n");
            }

        }
        public void Terminate()
        {
            if (_idleRegistered) Application.Idle -= Application_Idle_Watchdog;
            _idleRegistered = false;
            if (ReferenceEquals(_initializedInstance, this)) _initializedInstance = null;
        }

        private void RegisterIdle()
        {
            if (_idleRegistered) return;
            Application.Idle += Application_Idle_Watchdog;
            _idleRegistered = true;
        }

        // ==================== ★ 守护进程 (兼容天正的核心) ★ ====================
        private void Application_Idle_Watchdog(object sender, EventArgs e)
        {
            if (_buildingRibbon || _ribbonFailures >= 3 || DateTime.UtcNow < _nextRibbonCheckUtc) return;
            _nextRibbonCheckUtc = DateTime.UtcNow.AddMilliseconds(500);
            try
            {
                // 启动、打开图纸和工作空间切换过程中不修改 CAD 的共享 Ribbon。
                if (Application.DocumentManager.MdiActiveDocument == null ||
                    Convert.ToInt32(Application.GetSystemVariable("CMDACTIVE")) != 0) return;
                if (!_customLibrariesInitialized)
                {
                    _customLibrariesInitialized = true;
                    try { CustomBlockLibraryStore.RefreshSavedLibraries(); }
                    catch { }
                }
                if (!_isLispLoaded)
                {
                    _isLispLoaded = true;
                    AutoLoadLispFiles();
                }

                RibbonControl ribbon = ComponentManager.Ribbon;
                if (ribbon == null || !ribbon.IsLoaded || !ribbon.IsVisible) return;
                bool hasOurTab = false;
                foreach (RibbonTab tab in ribbon.Tabs)
                {
                    if (tab.Id == "XYD_TOOLKIT_TAB")
                    {
                        hasOurTab = true;
                        if (_activateWhenReady) tab.IsActive = true;
                        break;
                    }
                }

                // 天正或工作空间重建界面后，只补回自己的选项卡。
                if (!hasOurTab)
                {
                    BuildRibbon(_activateWhenReady);
                }
                _activateWhenReady = false;
            }
            catch (System.Exception ex)
            {
                _ribbonFailures++;
                _nextRibbonCheckUtc = DateTime.UtcNow.AddSeconds(5);
                ReportRibbonFailure(ex);
            }
        }

        // ==================== 隐藏命令：手动强制召唤菜单 ====================
        [CommandMethod("XYD_UI")]
        public void ForceLoadUI()
        {
            RibbonBuilder controller = _initializedInstance ?? this;
            controller._ribbonFailures = 0;
            controller._nextRibbonCheckUtc = DateTime.MinValue;
            try
            {
                var doc = Application.DocumentManager.MdiActiveDocument;
                RibbonControl ribbon = ComponentManager.Ribbon;
                if (ribbon == null || !ribbon.IsVisible ||
                    Convert.ToInt32(Application.GetSystemVariable("CLEANSCREENSTATE")) != 0)
                {
                    // 只在用户明确执行 XYD_UI 时恢复显示，不在启动时改变全屏/工具栏偏好。
                    controller.RegisterIdle();
                    controller._activateWhenReady = true;
                    doc?.SendStringToExecute("_.CLEANSCREENOFF _.RIBBON ", false, false, false);
                    doc?.Editor.WriteMessage("\n[XYD] 已请求恢复工具栏显示，待 CAD 界面加载完成后显示 XYD 面板。\n");
                    return;
                }
                controller.BuildRibbon(true);
                doc?.Editor.WriteMessage("\n[XYD] 工具栏已刷新。\n");
            }
            catch (System.Exception ex) { controller.ReportRibbonFailure(ex); }
        }

        // ==================== 绘制 Ribbon 菜单 ====================
        private void BuildRibbon(bool activate = false)
        {
            RibbonControl ribbon = ComponentManager.Ribbon;
            if (ribbon == null) return;
            if (_buildingRibbon) return;
            _buildingRibbon = true;
            try
            {
                InstallToolkitTab(ribbon, activate, CreateToolkitTab);
                _ribbonFailures = 0;
            }
            finally { _buildingRibbon = false; }
        }

        internal static void InstallToolkitTab(RibbonControl ribbon, bool activate, Func<RibbonTab> createTab)
        {
            // 先完整构造；失败时原有选项卡和 CAD/天正界面仍然存在。
            RibbonTab replacement = createTab();
            if (replacement == null) throw new InvalidOperationException("无法创建 XYD 工具栏。");
            RibbonTab previousActive = ribbon.ActiveTab;
            var previousTabs = new System.Collections.Generic.List<RibbonTab>();
            int position = ribbon.Tabs.Count;
            for (int i = 0; i < ribbon.Tabs.Count; i++)
                if (ribbon.Tabs[i].Id == "XYD_TOOLKIT_TAB")
                {
                    position = Math.Min(position, i);
                    previousTabs.Add(ribbon.Tabs[i]);
                }
            try
            {
                foreach (RibbonTab old in previousTabs) ribbon.Tabs.Remove(old);
                ribbon.Tabs.Insert(Math.Min(position, ribbon.Tabs.Count), replacement);
                if (activate || previousTabs.Contains(previousActive)) replacement.IsActive = true;
                else if (previousActive != null && ribbon.Tabs.Contains(previousActive)) previousActive.IsActive = true;
            }
            catch
            {
                if (ribbon.Tabs.Contains(replacement)) ribbon.Tabs.Remove(replacement);
                foreach (RibbonTab old in previousTabs)
                    if (!ribbon.Tabs.Contains(old)) ribbon.Tabs.Insert(Math.Min(position++, ribbon.Tabs.Count), old);
                if (previousActive != null && ribbon.Tabs.Contains(previousActive)) previousActive.IsActive = true;
                throw;
            }
        }

        private void ReportRibbonFailure(System.Exception ex)
        {
            try
            {
                string folder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XYDSignTool");
                System.IO.Directory.CreateDirectory(folder);
                System.IO.File.AppendAllText(System.IO.Path.Combine(folder, "Ribbon.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + ex + Environment.NewLine);
                Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(
                    "\n[XYD_ERROR] 工具栏加载失败，已保留原有界面。可输入 XYD_UI 重试，详细错误已记录到 Ribbon.log。\n");
            }
            catch { }
        }

        private RibbonTab CreateToolkitTab()
        {
            RibbonTab tab = new RibbonTab { Title = "XYD 工具集", Id = "XYD_TOOLKIT_TAB" };
            RibbonPanelSource panelLib = CreatePanel(tab, "图库中心");

            RibbonSplitButton dropSign = CreateSplitButton("标识图块", "block");
            dropSign.Items.Add(CreateButton("A 型标识", "XYD_INS:XYD-SIGNBLOCK_A", "block"));
            dropSign.Items.Add(CreateButton("B 型标识", "XYD_INS:XYD-SIGNBLOCK_B", "block"));
            dropSign.Items.Add(CreateButton("C 型标识", "XYD_INS:XYD-SIGNBLOCK_C", "block"));
            dropSign.Items.Add(CreateButton("D 型标识（中英）", "XYD_INS:XYD-SIGNBLOCK_D", "block"));
            dropSign.Items.Add(CreateButton("E 型标识", "XYD_INS:XYD-SIGNBLOCK_E", "block"));
            dropSign.Items.Add(CreateButton("F 型标识", "XYD_INS:XYD-SIGNBLOCK_F", "block"));
            dropSign.Items.Add(CreateButton("G 型标识", "XYD_INS:XYD-SIGNBLOCK_G", "block"));

            RibbonSplitButton dropFrame = CreateSplitButton("标准图框", "frame");
            dropFrame.Items.Add(CreateButton("A0 横向图框", "XYD_INS:XYD-TITLEBLOCK_A0", "frame"));
            dropFrame.Items.Add(CreateButton("A0+0.25 横向图框", "XYD_INS:XYD-TITLEBLOCK_A0+0.25", "frame"));
            dropFrame.Items.Add(CreateButton("A0+0.5 横向图框", "XYD_INS:XYD-TITLEBLOCK_A0+0.5", "frame"));
            dropFrame.Items.Add(CreateButton("A1 横向图框", "XYD_INS:XYD-TITLEBLOCK_A1", "frame"));
            dropFrame.Items.Add(CreateButton("A1+0.25 横向图框", "XYD_INS:XYD-TITLEBLOCK_A1+0.25", "frame"));
            dropFrame.Items.Add(CreateButton("A1+0.5 横向图框", "XYD_INS:XYD-TITLEBLOCK_A1+0.5", "frame"));
            dropFrame.Items.Add(CreateButton("A1+0.75 横向图框", "XYD_INS:XYD-TITLEBLOCK_A1+0.75", "frame"));
            dropFrame.Items.Add(CreateButton("A1+1 横向图框", "XYD_INS:XYD-TITLEBLOCK_A1+1", "frame"));
            dropFrame.Items.Add(CreateButton("A2 横向图框", "XYD_INS:XYD-TITLEBLOCK_A2", "frame"));
            dropFrame.Items.Add(CreateButton("A2 封面图框", "XYD_INS:XYD-TITLEBLOCK_A2封面", "frame"));
            dropFrame.Items.Add(CreateButton("A2+0.25 横向图框", "XYD_INS:XYD-TITLEBLOCK_A2+0.25", "frame"));
            dropFrame.Items.Add(CreateButton("A2+0.5 横向图框", "XYD_INS:XYD-TITLEBLOCK_A2+0.5", "frame"));
            dropFrame.Items.Add(CreateButton("A2+0.75 横向图框", "XYD_INS:XYD-TITLEBLOCK_A2+0.75", "frame"));
            dropFrame.Items.Add(CreateButton("A2+1 横向图框", "XYD_INS:XYD-TITLEBLOCK_A2+1", "frame"));
            dropFrame.Items.Add(CreateButton("A2+1.25 横向图框", "XYD_INS:XYD-TITLEBLOCK_A2+1.25", "frame"));
            dropFrame.Items.Add(CreateButton("A2+1.5 横向图框", "XYD_INS:XYD-TITLEBLOCK_A2+1.5", "frame"));
            dropFrame.Items.Add(CreateButton("A2+1.75 横向图框", "XYD_INS:XYD-TITLEBLOCK_A2+1.75", "frame"));
            dropFrame.Items.Add(CreateButton("A2+2 横向图框", "XYD_INS:XYD-TITLEBLOCK_A2+2", "frame"));
            dropFrame.Items.Add(CreateButton("A3 横向图框", "XYD_INS:XYD-TITLEBLOCK_A3", "frame"));
            dropFrame.Items.Add(CreateButton("A3+0.25 横向图框", "XYD_INS:XYD-TITLEBLOCK_A3+0.25", "frame"));
            dropFrame.Items.Add(CreateButton("A3+0.5 横向图框", "XYD_INS:XYD-TITLEBLOCK_A3+0.5", "frame"));
            dropFrame.Items.Add(CreateButton("A4 图框", "XYD_INS:XYD-TITLEBLOCK_A4", "frame"));
            AddCompactColumn(panelLib, dropSign, dropFrame,
                CreateButton("自定义图库", "XYD_CUSTOMBLOCKS ", "block", "添加自定义图块"));

            RibbonPanelSource panelSign = CreatePanel(tab, "标识系统算量");
            AddCompactColumn(panelSign,
                CreateButton("清单中心", "XYD_SIGN_CENTER ", "table", "标识清单中心"),
                CreateButton("映射清单", "XYD_SIGN_MAPPING ", "edit", "自定义映射清单"),
                CreateButton("算量造价", "XYD_COST ", "calc", "算量及造价"));
            AddCompactColumn(panelSign,
                CreateButton("图内表格", "XYD_CAD ", "table", "图内生成表格"),
                CreateButton("导出清单", "XYD_XLS ", "dir", "导出普通清单"),
                CreateButton("属性编辑", "XYD_EDIT ", "edit", "图纸属性编辑"));
            AddCompactColumn(panelSign,
                CreateButton("JSON 库", "XYD_JSON ", "json", "导出 JSON 库"));

            RibbonPanelSource panelBatch = CreatePanel(tab, "出图与目录");
            AddCompactColumn(panelBatch,
                CreateButton("批量出图", "XYD_BATCHPRINT ", "batchprint"),
                CreateButton("目录中心", "XYD_DIRECTORY ", "table", "图纸目录中心"),
                CreateButton("图框识别", "XYD_TITLEBLOCK_RULES ", "edit"));
            AddCompactColumn(panelBatch,
                CreateButton("图框编辑", "XYD_TITLEBLOCK_EDIT ", "edit", "高级图框编辑"));

            RibbonPanelSource panelStat = CreatePanel(tab, "统计与测量");
            AddCompactColumn(panelStat,
                CreateButton("图块计数", "XYD_COUNTBLK ", "blockcount"),
                CreateButton("动块长度", "XYD_DYNLEN ", "dynlen"),
                CreateButton("规格统计", "XYD_DYNSPEC ", "spec"));
            AddCompactColumn(panelStat,
                CreateButton("曲线转 PL", "XYD_LINE2PL ", "linelen"),
                CreateButton("线段总长", "XYD_LINELEN ", "linelen"),
                CreateButton("查找字段", "XYD_FINDTEXT ", "findtext", "批量查字段"));
            AddCompactColumn(panelStat,
                CreateButton("文本替换", "XYD_REPLACETEXT ", "replacetext", "批量文本替换"),
                CreateButton("查找相同", "XYD_FINDSAME ", "findsame"),
                CreateButton("自动排号", "XYD_AUTONUMBER ", "autonumber"));
            AddCompactColumn(panelStat,
                CreateButton("批量旋转", "XYD_MRO ", "rotate"),
                CreateButton("PL 打断", "XYD_PLBREAK ", "plbreak"),
                CreateButton("批量缩放", "XYD_MSCALE ", "scale"));

            RibbonPanelSource panelImage = CreatePanel(tab, "图片工具");
            AddCompactColumn(panelImage, CreateButton("批量嵌图", "XYD_BATCHOLE ", "embedimage"));

            RibbonPanelSource panelHelp = CreatePanel(tab, "帮助");
            AddCompactColumn(panelHelp, CreateButton("使用帮助", "XYD_HELP ", "help"));
            return tab;
        }

        private RibbonPanelSource CreatePanel(RibbonTab tab, string title)
        {
            RibbonPanelSource panelSource = new RibbonPanelSource { Title = title };
            // 使用宿主的默认缩放策略，紧凑布局本身节省宽度，不强制共享 Ribbon 禁止折叠。
            tab.Panels.Add(new RibbonPanel { Source = panelSource });
            return panelSource;
        }

        private void AddCompactColumn(RibbonPanelSource panel, params RibbonItem[] items)
        {
            // 每列最多三行：使用 AutoCAD 原生小按钮和小字，按当前 DPI 缩放。
            for (int first = 0; first < items.Length; first += 3)
            {
                RibbonRowPanel column = new RibbonRowPanel
                {
                    SubPanelResizeStyle = RibbonRowPanelResizeStyle.NeverWrap
                };
                for (int i = first; i < Math.Min(first + 3, items.Length); i++)
                {
                    if (i > first) column.Items.Add(new RibbonRowBreak());
                    column.Items.Add(items[i]);
                }
                panel.Items.Add(column);
            }
        }

        private RibbonSplitButton CreateSplitButton(string text, string iconType)
        {
            RibbonSplitButton splitBtn = new RibbonSplitButton
            {
                Text = text,
                ShowText = true,
                ShowImage = true,
                Size = RibbonItemSize.Standard,
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                ResizeStyle = RibbonItemResizeStyles.NoResize,
                ListStyle = RibbonSplitButtonListStyle.List,
                ListImageSize = RibbonImageSize.Standard,
                IsSynchronizedWithCurrentItem = false
            };

            if (!string.IsNullOrEmpty(iconType))
            {
                var imgSource = IconFactory.GetVectorIcon(iconType, 16);
                splitBtn.Image = imgSource;
                splitBtn.LargeImage = IconFactory.GetVectorIcon(iconType, 32);
            }
            return splitBtn;
        }

        private RibbonButton CreateButton(string text, string command, string iconType, string toolTipTitle = null)
        {
            RibbonButton btn = new RibbonButton
            {
                Text = text,
                ShowText = true,
                ShowImage = true,
                Size = RibbonItemSize.Standard,
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                ResizeStyle = RibbonItemResizeStyles.NoResize
            };

            if (!string.IsNullOrEmpty(iconType))
            {
                var imgSource = IconFactory.GetVectorIcon(iconType, 16);
                btn.Image = imgSource;
                btn.LargeImage = IconFactory.GetVectorIcon(iconType, 32);
            }

            btn.CommandParameter = command;
            btn.CommandHandler = new RibbonCommandHandler();
            ApplyCommandToolTip(btn, toolTipTitle ?? text, command);
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
                case "XYD_SIGN_CENTER": return "提取、勾选列、编辑及跨文件回写标识清单。";
                case "XYD_SIGN_MAPPING": return "选择属性块样本，配置清单列映射并保存模板。";
                case "XYD_TITLEBLOCK_EDIT": return "提取任意图框属性，批量编辑并同步本地电子签。";
                case "XYD_LINE2PL": return "曲线转 PL 或合并已有 PL；命令行 D 切换保留数据、Z 切换统一高程、F 设置拟合误差。默认不保留数据、顶点统一 Z=0，支持端点容差及多组合并。";
                case "XYD_PLBREAK": return "无缝打断 PL：默认一点分两段，M 切换多点，两点分三段；保留全部线段、圆弧、宽度及高程，回车确认多点，Esc 取消。";
                case "XYD_MSCALE": return "多个对象分别绕自身包围盒中心等比例缩放，保留中心高程；2 放大两倍，0.5 缩小一半。支持预选、图块属性联动及一次撤销。";
                case "XYD_COST":
                    return "统计标识属性块并生成算量及造价清单。";
                case "XYD_CAD":
                    return "在当前图纸中生成标识统计表格。";
                case "XYD_XLS":
                    return "导出当前图纸的普通标识清单。";
                case "XYD_EDIT":
                    return "批量编辑标识的尺寸、数量、用电量、备注和是否计数等属性。";
                case "XYD_JSON":
                    return "导出标识数据 JSON，用于后续复用或外部处理。";
                case "XYD_BATCHPRINT":
                    return "按图框识别结果批量输出 PDF。";
                case "XYD_BATCHOLE":
                    return "调用外挂 LSP 批量导入图片，结束后自动恢复 CAD 对话框设置。";
                case "XYD_DIRECTORY":
                    return "提取并双向编辑图框属性，生成、导入或导出图纸目录。";
                case "XYD_TITLEBLOCK_RULES":
                    return "配置用户自定义图框块名前缀和属性字段映射。";
                case "XYD_CUSTOMBLOCKS":
                    return "管理自定义 DWG 图库、设置显示图块并插入选中图块。";
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
                case "XYD_REPLACETEXT":
                    return "使用多组规则同步替换文字、块属性、标注、引线和表格内容。";
                case "XYD_FINDSAME":
                    return "按图层、图块名、线型、颜色等多个属性查找并选中相同对象。";
                case "XYD_AUTONUMBER":
                    return "按前后缀、跳号和空间顺序编辑图块属性编号，并支持插号顺延。";
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
                case "XYD_REPLACETEXT":
                    return "XYD_REPLACETEXT";
                case "XYD_FINDSAME":
                    return "XYD_FINDSAME";
                case "XYD_AUTONUMBER":
                    return "XYD_AUTONUMBER";
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
