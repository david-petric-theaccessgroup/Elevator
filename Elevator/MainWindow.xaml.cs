using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Elevator
{
    public partial class MainWindow : Window
    {
        private const string CacheFileName = "shortcuts.json";
        private readonly string CacheFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Elevator", CacheFileName);
        public ObservableCollection<ShortcutItem> Shortcuts { get; set; } = new();
        private const string EpmClientPath = @"C:\Program Files\Microsoft EPM Agent\EPMClient\EpmClientStub.exe";

        public MainWindow()
        {
            InitializeComponent();
            LoadShortcuts();
            ShortcutsGrid.ItemsSource = Shortcuts;
        }

        private void LoadShortcuts()
        {
            try
            {
                if (File.Exists(CacheFilePath))
                {
                    var json = File.ReadAllText(CacheFilePath);
                    var items = JsonSerializer.Deserialize<List<ShortcutItem>>(json);
                    if (items != null)
                        foreach (var item in items)
                            Shortcuts.Add(item);
                }
                else
                {
                    Shortcuts.Add(new ShortcutItem { Path = @"C:\Program Files\Microsoft Visual Studio\2022\Professional\Common7\IDE\devenv.exe" });
                    Shortcuts.Add(new ShortcutItem { Path = "explorer.exe" });
                    Shortcuts.Add(new ShortcutItem { Path = @"C:\Windows\system32\inetsrv\inetmgr.exe" });
                }
            }
            catch (Exception ex)
            {
                ShowStatus($"Error loading shortcuts: {ex.Message}", isError: true);
            }
        }

        private void SaveShortcuts()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CacheFilePath)!);
                var json = JsonSerializer.Serialize(Shortcuts);
                File.WriteAllText(CacheFilePath, json);
                ShowStatus("Shortcuts saved successfully");
            }
            catch (Exception ex)
            {
                ShowStatus($"Error saving shortcuts: {ex.Message}", isError: true);
            }
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Title = "Select Program", Filter = "Executable Files (*.exe)|*.exe|All Files (*.*)|*.*" };
            if (dlg.ShowDialog() == true)
            {
                Shortcuts.Add(new ShortcutItem { Path = dlg.FileName });
                SaveShortcuts();
                ShowStatus($"Added shortcut for {dlg.FileName}");
            }
        }



        private void QuickLaunchButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is ShortcutItem item)
            {
                LaunchApplication(item);
            }
        }

        private void RunAsAdminMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (ShortcutsGrid.SelectedItem is ShortcutItem item)
            {
                LaunchApplication(item, runAsAdmin: true);
            }
        }

        private void RemoveMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (ShortcutsGrid.SelectedItem is ShortcutItem item)
            {
                var result = MessageBox.Show($"Are you sure you want to remove this shortcut?",
                    "Confirm Removal", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    Shortcuts.Remove(item);
                    SaveShortcuts();
                    ShowStatus("Shortcut removed");
                }
            }
        }

        private void LaunchApplication(ShortcutItem item, bool runAsAdmin = false)
        {
            try
            {
                // Check if the file to launch is 'elevate.exe' or any specific file that needs EPM
                if (Path.GetFileName(item.Path).Equals("elevate.exe", StringComparison.OrdinalIgnoreCase))
                {
                    LaunchViaEpmClient(item.Path, runAsAdmin);
                }
                else
                {
                    var startInfo = new ProcessStartInfo(item.Path)
                    {
                        UseShellExecute = true
                    };

                    if (runAsAdmin)
                    {
                        startInfo.Verb = "runas";
                    }

                    Process.Start(startInfo);
                    ShowStatus($"Started: {item.Path}" + (runAsAdmin ? " (as administrator)" : ""));
                }
            }
            catch (Exception ex)
            {
                ShowStatus($"Failed to launch: {ex.Message}", isError: true);
                MessageBox.Show($"Failed to launch: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LaunchViaEpmClient(string targetPath, bool runAsAdmin = false)
        {
            try
            {
                if (!File.Exists(EpmClientPath))
                {
                    ShowStatus($"EPM Client not found at: {EpmClientPath}", isError: true);

                    if (runAsAdmin)
                    {
                        // Fallback to direct launch with admin rights if EPM Client is not available
                        LaunchDirectlyAsAdmin(targetPath);
                        return;
                    }
                    else
                    {
                        MessageBox.Show($"EPM Client not found at expected location:\n{EpmClientPath}",
                            "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }

                // Check if target exists
                if (!File.Exists(targetPath))
                {
                    ShowStatus($"Target application not found: {targetPath}", isError: true);
                    MessageBox.Show($"Target application not found at:\n{targetPath}",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Create process start info for EPM client
                var startInfo = new ProcessStartInfo(EpmClientPath)
                {
                    UseShellExecute = true,
                    Arguments = $"\"{targetPath}\"",
                    // Do NOT use the "runas" verb with EPM client as it might cause conflicts
                    CreateNoWindow = false
                };

                Debug.WriteLine($"Launching via EPM: {EpmClientPath} with args: {startInfo.Arguments}");

                // Launch asynchronously to avoid UI freezing if there's an EPM dialog
                Task.Run(() =>
                {
                    try
                    {
                        var process = Process.Start(startInfo);

                        // Update UI on the main thread
                        Dispatcher.Invoke(() =>
                        {
                            ShowStatus($"Started {Path.GetFileName(targetPath)} via EPM Client");
                        });
                    }
                    catch (Exception ex)
                    {
                        // Handle errors on the UI thread
                        Dispatcher.Invoke(() =>
                        {
                            string errorMsg = ex.Message;

                            // Check for the specific EPM Client error
                            if (errorMsg.Contains("0x8000FFFF") || errorMsg.Contains("-2147418113"))
                            {
                                errorMsg = "EPM Client error (0x8000FFFF). This typically occurs due to permission issues.\n\n" +
                                    "The application will try to launch directly instead.";

                                ShowStatus($"EPM Client error - trying direct launch", isError: true);

                                // Try direct launch as fallback
                                if (runAsAdmin)
                                {
                                    LaunchDirectlyAsAdmin(targetPath);
                                }
                                else
                                {
                                    LaunchDirectly(targetPath);
                                }
                                return;
                            }

                            ShowStatus($"Failed to launch via EPM Client: {errorMsg}", isError: true);
                            MessageBox.Show($"Failed to launch via EPM Client:\n{errorMsg}",
                                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        });
                    }
                });
            }
            catch (Exception ex)
            {
                string errorMsg = ex.Message;

                // Check for the specific EPM Client error
                if (errorMsg.Contains("0x8000FFFF") || errorMsg.Contains("-2147418113"))
                {
                    // Try direct launch as fallback
                    if (runAsAdmin)
                    {
                        LaunchDirectlyAsAdmin(targetPath);
                    }
                    else
                    {
                        LaunchDirectly(targetPath);
                    }
                }
                else
                {
                    ShowStatus($"Failed to launch via EPM Client: {errorMsg}", isError: true);
                    MessageBox.Show($"Failed to launch via EPM Client: {errorMsg}",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void LaunchDirectly(string targetPath)
        {
            try
            {
                var startInfo = new ProcessStartInfo(targetPath)
                {
                    UseShellExecute = true
                };

                var process = Process.Start(startInfo);
                ShowStatus($"Started {Path.GetFileName(targetPath)} directly");
            }
            catch (Exception ex)
            {
                ShowStatus($"Failed to launch directly: {ex.Message}", isError: true);
                MessageBox.Show($"Failed to launch directly: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LaunchDirectlyAsAdmin(string targetPath)
        {
            try
            {
                var startInfo = new ProcessStartInfo(targetPath)
                {
                    UseShellExecute = true,
                    Verb = "runas"
                };

                var process = Process.Start(startInfo);
                ShowStatus($"Started {Path.GetFileName(targetPath)} directly as administrator");
            }
            catch (Exception ex)
            {
                ShowStatus($"Failed to launch as administrator: {ex.Message}", isError: true);
                MessageBox.Show($"Failed to launch as administrator: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ShowStatus(string message, bool isError = false)
        {
            StatusMessage.Text = message;
            StatusMessage.Foreground = isError ?
                System.Windows.Media.Brushes.Red :
                System.Windows.Media.Brushes.MediumOrchid;
        }
    }

    public class ShortcutItem
    {
        public string Path { get; set; } = string.Empty;
    }
}