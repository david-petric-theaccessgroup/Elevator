using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;

namespace Elevator
{
    public class Program
    {
        private const string EpmClientPath = @"C:\Program Files\Microsoft EPM Agent\EPMClient\EpmClientStub.exe";
        private static bool isDirectRunMode = false;

        [STAThread]
        public static void Main(string[] args)
        {
            try
            {
                // Parse arguments
                isDirectRunMode = args.Any(a => a.Equals("--direct-run", StringComparison.OrdinalIgnoreCase));
                bool bypassEpm = args.Any(a => a.Equals("--bypass-epm", StringComparison.OrdinalIgnoreCase));

                // Show debug information in a message box if requested
                if (args.Any(a => a.Equals("--debug", StringComparison.OrdinalIgnoreCase)))
                {
                    string processInfo = GetProcessInfo();
                    MessageBox.Show(processInfo, "Debug Info", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                // Check if we should launch through EPM
                if (!isDirectRunMode && !bypassEpm && ShouldLaunchThroughEpmClient())
                {
                    LaunchThroughEpmClient(args);
                    return; // Exit after launching through EPM
                }

                // Normal application startup
                RunApplication();
            }
            catch (Exception ex)
            {
                // Last resort error handling
                MessageBox.Show($"Critical error starting application: {ex.Message}\n\n{ex.StackTrace}", 
                    "Application Error", MessageBoxButton.OK, MessageBoxImage.Error);
                
                // Try to run directly if we get an error during EPM startup
                if (!isDirectRunMode)
                {
                    try
                    {
                        // Restart in direct run mode
                        var exePath = Process.GetCurrentProcess().MainModule?.FileName;
                        if (!string.IsNullOrEmpty(exePath))
                        {
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = exePath,
                                Arguments = "--direct-run",
                                UseShellExecute = true
                            });
                        }
                    }
                    catch
                    {
                        // If this also fails, we've exhausted our options
                    }
                }
            }
        }

        private static void RunApplication()
        {
            // Start the WPF application
            var app = new App();
            app.InitializeComponent();
            app.Run();
        }

        private static string GetProcessInfo()
        {
            var process = Process.GetCurrentProcess();
            var info = new System.Text.StringBuilder();
            info.AppendLine($"Process ID: {process.Id}");
            info.AppendLine($"Process Name: {process.ProcessName}");
            info.AppendLine($"Process Path: {process.MainModule?.FileName ?? "Unknown"}");
            
            try
            {
                var parentId = GetParentProcessId(process.Id);
                info.AppendLine($"Parent Process ID: {parentId}");
                
                if (parentId > 0)
                {
                    try
                    {
                        var parentProcess = Process.GetProcessById(parentId);
                        info.AppendLine($"Parent Name: {parentProcess.ProcessName}");
                        info.AppendLine($"Parent Path: {parentProcess.MainModule?.FileName ?? "Unknown"}");
                    }
                    catch
                    {
                        info.AppendLine("Parent process information unavailable");
                    }
                }
            }
            catch
            {
                info.AppendLine("Could not determine parent process");
            }
            
            info.AppendLine($"EPM Client Path exists: {File.Exists(EpmClientPath)}");
            return info.ToString();
        }

        private static bool ShouldLaunchThroughEpmClient()
        {
            // Don't relaunch if we're in direct run mode
            if (isDirectRunMode)
                return false;

            // If EPM client doesn't exist, don't try to launch through it
            if (!File.Exists(EpmClientPath))
            {
                Debug.WriteLine("EPM Client not found, bypassing EPM launch");
                return false;
            }

            try
            {
                // Get current process info
                var currentProcess = Process.GetCurrentProcess();
                var parentProcessId = GetParentProcessId(currentProcess.Id);
                
                // If we can't determine the parent, try EPM (but with caution)
                if (parentProcessId <= 0)
                {
                    Debug.WriteLine("Could not determine parent process");
                    return true;
                }
                
                try
                {
                    // Check if we are already launched by EPM
                    using var parentProcess = Process.GetProcessById(parentProcessId);
                    string parentPath = parentProcess.MainModule?.FileName ?? string.Empty;
                    string parentName = Path.GetFileName(parentPath).ToLowerInvariant();
                    
                    // Common names for the EPM Client
                    bool isEpmParent = parentName.Contains("epm") || 
                                       parentName.Contains("clientstub") || 
                                       parentName.Equals("epmclientstub.exe", StringComparison.OrdinalIgnoreCase);
                    
                    Debug.WriteLine($"Parent process: {parentName}, Is EPM: {isEpmParent}");
                    
                    // If parent is already EPM, don't relaunch
                    return !isEpmParent;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error checking parent process: {ex.Message}");
                    return true; // Safer to try EPM
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in ShouldLaunchThroughEpmClient: {ex.Message}");
                return false; // If something goes wrong, skip the relaunch to avoid loops
            }
        }

        private static void LaunchThroughEpmClient(string[] args)
        {
            try
            {
                var currentExecutablePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(currentExecutablePath))
                {
                    MessageBox.Show("Failed to determine application path", 
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Add our flags to prevent loops
                var allArgs = args.Where(a => !a.Equals("--bypass-epm", StringComparison.OrdinalIgnoreCase))
                                 .Concat(new[] { "--bypass-epm" })
                                 .ToArray();
                
                var processArgs = string.Join(" ", allArgs.Select(arg => $"\"{arg}\""));
                
                // Create the process start info for EPM
                var startInfo = new ProcessStartInfo(EpmClientPath)
                {
                    UseShellExecute = true,
                    Arguments = $"\"{currentExecutablePath}\" {processArgs}",
                    // Don't set Verb to "runas" as this might conflict with EPM's elevation mechanism
                    CreateNoWindow = false,
                };

                Debug.WriteLine($"Launching via EPM: {EpmClientPath} with args: {startInfo.Arguments}");
                
                // Start the EPM Client
                var process = Process.Start(startInfo);
                
                // Wait briefly to ensure the process starts
                if (process != null)
                {
                    // Longer delay to ensure EPM has time to fully initialize
                    Thread.Sleep(1000);
                }
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("0x8000FFFF") || ex.Message.Contains("-2147418113"))
                {
                    // Special handling for the specific EPM error
                    MessageBox.Show("EPM Client error (0x8000FFFF). The application will now try to run directly.\n\n" +
                                    "This typically occurs if EPM Client cannot launch the application or there are permission issues.",
                                    "EPM Launch Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    
                    // Try to run directly
                    RunApplication();
                }
                else
                {
                    // Other errors
                    MessageBox.Show($"Failed to launch through EPM Client: {ex.Message}\n\n" +
                                    $"The application will try to run normally.", 
                                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    
                    // Still try to run the app directly
                    RunApplication();
                }
            }
        }

        private static int GetParentProcessId(int processId)
        {
            try
            {
                // First try WMI to get parent process
                using var cmd = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "wmic",
                        Arguments = $"process where ProcessId={processId} get ParentProcessId",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };
                
                cmd.Start();
                string output = cmd.StandardOutput.ReadToEnd().Trim();
                cmd.WaitForExit();

                string[] lines = output.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length >= 2)
                {
                    string parentProcessIdText = lines[1].Trim();
                    if (int.TryParse(parentProcessIdText, out int parentId) && parentId > 0)
                    {
                        return parentId;
                    }
                }
                
                // If WMIC fails, try an alternative method
                return -1;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting parent process ID: {ex.Message}");
                return -1;
            }
        }
    }
}