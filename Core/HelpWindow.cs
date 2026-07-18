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
            Title = "XYD 工具集 使用帮助";
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
                Text = "XYD 工具集 使用帮助",
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

            AddTitle(doc, "出图与目录");
            AddParagraph(doc,
                "“批量出图”可以识别模型空间和布局空间中的图框，并按图框范围批量导出 PDF。出图前可选择打印机、打印样式和纸张尺寸，程序会尽量匹配图框图幅，并支持一键注入图纸尺寸模板。");
            AddParagraph(doc,
                "“图纸目录中心”用于汇总图框属性，生成、编辑、导入和导出图纸目录数据。导入外部 JSON 时会合并现有数据，避免直接清空替换。");

            AddTitle(doc, "统计与测量");
            AddParagraph(doc,
                "提供常用统计工具，包括图块计数、动态块长度统计、规格统计、线段总长、批量查字段和批量旋转。");
            AddParagraph(doc,
                "部分命令支持先选择对象再执行，适合配合 AutoCAD 的快速选择工具使用。");

            AddTitle(doc, "图片工具");
            AddParagraph(doc,
                "“批量嵌图”用于批量将图片插入到 CAD 图纸中，可选择多张图片并按行排列。图片会以内嵌方式写入图纸，适合需要随 DWG 一起交付图片内容的场景。");

            AddTitle(doc, "基本使用流程");
            AddNumberedItem(doc, "在 AutoCAD 中打开需要处理的 DWG 文件。");
            AddNumberedItem(doc, "切换到 XYD 工具集 Ribbon 面板。");
            AddNumberedItem(doc, "根据需求选择图框插入、标识系统算量、目录管理、统计测量、批量出图或批量嵌图功能。");
            AddNumberedItem(doc, "批量出图前，建议先检查图框属性中的图名、图号、比例和图幅是否完整。");
            AddNumberedItem(doc, "如使用公司自有图框，请先配置图框识别规则，再进行目录生成或批量打印。");
            AddNumberedItem(doc, "批量打印完成后，请抽查 PDF 内容、比例、居中效果和图片显示效果。");
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
