using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Xml.Linq;
using EPOSGamingSuite.Patcher.Patches;

namespace EPOSGamingSuite.Patcher;

/// <summary>
/// Clean-room AppDomainManager hook for zero-touch runtime patching when configured in EPOSGamingSuite.exe.config.
/// </summary>
public sealed class EposHarmonyDomainManager : AppDomainManager
{
    public override void InitializeNewDomain(AppDomainSetup appDomainInfo)
    {
        base.InitializeNewDomain(appDomainInfo);
        try
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            Program.RegisterAssemblyResolver(baseDir);
            EposPatchBootstrap.ApplyAll(baseDir);
        }
        catch (Exception ex)
        {
            Trace.WriteLine("[EPOSGamingSuite.Patcher] AppDomainManager init error: " + ex);
        }
    }
}

public static class Program
{
    private const string DefaultInstallDir = @"C:\Program Files (x86)\EPOS\Gaming Suite";

    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            string installDir = ResolveInstallDirectory(args);
            if (args != null && args.Length > 0)
            {
                string cmd = args[0].Trim().ToLowerInvariant();
                if (cmd == "--install" || cmd == "-i")
                {
                    return InstallAppConfigHook(installDir);
                }
                if (cmd == "--uninstall" || cmd == "-u")
                {
                    return UninstallAppConfigHook(installDir);
                }
            }

