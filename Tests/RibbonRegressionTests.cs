using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Collections.Specialized;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Windows;

namespace XYDSignTool.Tests
{
    public class RibbonRegressionTests
    {
        private static readonly MethodInfo Install = typeof(RibbonBuilder).GetMethod("InstallToolkitTab", BindingFlags.Static | BindingFlags.NonPublic);
        private static readonly MethodInfo Create = typeof(RibbonBuilder).GetMethod("CreateToolkitTab", BindingFlags.Instance | BindingFlags.NonPublic);
        private static RibbonTab NewTab() { return (RibbonTab)Create.Invoke(new RibbonBuilder(), null); }
        private static void Apply(RibbonControl ribbon, bool activate, Func<RibbonTab> factory)
        {
            try { Install.Invoke(null, new object[] { ribbon, activate, factory }); }
            catch (TargetInvocationException ex) { throw ex.InnerException; }
        }
        private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

        [CommandMethod("XYD_RIBBON_REGRESSION")]
        public void Run()
        {
            var log = new StringBuilder();
            try
            {
                var ribbon = new RibbonControl();
                var cad = new RibbonTab { Id = "CAD_NATIVE", Title = "CAD" };
                var tarch = new RibbonTab { Id = "TARCH_NATIVE", Title = "TArch" };
                ribbon.Tabs.Add(cad); ribbon.Tabs.Add(tarch); ribbon.ActiveTab = tarch;
                Apply(ribbon, false, NewTab);
                Check(ReferenceEquals(ribbon.ActiveTab, tarch), "automatic load changed the active TArch tab");
                Check(ribbon.Tabs[0] == cad && ribbon.Tabs[1] == tarch, "native tabs were changed");
                log.AppendLine("PASS automatic load preserves CAD/TArch tabs and active tab");

                for (int i = 0; i < 10; i++) Apply(ribbon, false, NewTab);
                Check(ribbon.Tabs.Count == 3 && ribbon.ActiveTab == tarch, "rebuild duplicated tabs or took focus");
                log.AppendLine("PASS repeated rebuild keeps one XYD tab without taking focus");

                var stable = ribbon.Tabs[2];
                try { Apply(ribbon, false, delegate { throw new InvalidOperationException("expected factory failure"); }); }
                catch (InvalidOperationException) { }
                Check(ribbon.Tabs.Count == 3 && ribbon.Tabs[2] == stable && ribbon.ActiveTab == tarch, "factory failure changed the host");
                log.AppendLine("PASS construction failure leaves all existing tabs intact");

                bool throwOnce = true;
                NotifyCollectionChangedEventHandler fault = delegate(object sender, NotifyCollectionChangedEventArgs e)
                {
                    if (throwOnce && e.Action == NotifyCollectionChangedAction.Add)
                    { throwOnce = false; throw new InvalidOperationException("expected attach failure"); }
                };
                ribbon.Tabs.CollectionChanged += fault;
                try { Apply(ribbon, false, NewTab); } catch (InvalidOperationException) { }
                finally { ribbon.Tabs.CollectionChanged -= fault; }
                Check(!throwOnce && ribbon.Tabs.Count == 3 && ribbon.Tabs[2] == stable && ribbon.ActiveTab == tarch, "attach failure did not restore the old tab");
                log.AppendLine("PASS host collection failure rolls back XYD replacement");

                Apply(ribbon, true, NewTab);
                Check(ribbon.ActiveTab == ribbon.Tabs[2], "explicit refresh did not activate XYD");
                Apply(ribbon, false, NewTab);
                Check(ribbon.ActiveTab == ribbon.Tabs[2] && ribbon.Tabs.Count == 3, "active XYD tab was lost on replacement");
                Check(ribbon.Tabs[0] == cad && ribbon.Tabs[1] == tarch && cad.IsVisible && tarch.IsVisible, "other toolbars were hidden");
                log.AppendLine("PASS explicit refresh activates XYD; automatic repair retains its selection");
                var panel = ribbon.Tabs[2].Panels.Single(p => p.Source.Title == "统计与测量").Source;
                var button = panel.Items.OfType<RibbonRowPanel>().SelectMany(row => row.Items).OfType<RibbonButton>().Single(b => b.Text == "PL 打断");
                Check(button.Size == RibbonItemSize.Standard && button.ShowText && button.ShowImage && (string)button.CommandParameter == "XYD_PLBREAK ", "break button lost compact layout or command");
                Check(IconFactory.GetVectorIcon("plbreak") != null, "break icon missing");
                log.AppendLine("PASS PL break compact icon / label / command is present");
                var scale = panel.Items.OfType<RibbonRowPanel>().SelectMany(row => row.Items).OfType<RibbonButton>().Single(b => b.Text == "批量缩放");
                Check((string)scale.CommandParameter == "XYD_MSCALE " && scale.Size == RibbonItemSize.Standard, "scale command missing");
                foreach (var row in panel.Items.OfType<RibbonRowPanel>())
                    Check(row.Items.OfType<RibbonButton>().Count() <= 3, "more than three rows");
                foreach (var p in ribbon.Tabs[2].Panels) foreach (var row in p.Source.Items.OfType<RibbonRowPanel>()) foreach (var b in row.Items.OfType<RibbonButton>())
                {
                    Check(b.Image == null || (b.Image.Width == 16 && b.Image.Height == 16), "small image must be 16 DIP: " + b.Text);
                    Check(b.LargeImage == null || (b.LargeImage.Width == 32 && b.LargeImage.Height == 32), "large image must be 32 DIP: " + b.Text);
                    var split = b as RibbonSplitButton;
                    if (split != null) foreach (var child in split.Items.OfType<RibbonButton>()) Check(child.Image == null || (child.Image.Width == 16 && child.Image.Height == 16), "menu image size");
                }
                log.AppendLine("PASS all small / large / dropdown images sized correctly; scale fits existing column");
                log.AppendLine("Assembly=" + typeof(RibbonBuilder).Assembly.FullName);
            }
            catch (System.Exception ex) { log.AppendLine("FAIL " + ex); }
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XYD_RIBBON_TEST_ROOT"), "artifacts", "ribbon-regression", "results.txt"), log.ToString(), Encoding.UTF8);
        }
    }
}
