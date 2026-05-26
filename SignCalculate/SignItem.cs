using System;
using Autodesk.AutoCAD.DatabaseServices; // 引入 CAD 实体命名空间

namespace XYDSignTool
{
    public class SignItem
    {
        // ★ 新增：让这个数据行和 CAD 里的真实图块绑定
        public ObjectId ObjId { get; set; }

        public string BlockName { get; set; }
        public string Mode { get; set; }
        public string Area { get; set; }
        public string No { get; set; }
        public string Name { get; set; }
        public string InstallType { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public string Thickness { get; set; }
        public string Weight { get; set; }
        public int Qty { get; set; }
        public string Tech { get; set; }
        public string Power { get; set; }

        public int CategoryId { get; set; }
        public double BasePrice { get; set; }

        public double AreaSqM
        {
            get { return (Width * Height) / 1000000.0; }
        }

        public double UnitPrice
        {
            get
            {
                if (Tech == "PC板面板，不锈钢围边发光立体字")
                {
                    return BasePrice;
                }
                else
                {
                    return BasePrice * AreaSqM;
                }
            }
        }

        public double TotalPrice
        {
            get { return UnitPrice * Qty; }
        }
    }
}