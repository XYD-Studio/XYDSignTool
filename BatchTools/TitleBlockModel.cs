using System;
using System.ComponentModel;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace XYDSignTool
{
    public class TitleBlockModel : INotifyPropertyChanged
    {
        public ObjectId ObjId { get; set; }
        public string DocumentName { get; set; }
        public string LayoutName { get; set; }
        public bool IsModelSpace { get; set; }

        private string _drawNum;
        public string DrawNum { get { return _drawNum; } set { _drawNum = value; OnPropertyChanged("DrawNum"); } }

        private string _drawTitle;
        public string DrawTitle { get { return _drawTitle; } set { _drawTitle = value; OnPropertyChanged("DrawTitle"); } }

        private string _pageSize;
        public string PageSize { get { return _pageSize; } set { _pageSize = value; OnPropertyChanged("PageSize"); } }

        private string _drawScale;
        public string DrawScale { get { return _drawScale; } set { _drawScale = value; OnPropertyChanged("DrawScale"); } }

        private string _version;
        public string Version { get { return _version; } set { _version = value; OnPropertyChanged("Version"); } }

        private string _date;
        public string Date { get { return _date; } set { _date = value; OnPropertyChanged("Date"); } }

        private string _remarks;
        public string Remarks { get { return _remarks; } set { _remarks = value; OnPropertyChanged("Remarks"); } }

        public Point3d MinPt { get; set; }
        public Point3d MaxPt { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
