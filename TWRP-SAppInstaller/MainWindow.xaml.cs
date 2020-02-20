using ApkParser;
using APKParser = ApkParser;
using MahApps.Metro.Controls;
using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

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
            string installer = new StreamReader(GetResource("system.system.installer.sh")).ReadToEnd();

            string CP = "", CHMOD = "";
            int itemsCount = listView.Items.Count;

            Dispatcher.Invoke(() =>
            {
                progressValue.Value = 0;
                progressValue.Maximum = itemsCount;
            });
            foreach (ListViewItemsData item in listView.Items) // Modify Installer Files
            {
                string itemFolder = item.Path.EndsWith("/") ? itemFolder = item.Path.Remove(item.Path.Length - 1) : item.Path;
                itemFolder = itemFolder.Substring(itemFolder.LastIndexOf('/')).Replace("/", "");
                
                CP += $"cp -r /tmp/install/system/{itemFolder}/ {item.Path}{Environment.NewLine}";
                CHMOD += $"chmod 755 {item.Path}{Environment.NewLine}chmod 644 {item.Path + itemFolder + ".apk" + Environment.NewLine}";

                Dispatcher.Invoke(() =>
                {
                    progressValue.Value++;
                    progressText.Text = $"Modifying Apps {progressValue.Value}/{itemsCount}";
                });
            }
            installer = installer.Replace("{{CP}}", CP).Replace("{{CHMOD}}", CHMOD);

            string[] fileNames = { "/system/busybox", "/system/system/configure.sh", "/META-INF/com/google/android/update-binary" };

            // Make Temp Path
            string Folder = GetTemporaryDirectory();

            // Write Installer - Updater
            new FileInfo(Folder + @"\system\system\installer.sh").Directory.Create();
            new FileInfo(Folder + @"\META-INF\com\google\android\updater-script").Directory.Create();
            File.WriteAllText(Folder + @"\system\system\installer.sh", installer);
            File.WriteAllText(Folder + @"\META-INF\com\google\android\updater-script", updater);

            Dispatcher.Invoke(() =>
            {
                progressValue.Value = 0;
                itemsCount = fileNames.Length;
                progressValue.Maximum = itemsCount;
            });

            foreach (var file in fileNames) // Write Installer Files
            {
                using (var fileStream = File.Create(Folder + "\\" + file))
                {
                    GetResource(file.Substring(1).Replace('/', '.').Replace("META-INF", "META_INF")).Seek(0, SeekOrigin.Begin);
                    GetResource(file.Substring(1).Replace('/', '.').Replace("META-INF", "META_INF")).CopyTo(fileStream);
                }
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

            foreach (ListViewItemsData item in listView.Items) // Write APK Files
            {
                string itemFolder = item.Path.EndsWith("/") ? itemFolder = item.Path.Remove(item.Path.Length - 1) : item.Path;
                itemFolder = itemFolder.Substring(itemFolder.LastIndexOf('/')).Replace("/", "");
                new FileInfo(Folder + $@"\system\system\{itemFolder}\{itemFolder}.apk").Directory.Create();
                File.Copy(item.ApkFile, Folder + $@"\system\system\{itemFolder}\{itemFolder}.apk");
                Dispatcher.Invoke(() =>
                {
                    progressValue.Value++;
                    progressText.Text = $"Adding Apps {progressValue.Value}/{itemsCount}";
                });
            }
            // Make ZIP Output & Remove Temp Directory
            ZipFile.CreateFromDirectory(Folder, outFileName, CompressionLevel.NoCompression, false);
            Thread.Sleep(500);
            Directory.Delete(Folder, true);
        }

        private Stream GetResource(string name)
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "TWRP_SAppInstaller.Resources.Sample." + name;
            return assembly.GetManifestResourceStream(resourceName);
        }

        private string GetResourcesNames()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var a = "";
            foreach (var b in assembly.GetManifestResourceNames())
            {
                a += b + "\r\n";
            }
            return a;
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

        public string GetTemporaryDirectory()
        {
            string tempDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName().Replace(".", string.Empty));
            Directory.CreateDirectory(tempDirectory);
            return tempDirectory;
        }
    }
}
