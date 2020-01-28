using ApkParser;
using ICSharpCode.SharpZipLib.Core;
using ICSharpCode.SharpZipLib.Zip;
using MahApps.Metro.Controls;
using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using APKParser = ApkParser;

namespace TWRP_SAppInstaller
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : MetroWindow
    {
        public class ListViewItemsData
        {
            public string Image { get; set; }
            public string Package { get; set; }
            public string Title { get; set; }
            public string Version { get; set; }
            public string Path { get; set; }
            public string ApkFile { get; set; }
        }
        private OpenFileDialog openApkFile = new OpenFileDialog()
        {
            Title = "Select your app",
            DefaultExt = ".APK",
            Filter = "APK Files|*.apk"
        };
        private SaveFileDialog saveOutput = new SaveFileDialog()
        {
            Title = "Select output path",
            DefaultExt = ".zip",
            Filter = "ZIP Files|*.zip"
        };
        private BackgroundWorker worker = new BackgroundWorker();
        public ObservableCollection<ListViewItemsData> ListViewItemsCollections { get; } = new ObservableCollection<ListViewItemsData>();
        private ApkInfo Info = null;
        private bool IsSavable = false;
        public MainWindow()
        {
            InitializeComponent();
            openApkFile.FileOk += LoadedAPKFile;
            saveOutput.FileOk += SelectOutFile;
            worker.DoWork += Work;
            worker.RunWorkerCompleted += (s, e) => {
                Dispatcher.Invoke(() =>
                {
                    progressText.Text = "Finished !";
                });
            };
        }

        private void Contact(object sender, RoutedEventArgs e) => Process.Start("https://kazem_ma79.github.io/");

        private void Work(object sender, DoWorkEventArgs e) => Make(saveOutput.FileName);

        private void RemoveApp(object sender, RoutedEventArgs e) => listView.Items.Remove((sender as Button).Tag);

        private void AppSelectBtn_Click(object sender, RoutedEventArgs e) => openApkFile.ShowDialog();

        private void SaveBtn_Click(object sender, RoutedEventArgs e) => saveOutput.ShowDialog();

        private void SelectOutFile(object sender, CancelEventArgs e) => IsSavable = true;

        private void LoadedAPKFile(object sender, CancelEventArgs e)
        {
            ApkPath.Text = openApkFile.FileName;
            try
            {
                Info = APKParser.ApkParser.Parse(openApkFile.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Can't read apk data");
            }
            AppDetails.Content = $"Title: {Info.Label + Environment.NewLine}" +
                $"Package Name: {Info.PackageName + Environment.NewLine}" +
                $"Version: {Info.VersionName + Environment.NewLine}";
        }

        private void AppAddBtn_Click(object sender, RoutedEventArgs e)
        {
            #region Input Validation
            if (ApkPath.Text != openApkFile.FileName)
            {
                MessageBox.Show("Please select an apk file!");
                return;
            }
            try
            {
                if (AppExists(Info.PackageName, AppPath.Text))
                {
                    MessageBox.Show("App already selected!");
                    return;
                }
            }
            catch(Exception ex)
            {
                MessageBox.Show(ex.Message);
                return;
            }
            AppPath.Text = AppPath.Text.Trim();
            #endregion

            listView.Items.Add(new ListViewItemsData()
            {
                Image = Info.Icons[0].ToString(),
                Package = Info.PackageName,
                Title = Info.Label,
                Path = AppPath.Text,
                Version = Info.VersionName,
                ApkFile = ApkPath.Text
            });
        }

        private bool AppExists(string PackageName, string Path)
        {
            foreach (ListViewItemsData item in listView.Items)
            {
                if (item.Package == PackageName && item.Path == Path) return true;
            }
            return false;
        }

        private void Make(string outFileName)
        {
            string updater = new StreamReader(GetResource("META_INF.com.google.android.updater-script")).ReadToEnd();
            string installer = new StreamReader(GetResource("install.bin.installer.sh")).ReadToEnd();

            string PKG_APPS = "", CP = "", CHMOD = "";
            int itemsCount = listView.Items.Count;

            Dispatcher.Invoke(() =>
            {
                progressValue.Value = 0;
                progressValue.Maximum = itemsCount;
            });
            foreach (ListViewItemsData item in listView.Items)
            {
                string itemFolder = item.Path.EndsWith("/") ? itemFolder = item.Path.Remove(item.Path.Length - 1) : item.Path;
                itemFolder = itemFolder.Substring(itemFolder.LastIndexOf('/')).Replace("/", "");

                PKG_APPS += $"ui_print(\"* {item.Title} => {item.Package} *\");{Environment.NewLine}";
                CP += $"cp -r /tmp/install/bin/{itemFolder} {item.Path}{Environment.NewLine}";
                CHMOD += $"chmod 755 {item.Path}{Environment.NewLine}chmod 644 {item.Path + itemFolder + ".apk" + Environment.NewLine}";

                Dispatcher.Invoke(() =>
                {
                    progressValue.Value++;
                    progressText.Text = $"Modifying Apps {progressValue.Value}/{itemsCount}";
                });
            }
            PKG_APPS += "ui_print(\"*****************************************\");";
            updater = updater.Replace("{{PKG_APPS}}", PKG_APPS);
            installer = installer.Replace("{{CP}}", CP).Replace("{{CHMOD}}", CHMOD);

            string[] fileNames = { "/install/bin/busybox", "/install/bin/configure.sh", "/META-INF/CERT.RSA", "/META-INF/CERT.SF", "/META-INF/MANIFEST.MF", "/META-INF/com/google/android/update-binary" };
            using (var fs = File.Create(outFileName))
            using (var outStream = new ZipOutputStream(fs))
            {
                //Installer
                outStream.PutNextEntry(new ZipEntry("/install/bin/installer.sh"));
                var buffer = new byte[4096];
                StreamUtils.Copy(GenerateStreamFromString(installer), outStream, buffer);
                outStream.CloseEntry();

                //Updater
                outStream.PutNextEntry(new ZipEntry("/META-INF/com/google/android/updater-script"));
                buffer = new byte[4096];
                StreamUtils.Copy(GenerateStreamFromString(updater), outStream, buffer);
                outStream.CloseEntry();

                Dispatcher.Invoke(() =>
                {
                    progressValue.Value = 0;
                    itemsCount = fileNames.Length;
                    progressValue.Maximum = itemsCount;
                });
                foreach (var file in fileNames)
                {
                    outStream.PutNextEntry(new ZipEntry(file));
                    buffer = new byte[4096];
                    StreamUtils.Copy(GetResource(file.Substring(1).Replace('/', '.').Replace("META-INF", "META_INF")), outStream, buffer);
                    outStream.CloseEntry();
                    Dispatcher.Invoke(() =>
                    {
                        progressValue.Value++;
                        progressText.Text = $"Modifying Installer Files {progressValue.Value}/{itemsCount}";
                    });
                }

                Dispatcher.Invoke(() =>
                {
                    progressValue.Value = 0;
                    itemsCount = listView.Items.Count;
                    progressValue.Maximum = itemsCount;
                });
                foreach (ListViewItemsData item in listView.Items)
                {
                    string itemFolder = item.Path.EndsWith("/") ? itemFolder = item.Path.Remove(item.Path.Length - 1) : item.Path;
                    itemFolder = itemFolder.Substring(itemFolder.LastIndexOf('/')).Replace("/", "");
                    outStream.PutNextEntry(new ZipEntry($"/install/bin/{itemFolder}/{itemFolder}.apk"));
                    buffer = new byte[4096];
                    StreamUtils.Copy(File.OpenRead(item.ApkFile), outStream, buffer);
                    outStream.CloseEntry();
                    Dispatcher.Invoke(() =>
                    {
                        progressValue.Value++;
                        progressText.Text = $"Adding Apps {progressValue.Value}/{itemsCount}";
                    });
                }
            }
        }

        private Stream GetResource(string name)
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "TWRP_SAppInstaller.Resources.Sample." + name;

            return assembly.GetManifestResourceStream(resourceName);
        }

        private void Build(object sender, RoutedEventArgs e)
        {
            if (IsSavable)
            {
                if(!worker.IsBusy)
                    worker.RunWorkerAsync();
                else
                    MessageBox.Show("Working...");
            }
            else
                MessageBox.Show("Please select output file.");
        }

        public static Stream GenerateStreamFromString(string s)
        {
            var stream = new MemoryStream();
            var writer = new StreamWriter(stream);
            writer.Write(s);
            writer.Flush();
            stream.Position = 0;
            return stream;
        }
    }
}
