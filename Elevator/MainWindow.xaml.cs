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

namespace Elevator
{
    public partial class MainWindow : Window
    {
        private const string CacheFileName = "shortcuts.json";
        private readonly string CacheFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Elevator", CacheFileName);
        public ObservableCollection<ShortcutItem> Shortcuts { get; set; } = new();

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
                    // Default shortcuts
                    Shortcuts.Add(new ShortcutItem { Key = "v", Path = @"C:\Program Files\Microsoft Visual Studio\2022\Professional\Common7\IDE\devenv.exe" });
                    Shortcuts.Add(new ShortcutItem { Key = "e", Path = @"C:\Program Files\Microsoft Visual Studio\2022\Enterprise\Common7\IDE\devenv.exe" });
                    Shortcuts.Add(new ShortcutItem { Key = "x", Path = "explorer.exe" });
                    Shortcuts.Add(new ShortcutItem { Key = "i", Path = @"C:\Windows\system32\inetsrv\inetmgr.exe" });
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
                var key = PromptForKey();
                if (!string.IsNullOrWhiteSpace(key) && !ShortcutsExists(key))
                {
                    Shortcuts.Add(new ShortcutItem { Key = key, Path = dlg.FileName });
                    SaveShortcuts();
                    ShowStatus($"Added shortcut '{key}' for {dlg.FileName}");
                }
                else
                {
                    ShowStatus("Key already exists or is invalid.", isError: true);
                    MessageBox.Show("Key already exists or invalid.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private bool ShortcutsExists(string key) => Shortcuts.Any(s => s.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

        private string? PromptForKey()
        {
            var dialog = new InputDialog("Enter shortcut key (single letter):");
            if (dialog.ShowDialog() == true)
                return dialog.ResponseText.Trim();
            return null;
        }

        private void LaunchButton_Click(object sender, RoutedEventArgs e)
        {
            LaunchSelectedItem();
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
                var result = MessageBox.Show($"Are you sure you want to remove the shortcut for '{item.Key}'?", 
                    "Confirm Removal", MessageBoxButton.YesNo, MessageBoxImage.Question);
                
                if (result == MessageBoxResult.Yes)
                {
                    Shortcuts.Remove(item);
                    SaveShortcuts();
                    ShowStatus($"Removed shortcut '{item.Key}'");
                }
            }
        }
        
        private void LaunchSelectedItem()
        {
            if (ShortcutsGrid.SelectedItem is ShortcutItem item)
            {
                LaunchApplication(item);
            }
            else
            {
                ShowStatus("No shortcut selected", isError: true);
            }
        }
        
        private void LaunchApplication(ShortcutItem item, bool runAsAdmin = false)
        {
            try
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
            catch (Exception ex)
            {
                ShowStatus($"Failed to launch: {ex.Message}", isError: true);
                MessageBox.Show($"Failed to launch: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
        public string Key { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
    }
}