using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Fonts.Inter;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;

using ControlCatalog.Pages;

namespace ControlCatalog.NetCore
{
    static class Program
    {
        private static bool s_useFramebuffer;
        
        [STAThread]
        static int Main(string[] args)
        {
            if (args.Contains("--fbdev"))
            {
                s_useFramebuffer = true;
            }

            if (args.Contains("--wait-for-attach"))
            {
                Console.WriteLine("Attach debugger and use 'Set next statement'");
                while (true)
                {
                    Thread.Sleep(100);
                    if (Debugger.IsAttached)
                        break;
                }
            }

            var builder = BuildAvaloniaApp();

            double GetScaling()
            {
                var idx = Array.IndexOf(args, "--scaling");
                if (idx != 0 && args.Length > idx + 1 &&
                    double.TryParse(args[idx + 1], NumberStyles.Any, CultureInfo.InvariantCulture, out var scaling))
                    return scaling;
                return 1;
            }
            if (s_useFramebuffer)
            {
                 SilenceConsole(); 
                 return builder.StartLinuxFbDev(args, scaling: GetScaling());
            }
            else if (args.Contains("--vnc"))
            {
                return builder.StartWithHeadlessVncPlatform(null, 5901, args, ShutdownMode.OnMainWindowClose);
            }
            else if (args.Contains("--full-headless"))
            {
                return builder
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions
                    {
                        UseHeadlessDrawing = true
                    })
                    .AfterSetup(_ =>
                    {
                        DispatcherTimer.RunOnce(async () =>
                        {
                            var window = ((IClassicDesktopStyleApplicationLifetime)Application.Current.ApplicationLifetime)
                                .MainWindow;
                            var tc = window.GetLogicalDescendants().OfType<TabControl>().First();
                            foreach (var page in tc.Items.Cast<TabItem>().ToList())
                            {
                                if (page.Header.ToString() == "DatePicker" || page.Header.ToString() == "TreeView")
                                    continue;
                                Console.WriteLine("Selecting " + page.Header);
                                tc.SelectedItem = page;
                                await Task.Delay(50);
                            }
                            Console.WriteLine("Selecting the first page");
                            tc.SelectedItem = tc.Items.OfType<object>().First();
                            await Task.Delay(500);
                            Console.WriteLine("Clicked through all pages, triggering GC");
                            for (var c = 0; c < 3; c++)
                            {
                                GC.Collect(2, GCCollectionMode.Forced);
                                await Task.Delay(50);
                            }

                            void FormatMem(string metric, long bytes)
                            {
                                Console.WriteLine(metric + ": " + bytes / 1024 / 1024 + "MB");
                            }

                            FormatMem("GC allocated bytes", GC.GetTotalMemory(true));
                            FormatMem("WorkingSet64", Process.GetCurrentProcess().WorkingSet64);
                        }, TimeSpan.FromSeconds(1));
                    })
                    .StartWithClassicDesktopLifetime(args);
            }
            else if (args.Contains("--drm"))
            {
                SilenceConsole();
                return builder.StartLinuxDrm(args, scaling: GetScaling());
            }
            else if (args.Contains("--dxgi"))
            {
                builder.With(new Win32PlatformOptions()
                {
                    CompositionMode = new [] { Win32CompositionMode.LowLatencyDxgiSwapChain }
                });
                return builder.StartWithClassicDesktopLifetime(args);
            }
            else
                return builder.StartWithClassicDesktopLifetime(args);
        }

        /// <summary>
        /// This method is needed for IDE previewer infrastructure
        /// </summary>
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .With(new X11PlatformOptions
                {
                    EnableMultiTouch = true,
                    UseDBusMenu = true,
                    EnableIme = true
                })
                .With(new CompositionOptions()
                {
                    UseRegionDirtyRectClipping = true
                })
                .UseSkia()
                .WithInterFont()
                .AfterSetup(builder =>
                {
                    if (!s_useFramebuffer)
                    {
                        builder.Instance!.AttachDevTools(new Avalonia.Diagnostics.DevToolsOptions()
                        {
                            StartupScreenIndex = 1,
                        });
                    }

                    EmbedSample.Implementation = OperatingSystem.IsWindows() ? (INativeDemoControl)new EmbedSampleWin()
                        : OperatingSystem.IsMacOS() ? new EmbedSampleMac()
                        : OperatingSystem.IsLinux() ? new EmbedSampleGtk()
                        : null;
                })
                .LogToTrace();

        static void SilenceConsole()
        {
            new Thread(() =>
            {
                Console.CursorVisible = false;
                while (true)
                    Console.ReadKey(true);
            })
            { IsBackground = true }.Start();
        }
    }
}
