using AdonisUI;
using DieselBundleViewer.Models;
using DieselBundleViewer.Objects;
using DieselBundleViewer.Services;
using DieselBundleViewer.Views;
using DieselEngineFormats.Bundle;
using DieselEngineFormats.Crate;
using DieselEngineFormats.Utils;
using Microsoft.Win32;
using Prism.Commands;
using Prism.Dialogs;
using Prism.Mvvm;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DieselBundleViewer.ViewModels
{
    public class MainWindowViewModel : BindableBase
    {
        #region Properties / Fields
        private string _title = "Diesel Bundle Viewer";
        public PackageDatabase DB { get; private set; }
        public TreeEntryViewModel Root { get; set; }

        public string Title { get => _title; set => SetProperty(ref _title, value); }

        public string AssetsDir { get; set; }

        private string status;
        public string Status { get => status; set => SetProperty(ref status, value); }

        private string fileStatus;
        public string FileStatus { get => fileStatus; set => SetProperty(ref fileStatus, value); }

        private int gridViewScale = 90;
        public int GridViewScale { get => gridViewScale; set => SetProperty(ref gridViewScale, value); }

        public Dictionary<Idstring, PackageHeader> PackageHeaders;
        public Dictionary<Idstring, CrateFile> CrateFiles;

        public Dictionary<uint, FileEntry> FileEntries { get; set; }

        //Used by scripts.
        public Dictionary<Tuple<Idstring, Idstring, Idstring>, FileEntry> RawFiles;

        public ObservableCollection<EntryViewModel> ToRender { get; set; }
        public ObservableCollection<TreeEntryViewModel> FoldersToRender { get; set; }

        public static List<Script> Scripts => ScriptActions.Scripts;
        public static bool ScriptsVisible => Scripts.Count > 0;
        public ObservableCollection<string> RecentFiles { get; set; }
        public bool RecentFilesVis => RecentFiles.Count > 0;

        public List<Idstring> Bundles { get; set; }
        public List<Idstring> SelectedBundles { get; set; }

        public LinkedList<PageData> Pages = new();
        public LinkedListNode<PageData> CurrentPage;
        public string CurrentDir { get => CurrentPage?.Value.Path; set => Navigate(value); }

        public DelegateCommand OpenFileDialog { get; }
        public DelegateCommand<string> OpenFile { get; }
        public DelegateCommand OpenUpdateHashlistDialog { get; }
        public DelegateCommand OpenAboutDialog { get; }
        public DelegateCommand OpenSettingsDialog { get; }
        public DelegateCommand OpenFindDialog { get; }
        public DelegateCommand OpenBundleSelectorDialog { get; }
        public DelegateCommand ForwardDir { get; }
        public DelegateCommand BackDir { get; }
        public DelegateCommand OnKeyDown { get; }
        public DelegateCommand CloseBLB { get; }
        public DelegateCommand<string> SetViewStyle { get; }
        public DelegateCommand OpenHowToUse { get; }
        public DelegateCommand ExtractAll { get; }

        private Point DragStartLocation;

        private bool DragStart;
        public bool Dragging { get; private set; }

        private CancellationTokenSource cancelLastTask;

        private UserControl entriesStyle;
        public UserControl EntriesStyle { get => entriesStyle; set => SetProperty(ref entriesStyle, value); }

        #endregion

        public MainWindowViewModel(DialogService dialogService)
        {
            Utils.CurrentDialogService = dialogService;
            Utils.CurrentWindow = this;

            //Lists and stuff for the bundles/files/etc
            PackageHeaders = [];
            CrateFiles = [];
            Bundles = [];
            ToRender = [];
            FoldersToRender = [];
            SelectedBundles = [];
            RawFiles = [];
            RecentFiles = new ObservableCollection<string>(Settings.Data.RecentFiles);

            //Commands / Events
            OpenFileDialog = new DelegateCommand(OpenFileDialogExec);
            OpenFile = new DelegateCommand<string>(OpenFileExec);
            CloseBLB = new DelegateCommand(CloseBLBExec, () => Root != null || cancelLastTask != null);
            OpenBundleSelectorDialog = new DelegateCommand(OpenBundleSelectorDialogExec, () => Root != null);
            OpenFindDialog = new DelegateCommand(OpenFindDialogExec, () => Root != null);
            BackDir = new DelegateCommand(BackDirExec, ()=> CurrentPage?.Previous != null);
            ForwardDir = new DelegateCommand(ForwardDirExec, ()=> CurrentPage?.Next != null);
            OnKeyDown = new DelegateCommand(OnKeyDownExec);
            SetViewStyle = new DelegateCommand<string>(style => SetViewStyleExec(style, true));
            OpenUpdateHashlistDialog = new DelegateCommand(() => Utils.ShowDialog("UpdateHashlistDialog", null, true));
            OpenAboutDialog = new DelegateCommand(() => Utils.ShowDialog("AboutDialog", null, true));
            OpenSettingsDialog = new DelegateCommand(() => Utils.ShowDialog("SettingsDialog", r => UpdateSettings()));
            OpenHowToUse = new DelegateCommand(() => Utils.OpenURL("https://github.com/Luffyyy/DieselBundleViewer/wiki/How-to-Use"));
            ExtractAll = new DelegateCommand(ExtractAllExec, () => Root != null);

            Utils.OnMouseMoved += OnMouseMoved;

            //Set status to default
            CloseBLBExec();

            //Grid or list?
            EntriesStyle = new EntryListView();

            UpdateSettings();
        }

        #region Commands
        void OnKeyDownExec()
        {
            if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
            {
                if (Keyboard.IsKeyDown(Key.F) && OpenFindDialog.CanExecute())
                    OpenFindDialog.Execute();
                else if (Keyboard.IsKeyDown(Key.O) && OpenFileDialog.CanExecute())
                    OpenFileDialog.Execute();
                else if(Keyboard.IsKeyDown(Key.B) && OpenBundleSelectorDialog.CanExecute())
                    OpenBundleSelectorDialog.Execute();
            }
        }

        void UpdateSettings()
        {
            ResourceLocator.SetColorScheme(App.Current.Resources, 
                Settings.Data.DarkMode ? ResourceLocator.DarkColorScheme : ResourceLocator.LightColorScheme);
            RenderNewItems();
        }

        void OpenFindDialogExec()
        {
            if (Utils.DialogOpened("FindDialog"))
                return;

            Utils.ShowDialog("FindDialog", r => {
                string search = r.Parameters.GetValue<string>("Search");
                if (!string.IsNullOrEmpty(search))
                {
                    bool useRegex = FindDialogViewModel.UseRegex;
                    bool matchWord = FindDialogViewModel.MatchWord;
                    bool fullPath = FindDialogViewModel.FullPath;
                    Navigate(new PageData($"Search Results: '{search}' (Use Regex: {useRegex}, Match Word: {matchWord}, Full Path: {fullPath})")
                    {
                        IsSearch = true,
                        Search = search,
                        FullPath = fullPath,
                        UseRegex = useRegex,
                        MatchWord = matchWord
                    });
                }
            });
        }

        void OpenBundleSelectorDialogExec()
        {
            DialogParameters pms = new()
            {
                { "Bundles", Bundles }
            };
            Utils.ShowDialog("BundleSelectorDialog", pms, r =>
            {
                var children = Root.Owner.GetAllChildren(true);
                foreach(var entry in children)
                {
                    if (entry is FolderEntry folder)
                        folder.ResetTotalSize();
                }
                var selectedBundles = pms.GetValue<List<Idstring>>("SelectedBundles");
                if(selectedBundles != null)
                    SelectedBundles = selectedBundles;
                RenderNewItems();
            }, true);
        }

        void SetViewStyleExec(string style, bool resetScale=false)
        {
            bool isGrid = style == "grid";
            if (resetScale)
                GridViewScale = isGrid ? 90 : 64;
            if (isGrid)
                EntriesStyle = new EntryGridView();
            else
                EntriesStyle = new EntryListView();
        }

        public void OnMouseWheel(MouseWheelEventArgs e)
        {
            if(Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
            {
                e.Handled = true;

                int scale = GridViewScale;

                if(e.Delta > 0)
                    scale += 8;
                else if(e.Delta < 0)
                    scale -= 8;

                GridViewScale = Math.Clamp(scale, 64, 150);
                if (GridViewScale == 64)
                {
                    if(EntriesStyle is EntryGridView)
                        SetViewStyleExec("list");
                }
                else if(EntriesStyle is EntryListView)
                    SetViewStyleExec("grid");
            }
        }

        public void OnMouseMoved(Point pos)
        {
            Point diff = new(pos.X - DragStartLocation.X, pos.Y - DragStartLocation.Y);
            if (DragStart)
            {
                if (Math.Abs(diff.X) > 4 && Math.Abs(diff.Y) > 4)
                {
                    DragDropController controller = new(Settings.Data.ExtractFullDir);
                    var selected = new List<IEntry>();
                    foreach(var vm in ToRender)
                    {
                        if (vm.IsSelected)
                            selected.Add(vm.Owner);
                    }
                    controller.DoDragDrop(selected);
                    Dragging = true;
                    DragStart = false;
                }
            }
        }

        //Called from View.
        public void OnClick()
        {
            DragStartLocation = Utils.MousePos;
            DragStart = true;
        }

        public void OnRelease()
        {
            Dragging = false;
            DragStart = false;
        }

        public async void OpenFileDialogExec()
        {
            OpenFileDialog ofd = new() { Filter = "Bundle/Crate Database (*.blb;*.properties)|*.blb;*.properties|Bundle Database File (*.blb)|*.blb|Crate Properties (*.properties)|*.properties" };

            //Here we try to default to 2 very possible directories of PD2 which most users will use it for.
            if(!RecentFilesVis)
            {
                string possiblePath = @"C:\Program Files (x86)\Steam\steamapps\common\PAYDAY 2\assets";
                bool found = Directory.Exists(possiblePath);
                if (!found)
                {
                    possiblePath = @"D:\SteamLibrary\steamapps\common\PAYDAY 2\assets";
                    found = Directory.Exists(possiblePath);
                }

                if (found)
                    ofd.InitialDirectory = possiblePath;
            }

            var result = ofd.ShowDialog();
            if (result == true)
            {
                string fileName = ofd.FileName;

                //If the file already exists in the recents, bump it.
                RecentFiles.Remove(fileName);

                RecentFiles.Insert(0, fileName);

                Settings.Data.RecentFiles = [.. RecentFiles];
                Settings.SaveSettings();

                RaisePropertyChanged(nameof(RecentFilesVis));

                await OpenFileImpl(fileName);
            }
        }

        public async void OpenFileExec(string fileName) => await OpenFileImpl(fileName);

        private Task OpenFileImpl(string fileName)
        {
            if (fileName.EndsWith(".properties", StringComparison.OrdinalIgnoreCase))
                return OpenCrateFile(fileName);
            else
                return OpenBLBFile(fileName);
        }

        public void ExtractAllExec()
        {
            if (Root == null) return;

            var fbd = new System.Windows.Forms.FolderBrowserDialog();
            if (fbd.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

            ProgressDialogViewModel.RunOperation((progress, ct) =>
                FileManager.ExtractAll(fbd.SelectedPath, Root.Owner.GetAllChildren(), CurrentDir, progress, ct)
            );
        }

        public void CloseBLBExec()
        {
            cancelLastTask?.Cancel();

            Status = "Start by opening a blb file. Press 'File->Open' and navigate to the assets directory of the game";
            Pages.Clear();
            CurrentDir = "";
            FileStatus = "";

            if (Root != null)
            {
                Bundles = null;
                FileEntries = null;
                Root = null;
                DB = null;
                PackageHeaders.Clear();
                CrateFiles.Clear();
                ToRender.Clear();
                FoldersToRender.Clear();
                SelectedBundles.Clear();
                RawFiles.Clear();
                GC.Collect();
                OpenFindDialog.RaiseCanExecuteChanged();
                ExtractAll.RaiseCanExecuteChanged();
                CloseBLB.RaiseCanExecuteChanged();
                OpenBundleSelectorDialog.RaiseCanExecuteChanged();
            }
        }
        #endregion

        private (Stopwatch timer, CancellationTokenSource cancelSource) BeginLoad()
        {
            Stopwatch timer = new();
            timer.Start();

            CloseBLBExec();

            CancellationTokenSource cancelSource = new();
            cancelLastTask = cancelSource;

            CloseBLB.RaiseCanExecuteChanged();

            return (timer, cancelSource);
        }

        private bool FinishLoad(Stopwatch timer, CancellationTokenSource cancelSource, IEnumerable<Idstring> bundleKeys)
        {
            timer.Stop();

            if (cancelSource.IsCancellationRequested)
                return false;

            cancelLastTask = null;

            OpenFindDialog.RaiseCanExecuteChanged();
            ExtractAll.RaiseCanExecuteChanged();
            OpenBundleSelectorDialog.RaiseCanExecuteChanged();
            CloseBLB.RaiseCanExecuteChanged();

            Bundles = [.. bundleKeys];
            FoldersToRender.Clear();
            FoldersToRender.Add(Root);

            Status = $"Done. Took {timer.ElapsedMilliseconds / 1000} seconds";
            return true;
        }

        private void CommitFileEntries(CancellationTokenSource cancelSource)
        {
            foreach (var fe in FileEntries.Values)
            {
                if (cancelSource.IsCancellationRequested)
                    return;

                fe.LoadPath();
                RawFiles.Add(new Tuple<Idstring, Idstring, Idstring>(fe.PathIds, fe.LanguageIds, fe.ExtensionIds), fe);
                Root?.Owner.AddFileEntry(fe);
            }
        }

        public async Task OpenBLBFile(string filePath)
        {
            var (timer, CancelSource) = BeginLoad();

            await Task.Run(() =>
            {
                if (CancelSource.IsCancellationRequested)
                    return;

                //Create the root tree entry, here all other folders will reside.
                Root = new TreeEntryViewModel(this, new FolderEntry { EntryPath = "", Name = "assets" });

                Status = "Preparing to open blb file...";
                AssetsDir = Path.GetDirectoryName(filePath);
                Status = "Reading blb file";

                DB = new PackageDatabase(filePath);
                Status = "Getting bundle headers";

                List<string> Files = Directory.EnumerateFiles(AssetsDir, "*.bundle").ToList();

                var entries = DB.GetDatabaseEntries();
                FileEntries = DatabaseEntryToFileEntry(entries);

                bool foundHashlist = false;

                List<string> FilterFiles = [];
                for (int i = 0; i < Files.Count; i++)
                {
                    if (CancelSource.IsCancellationRequested)
                        return;

                    string file = Files[i];
                    if (!file.EndsWith("_h.bundle"))
                        FilterFiles.Add(file);
                }
                for (int i = 0; i < FilterFiles.Count; i++)
                {
                    if (CancelSource.IsCancellationRequested)
                        return;

                    string file = FilterFiles[i];

                    Status = string.Format("Loading bundle {0} {1}/{2}", file, i, FilterFiles.Count);

                    PackageHeader bundle = new();
                    if (!bundle.Load(file))
                        continue;

                    foreach (PackageFileEntry be in bundle.Entries)
                    {
                        if (CancelSource.IsCancellationRequested)
                            return;

                        if (!foundHashlist)
                        {
                            DatabaseEntry ne = DB.EntryFromID(be.ID);
                            if (ne._path == 0x9234DD22C60D71B8)
                            {
                                Console.WriteLine("Found hashlist, loading...");
                                General.ReadHashlistAndLoad(file, be);
                                foundHashlist = true;
                            }
                        }

                        if (FileEntries.TryGetValue(be.ID, out FileEntry value))
                        {
                            FileEntry fileEntry = value;
                            fileEntry.AddBundleEntry(be);
                            FolderEntry folderEntry = fileEntry.Parent;
                        }
                    }

                    PackageHeaders.Add(bundle.Name, bundle);
                }

                CommitFileEntries(CancelSource);

                GC.Collect();
            });

            if (!FinishLoad(timer, CancelSource, PackageHeaders.Keys))
                return;

            //Finally, render the items.
            RenderNewItems();
        }

        /// <summary>
        /// Opens a directory of self-contained .crate packages. filePath is
        /// the crates.properties file (used both to locate the assets
        /// directory and for its variant bitflag table) -- each .crate's own
        /// TOC already carries its entries' hashes, so no other external
        /// index is needed.
        /// </summary>
        public async Task OpenCrateFile(string filePath)
        {
            var (timer, CancelSource) = BeginLoad();

            await Task.Run(() =>
            {
                if (CancelSource.IsCancellationRequested)
                    return;

                //Create the root tree entry, here all other folders will reside.
                Root = new TreeEntryViewModel(this, new FolderEntry { EntryPath = "", Name = "assets" });

                Status = "Preparing to open crate assets...";
                AssetsDir = Path.GetDirectoryName(filePath);

                string hashlistPath = Path.Combine(AssetsDir, "hashlist");
                if (File.Exists(hashlistPath))
                {
                    Status = "Loading hashlist";
                    HashIndex.Load(hashlistPath);
                }

                // VariantFlag -> resolved name (e.g. "english", "d3d11"), from crates.properties.
                Dictionary<ulong, Idstring> variantNames = [];
                var props = new CratePropertyList(filePath);
                foreach (var prop in props.Properties)
                    variantNames[prop.Flag] = prop.Name;

                List<string> Files = Directory.EnumerateFiles(AssetsDir, "*.crate").ToList();

                // Keyed by (ext, path, variant) so each language/platform variant gets its own FileEntry.
                Dictionary<(ulong ext, ulong path, ulong variant), FileEntry> entryLookup = [];

                for (int i = 0; i < Files.Count; i++)
                {
                    if (CancelSource.IsCancellationRequested)
                        return;

                    string file = Files[i];
                    string crateName = Path.GetFileNameWithoutExtension(file);

                    Status = string.Format("Loading crate {0} {1}/{2}", crateName, i, Files.Count);

                    CrateFile crate;
                    try
                    {
                        crate = new CrateFile(file);
                    }
                    catch (Exception exc)
                    {
                        Console.WriteLine("Failed to load crate {0}: {1}", file, exc.Message);
                        continue;
                    }

                    // .crate filenames are always the literal on-disk name, unlike bundle names.
                    Idstring crateIds = new(crateName, true);

                    foreach (CrateFileEntry ce in crate.Entries)
                    {
                        if (CancelSource.IsCancellationRequested)
                            return;

                        var key = (ce.Extension.Hashed, ce.Path.Hashed, ce.VariantFlag);
                        if (!entryLookup.TryGetValue(key, out FileEntry fe))
                        {
                            variantNames.TryGetValue(ce.VariantFlag, out Idstring variantName);
                            fe = new FileEntry
                            {
                                PathIds = ce.Path,
                                ExtensionIds = ce.Extension,
                                LanguageIds = ce.VariantFlag != 0 ? variantName : null,
                            };
                            entryLookup[key] = fe;
                        }

                        fe.AddCrateEntry(crateIds, ce);
                    }

                    CrateFiles.Add(crateIds, crate);
                }

                FileEntries = entryLookup.Values
                    .Select((fe, id) => (fe, id))
                    .ToDictionary(t => (uint)t.id, t => t.fe);

                CommitFileEntries(CancelSource);

                GC.Collect();
            });

            if (!FinishLoad(timer, CancelSource, CrateFiles.Keys))
                return;

            //Finally, render the items.
            RenderNewItems();
        }

        public void UpdateFileStatus()
        {
            string newStatus = ToRender.Count + " Items |";
            ulong totalSize = 0;
            uint totalSelected = 0;
            string size = "";

            foreach (var entry in ToRender)
            {
                if (entry.IsSelected)
                {
                    if (entry.IsFolder)
                        totalSize += (entry.Owner as FolderEntry).TotalSize;
                    else
                        totalSize += entry.Owner.Size;

                    totalSelected++;
                }
            }

            if (totalSize > 0)
                size = Utils.FriendlySize(totalSize);

            FileStatus = newStatus + $" {totalSelected} items selected {size} |";
        }

        public void SetDir(LinkedListNode<PageData> dir)
        {
            SetProperty(ref CurrentPage, dir, nameof(CurrentDir));
            DirChanged();
        }

        public void DirChanged()
        {
            BackDir.RaiseCanExecuteChanged();
            ForwardDir.RaiseCanExecuteChanged();
            RenderNewItems();
        }

        public void Navigate(string dir) => Navigate(new PageData(dir));
         
        public void Navigate(PageData dir)
        {
            if (CurrentPage != null && CurrentPage.Next != null)
            {
                var node = CurrentPage.Next;
                while(node != null) {
                    var next = node.Next;
                    Pages.Remove(node);
                    node = next;
                }
            }
            SetDir(Pages.AddLast(dir));
        }

        public void ForwardDirExec()
        {
            if (CurrentPage.Next != null)
                SetDir(CurrentPage.Next);
        }

        public void BackDirExec()
        {
            var next = CurrentPage.Previous;
            if (next != null)
                SetDir(next);
        }

        public void RenderNewItems()
        {
            if (ToRender == null || Root == null)
                return;

            ToRender.Clear();

            if (cancelLastTask == null)
            {
                List<FileEntry> files = [];
                List<FolderEntry> folders = [];

                PageData page = CurrentPage.Value;

                if (string.IsNullOrEmpty(page.Search))
                    Root.Owner.ForEachEntryInDirectory(CurrentDir, entry => CheckRenderEntry(entry, files, folders));
                else
                    Root.Owner.ForEachEntry(entry => CheckRenderEntry(entry, files, folders));

                folders.Sort(SortEntry);
                files.Sort(SortEntry);

                List<EntryViewModel> list = [];
                bool firstFiles = page.SortBy != Sorting.Type && !page.Ascending;
                if (firstFiles)
                {
                    foreach (var entry in files)
                    {
                        ToRender.Add(new EntryViewModel(entry));
                    }
                }
  
                foreach (var entry in folders)
                {
                    ToRender.Add(new EntryViewModel(entry));
                }
                if(!firstFiles)
                {
                    foreach (var entry in files)
                    {
                        ToRender.Add(new EntryViewModel(entry));
                    }
                }
                Root.CheckExpands();
            }
            UpdateFileStatus();
        }

        public void CheckRenderEntry(IEntry entry, List<FileEntry> files, List<FolderEntry> folders)
        {
            PageData page = CurrentPage.Value;
            string search = page.Search;

            if (SelectedBundles.Count > 0 && !entry.InBundles(SelectedBundles))
                return;

            bool found = string.IsNullOrEmpty(page.Search);
            if (!found)
            {
                string searchUsing;
                if (page.FullPath)
                    searchUsing = entry.EntryPath;
                else
                    searchUsing = entry.Name;

                if (page.UseRegex)
                    found = Regex.IsMatch(searchUsing, search);
                else if (page.MatchWord)
                    found = searchUsing == search;
                else
                    found = searchUsing.Contains(search);
            }

            if (found)
            {
                if (entry is FileEntry file && file.HasData())
                    files.Add(file);
                else if (entry is FolderEntry folder && folder.HasVisibleFiles())
                    folders.Add(folder);
            }
        }

        public int SortEntry(IEntry a, IEntry b)
        {
            bool asc = CurrentPage.Value.Ascending;
            Sorting sortBy = CurrentPage.Value.SortBy;

            if (!asc)
            {
                (b, a) = (a, b);
            }

            int check = sortBy switch
            {
                Sorting.Name => a.Name.CompareTo(b.Name),
                Sorting.Type => a.Type.CompareTo(b.Type),
                Sorting.Size => a.Size.CompareTo(b.Size),
                _ => 0
            };
            if (check == 0 && sortBy != Sorting.Name)
                return a.Name.CompareTo(b.Name);

            return check;
        }

        public Dictionary<uint, FileEntry> DatabaseEntryToFileEntry(List<DatabaseEntry> entries)
        {
            Dictionary<uint, FileEntry> fileEntries = [];

            foreach (DatabaseEntry ne in entries)
            {
                FileEntry fe = new(ne);
                fileEntries.Add(ne.ID, fe);
            }
            return fileEntries;
        }

        public bool IsSelectingMultiple()
        {
            bool foundOne = false;
            foreach (var entry in ToRender)
            {
                if (entry.IsSelected)
                {
                    if (!foundOne)
                        foundOne = true;
                    else
                        return true;
                }
            }
            return false;
        }

        public List<EntryViewModel> SelectedEntries()
        {
            List<EntryViewModel> list = [];
            foreach(var entry in ToRender)
            {
                if(entry.IsSelected)
                    list.Add(entry);
            }
            return list;
        }
    }
}
