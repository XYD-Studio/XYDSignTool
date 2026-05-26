***

# 📂 [XYDSignTool] AutoCAD 商业级插件开发上下文交接文档

## 1. 项目概况 (Project Overview)
*   **项目名称**：XYD 工具集 (XYDSignTool)
*   **开发环境**：Visual Studio (C# .NET Framework), 兼容 AutoCAD 2016-2024。
*   **核心目标**：开发一款商业级、高容错、免配置的 AutoCAD 综合插件。包含标识算量与造价、内置图块一键插入、跨图纸提取目录、智能批量打印等功能。
*   **部署方式**：采用 Autodesk 官方 `.bundle` 机制，配合 Inno Setup 制作中英文双语安装包。

## 2. 核心技术栈与架构规范 (Tech Stack & Architecture)
*   **纯 C# WPF 界面 (无 XAML)**：为了避免环境编译坑，所有的 UI（如 `DataGrid` 交互表格、预检弹窗）全部使用纯 C# 代码在内存中动态构建。
*   **纯代码矢量绘图 (`DrawingContext`)**：Ribbon 面板上的所有图标（算量、打印、表格、JSON等）均不依赖外部图片，全部由 `IconFactory` 使用 WPF 矢量绘制，解决 AutoCAD 沙箱拦截图片的 BUG。
*   **多文档跨文件通信 (ObjectDBX & Session)**：通过 `Database(false, true)` 后台静默读取未打开的 DWG，通过 `CommandFlags.Session` 使得命令可以跨标签页执行（主要用于批量打印）。
*   **零第三方依赖**：解析 JSON、生成 Excel HTML 报表，全部手写底层文件流处理，不引入 `Newtonsoft` 或 `EPPlus`，确保单 DLL 极简打包。

## 3. 项目目录结构 (Directory Structure)
代码严格按照工程化、模块化划分：
```text
[XYDSignTool] (C# Project)
 ├── Core/
 │    ├── RibbonBuilder.cs      // 菜单生成、LISP静默加载、命令路由拦截、打印配置物理注入
 │    ├── IconFactory.cs        // 纯 C# 矢量图标绘制工厂
 │    └── BlockManager.cs       // 图块克隆引擎 (扫描 Blocks 目录下所有母盘并带属性克隆)
 ├── SignCalculate/
 │    ├── SignItem.cs           // 标识数据实体模型 (内置面积、造价自动计算属性)
 │    ├── SignExtractor.cs      // 标识属性块提取与 JSON 解析引擎
 │    ├── ExcelExporter.cs      // 造价报表/清单报表无依赖导出引擎
 │    ├── CadTableGenerator.cs  // CAD 原生表格生成器 (带 XYD-Style 字体强制注入)
 │    ├── AreaSorterWindow.cs   // 大类排序交互界面 (WPF)
 │    ├── PriceManagerWindow.cs // 单价管理及 CSV 导入/导出界面 (WPF)
 │    └── SignEditorWindow.cs   // 图纸属性网格双向绑定编辑器 (WPF DataGrid)
 ├── BatchTools/
 │    ├── TitleBlockModel.cs    // 图框数据实体模型
 │    ├── TitleBlockExtractor.cs// 图框提取引擎 (处理正则清洗、多图纸遍历)
 │    ├── NativePlotEngine.cs   // [★问题焦点] AutoCAD C# 原生高级打印引擎
 │    ├── BatchCommands.cs      // [★待重构] 批量提取、打印、多文档交互的命令入口
 │    ├── DocSelectorWindow.cs  // 跨图纸多选界面 (WPF)
 │    ├── DirectoryEditorWindow.cs // 图纸目录动态列排版/批量填入编辑器 (WPF DataGrid)
 │    └── PlotPreflightWindow.cs// 智能打印预检与纸张映射界面 (WPF)
 └── Commands.cs                // 核心算量业务命令入口 (XYD_COST, XYD_EDIT 等)
```
打包输出结构 (`.bundle`)：
`Contents/` 目录下除了 DLL，还包含 `Blocks` (存放图块母盘)、`Lisp` (存放外挂脚本)、`Plotters` (PC3文件)、`PlotStyles` (CTB文件)、`PMPFiles` (PMP图纸配置文件)。

## 4. 👩‍💻 用户习惯与 AI 输出绝对规范 (Strict Rules for AI)
**接管本项目的 AI 必须严格遵守以下规则，违者将被用户严厉驳回：**
1.  **【禁止省略代码】**：提供代码更新时，**必须提供该文件的 100% 完整代码**，绝对禁止使用 `// ... (此处省略原有代码)` 的形式。用户习惯直接使用 `Ctrl+A` 复制覆盖。
2.  **【拒绝静默妥协】**：商业软件思维！宁可程序弹窗报错拦截（Fail-Fast），也**绝对不允许**程序在用户不知情的情况下偷偷降级处理（例如：找不到 A1 纸就偷偷用 A3 打印）。所有异常情况必须在界面上引导用户处理。
3.  **【极致体验】**：注重用户的交互体验。尽量合并重复操作步骤，提供批量处理按钮。

---

## 5. 🚨 待解决的核心任务与 BUG 清单 (Outstanding Tasks)

新接手的 AI，请仔细阅读以下需求，并直接给出相应的代码解决方案：

### 🔴 任务一：攻克批量打印 `eInvalidInput` 终极崩溃 Bug
*   **现象**：在 `NativePlotEngine.cs` 执行批量打印时，即便已经完全重置了 `PlotSettings`、设定了纯 2D 的 `Extents2d` 坐标、关闭了背景打印，在调用 `SetPlotWindowArea` 或相关打印配置时，部分图纸依然会报 `eInvalidInput` 而导致打印崩溃。
*   **排查方向建议**：这可能是因为图框处于图纸空间（PaperSpace）或存在 UCS 坐标系偏差，直接传入 World Extents 导致非法；或者是因为新创建的 `PlotSettings` 没有关联到正确的激活设备上下文中。必须彻底改写 `NativePlotEngine`，提供绝对安全的坐标系转换与 Layout 绑定机制。
* 用户补充：现在问题是可以打印出来了，但是图纸没有居中，由于图纸设置了出血，输出的PDF内容不全（没有铺满），并且在布局中有图框，还是会报错

### 🟡 任务二：重名文件拦截与警告机制
*   **需求**：在批量打印之前，程序需要检查提取到的图框列表。如果发现有两张图纸的 **“图号 + 图纸名称”** 完全一样（导致生成的 PDF 文件名冲突），必须在打印前弹窗警告用户，列出重名的图纸，并终止打印。如果图名重复但图号不同，则不算重名。同时还需要检测图框的PAGESIZE属性是否为空，如有为空的也要告知是哪个

### 🟡 任务三：更换高级路径选择器
*   **需求**：批量打印时选择 PDF 输出路径的对话框，目前用的是 `System.Windows.Forms.FolderBrowserDialog`，非常难用。要求替换为一个**支持在底部输入框直接粘贴路径**的现代文件夹选择对话框（例如利用 `Microsoft.Win32.SaveFileDialog` 变通，或使用 CommonOpenFileDialog 技术，要求不引入外部 DLL）。

### 🟡 任务四：目录功能“三合一”超级面板重构
*   **现状**：目前 `生成图内目录(XYD_DIRALL)`、`导出JSON(XYD_DIRJSON)`、`导出Excel(XYD_DIREXCEL)` 是三个分离的命令，每次都要重新提取和配置字段，容易出错。
*   **需求**：
    1.  合并为一个统一的入口命令（如 `XYD_DIRECTORY`）。
    2.  扫描提取后，直接弹出 `DirectoryEditorWindow`（动态列排版界面）。
    3.  将界面底部的“确定/取消”按钮，重构为三个功能按钮：`[在图纸中生成]`、`[导出 Excel]`、`[导出 JSON]`，外加一个 `[退出]`。
    4.  **核心交互要求**：点击 `[在图纸中生成]` 后，UI 界面必须**暂时隐藏 (Hide)**，让用户在 CAD 图纸中点选插入点。生成表格后，UI 界面**自动恢复弹出 (Show)**，用户可以继续点击 `[导出 Excel]`。只有点击 `[退出]` 时，才真正销毁对话框。

***