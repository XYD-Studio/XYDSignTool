using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace XYDSignTool
{
    public sealed class PreparedSignature : IDisposable
    {
        public SignaturePosition Position;
        public string Person;
        public string DefinitionName;
        public List<Entity> Geometry = new List<Entity>();
        public Extents3d Bounds;
        public void Dispose() { foreach (var e in Geometry) e.Dispose(); Geometry.Clear(); }
    }
    public static class ElectronicSignatureService
    {
        private const string AppName = "XYD_SIGNATURE_V13";
        private const string Prefix = "XYD_SIG_";
        public static List<SignaturePerson> Catalog(string file)
        {
            using (var db = new Database(false, true))
            {
                db.ReadDwgFile(file, FileShare.Read, true, "");
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var nested = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var inUse = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    foreach (ObjectId id in bt)
                    {
                        var btr = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
                        if (!btr.IsLayout && !btr.IsAnonymous && !btr.IsFromExternalReference) names.Add(btr.Name);
                        foreach (ObjectId child in btr)
                        {
                            var br = tr.GetObject(child, OpenMode.ForRead) as BlockReference;
                            if (br == null) continue;
                            string name = AttributeMappingService.EffectiveName(br, tr);
                            if (btr.IsLayout) inUse.Add(name); else nested.Add(name);
                        }
                    }
                    return names.Where(n => !n.StartsWith("XYD_SIG_GEOMETRY_", StringComparison.OrdinalIgnoreCase)).OrderBy(n => n).Select(n => new SignaturePerson { Name = n, BlockName = n }).ToList();
                }
            }
        }
        public static List<PreparedSignature> Prepare(AttributeMappingProfile p, MappingRule rule,
            Dictionary<string, AttributeReference> attrs, Dictionary<string, string> updates)
        {
            var result = new List<PreparedSignature>();
            var failures = new List<string>();
            try
            {
                foreach (var position in rule.Signatures)
                {
                    string person = "";
                    try
                    {
                        if (!updates.TryGetValue(position.NameTag, out person)) person = attrs[position.NameTag].TextString;
                        person = (person ?? "").Trim();
                        if (person.Length == 0) { result.Add(new PreparedSignature { Position = position, Person = "" }); continue; }
                        var candidates = p.People.Where(s => string.Equals((s.Name ?? "").Trim(), person, StringComparison.OrdinalIgnoreCase)).ToList();
                        if (candidates.Count != 1) throw new InvalidOperationException("签名匹配缺失或不唯一，请在签名库映射中设置。");
                        result.Add(PrepareOne(p, candidates[0], position));
                    }
                    catch (System.Exception ex) { failures.Add(position.Role + " / " + person + " / " + position.NameTag + "：" + ex.Message); }
                }
                if (failures.Count > 0) throw new InvalidOperationException(string.Join("\n", failures));
                return result;
            }
            catch { foreach (var item in result) item.Dispose(); throw; }
        }
        public static PreparedSignature PrepareOne(AttributeMappingProfile p, SignaturePerson person, SignaturePosition slot)
        {
            var result = new PreparedSignature { Position = slot, Person = person.Name };
            try
            {
                using (var db = new Database(false, true))
                {
                    db.ReadDwgFile(p.SignatureLibraryPath, FileShare.Read, true, "");
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                        if (!bt.Has(person.BlockName)) throw new InvalidOperationException("签名库中没有图块：" + person.BlockName);
                        var otherPeople = new HashSet<string>(p.People.Where(s => !string.Equals(s.BlockName, person.BlockName, StringComparison.OrdinalIgnoreCase)).Select(s => s.BlockName), StringComparer.OrdinalIgnoreCase);
                        foreach (ObjectId id in bt)
                        {
                            var candidate = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
                            if (!candidate.IsLayout && !candidate.IsAnonymous && !candidate.Name.StartsWith("XYD_SIG_GEOMETRY_", StringComparison.OrdinalIgnoreCase) && !candidate.Name.Equals(person.BlockName, StringComparison.OrdinalIgnoreCase)) otherPeople.Add(candidate.Name);
                        }
                        Flatten(db, tr, bt[person.BlockName], Matrix3d.Identity, otherPeople, new HashSet<ObjectId>(), result.Geometry);
                    }
                }
                if (result.Geometry.Count == 0) throw new InvalidOperationException("签名块没有可用矢量几何：" + person.BlockName);
                bool first = true;
                var key = new StringBuilder(person.Name);
                foreach (var e in result.Geometry)
                {
                    var ext = e.GeometricExtents;
                    if (first) { result.Bounds = ext; first = false; } else result.Bounds.AddExtents(ext);
                    key.Append('|').Append(e.GetType().Name).Append('|').Append(ext.MinPoint).Append('|').Append(ext.MaxPoint);
                }
                // File digest is used only as an opaque definition cache key, never copied as library content.
                using (var sha = SHA256.Create()) using (var stream = File.OpenRead(p.SignatureLibraryPath))
                    key.Append(Convert.ToBase64String(sha.ComputeHash(stream)));
                using (var sha = SHA256.Create()) result.DefinitionName = Prefix + BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(key.ToString()))).Replace("-", "").Substring(0, 24);
                return result;
            }
            catch { result.Dispose(); throw; }
        }
        private static void Flatten(Database db, Transaction tr, ObjectId id, Matrix3d transform,
            HashSet<string> otherPeople, HashSet<ObjectId> visiting, List<Entity> result)
        {
            if (!visiting.Add(id)) throw new InvalidOperationException("签名存在循环嵌套。");
            var btr = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
            if (btr.IsFromExternalReference || btr.IsFromOverlayReference || btr.IsLayout) throw new InvalidOperationException("签名不能引用外参或整张图库。");
            if (otherPeople.Contains(btr.Name)) throw new InvalidOperationException("签名依赖其他人员图块：" + btr.Name);
            foreach (ObjectId child in btr)
            {
                var e = tr.GetObject(child, OpenMode.ForRead) as Entity;
                if (e == null || !e.Visible) continue;
                var block = e as BlockReference;
                if (block != null) { Flatten(db, tr, block.BlockTableRecord, transform * block.BlockTransform, otherPeople, visiting, result); continue; }
                if (e is AttributeDefinition) continue;
                // Rebuild primitives from value geometry: Clone would retain foreign style/owner IDs
                // after the source database is disposed, and may follow unrelated dictionaries.
                var clone = CopyGeometry(e);
                clone.TransformBy(transform);
                result.Add(clone);
            }
            visiting.Remove(id);
        }
        private static Entity CopyGeometry(Entity source)
        {
            var line = source as Line;
            if (line != null) return new Line(line.StartPoint, line.EndPoint);
            var pl = source as Polyline;
            if (pl != null)
            {
                var copy = new Polyline { Normal = pl.Normal, Elevation = pl.Elevation, Closed = pl.Closed, Thickness = pl.Thickness };
                for (int i = 0; i < pl.NumberOfVertices; i++) copy.AddVertexAt(i, pl.GetPoint2dAt(i), pl.GetBulgeAt(i), pl.GetStartWidthAt(i), pl.GetEndWidthAt(i));
                return copy;
            }
            var circle = source as Circle; if (circle != null) return new Circle(circle.Center, circle.Normal, circle.Radius);
            var arc = source as Arc; if (arc != null) return new Arc(arc.Center, arc.Normal, arc.Radius, arc.StartAngle, arc.EndAngle);
            var ellipse = source as Ellipse; if (ellipse != null) return new Ellipse(ellipse.Center, ellipse.Normal, ellipse.MajorAxis, ellipse.RadiusRatio, ellipse.StartParam, ellipse.EndParam);
            var spline = source as Spline; if (spline != null) { var copy = new Spline(); copy.NurbsData = spline.NurbsData; return copy; }
            var text = source as DBText;
            if (text != null)
            {
                if (text.HasFields) throw new InvalidOperationException("签名文字含字段，请先转为独立几何。");
                return new DBText { TextString = text.TextString, Position = text.Position, Height = text.Height, Rotation = text.Rotation, Normal = text.Normal, WidthFactor = text.WidthFactor, Oblique = text.Oblique };
            }
            var mtext = source as MText;
            if (mtext != null)
            {
                if (mtext.HasFields) throw new InvalidOperationException("签名文字含字段，请先转为独立几何。");
                return new MText { Contents = mtext.Contents, Location = mtext.Location, TextHeight = mtext.TextHeight, Width = mtext.Width, Rotation = mtext.Rotation, Normal = mtext.Normal, Attachment = mtext.Attachment };
            }
            var solid = source as Solid;
            if (solid != null) { var copy = new Solid(); for (int i = 0; i < 4; i++) copy.SetPointAt((short)i, solid.GetPointAt((short)i)); return copy; }
            var hatch = source as Hatch;
            if (hatch != null)
            {
                if (!string.Equals(hatch.PatternName, "SOLID", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("签名填充需要为 SOLID，避免改变原图案或携带外部依赖。");
                var copy = new Hatch { Normal = hatch.Normal, Elevation = hatch.Elevation, Associative = false };
                copy.SetHatchPattern(HatchPatternType.PreDefined, "SOLID");
                for (int i = 0; i < hatch.NumberOfLoops; i++)
                {
                    var loop = hatch.GetLoopAt(i);
                    copy.AppendLoop(loop);
                }
                return copy;
            }
            throw new InvalidOperationException("签名含不支持隔离的对象：" + source.GetType().Name + "，请先转换为线段、二维多段线、圆弧、圆、椭圆、样条曲线或实心填充。");
        }
        public static bool Apply(Database db, Transaction tr, BlockReference frame, MappingRule rule, List<PreparedSignature> prepared, bool remove)
        {
            var space = (BlockTableRecord)tr.GetObject(frame.OwnerId, OpenMode.ForWrite);
            var existing = new Dictionary<string, List<BlockReference>>();
            foreach (ObjectId id in space)
            {
                var br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                if (br == null) continue;
                using (var x = br.GetXDataForApplication(AppName))
                {
                    if (x == null) continue;
                    var v = x.AsArray();
                    if (v.Length < 3 || (string)v[1].Value != frame.Handle.ToString()) continue;
                    string slot = (string)v[2].Value;
                    if (!existing.ContainsKey(slot)) existing[slot] = new List<BlockReference>();
                    existing[slot].Add(br);
                }
            }
            bool changed = false;
            foreach (var set in existing.Values) foreach (var br in set) { br.UpgradeOpen(); br.Erase(); changed = true; }
            if (remove) return changed;
            var reg = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
            if (!reg.Has(AppName)) { reg.UpgradeOpen(); var entry = new RegAppTableRecord { Name = AppName }; reg.Add(entry); tr.AddNewlyCreatedDBObject(entry, true); }
            foreach (var s in prepared ?? new List<PreparedSignature>())
            {
                if (string.IsNullOrEmpty(s.Person)) continue;
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                ObjectId definition;
                if (bt.Has(s.DefinitionName))
                {
                    definition = bt[s.DefinitionName];
                    if (!IsManagedDefinition(tr.GetObject(definition, OpenMode.ForRead))) throw new InvalidOperationException("目标图纸中的同名签名定义不属于本插件，已停止覆盖：" + s.DefinitionName);
                }
                else
                {
                    // Isolate flattened geometry in a fresh database, so source block dictionaries cannot leak.
                    using (var isolated = new Database(true, true))
                    {
                        ObjectId sourceId;
                        using (var it = isolated.TransactionManager.StartTransaction())
                        {
                            var ibt = (BlockTable)it.GetObject(isolated.BlockTableId, OpenMode.ForWrite);
                            var ibtr = new BlockTableRecord { Name = s.DefinitionName }; sourceId = ibt.Add(ibtr); it.AddNewlyCreatedDBObject(ibtr, true);
                            foreach (var geometry in s.Geometry)
                            {
                                var e = (Entity)geometry.Clone();
                                e.SetDatabaseDefaults(isolated); e.Layer = "0";
                                ibtr.AppendEntity(e); it.AddNewlyCreatedDBObject(e, true);
                            }
                            it.Commit();
                        }
                        db.WblockCloneObjects(new ObjectIdCollection { sourceId }, db.BlockTableId, new IdMapping(), DuplicateRecordCloning.Ignore, false);
                    }
                    definition = ((BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead))[s.DefinitionName];
                    var managed = tr.GetObject(definition, OpenMode.ForWrite);
                    using (var marker = new ResultBuffer(new TypedValue(1001, AppName), new TypedValue(1000, "DEFINITION"))) managed.XData = marker;
                }
                double w = s.Bounds.MaxPoint.X - s.Bounds.MinPoint.X, h = s.Bounds.MaxPoint.Y - s.Bounds.MinPoint.Y;
                double scale = Math.Min(s.Position.Width / Math.Max(w, 1e-8), s.Position.Height / Math.Max(h, 1e-8));
                var center = new Point3d((s.Bounds.MinPoint.X + s.Bounds.MaxPoint.X) / 2, (s.Bounds.MinPoint.Y + s.Bounds.MaxPoint.Y) / 2, 0);
                Matrix3d local = Matrix3d.Displacement(new Vector3d(s.Position.X, s.Position.Y, s.Position.Z)) * Matrix3d.Scaling(scale, Point3d.Origin) * Matrix3d.Displacement(Point3d.Origin - center);
                var signature = new BlockReference(Point3d.Origin, definition);
                signature.TransformBy(frame.BlockTransform * local);
                signature.LayerId = frame.LayerId;
                space.AppendEntity(signature); tr.AddNewlyCreatedDBObject(signature, true);
                signature.XData = new ResultBuffer(new TypedValue(1001, AppName), new TypedValue(1000, frame.Handle.ToString()), new TypedValue(1000, s.Position.Id), new TypedValue(1000, s.Person));
                changed = true;
            }
            return changed;
        }
        public static void PurgeUnused(Database db, Transaction tr)
        {
            var ids = new ObjectIdCollection();
            var referenced = new HashSet<ObjectId>();
            foreach (ObjectId spaceId in (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead))
            {
                var space = (BlockTableRecord)tr.GetObject(spaceId, OpenMode.ForRead);
                foreach (ObjectId entityId in space)
                {
                    if (entityId.IsErased) continue;
                    var br = tr.GetObject(entityId, OpenMode.ForRead, false) as BlockReference;
                    if (br != null) referenced.Add(br.BlockTableRecord);
                }
            }
            foreach (ObjectId id in (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead))
            {
                var btr = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
                if (btr.Name.StartsWith(Prefix, StringComparison.Ordinal) && IsManagedDefinition(btr) && !referenced.Contains(id)) ids.Add(id);
            }
            db.Purge(ids);
            foreach (ObjectId id in ids) tr.GetObject(id, OpenMode.ForWrite).Erase();
        }
        private static bool IsManagedDefinition(DBObject definition)
        {
            using (var marker = definition.GetXDataForApplication(AppName))
            {
                if (marker == null) return false; var values = marker.AsArray();
                return values.Length == 2 && string.Equals(values[1].Value as string, "DEFINITION", StringComparison.Ordinal);
            }
        }
    }
}
