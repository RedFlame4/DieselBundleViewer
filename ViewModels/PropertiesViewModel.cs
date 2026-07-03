using DieselBundleViewer.Models;
using DieselEngineFormats.Bundle;
using DieselEngineFormats.Crate;
using Prism.Dialogs;
using Prism.Mvvm;
using System.Collections.ObjectModel;
using DieselBundleViewer.Services;

namespace DieselBundleViewer.ViewModels
{
    // Common shape for anything shown in the Properties dialog's Bundles/Crates grid.
    public interface IPackageEntryViewModel
    {
        string Name { get; }
        string HashedName { get; }
        string Size { get; }
        ulong Address { get; }
        ulong Length { get; }
    }

    public class PackageFileViewModel(PackageFileEntry entry) : BindableBase, IPackageEntryViewModel
    {
        private PackageFileEntry _entry = entry;

        public string Name => _entry.PackageName.UnHashed;
        public string HashedName => _entry.PackageName.HashedString;
        public string Size => Utils.FriendlySize((ulong)_entry.Length);
        public ulong Address => _entry.Address;
        public ulong Length => (ulong)_entry.Length;
    }

    public class CrateFileViewModel(Idstring crateName, CrateFileEntry entry) : BindableBase, IPackageEntryViewModel
    {
        private readonly CrateFileEntry _entry = entry;

        public string Name => crateName.UnHashed;
        public string HashedName => crateName.HashedString;
        public string Size => Utils.FriendlySize(_entry.RawSize);
        public ulong Address => _entry.Offset;
        public ulong Length => _entry.RawSize;
    }

    public class PropertiesViewModel : DialogBase
    {
        public override string Title => entryVM.Name + " Properties";

        private EntryViewModel entryVM;

        public ObservableCollection<IPackageEntryViewModel> Bundles { get; }
        public bool FolderVisibility => (entryVM != null && entryVM.IsFolder);
        public bool FileVisibility => !FolderVisibility;

        public string FolderContains
        {
            get
            {
                if (entryVM == null || !entryVM.IsFolder)
                    return "";

                var children = (entryVM.Owner as FolderEntry).GetAllChildren();
                uint files = 0;
                uint folders = 0;
                foreach(var child in children)
                {
                    if (child is FileEntry)
                        files++;
                    else
                        folders++;
                }
                return $"{files} Files, {folders} Folders";
            }
        }
        public string Icon => entryVM?.Icon;
        public string Name => entryVM?.Name;
        public string Type => entryVM?.Type;
        public string Size { 
            get
            {
                if (entryVM == null)
                    return "";

                if (entryVM.IsFolder)
                    return Utils.FriendlySize((entryVM.Owner as FolderEntry).TotalSize);
                else
                    return entryVM?.FriendlySize;
            }
        }
        public string EntryPath => entryVM?.EntryPath;
        public string HashedName
        {
            get {
                if (entryVM == null || entryVM.Owner is FolderEntry)
                    return "";
                else
                    return (entryVM.Owner as FileEntry).PathIds.HashedString;
            }
        }

        public PropertiesViewModel() : base()
        {
            Bundles = new ObservableCollection<IPackageEntryViewModel>();
        }

        protected override void PostDialogOpened(IDialogParameters pms)
        {
            entryVM = pms.GetValue<EntryViewModel>("Entry");
            RaisePropertyChanged(nameof(FileVisibility));
            RaisePropertyChanged(nameof(FolderVisibility));
            RaisePropertyChanged(nameof(Name));
            RaisePropertyChanged(nameof(Icon));
            RaisePropertyChanged(nameof(Type));
            RaisePropertyChanged(nameof(Size));
            RaisePropertyChanged(nameof(EntryPath));
            RaisePropertyChanged(nameof(HashedName));
            RaisePropertyChanged(nameof(FolderContains));

            if (entryVM.Owner is FileEntry fileEntry)
            {
                foreach(var pair in fileEntry.BundleEntries)
                    Bundles.Add(new PackageFileViewModel(pair.Value));

                if (fileEntry.CrateEntry != null)
                    Bundles.Add(new CrateFileViewModel(fileEntry.CrateName, fileEntry.CrateEntry));
            }
        }
    }
}
