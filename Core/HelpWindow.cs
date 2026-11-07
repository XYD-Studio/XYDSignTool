using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace XYDSignTool
{
    public class HelpWindow : Window
    {
        private const string SupportEmail = "xyd@xy-d.top";
        private const string GitHubUrl = "https://github.com/XYD-Studio/XYDSignTool";
        private const string StudioUrl = "https://www.xy-d.top/";
        private const string LogoResourceUri = "pack://application:,,,/XYDSignTool;component/Resources/logo.png";

        public HelpWindow()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            Title = "XYD 工具集 V1.3 使用帮助";
            Width = 900;
            Height = 680;
            MinWidth = 720;
            MinHeight = 520;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ShowInTaskbar = false;
            Background = new SolidColorBrush(Color.FromRgb(244, 246, 248));

            DockPanel root = new DockPanel();

            Border header = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(37, 48, 63)),
                Padding = new Thickness(20, 16, 20, 14)
            };
            DockPanel.SetDock(header, Dock.Top);

            DockPanel headerContent = new DockPanel();
            Button logoButton = CreateLogoButton();
            if (logoButton != null)
            {
                DockPanel.SetDock(logoButton, Dock.Left);
                headerContent.Children.Add(logoButton);
            }

            StackPanel headerStack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center
            };
            headerStack.Children.Add(new TextBlock
            {
                Text = "XYD 工具集 V1.3 使用帮助",
                Foreground = Brushes.White,
                FontSize = 22,
                FontWeight = FontWeights.SemiBold
            });
            headerStack.Children.Add(new TextBlock
            {
                Text = "玄宇绘世设计工作室",
                Foreground = new SolidColorBrush(Color.FromRgb(190, 202, 216)),
                FontSize = 13,
                Margin = new Thickness(0, 6, 0, 0)
            });
            headerContent.Children.Add(headerStack);
            header.Child = headerContent;
            root.Children.Add(header);

            StackPanel bottom = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(16)
            };
            DockPanel.SetDock(bottom, Dock.Bottom);

            Button copyEmail = new Button
            {
                Content = "复制邮箱",
                Width = 96,
                Height = 30,
                Margin = new Thickness(0, 0, 10, 0)
            };
            copyEmail.Click += (s, e) =>
            {
                Clipboard.SetText(SupportEmail);
                MessageBox.Show("邮箱已复制到剪贴板。", "XYD 工具集", MessageBoxButton.OK, MessageBoxImage.Information);
            };

            Button copyGitHub = new Button
            {
                Content = "复制仓库",
                Width = 96,
                Height = 30,
                Margin = new Thickness(0, 0, 10, 0)
            };
            copyGitHub.Click += (s, e) =>
            {
                Clipboard.SetText(GitHubUrl);
                MessageBox.Show("GitHub 仓库地址已复制到剪贴板。", "XYD 工具集", MessageBoxButton.OK, MessageBoxImage.Information);
            };

            Button close = new Button
            {
                Content = "关闭",
                Width = 88,
                Height = 30,
                IsCancel = true,
                IsDefault = true
            };
            close.Click += (s, e) => Close();

            bottom.Children.Add(copyEmail);
            bottom.Children.Add(copyGitHub);
            bottom.Children.Add(close);
            root.Children.Add(bottom);

            FlowDocumentScrollViewer viewer = new FlowDocumentScrollViewer
            {
                Margin = new Thickness(20, 18, 20, 0),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Document = BuildDocument()
            };
            root.Children.Add(viewer);

            Content = root;
        }

        private static Button CreateLogoButton()
        {
            try
            {
                BitmapImage logoSource = new BitmapImage();
                logoSource.BeginInit();
                logoSource.UriSource = new Uri(LogoResourceUri, UriKind.Absolute);
                logoSource.CacheOption = BitmapCacheOption.OnLoad;
                logoSource.EndInit();
                logoSource.Freeze();

                Image logo = new Image
                {
                    Source = logoSource,
                    Width = 92,
                    Height = 92,
                    Stretch = Stretch.Uniform
                };

                Button button = new Button
                {
                    Content = logo,
                    Width = 92,
                    Height = 92,
                    Padding = new Thickness(0),
                    Margin = new Thickness(0, 0, 18, 0),
                    Background = Brushes.Transparent,
                    BorderBrush = Brushes.Transparent,
                    Cursor = Cursors.Hand,
                    ToolTip = "访问玄宇绘世官网",
                    Focusable = false
                };
                button.Click += (s, e) => OpenStudioWebsite();
                return button;
            }
            catch
            {
                return null;
            }
        }

        private static void OpenStudioWebsite()
        {
            try
            {
                Process.Start(new ProcessStartInfo(StudioUrl) { UseShellExecute = true });
            }
            catch
            {
                Clipboard.SetText(StudioUrl);
                MessageBox.Show("无法自动打开官网，网址已复制到剪贴板。", "XYD 工具集", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private FlowDocument BuildDocument()
        {
            FlowDocument doc = new FlowDocument
            {
                FontFamily = new FontFamily("Microsoft YaHei UI"),
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(31, 41, 55)),
                PagePadding = new Thickness(0),
                LineHeight = 23
            };

            AddTitle(doc, "插件介绍");
            AddParagraph(doc,
                "XYD 工具集是一套面向 AutoCAD 制图、标识系统算量、图纸目录管理和批量出图的效率插件，适用于标识导视、铁路/高铁车站标识施工图、地铁车站标识施工图、建筑配套图纸、图框管理、清单统计和批量打印等日常制图场景。");
            AddParagraph(doc,
                "插件由玄宇绘世设计工作室开发，目标是减少重复操作，让图纸整理、属性统计、目录生成和批量出图更加稳定、快速、可控。");

            AddTitle(doc, "图库中心");
            AddParagraph(doc,
                "图库中心可快速插入预设标识图块和标准图框。插入图框后，程序会自动处理图框属性，例如图名、图号、比例、图幅等信息，便于后续目录生成和批量出图。");
            AddParagraph(doc,
                "“添加自定义图块”用于把一个或多个公司自有 DWG 文件作为图块图库。首次使用时选择 DWG，程序会列出其中可直接插入的普通块、属性块和动态块，并记住这些图库；以后打开 AutoCAD 可直接搜索和插入，无需重复选择文件。");
            AddParagraph(doc,
                "自定义图库窗口支持添加、移除、重新定位和刷新 DWG。模型空间、布局、匿名块和外部参照等内部定义不会列入清单。当前图纸存在同名图块时，可选择保留当前定义、使用图库定义替换或取消插入；替换定义会同步更新图中已有的同名块。");
            AddParagraph(doc,
                "如果图库 DWG 中的主图块包含辅助图块，可选中该图库后点击“设置显示”，只勾选希望在插入列表中看到的图块。隐藏辅助图块不会影响主图块的嵌套依赖；设置会长期保存，也可切换为“全部自动显示”。");
            AddParagraph(doc,
                "插入图框后，可以根据公司图框版式，将图框中的图号、图名、比例等属性文字手动调整到对应位置，但请注意不要将图框炸开，否则属性块信息可能丢失，影响后续识别、目录生成和批量出图。");
            AddParagraph(doc,
                "如果贵公司的图框本身就是属性块形式制作的，也可以通过本插件进行适配。用户可在“图框识别/自定义识别设置”中配置图块名称开头，以及图名、图号、比例、图幅对应的属性名。配置完成后，程序即可识别公司已有图框，无需替换成插件自带图框。");
            AddParagraph(doc,
                "如果图幅信息不在属性中，也可以从图块名称中提取图幅，例如包含 A0、A1、A2+2、A4 等内容的图块名称。");

            AddTitle(doc, "标识系统算量");
            AddParagraph(doc,
                "标识系统算量功能主要通过识别图库中心插入的标识图块进行统计，适用于铁路/高铁车站标识施工图设计、地铁车站标识施工图设计等场景。");
            AddParagraph(doc,
                "插入标识图块后，可以根据项目需要修改图块属性，系统会自动识别并参与统计。属性块中如果没有“数量”属性，则默认数量为 1。");
            AddParagraph(doc,
                "如果需要用一个编号表达多个连续标识，可以在编号中使用 ~ 连接，例如：ZF-XQ-01~03。该编号会被识别为 3 个标识。请注意，连续编号必须使用英文波浪线 ~ 连接，其他符号无效。");
            AddParagraph(doc,
                "标识数量表可以直接在 CAD 图中生成，也可以导出为 Excel 或 JSON 文件。插件还支持通过自定义单价一键生成标识概算。需要注意的是，系统中的单价仅供参考，不作为结算、审计或其他正式依据。");
            AddParagraph(doc,
                "CAD 数量表、Excel 清单、造价清单和 JSON 数据始终包含厚度列，未填写厚度的标识保留空值。普通和造价 Excel 清单的第一行标题会自动使用最终保存的文件名称（不含 .xls 扩展名）。造价计算时，单价管理器首次只显示本次最终合并数据中实际使用的工艺；导入 CSV 后追加的其他工艺会标记为“本次未使用”，不参与当前造价，但会保存单价供后续图纸使用。");
            AddParagraph(doc,
                "“图纸属性编辑”可直接修改标识区域、编号、名称、安装方式、尺寸、重量、数量、工艺、用电量、备注和是否计数。为“否”的不计数标识也会在编辑器中显示，可修改为“是”后重新参与统计；这些标识在未启用前仍会被算量和普通清单跳过。");

            AddTitle(doc, "出图与目录");
            AddParagraph(doc,
                "“批量出图”可以识别模型空间和布局空间中的图框，并按图框范围批量导出 PDF。出图前可选择打印机、打印样式和纸张尺寸，程序会尽量匹配图框图幅，并支持一键注入图纸尺寸模板。");
            AddParagraph(doc,
                "“图纸目录中心”用于汇总图框属性，生成、编辑、导入和导出图纸目录数据。导入外部 JSON 时会合并现有数据，避免直接清空替换。");
            AddParagraph(doc,
                "目录中心中的图号、图名、图幅、比例、版本、日期和备注可双向编辑。点击“保存并回写图框属性”后，打开的图纸只更新当前内存并等待用户保存；未打开的外部 DWG 会在原目录创建带时间戳的 XYDBackup 备份后再保存。JSON 导入行没有源图框，仅参与生成和导出，不会写入 DWG。");

            AddTitle(doc, "V1.3 标识清单中心与映射复用");
            AddParagraph(doc, "XYD_SIGN_CENTER 打开清单中心；XYD_CAD、XYD_XLS、XYD_COST 继续保留并进入同一编辑表格，XYD_EDIT 包含不计数标识。每个业务列都有显示复选框，支持全选、全不选、拖动表头排序。图内表格、普通 Excel、造价 Excel 和清单 JSON 按勾选列及顺序输出，隐藏列仍参与计算。造价导出前确认本次工艺单价。XYD_JSON 保留完整数据复用格式；清单 JSON 仅输出所选列。");
            AddNumberedItem(doc, "运行 XYD_SIGN_MAPPING，点击“添加属性块样本”，在当前图纸选择一个或多个属性块。界面显示有效块名、属性标签、说明、示例及只读状态。");
            AddNumberedItem(doc, "勾选所需属性并填写清单列名。不同块类型可使用同一列名；同一个块的一个属性只映射一次。点击“更新列设置”，勾选显示列、汇总分组列和数量列；无数量列时按实例计数。");
            AddNumberedItem(doc, "可继续添加样本，已有规则保留。需要复用时填写模板名后保存，也可另存、加载、删除或仅本次使用。列名可编辑，内部列标识保持不变。");
            AddNumberedItem(doc, "点击“使用本次映射并提取”，选择当前或多个已打开图纸、添加多个外部 DWG，再选择模型、布局或全部空间。打开图纸读取当前内存；同一路径只添加一次。");
            AddParagraph(doc, "汇总预览与汇总导出独立于明细编辑。其他列包含多个值时用“；”展示不同值集合；数量相加或按实例计数。汇总行不能回写。直接编辑明细，或按选中行、筛选结果、全部行填入新值；“同值替换”按本列原值精确匹配。未点击回写时不会修改 DWG。");
            AddParagraph(doc, "回写只更新修改过的实际属性，并核对原值和源图块位置。中英文两行共享属性改成不同值会指出对应图块和模式；字段、常量、锁定图层、外参及没有来源的 JSON 不能写入。每个文件作为一个事务，失败回滚；外部 DWG 保存前生成 XYDBackup 备份并保持原版本，已打开图纸仅更新内存，请手动保存。");

            AddTitle(doc, "V1.3 高级图框编辑与电子签设置教程");
            AddParagraph(doc, "运行 XYD_TITLEBLOCK_EDIT，图框属性映射与清单映射操作一致。空图名、封面和没有可打印范围的图框也能提取编辑。可将列的用途指定为图名、图号、比例、图幅、版本、日期、备注，供目录和打印识别；保存的图框模板会用于后续会话。");
            AddNumberedItem(doc, "先准备本地签名 DWG：每个人的签名为独立命名图块，推荐块名与姓名完全一致，例如“张三”。签名使用线段、二维多段线、圆弧、圆、椭圆、样条或实心填充。不要引用其他人员图块、外参、整库、图片或 OLE。需要嵌套的公共几何块使用 XYD_SIG_GEOMETRY_ 前缀，且只能包含该签名的必要几何。");
            AddNumberedItem(doc, "进入图框映射中的“电子签设置”，选择本地签名库，系统按姓名与同名块建立对应。可在人员表手动改正姓名或块名，一人只能有一个确定匹配。");
            AddNumberedItem(doc, "点击“添加签名位置”，选择图框样本，点选签名栏的两个角。随后在位置表填写角色和姓名属性标签；可使用设计、校对、审核、项目经理、负责人、会签或自定义角色。同一姓名属性可重复添加多个位置，每种不同有效块名的版式分别配置。");
            AddNumberedItem(doc, "图签位置不勾选“会签”，保留姓名文字。会签位置勾选“会签”，使用独立会签姓名属性；图签与会签即使同一人，也要使用不同标签。会签后台姓名值保留用于编辑和匹配，但文字始终隐藏，不受是否添加电子签控制。");
            AddNumberedItem(doc, "选中人员和位置，预览签名，检查区域宽高后应用设置并保存模板。位置使用图框局部坐标，目标图框移动、旋转和缩放时会一起变换，签名按区域等比缩放。");
            AddNumberedItem(doc, "提取当前或跨文件图框，完成姓名编辑。电子签默认关闭；仅隐藏会签文字时直接回写即可。需要电子签时勾选“增加 / 同步电子签”再回写。缺失、歧义或隔离检查失败会显示图框句柄、角色和姓名，并回滚该文件；空姓名不插入签名。");
            AddParagraph(doc, "只有本次实际使用的个人签名几何会进入目标 DWG，完整库和完整人员索引不会写入目标或安装包。重复同步会替换原位置，换人后替换对应签名并清理无引用的旧定义。取消添加勾选不会删除既有签名；需删除时点击“移除生成签名”。签名库始终保存在您自己的本地目录，模板只记录路径及配置。");

            AddTitle(doc, "V1.3 曲线转多段线与新增纸张");
            AddParagraph(doc, "曲线转 PL（XYD_LINE2PL）接受各类有限曲线：直线、圆弧、圆、矩形/轻量多段线、旧式二维和三维多段线、椭圆/椭圆弧、样条、螺旋线，以及多线和面域的边界。先选后运行或运行后点选/框选，回车完成，Esc 取消。设置采用 CAD 命令行关键词：设置端点合并容差或[是否保留数据(D)/是否统一Z坐标(Z)/拟合误差(F)]。D、Z 直接切换并回到同一提示；F 设置正数拟合误差后回到主提示，输入容差或回车开始。容差首次 0.01、允许零；拟合误差首次 0.01、必须大于零，两者分别记忆，不再自动逐项询问。每次运行默认不保留附加数据、统一 Z。全选 PL 自动按端点容差合并开放路径，已闭合轮廓保持独立。");
            AddParagraph(doc, "统一 Z 开启时保留 X/Y，把顶点投影到世界坐标 Z=0；水平圆弧/圆保留精确弧度，倾斜圆弧/圆按拟合误差投影成折线，分段宽度按参数插值保留，厚度数值保留且拉伸方向统一为世界 Z 轴。纯竖直线投影为零长度时保留并提示。关闭统一 Z 后保留原高程及共面路径平面，非共面路径生成三维 PL，无法保留宽度或厚度时保留原对象。椭圆、样条、螺旋线及拟合旧式多段线按拟合误差处理。仅检查开放曲线的端点，内部交叉不拆分；间隙保留当前模式下的端点并补短连接。多组、孤立曲线、闭环和分叉分别处理，不同显示属性分组。");
            AddParagraph(doc, "带 XData、扩展字典或关联的对象也能转换。默认不保留数据：允许不同附加数据的线合并，新 PL 不附带原 XData、扩展字典和关联，显示属性保留。D 开启数据保留后，相同数据可合并，不同数据及独立字典、句柄引用或持久关联分别转换，保留主对象句柄、完整数据及关联；复杂轮廓的字典保留在主 PL，其他边界复制 XData，原生组包含所有新边界。一次 U 恢复整次操作及原数据。锁定、零长度、无限直线/射线及无法转换的对象保留并给出原因。例：先输入 D、Z，最后回车，即保留数据及原高程。");
            AddTitle(doc, "V1.3 PL 无缝打断");
            AddParagraph(doc, "点击“统计与测量”中的小图标“PL 打断”，或运行 XYD_PLBREAK。选择一根轻量、旧式二维或三维 PL；也可以先选一根再运行。每次默认单点模式，指定一个内部点后立即将开放 PL 分成两根，不删除任何线段，不产生缺口。命令提示“指定打断点或[多点(M)]”，点击多点或输入 M 切换；连续指定多个点，回车统一执行，两个不同内部断点得到三根开放 PL。多个断点按曲线位置排序，重复点和开放线端点跳过；离线点投影到所选 PL，支持对象捕捉和当前 UCS。");
            AddParagraph(doc, "多点模式可输入 U 撤销最后一个暂存点；S 切回单点并清除暂存点，重新指定一个点。Esc 取消整次操作，暂存期间原线不变。闭合 PL 单点打开成一根开放 PL，起终点都在指定位置；多个断点按环分段，两个不同点得到两根，原闭合边也完整保留。打断保留精确圆弧、分段宽度、厚度、法向、高程及显示属性，不使用曲线转换的统一 Z 设置。主段保留原句柄与扩展字典，所有段保留 XData，原生组包含各段；一次 CAD U 撤销恢复原线及数据。锁定图层和零长度 PL 保留并提示。");
            AddParagraph(doc, "图框库已同步新增 A4、A3+0.25、A3+0.5、A0+0.25、A0+0.5。对应毫米纸张分别为 210×297、525×297、630×297、1486.25×841、1783.5×841。批量打印中选择 PC3 后点击“注入图纸尺寸”，程序备份 PC3/PMP、关联模板并刷新；A0/A3 不会匹配为加长纸。图库、PMP 均随四段安装资源发布。");

            AddTitle(doc, "统计与测量");
            AddParagraph(doc,
                "提供常用统计和编辑工具，包括图块计数、动态块长度统计、规格统计、曲线转 PL、PL 无缝打断、线段总长、批量查字段、批量文本替换、查找相同、自动排号、批量旋转和批量缩放。");
            AddParagraph(doc, "批量缩放（XYD_MSCALE）：先选对象再运行，或运行后点选/框选并回车结束选择。输入正倍率，2 放大两倍，0.5 缩小一半；默认 1，回车不修改对象。每个对象分别绕自身世界坐标包围盒中心等比例缩放，包含真实 Z 坐标；不同对象的中心位置保持不变。图块按实例变换，属性文字自动联动，不修改共享图块定义，重复选择父图块和属性时只处理一次。锁定、只读、无法获取边界或无法变换的对象保留并报告原因；一个对象及其属性全部成功才提交，一次 CAD U 撤销整次缩放。Esc 取消输入时原图不变。");
            AddParagraph(doc,
                "部分命令支持先选择对象再执行，适合配合 AutoCAD 的快速选择工具使用。");
            AddParagraph(doc,
                "“批量文本替换”可一次填写多组替换规则，并选择严格匹配或模糊匹配。所有规则都以原始文字为基准同步处理，因此支持 A 替换为 B、B 替换为 C、C 替换为 A 这类循环替换。可处理单行文字、多行文字、块属性、标注覆盖文字、多重引线和表格单元格。");
            AddParagraph(doc,
                "“查找相同”可以先选中一个样本对象后执行，也可先启动命令再选样本。在窗口中勾选对象类型、图层、图块名、线型、颜色、线宽、透明度或类型专属属性，再选择当前空间、整张 DWG 或手动范围，程序会直接选中同时满足所有条件的对象。");
            AddParagraph(doc,
                "“自动排号”用于连续修改普通块或动态块的属性编号。可设置属性标签、前缀、后缀、起始数字、补零位数和递增步长，也可从 A015 这类已有编号自动续到 A016。");
            AddParagraph(doc,
                "半自动模式会按点选顺序逐个写入，并支持命令内撤销和重新设置。全自动模式可按当前 UCS 的横向、纵向、普通或蛇形顺序排列，写入前会显示旧值和新值预览。");
            AddParagraph(doc,
                "已完成排号后发现中间漏号时，可选择“插号顺延”模式。先选漏号图块，再点选它后面第一个已有编号图块；程序会给漏号块补上该号码，并将同属性、同前后缀的后续编号整体顺延。所有改动在预览确认后一次写入，可用 CAD 撤销整体恢复。");
            AddParagraph(doc,
                "跳号可配置一个或多个禁用数字，并选择只跳过尾号或跳过数字部分任意一位命中的编号。最近规则和下一号保存在当前 DWG 中，锁定图层、外部参照、字段属性和缺失目标属性的图块会安全跳过。");

            AddTitle(doc, "图片工具");
            AddParagraph(doc,
                "“批量嵌图”用于批量将图片插入到 CAD 图纸中，可选择多张图片并按行排列。图片会以内嵌方式写入图纸，适合需要随 DWG 一起交付图片内容的场景。");

            AddTitle(doc, "基本使用流程");
            AddNumberedItem(doc, "在 AutoCAD 中打开需要处理的 DWG 文件。");
            AddNumberedItem(doc, "切换到 XYD 工具集 Ribbon 面板。");
            AddParagraph(doc, "工具栏采用小图标配短文字、每列最多三行的紧凑布局，减少折叠。鼠标停留在按钮上可查看完整功能名称和命令，图库下拉列表也使用小图标。自动加载保留 CAD 或天正当前选项卡；手动执行 XYD_UI 可显示并刷新 XYD 面板。");
            AddParagraph(doc, "小按钮与下拉菜单使用完整缩放的 16×16 逻辑尺寸矢量图标，大图使用 32×32，保留留白并适应显示缩放，避免图标裁切。批量缩放位于批量旋转所在列。");
            AddParagraph(doc, "如果所有工具栏都不显示，请依次输入 CLEANSCREENOFF、RIBBON、XYD_UI。工具栏加载错误会记录在用户配置目录 XYDSignTool 的 Ribbon.log 中，连续失败会停止自动重试。");
            AddNumberedItem(doc, "根据需求选择图框插入、标识系统算量、目录管理、统计测量、批量出图或批量嵌图功能。");
            AddNumberedItem(doc, "批量出图前，建议先检查图框属性中的图名、图号、比例和图幅是否完整。");
            AddNumberedItem(doc, "如使用公司自有图框，请先配置图框识别规则，再进行目录生成或批量打印。");
            AddNumberedItem(doc, "批量打印完成后，请抽查 PDF 内容、比例、居中效果和图片显示效果。");
            AddParagraph(doc, "批量打印预检仅列出有效 CTB 颜色相关打印样式，继续使用您选定的 CTB。图纸采用 STB 命名样式时，系统在完整临时副本中自动转换为 CTB，确认转换和样式应用成功后才输出 PDF。已打开图纸使用当前内存内容，未保存修改也参与本次输出；模型、布局和相对引用保留。原 DWG 不转换、不保存。选用黑白 CTB 按黑白设置输出，选用彩色 CTB 则遵循其颜色与线宽设置。转换、样式缺失或打印失败会报告来源及原因，既有 PDF 只在新 PDF 成功后替换。Esc 取消或运行 XYD_BATCHPRINT_CANCEL 停止队列，任务结束会关闭临时副本并恢复原活动图纸。");
            AddNumberedItem(doc, "标识算量结果和概算金额建议在正式提交前进行人工复核。");

            AddTitle(doc, "使用建议");
            AddBulletItem(doc, "图框属性越规范，目录生成和批量出图越稳定。");
            AddBulletItem(doc, "自定义图框建议统一图块名前缀，方便程序识别。");
            AddBulletItem(doc, "图框可以移动属性文字位置，但不要炸开图框属性块。");
            AddBulletItem(doc, "标识编号如需表达连续数量，请使用 ~，例如 ZF-XQ-01~03。");
            AddBulletItem(doc, "批量出图前建议先用少量图纸测试打印机、纸张和打印样式。");
            AddBulletItem(doc, "嵌入大量高清图片会增加 DWG 文件体积，属于正常现象。");
            AddBulletItem(doc, "自动统计和概算结果仅作为辅助，请在正式交付前人工复核关键数据。");

            AddTitle(doc, "定制开发");
            AddParagraph(doc,
                "如果你需要根据公司图框、算量规则、清单格式、打印流程或内部制图标准进行定制开发，我们支持插件功能扩展和企业级定制。");
            AddParagraph(doc, "定制需求可通过邮箱联系：" + SupportEmail);

            AddTitle(doc, "开源发布");
            AddParagraph(doc, "本软件由玄宇绘世设计工作室免费开放源代码发布，GitHub 仓库地址：" + GitHubUrl);
            AddParagraph(doc,
                "本项目采用“免费开放源代码非商业同源共享许可协议”。欢迎用于学习、交流、试用和非商业分享。分享时请保留软件名称、工作室名称、版权声明和 GitHub 仓库地址。");
            AddParagraph(doc,
                "如果你修改、扩展或基于本项目进行二次开发并对外分发，必须继续公开完整源代码，并保留本项目版权声明和仓库地址。");
            AddParagraph(doc,
                "禁止任何人对本插件进行倒卖、付费转售、打包售卖、去除版权后再分发，或以盈利为目的将本插件作为商品销售。商业授权、企业定制、闭源集成或付费交付请联系玄宇绘世设计工作室。");

            AddTitle(doc, "版权声明");
            AddParagraph(doc, "本插件由玄宇绘世设计工作室开发并维护。");
            AddParagraph(doc,
                "未经授权，不得对本插件进行反编译、破解、复制分发、二次销售或用于其他侵犯作者权益的行为。");
            AddParagraph(doc,
                "本插件作为 AutoCAD 辅助工具提供，使用过程中产生的图纸、清单、打印文件和统计结果，请用户根据实际项目要求自行核对。因使用不当、图纸数据异常、第三方软件环境问题，或用户未复核统计/概算结果造成的偏差，开发者不承担由此产生的项目责任。");
            AddParagraph(doc, "© 玄宇绘世设计工作室. All rights reserved.");

            return doc;
        }

        private static void AddTitle(FlowDocument doc, string text)
        {
            Paragraph paragraph = new Paragraph(new Run(text))
            {
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(17, 71, 122)),
                Margin = new Thickness(0, 18, 0, 7)
            };
            doc.Blocks.Add(paragraph);
        }

        private static void AddParagraph(FlowDocument doc, string text)
        {
            doc.Blocks.Add(new Paragraph(new Run(text))
            {
                Margin = new Thickness(0, 0, 0, 8)
            });
        }

        private static void AddBulletItem(FlowDocument doc, string text)
        {
            doc.Blocks.Add(new Paragraph(new Run("• " + text))
            {
                Margin = new Thickness(12, 0, 0, 5)
            });
        }

        private static void AddNumberedItem(FlowDocument doc, string text)
        {
            int number = 1;
            if (doc.Blocks.LastBlock is Paragraph last && last.Tag is int lastNumber)
            {
                number = lastNumber + 1;
            }

            Paragraph paragraph = new Paragraph(new Run(number + ". " + text))
            {
                Margin = new Thickness(12, 0, 0, 5),
                Tag = number
            };
            doc.Blocks.Add(paragraph);
        }
    }
}
