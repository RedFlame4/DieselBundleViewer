using Prism.Ioc;
using DieselBundleViewer.Views;
using System.Windows;
using System.Runtime.InteropServices;
using DieselEngineFormats.Bundle;
using DieselBundleViewer.ViewModels;
using DieselBundleViewer.Services;
using System.IO;
using System;
using System.Diagnostics;
using DieselEngineFormats;
using DieselEngineFormats.ScriptData;
using System.Text;
using System.Collections.Generic;
using AdonisUI;
using System.Windows.Threading;

namespace DieselBundleViewer
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App
    {
        [DllImport("Kernel32")]
        public static extern void AllocConsole();

        [DllImport("Kernel32")]
        public static extern void FreeConsole();

        public App()
        {
#if !DEBUG
            Dispatcher.UnhandledException += OnException;
            if(File.Exists("debug"))
#endif
            AllocConsole();


            Console.WriteLine("Loading local hashlist");
            if (File.Exists("Data/hashlist"))
                HashIndex.LoadParallel("Data/hashlist");
            else
                Console.WriteLine("Local hashlist is missing!");

            LoadConverters();
        }

        void OnException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show($"An error has occurred: \n {e.Exception.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        protected override void RegisterTypes(IContainerRegistry containerRegistry)
        {
            containerRegistry.RegisterDialog<ConvertFileDialog, ConvertFileDialogViewModel>();
            containerRegistry.RegisterDialog<UpdateHashlistDialog, UpdateHashlistDialogViewModel>();
            containerRegistry.RegisterDialog<AboutDialog, AboutDialogViewModel>();
            containerRegistry.RegisterDialog<SettingsDialog, SettingsDialogViewModel>();
            containerRegistry.RegisterDialog<FindDialog, FindDialogViewModel>();
            containerRegistry.RegisterDialog<BundleSelectorDialog, BundleSelectorDialogViewModel>();
            containerRegistry.RegisterDialog<PropertiesDialog, PropertiesViewModel>();
            containerRegistry.RegisterDialog<ProgressDialog, ProgressDialogViewModel>();

            containerRegistry.RegisterDialogWindow<DialogWindow>();
        }

        private void LoadConverters()
        {
            ScriptActions.AddConverter(new FormatConverter
            {
                Key = "script_cxml",
                Title = "Custom XML",
                Extension = "xml",
                ExportEvent = (MemoryStream ms, bool escape) =>
                {
                    try
                    {
                        Dictionary<string, object> root = new ScriptData(new BinaryReader(ms), Utils.IsRaid()).Root;
                        return new CustomXMLNode("table", root, "").ToString(0, escape);
                    } catch (Exception e)
                    {
                        MessageBox.Show($"Failed to read scriptdata: \n {e.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return null;
                    }
                },
                Type = "scriptdata"
            });

            //Temporary until I get source code of this DLL, hopefully.
            ScriptActions.AddConverter(new FormatConverter
            {
                Key = "stream",
                Title = "Stream to Wav",
                RequiresAttention = false,
                Extension = "wav",
                Type = "stream",
                SaveEvent = (Stream stream, string toPath) =>
                {
                    // PD2 stream/bnk audio is Wwise-encoded (IMA ADPCM or Vorbis) at
                    // varying sample rates. The bundled Wwise Sound Library mis-decodes
                    // some of these (e.g. 32kHz gun SFX), so route through vgmstream-cli,
                    // which reads the real format from each WEM header and decodes correctly.
                    // Resolved relative to the app's own directory (the build copies
                    // vgmstream-win64 into the output folder), so this works on any
                    // machine/clone without a hardcoded absolute path.
                    string vgmstreamPath = Path.Combine(AppContext.BaseDirectory, "vgmstream-win64", "vgmstream-cli.exe");

                    if (!File.Exists(vgmstreamPath))
                    {
                        MessageBox.Show($"Could not find vgmstream-cli.exe at:\n{vgmstreamPath}", "Sound export error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    // vgmstream selects its parser by file extension, so hand the raw
                    // Wwise bytes over as a .wem file.
                    string tempWem = Path.Combine(Path.GetTempPath(), "dbv_" + Guid.NewGuid().ToString("N") + ".wem");
                    try
                    {
                        using (FileStream tmp = new FileStream(tempWem, FileMode.Create, FileAccess.Write))
                        {
                            stream.Position = 0;
                            stream.CopyTo(tmp);
                        }

                        string outDir = Path.GetDirectoryName(toPath);
                        if (!string.IsNullOrEmpty(outDir))
                            Directory.CreateDirectory(outDir);

                        ProcessStartInfo psi = new ProcessStartInfo
                        {
                            FileName = vgmstreamPath,
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardError = true,
                        };
                        psi.ArgumentList.Add("-o");
                        psi.ArgumentList.Add(toPath);
                        psi.ArgumentList.Add(tempWem);

                        using Process proc = Process.Start(psi);
                        string stderr = proc.StandardError.ReadToEnd();
                        proc.WaitForExit();

                        if (proc.ExitCode != 0 || !File.Exists(toPath))
                            Console.WriteLine($"vgmstream failed for {toPath}: {stderr}");
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"Error converting sound to {toPath}: {e.Message}");
                    }
                    finally
                    {
                        if (File.Exists(tempWem))
                        {
                            try { File.Delete(tempWem); } catch { }
                        }
                    }
                },
            });

            ScriptActions.AddConverter(new FormatConverter
            {
                Key = "diesel_strings",
                Title = "Diesel",
                Extension = "strings",
                ImportEvent = (path) => new StringsFile(path),
                Type = "strings"
            });

            ScriptActions.AddConverter(new FormatConverter
            {
                Key = "movie",
                Title = "Bink Video",
                Extension = "bik",
                Type = "movie",
                RequiresAttention = false
            });

            //Loop each XML format to have it automatically get .xml suffix

            ScriptActions.AddConverter(new FormatConverter
            {
                Key = "xmL_conversion",
                Type = "text",
                Extension = "xml",
                RequiresAttention = false
            });

            ScriptActions.AddConverter(new FormatConverter
            {
                Key = "texture_dds",
                Title = "DDS",
                Extension = "dds",
                Type = "texture",
                RequiresAttention = false
            });

            ScriptActions.AddConverter(new FormatConverter
            {
                Key = "strings_csv",
                Title = "CSV",
                Extension = "csv",
                ExportEvent = (MemoryStream ms, bool arg0) =>
                {
                    //Excel doesn't seem to like it?
                    StringsFile str = new StringsFile(ms);
                    StringBuilder builder = new StringBuilder();
                    builder.Append("ID,String\n");
                    foreach (var entry in str.LocalizationStrings)
                        builder.Append("\"" + entry.ID.ToString() + "\",\"" + entry.Text + "\"\n");
                    Console.WriteLine(builder.ToString());
                    return builder.ToString();
                },
                Type = "strings"
            });

            ScriptActions.AddConverter(new FormatConverter
            {
                Key = "script_json",
                Title = "JSON",
                Extension = "json",
                ExportEvent = (MemoryStream ms, bool arg0) =>
                {
                    try
                    {
                        ScriptData sdata = new ScriptData(new BinaryReader(ms), Utils.IsRaid());
                        return (new JSONNode("table", sdata.Root, "")).ToString();
                    }
                    catch (Exception e)
                    {
                        MessageBox.Show($"Failed to read scriptdata: \n {e.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return null;
                    }
                },
                Type = "scriptdata"
            });

            ScriptActions.AddConverter(new FormatConverter
            {
                Key = "strings_json",
                Title = "JSON",
                Extension = "json",
                ExportEvent = (MemoryStream ms, bool arg0) =>
                {
                    StringsFile str = new StringsFile(ms);
                    StringBuilder builder = new StringBuilder();
                    builder.Append("{\n");
                    for (int i = 0; i < str.LocalizationStrings.Count; i++)
                    {
                        StringEntry entry = str.LocalizationStrings[i];
                        builder.Append('\t');
                        builder.Append("\"" + entry.ID + "\" : \"" + entry.Text + "\"");
                        if (i < str.LocalizationStrings.Count - 1)
                            builder.Append(',');
                        builder.Append('\n');
                    }
                    builder.Append('}');
                    Console.WriteLine(builder.ToString());
                    return builder.ToString();
                },
                Type = "strings"
            });
        }

        protected override Window CreateShell()
        {
            return Container.Resolve<MainWindow>();
        }
    }
}
