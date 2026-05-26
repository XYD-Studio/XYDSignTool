把从 AutoCAD 已配置好的打印机中导出的 PMP 图纸尺寸模板放在这个目录。

推荐文件名：
XYD_PaperSizes.pmp

构建后，该目录会复制到插件 DLL 同级的 PMPFiles 目录。
批量打印预检窗口中的“一键注入图纸尺寸”会读取这里的 .pmp 文件，并复制到用户 AutoCAD 的 PMPFiles 目录。
