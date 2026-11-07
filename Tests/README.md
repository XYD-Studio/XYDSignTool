# V1.3 本地回归与验证记录

运行：

```powershell
./Tests/Run-V13Tests.ps1
./Tests/Run-RibbonTests.ps1
./Tests/Run-CurveTests.ps1
./Tests/Run-UpgradeTests.ps1
./Packaging/Build-AutoCADPackage.ps1 -SkipSdkDownload
```

测试 DLL 只用于开发，不进入插件安装包。通过本机 AutoCAD 2024 的 `accoreconsole.exe` 加载真实插件与真实 AutoCAD API，使用临时图纸和临时 PC3，测试完成后删除临时 PC3、恢复用户容差设置。签名测试库是自动生成的 100 人合成矢量库，不含真实人员签名。

## 已覆盖的自动化运行验证

- 几何：10 线分 3 组、全部孤立、不同显示属性、闭环、分叉、间隙端点不移动、防止跨容差的传递聚类、共面高程和非共面三维 PL。
- 原生命令：预选运行、运行后选择（零容差）、10 LINE 变 3 PL、一次 `U` 撤销恢复 10 个原 LINE；转换使用文件内事务及路径子事务，失败路径保留原线。
- 映射与模板：空图名仍可扫描、不同块类型的不同标签映射同一列、模型/布局范围、常量展示和只读、XML 保存/加载/编辑、改列名不改稳定列标识、数量汇总及无数量列时按实例计数、按选定列导出。
- 来源与回写：同名不同目录分别保存、完整路径比较、未保存的当前文档仅内存更新、外部备份、AC1027 原版本保存、提取后原值改变时阻止覆盖、只读文件报告、共享中英文属性冲突时整文件回滚。
- 标准算量：连续编号数量推断、不计数过滤、面积和造价公式、计算列不参与回写、连续两次修改尺寸后刷新组合属性原值、完整 JSON 的引号/反斜线/换行及中英文模式往返、JSON 不具有回写来源。
- 电子签：100 人库仅导入使用的 8 个隔离定义、9 个位置中的同人多位置、旋转和缩放后中心定位、两次执行不叠加、取消签名开关时会签文字仍隐藏且后台值保留、图签文字保留、更换姓名同步两处并清理旧定义、保存重开后检查引用和人员索引、嵌套其他人员签名拦截、样条曲线几何重建。
- 资源与打印：全部 22 个图框入口按别名导入；A0/A3 不误匹配加长纸；临时 PC3 关联新 PMP，新增 A4、A3+0.25、A3+0.5、A0+0.25、A0+0.5 分别出 PDF；布局 A4 出 PDF；旋转 UCS 出图并恢复上下文。
- WPF：清单、映射、签名设置窗口构造和数据绑定，渲染截图检查布局。新增业务列勾选、姓名属性和角色下拉选择保留原有 WPF 技术栈。

运行结果、DWG、PDF、界面 PNG、图库尺寸及纸张清单位于被 Git 忽略的 `artifacts/v13-tests`。PDF 页面尺寸与模板相符，驱动按 PDF 点精度取整；已检查渲染效果。

## V1.3 Ribbon 兼容性回归

`Run-RibbonTests.ps1` 在 AutoCAD 2024 引擎中加载实际 V1.3 DLL，使用独立 Ribbon 控件验证：自动加载保留 CAD/天正选项卡和当前选项卡，连续十次刷新不重复、不抢焦点，构造失败不改变原有界面，模拟宿主集合异常时恢复旧 XYD 选项卡，以及手动刷新后保留 XYD 的选择。运行结果在 `artifacts/ribbon-regression/results.txt`。使用临时 DWG 和受信任的 Program Files 临时测试目录，测试结束后清理，不调整 SECURELOAD 或 TRUSTEDPATHS。

已在本机 T30 天正建筑 + AutoCAD 2024 的实际窗口检查：执行 `CLEANSCREENOFF`、`RIBBON` 后 CAD、天正、XYD 选项卡都可见。该观察确认菜单没有丢失，尚未复现最初触发隐藏的操作。新版紧凑布局在 1024/1280 像素宽度下渲染正常，采用宿主自适应布局；自动加载不切换当前选项卡，异常限次重试，手动 `XYD_UI` 可恢复显示。引擎内控件回归不等同于所有桌面启动、工作空间和天正版本的完整验收。

V1.3 四段 DLL 和安装包已构建。下面的 V1.3 业务回归和资源记录保留为历史验证记录。

