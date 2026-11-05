using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Xml.Linq;
using EPOSGamingSuite.Patcher.Patches;

namespace EPOSGamingSuite.Patcher;

/// <summary>
/// Clean-room AppDomainManager hook for zero-touch runtime patching when configured in EPOSGamingSuite.exe.config.
/// Automatically loaded by the .NET Framework CLR every time EPOSGamingSuite.exe starts.
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
            EposPatchBootstrap.LogPatchStatus("AppDomainManager init error: " + ex);
        }
    }
}

public static class Program
{
    private const string DefaultInstallDir = @"C:\Program Files (x86)\EPOS\Gaming Suite";
    private const string DomainManagerAsmValue = "EPOSGamingSuite.Patcher, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null";
    private const string DomainManagerTypeValue = "EPOSGamingSuite.Patcher.EposHarmonyDomainManager";

    [STAThread]
    public static int Main(string[] args)
    {
        string installDir = ResolveInstallDirectory(args);
        bool silent = false;

        if (args != null && args.Length > 0)
        {
            string cmd = args[0].Trim().ToLowerInvariant();
            if (cmd == "--uninstall" || cmd == "-u")
            {
                return UninstallAppConfigHook(installDir);
            }
            if (cmd == "--install-silent" || cmd == "--silent" || cmd == "-s")
            {
                silent = true;
            }
        }

        return InstallAppConfigHook(installDir, silent);
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

    private static int InstallAppConfigHook(string installDir, bool silent)
    {
        try
        {
            string configPath = Path.Combine(installDir, "EPOSGamingSuite.exe.config");
            if (!File.Exists(configPath))
            {
                if (!silent)
                {
                    MessageBox.Show(
                        $"Could not find EPOSGamingSuite.exe.config at:\n{configPath}\n\nPlease make sure the official EPOS Gaming Suite is installed first.",
                        "EPOS Gaming Suite Community Patch",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
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

            runtime.Element("appDomainManagerAssembly")?.Remove();
            runtime.Element("appDomainManagerType")?.Remove();
            runtime.Add(new XElement("appDomainManagerAssembly", new XAttribute("value", DomainManagerAsmValue)));
            runtime.Add(new XElement("appDomainManagerType", new XAttribute("value", DomainManagerTypeValue)));
            doc.Save(configPath);

            EposPatchBootstrap.LogPatchStatus($"Installed persistent AppDomainManager hook into {configPath}.");

            if (!silent)
            {
                MessageBox.Show(
                    $"Successfully installed the persistent Harmony runtime patch into:\n{configPath}\n\nPlease close EPOS Gaming Suite (from the System Tray if running) and reopen it normally.",
                    "EPOS Gaming Suite Community Patch",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            return 0;
        }
        catch (Exception ex)
        {
            if (!silent)
            {
                MessageBox.Show(
                    $"Failed to install patch (please run as Administrator and ensure EPOS Gaming Suite is closed):\n{ex.Message}",
                    "EPOS Gaming Suite Community Patch",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
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

            MessageBox.Show(
                "Successfully removed the Harmony runtime hook from EPOSGamingSuite.exe.config.",
                "EPOS Gaming Suite Community Patch",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to uninstall hook:\n{ex.Message}",
                "EPOS Gaming Suite Community Patch",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return 1;
        }
    }
}
