using DieselBundleViewer.Services;
using DieselBundleViewer.ViewModels;
using DieselEngineFormats.Bundle;
using DieselEngineFormats.Crate;
using DieselEngineFormats.Utils;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;

namespace DieselBundleViewer.Models
{
    public class FileEntry : IEntry
    {
        private string _name, _fullpath;
        private PackageFileEntry _max_entry = null;

        public Idstring PathIds;
        public Idstring LanguageIds;
        public Idstring ExtensionIds;

        private ulong _size;
        public ulong Size {
            get {
                if (_size == 0 && BundleEntries.Count > 0)
                {
                    _size = (ulong)BundleEntries.Values.Sum(be => (long)be.Length) / (ulong)BundleEntries.Count;
                }
                else if (_size == 0 && CrateEntry != null)
                {
                    _size = CrateEntry.RawSize;
                }
                return _size;
            }
        }

        public string EntryPath
        {
            get
            {
                if (_fullpath == null)
                {
                    _fullpath = PathIds.ToString();

                    if (LanguageIds != null)
                        _fullpath += "." + LanguageIds.ToString();

                    _fullpath += "." + ExtensionIds.ToString();
                }
                return _fullpath;
            }
            set => _fullpath = value;
        }

        public string Name
        {
            get
            {
                if (_name == null)
                    _name = Path.GetFileName(EntryPath);

                return _name;
            }

            set => _name = value;
        }

        public Dictionary<Idstring, PackageFileEntry> BundleEntries { get; set; }

        // The source .crate for this asset. The same asset id can appear in a
        // base crate and one or more patch crates; crates are loaded in ascending
        // priority order so the highest-priority one (see CratePriority) wins.
        public Idstring CrateName { get; private set; }
        public CrateFileEntry CrateEntry { get; private set; }

        public bool HasPackageData => BundleEntries.Count > 0 || CrateEntry != null;

        public DatabaseEntry DBEntry { get; set; }

        public string Type => ExtensionIds?.ToString();

        public FolderEntry Parent { get; set; }

        public FileEntry() {
            BundleEntries = new Dictionary<Idstring, PackageFileEntry>();
        }

        public FileEntry(DatabaseEntry dbEntry) : this() {
            DBEntry = dbEntry;
        }

        public void LoadPath()
        {
            if(DBEntry != null)
                General.GetFilepath(DBEntry, out PathIds, out LanguageIds, out ExtensionIds, DBEntry.Parent);
        }

        public void AddBundleEntry(PackageFileEntry entry)
        {
            if (!BundleEntries.ContainsKey(entry.PackageName))
            {
                BundleEntries.Add(entry.PackageName, entry);
                _max_entry = null;
            }
        }

        public void AddCrateEntry(Idstring crateName, CrateFileEntry entry)
        {
            // Last write wins: crates are loaded in ascending priority order
            // (see OpenCrateFile / CratePriority), so a patch crate loaded after
            // the base crate overrides it as the source of truth.
            CrateName = crateName;
            CrateEntry = entry;
        }

        /// <summary>
        /// Checks if the file is in a bundle or crate.
        /// </summary>
        /// <param name="name">The name (idstring) of the bundle/crate</param>
        /// <returns>true if it's in the bundle/crate</returns>
        public bool InBundle(Idstring name) => BundleEntries.ContainsKey(name) || (CrateName != null && CrateName.Equals(name));

        /// <summary>
        /// Returns whether or not the file is in one of the bundles/crates provided in the arguments
        /// </summary>
        /// <param name="names">Names (idstring) of packages to test with</param>
        public bool InBundles(List<Idstring> names)
        {
            foreach (var bundle in names)
            {
                if (InBundle(bundle))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Determines whether or not a file has any data to extract. Cooked physics are ignored since they often are 0 bytes but still exist.
        /// </summary>
        /// <returns>true if the file has data</returns>
        public bool HasData()
        {
            return Settings.Data.DisplayEmptyFiles || Type == "cooked_physics" || Size > 0;
        }

        public object FileData(PackageFileEntry be = null, FormatConverter exporter = null)
        {
           if (exporter == null)
                return FileStream(be);
           else
           {
                MemoryStream stream = FileStream(be);
                return stream == null ? null : exporter.Export(stream);
            }
        }

        /// <summary>
        /// Runs a package-specific byte read, logging null/failure cases uniformly.
        /// </summary>
        private static byte[] TryReadBytes<T>(T entry, Func<T, byte[]> read) where T : class
        {
            if (entry == null)
            {
                Console.WriteLine("Entry null?");
                return null;
            }

            try
            {
                return read(entry);
            }
            catch (Exception exc)
            {
                Console.WriteLine("FAIL");
                Console.WriteLine(exc.Message);
                Console.WriteLine(exc.StackTrace);
            }

            return null;
        }

        /// <summary>
        /// Returns the bytes[] of the file
        /// </summary>
        /// <param name="entry">A package entry to use for the data. Defaults to what MaxBundleEntry returns.</param>
        private byte[] FileEntryBytes(PackageFileEntry entry) => TryReadBytes(entry, e =>
        {
            string bundle_path = Path.Combine(Utils.CurrentWindow.AssetsDir, e.Parent.BundleName + ".bundle");
            if (!File.Exists(bundle_path))
            {
                Console.WriteLine("Bundle: {0}, does not exist", bundle_path);
                return null;
            }

            using FileStream fs = new FileStream(bundle_path, FileMode.Open, FileAccess.Read);
            using BinaryReader br = new BinaryReader(fs);
            if (e.Length != 0)
            {
                fs.Position = e.Address;
                return br.ReadBytes((int)(e.Length == -1 ? fs.Length - fs.Position : e.Length));
            }
            else
                return new byte[0];
        });

        /// <summary>
        /// Returns the bytes[] of a crate-backed entry, decompressing as needed.
        /// </summary>
        private byte[] CrateEntryBytes(CrateFileEntry entry) => TryReadBytes(entry, e => e.Parent.ReadEntry(e));

        /// <summary>
        /// Returns a MemoryStream of the file.
        /// </summary>
        /// <param name="entry">A package entry to use for the data. Defaults to what MaxBundleEntry returns.</param>
        public MemoryStream FileStream(PackageFileEntry entry = null)
        {
            byte[] bytes = FileBytes(entry);
            if (bytes == null)
                return null;

            MemoryStream stream = new MemoryStream(bytes) { Position = 0 };
            return stream;
        }

        public byte[] FileBytes(PackageFileEntry entry = null)
        {
            if (entry != null || BundleEntries.Count > 0)
                return FileEntryBytes(entry ?? MaxBundleEntry());

            return CrateEntryBytes(CrateEntry);
        }

        /// <summary>
        /// Returns the bundle that has the largest version of the file.
        /// </summary>
        public PackageFileEntry MaxBundleEntry()
        {
            if (BundleEntries.Count == 0)
                return null;

            if (_max_entry == null)
            {
                _max_entry = null;
                foreach (var pair in BundleEntries)
                {
                    PackageFileEntry entry = pair.Value;
                    if (_max_entry == null)
                    {
                        _max_entry = entry;
                        continue;
                    }

                    if (entry.Length > _max_entry.Length)
                        _max_entry = entry;
                }

            }

            return _max_entry;
        }
    }
}