## 编译与发布验证

曲线转换修复保持 V1.3 / 1.3.0.0，命令仍为 `XYD_LINE2PL`。`Run-CurveTests.ps1` 使用真实 AutoCAD 2024 引擎验证精确圆弧（含大于半圆、倾斜平面及厚度）、圆的面积/周长、矩形、既有圆弧和变宽、反向合并、旧式二维 OCS 和拟合多段线、三维多段线、椭圆、平面/空间样条、螺旋线、非共面圆弧拟合、多线和面域边界，以及预选/运行后选择与一次撤销。拟合结果密集采样核验偏差。测试输出在 `artifacts/curve-regression`；恢复两个容差文件，不修改安全设置或已安装 DLL。

2026-10-02：安装包内 AutoCAD2021–2024 的实际 DLL 已通过 20 项回归，包含变宽旧式拟合多段线和直接回车使用默认容差。可使用 `./Tests/Run-CurveTests.ps1 -PluginPath 'Installer/Build/XYDSignTool.bundle/Contents/AutoCAD2021-2024/XYDSignTool.dll'` 复验；`tested-plugin.sha256` 记录实际受测 DLL，四段 DLL 版本均核对为 1.3.0.0。

带扩展数据转换回归增至 25 项：多应用 XData、中文/二进制值、完整浮点精度比较、相同数据合并/不同数据独立转换、嵌套字典及自引用、原句柄、DWG 保存重开、原生组关联、三维顶点和多线全部边界、失败回滚，以及实际命令一次 U 后恢复原 LINE、附加数据和组。使用原生对象替换，避开在事务内交换标识造成的撤销数据丢失；关联对象不进入事务式替换。

2026-10-03：命令行设置升级回归增至 34 项，版本继续 V1.3。新增默认不保留数据/统一 Z=0、D/Z 反复切换、F 选择后返回设置、直接回车和零合并容差、选完对象不再重复询问、保留与丢弃两种路径的失败回滚；密集采样核验倾斜大圆弧/圆/变宽 PL、空间样条和螺旋线的 XY 投影，水平及反法向圆弧仍精确保留，纯竖直线保留。原生命令验证不同数据的五条线默认合并、开启保留后分离并保持字典及组，以及六条轻量/旧式二维/三维 PL 在不同高程下合并成三组，保留圆弧、变宽与闭合轮廓；两种模式均一次 U 恢复原对象、高程、数据和关联。默认提示数值不受图纸 LUPREC=0 的显示舍入影响。实机引擎验证仅为 AutoCAD 2024，其他版本为编译验证。

PL 打断 `XYD_PLBREAK` 回归增至 **57 项**，安装包中实际 AutoCAD2021–2024 DLL 已通过全部项目，哈希与 `tested-plugin.sha256` 一致。新增 23 项覆盖：精确圆弧和变宽插值、倾斜平面和厚度、乱序多点和重复/端点过滤、既有顶点断点、离线点投影、闭合环单点打开和多点分段（包含原起点及闭合边圆弧/宽度）、旧式二维/三维/拟合 PL。真实命令验证预选与运行后选择、M/S 模式切换、暂存 U、多点回车、开放/闭合/三维各一次 CAD U、原句柄/字典自引用/XData/组/三维顶点归属、旋转平移 UCS、取消暂存以及锁定图层不修改。每个结果检查总长度，并密集采样原曲线验证几何完整，无间隙。