            string exePath = Path.Combine(installDir, "EPOSGamingSuite.exe");
            if (!File.Exists(exePath))
            {
                MessageBox.Show(
                    $"Could not find original EPOSGamingSuite.exe at:\n{exePath}\n\nPlease install the official EPOS Gaming Suite first or pass the installation path as an argument.",
                    "EPOS Gaming Suite Runtime Patcher",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return 1;
            }

            Environment.CurrentDirectory = installDir;
            RegisterAssemblyResolver(installDir);

            // Ensure no unpatched/zombie EPOSGamingSuite.exe process is holding \\.\pipe\GSUIPipe (Error 231)
            StopExistingUnpatchedInstances();

            // Load original assemblies into current AppDomain
            Assembly servicesAsm = Assembly.LoadFrom(Path.Combine(installDir, "GamingSuite.UI.Services.dll"));
            Assembly uiAsm = Assembly.LoadFrom(exePath);

            // Set WPF ResourceAssembly via reflection BEFORE Application.ResourceAssembly getter is ever called
            SetWpfResourceAssembly(uiAsm);

            // Apply all Lib.Harmony runtime patches in memory
            EposPatchBootstrap.ApplyAll(installDir, servicesAsm, uiAsm);

            // Invoke original WPF entry point (Sennheiser.Gs.UI.App.Main)
            MethodInfo entryPoint = uiAsm.EntryPoint;
            if (entryPoint == null)
            {
                throw new InvalidOperationException("EPOSGamingSuite.exe does not contain a valid CLR entry point.");
            }

            object[] invokeArgs = entryPoint.GetParameters().Length == 0 ? null : new object[] { new string[0] };
            entryPoint.Invoke(null, invokeArgs);
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to launch patched EPOS Gaming Suite:\n{ex.Message}\n\n{ex}",
                "EPOS Gaming Suite Runtime Patcher",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return 1;
        }
    }

    public static void RegisterAssemblyResolver(string installDir)
    {
        string patcherDir = Path.GetDirectoryName(typeof(Program).Assembly.Location) ?? installDir;
        AppDomain.CurrentDomain.AssemblyResolve += (_, eventArgs) =>
        {
            try
            {
                string simpleName = new AssemblyName(eventArgs.Name).Name;
                if (string.IsNullOrEmpty(simpleName) || simpleName.EndsWith(".resources", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                string candidateInstallDll = Path.Combine(installDir, simpleName + ".dll");
                if (File.Exists(candidateInstallDll))
                {
                    return Assembly.LoadFrom(candidateInstallDll);
                }

                string candidateInstallExe = Path.Combine(installDir, simpleName + ".exe");
                if (File.Exists(candidateInstallExe))
                {
                    return Assembly.LoadFrom(candidateInstallExe);
                }

                string candidatePatcherDll = Path.Combine(patcherDir, simpleName + ".dll");
                if (File.Exists(candidatePatcherDll))
                {
                    return Assembly.LoadFrom(candidatePatcherDll);
                }
            }
            catch
            {
            }
            return null;
        };
    }

    private static void StopExistingUnpatchedInstances()
    {
        try
        {
            int currentPid = Process.GetCurrentProcess().Id;
            foreach (Process proc in Process.GetProcessesByName("EPOSGamingSuite"))
            {
                if (proc.Id != currentPid)
                {
                    try
                    {
                        proc.Kill();
                        proc.WaitForExit(2000);
                    }
                    catch
                    {
                    }
                }
            }
        }
        catch
        {
        }
    }

    private static void SetWpfResourceAssembly(Assembly uiAsm)
    {
        try
        {
            FieldInfo resField = typeof(Application).GetField("_resourceAssembly", BindingFlags.NonPublic | BindingFlags.Static);
            resField?.SetValue(null, uiAsm);

            Type baseUriHelperType = typeof(Application).Assembly.GetType("System.Windows.Navigation.BaseUriHelper");
            PropertyInfo resProp = baseUriHelperType?.GetProperty("ResourceAssembly", BindingFlags.NonPublic | BindingFlags.Static);
            resProp?.SetValue(null, uiAsm, null);
        }
        catch
        {
        }
    }

    private static string ResolveInstallDirectory(string[] args)
    {
        if (args != null)
        {
            foreach (string arg in args)
            {
                if (!arg.StartsWith("-", StringComparison.Ordinal) && Directory.Exists(arg) && File.Exists(Path.Combine(arg, "EPOSGamingSuite.exe")))
                {
                    return Path.GetFullPath(arg);
                }
            }
        }

        string localDir = Path.GetDirectoryName(typeof(Program).Assembly.Location) ?? Environment.CurrentDirectory;
        if (File.Exists(Path.Combine(localDir, "EPOSGamingSuite.exe")) && File.Exists(Path.Combine(localDir, "GamingSuite.UI.Services.dll")))
        {
            return localDir;
        }

        return DefaultInstallDir;
    }

    private static int InstallAppConfigHook(string installDir)
    {
        try
        {
            string configPath = Path.Combine(installDir, "EPOSGamingSuite.exe.config");
            if (!File.Exists(configPath))
            {
                MessageBox.Show($"Config file not found: {configPath}", "EPOS Patcher", MessageBoxButton.OK, MessageBoxImage.Error);
                return 1;
            }

            string patcherDir = Path.GetDirectoryName(typeof(Program).Assembly.Location)!;
            if (!string.Equals(Path.GetFullPath(patcherDir).TrimEnd('\\'), Path.GetFullPath(installDir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(Path.Combine(patcherDir, "EPOSGamingSuite.Patcher.exe"), Path.Combine(installDir, "EPOSGamingSuite.Patcher.exe"), overwrite: true);
                File.Copy(Path.Combine(patcherDir, "0Harmony.dll"), Path.Combine(installDir, "0Harmony.dll"), overwrite: true);
            }

            string backupPath = configPath + ".bak";
            if (!File.Exists(backupPath))
            {
                File.Copy(configPath, backupPath, overwrite: false);
            }

            XDocument doc = XDocument.Load(configPath);
            XElement root = doc.Root ?? new XElement("configuration");
            XElement runtime = root.Element("runtime");
            if (runtime == null)
            {
                runtime = new XElement("runtime");
                root.Add(runtime);
            }

            runtime.SetElementValue("appDomainManagerAssembly", "EPOSGamingSuite.Patcher, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null");
            runtime.SetElementValue("appDomainManagerType", "EPOSGamingSuite.Patcher.EposHarmonyDomainManager");
            doc.Save(configPath);

            MessageBox.Show(
                $"Successfully installed the Harmony runtime hook into:\n{configPath}\n\nLaunching the original EPOSGamingSuite.exe will now automatically load all runtime fixes.",
                "EPOS Gaming Suite Runtime Patcher",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to install hook (Administrator privileges required):\n{ex.Message}", "EPOS Patcher", MessageBoxButton.OK, MessageBoxImage.Error);
            return 1;
        }
    }

    private static int UninstallAppConfigHook(string installDir)
    {
        try
        {
            string configPath = Path.Combine(installDir, "EPOSGamingSuite.exe.config");
            string backupPath = configPath + ".bak";
            if (File.Exists(backupPath))
            {
                File.Copy(backupPath, configPath, overwrite: true);
            }
            else if (File.Exists(configPath))
            {
                XDocument doc = XDocument.Load(configPath);
                XElement runtime = doc.Root?.Element("runtime");
                runtime?.Element("appDomainManagerAssembly")?.Remove();
                runtime?.Element("appDomainManagerType")?.Remove();
                doc.Save(configPath);
            }

            MessageBox.Show("Successfully removed the Harmony runtime hook from EPOSGamingSuite.exe.config.", "EPOS Patcher", MessageBoxButton.OK, MessageBoxImage.Information);
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to uninstall hook:\n{ex.Message}", "EPOS Patcher", MessageBoxButton.OK, MessageBoxImage.Error);
            return 1;
        }
    }
}