闭合单点处理使用原生精确子曲线重组，在指定位置留一个断口；旧式闭合多点会合并原生 API 在原起点额外生成的两端片段。共享对象替换沿用原身份和撤销机制，不把附加数据丢弃设置或统一 Z 设置带入打断命令。API 依据：[Curve.GetSplitCurves](https://help.autodesk.com/cloudhelp/2019/ENU/OARX-ManagedRefGuide/files/OREFNET-Autodesk_AutoCAD_DatabaseServices_Curve_GetSplitCurves_DoubleCollection.html)、[DBObject.HandOverTo](https://help.autodesk.com/cloudhelp/2018/ENU/OARX-ManagedRefGuide/files/OREFNET-Autodesk_AutoCAD_DatabaseServices_DBObject_HandOverTo__CallerMustClose__DBObject__MarshalAsUnmanagedType_U1__bool__MarshalAsUnmanagedType_U1__bool.html)。

四段 DLL：AutoCAD 2018、2019–2020、2021–2024、2025–2026。程序集均为 1.3.0.0；安装器为 V1.3 / 1.3.0。四段资源的 SHA256 与根目录正式 `Blocks`、`PMPFiles` 一致。

新版图框来源 `bin/x64/Debug/Blocks/XYD_TitleBlocks.dwg` 已复制到根目录正式资源和 `Installer/Contents/Blocks`，安装资源旧文件备份为 `XYD_TitleBlocks.preV13_20261001_113257.bak`；新图框 SHA256 为 `4FD3FB058961B147F0262C2362C2DF50B76989C6F14F5873668E8AD9A606AA75`。

AutoCAD 2024 已做上述自动化运行验证。其他 AutoCAD 年份仅完成编译验证，未宣称实机全版本测试。完整桌面中的交互点选、Esc、Ribbon、打印窗口与现场图库配置仍需按教程试用；渲染窗口测试不等同于全部桌面交互验收。没有执行 Git push。

## 2026-11-02 图标、CTB 打印和批量缩放

本次继续保持 V1.3 / 1.3.0.0。四段 Release DLL、Debug DLL 和安装包均已重建，正式图块与 PMP 在四段安装资源中的 SHA256 全部一致。

- `Run-UpgradeTests.ps1 -PluginPath 'Installer/Build/XYDSignTool.bundle/Contents/AutoCAD2021-2024/XYDSignTool.dll'`：实际安装包 DLL 在 AutoCAD 2024 Core Console 中通过 10 项检查，涵盖图标完整 16/32 DIP 画布、冻结缓存、100/125/150/200% DPI 渲染，以及独立中心与真实 Z、放大/缩小、倍率 1、无效倍率、图块属性只变换一次、父子去重、锁定属性整块回滚、旋转 UCS、预选/后选、取消和一次 U。
- Ribbon 回归 7 项通过，包括所有小按钮、下拉项的 16 DIP 图标、大图的 32 DIP 图标、批量缩放命令绑定和每列最多三行；保留 CAD/天正选项卡及焦点的原有回归继续通过。实际 T30 天正 + AutoCAD 2024 窗口中确认 XYD 小图标完整，缩放按钮位于旋转同列。四档 DPI 是渲染检查，实际桌面只验证本机当前 DPI。
- 原有曲线转换及 PL 打断 57 项在本次安装包实际 DLL 上通过；两个运行脚本各自的 `tested-plugin.sha256` 均与交付 DLL 一致。
- 完整 AutoCAD 2024 中运行 CTB/STB 混合批次：同名不同目录分别输出，原生转换完成后出图，源文件 SHA256 不变，恢复原活动文档并关闭临时副本。还验证已打开但未保存的模型和布局、自定义 CTB、动态图块独立缩放、取消清理、失败时旧 PDF 保留。完整文档队列需要 GUI 宿主，不能在 Core Console 中用 `DocumentManager.Open` 代替。
- 相对路径外参及已加载图片用独立临时 DWG 检查：完整副本保留引用，副本路径解析为原资源的绝对路径，源图纸中的相对路径不变。测试先保存并重新读取样本，为图片路径提供实际 `Database.Filename`，避免把无文件名的新数据库误作有效相对路径样本。
- 用 Poppler 渲染、PyMuPDF 读取六份测试 PDF：黑白 CTB 的 ACI 测试线全部为黑色；本机自定义 `acad.ctb` 保留其红色规则，未强制替换成黑白；失败输出保留旧 PDF 字节，取消未生成 PDF。没有修改用户 CTB。

测试代码为 `UpgradeTests.cs` 和 `UpgradeAdditionalTests.cs`，输出位于 `artifacts/upgrade-regression`。完整 CAD 由于已经自动加载已安装的 V1.3，采用同一份源码、不同程序集名的验证 DLL，避免宿主复用旧程序集；开发验证 DLL 和测试图纸不进入安装包。原始追加测试记录保留调试过程；`additional-final-results.txt` 按测试名称列出最后一次结果。

完整 CAD 的追加验证命令仅允许在含 `LiveVerification_` 的专用测试路径下执行，会改写该临时图纸内容，禁止用于工作图纸。准备相同目录中的 `test-root.txt` 指向仓库、加载验证 DLL 和测试 DLL 后，运行 `XYD_UPGRADE_ADDITIONAL`；只检查引用副本可运行 `XYD_UPGRADE_REFERENCES`。所有年份的实机声明仍仅限 AutoCAD 2024，其余为编译验证。
