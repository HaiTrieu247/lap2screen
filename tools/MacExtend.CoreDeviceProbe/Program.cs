using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MacExtend.Core.CoreDevice;
using MacExtend.CoreDeviceHost;

namespace MacExtend.CoreDeviceProbe
{
    internal static class Program
    {
        private const string UsageText =
            "MacExtend CoreDevice probe: runs pymobiledevice3 serve-web for one device and lets you test it.\n" +
            "\n" +
            "  MacExtend.CoreDeviceProbe.exe --python PATH --udid UDID [--transport userspace|tunnel] [--port N]\n" +
            "\n" +
            "  --python     absolute path to tools\\coredevice\\.venv\\Scripts\\python.exe (made by setup.ps1)\n" +
            "  --udid       the device UDID (pymobiledevice3 usbmux list)\n" +
            "  --transport  userspace (default; iOS 17.4+ over USB, no admin) or tunnel (a tunneld you started as admin)\n" +
            "  --port       loopback HTTP port for the viewer, default 8080\n" +
            "\n" +
            "The probe binds 127.0.0.1 only, sends no input on its own and stops serve-web with one Ctrl+C.\n" +
            "See tools/coredevice/README.md.";

        private const string MenuText =
            "commands:\n" +
            "  status                       state, job processes and what the probe thinks is held\n" +
            "  open                         open the viewer in the default browser\n" +
            "  stop                         stop serve-web (one Ctrl+C, up to 40 s for its cleanup)\n" +
            "  quit                         stop if needed, then exit\n" +
            "  Ctrl+C                       like quit, also while a stop runs (no second signal to serve-web)\n" +
            ProbeCommands.Usage;

        private static int Main(string[] args)
        {
            if (CtrlCHelper.TryRun(args, out var helperExit)) return helperExit;

            CtrlCHelper.ClearInheritedCtrlCIgnore();

            // .NET Framework defaults: "Expect: 100-continue" on POSTs and Nagle's delay on small writes
            ServicePointManager.Expect100Continue = false;
            ServicePointManager.UseNagleAlgorithm = false;

            if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
            {
                Console.Out.WriteLine(UsageText);
                return args.Length == 0 ? ExitCodes.Usage : ExitCodes.Stopped;
            }
            if (!TryParse(args, out var settings, out var error))
            {
                Console.Error.WriteLine("error: " + error);
                Console.Error.WriteLine();
                Console.Error.WriteLine(UsageText);
                return ExitCodes.Usage;
            }

            var helper = Process.GetCurrentProcess().MainModule.FileName;
            using (var session = new ProbeSession(settings, helper))
            {
                var stopRequested = 0;
                Console.CancelKeyPress += (sender, e) =>
                {
                    // the probe's own Ctrl+C never reaches serve-web (it has its own console). It always means "stop,
                    // then exit": it joins the one stop (starting it, or waiting for one a stop command began) and
                    // exits when that is done; a later Ctrl+C only reports
                    e.Cancel = true;
                    if (Interlocked.Exchange(ref stopRequested, 1) == 1)
                    {
                        ProbeSession.Out("already stopping");
                        return;
                    }
                    Task.Run(() =>
                    {
                        session.StopAsync("Ctrl+C").Wait();
                        Environment.Exit(session.ExitCode);
                    });
                };

                if (!session.StartAsync().GetAwaiter().GetResult())
                {
                    session.StopAsync("startup failed").Wait();
                    return session.ExitCode;
                }

                ProbeSession.Out("the viewer is not opened automatically. Type open to use it; close the viewer tab before tap/hold/drag/key/release/home: both send input and the probe cannot see the viewer's.");
                ProbeSession.Out("type help for commands");
                Menu(session);
                session.StopAsync("quit").Wait();
                return session.ExitCode;
            }
        }

        private static void Menu(ProbeSession session)
        {
            while (true)
            {
                var line = Console.In.ReadLine();
                if (line == null)
                {
                    session.StopAsync("stdin closed").Wait();
                    return;
                }
                var words = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (words.Length == 0) continue;
                var command = words[0].ToLowerInvariant();
                var rest = words.Skip(1).ToArray();
                switch (command)
                {
                    case "help":
                    case "?":
                        Console.Out.WriteLine(MenuText);
                        break;
                    case "status":
                        session.PrintStatus();
                        break;
                    case "open":
                        if (session.State != SessionState.Ready)
                        {
                            ProbeSession.Out("error: serve-web is not ready");
                            break;
                        }
                        try
                        {
                            Process.Start(new ProcessStartInfo(session.ViewerUrl) { UseShellExecute = true })?.Dispose();
                            ProbeSession.Out("opened " + session.ViewerUrl + " (close the tab before using the probe's input commands)");
                        }
                        catch (Exception e) when (e is System.ComponentModel.Win32Exception || e is InvalidOperationException)
                        {
                            ProbeSession.Out("error: could not open the browser: " + e.Message + "; open " + session.ViewerUrl + " by hand");
                        }
                        break;
                    case "stop":
                        session.StopAsync("stop command").Wait();
                        break;
                    case "quit":
                    case "exit":
                        return;
                    default:
                        if (ProbeCommands.IsInputCommand(command))
                        {
                            ProbeCommands.RunAsync(session, command, rest).GetAwaiter().GetResult();
                        }
                        else
                        {
                            ProbeSession.Out("unknown command; type help");
                        }
                        break;
                }
            }
        }

        private static bool TryParse(string[] args, out ProbeSettings settings, out string error)
        {
            settings = null;
            error = null;
            string python = null, udid = null, script = null;
            var transport = CoreDeviceTransport.Userspace;
            var port = CoreDeviceOptions.DefaultHttpPort;
            TimeSpan? startup = null, grace = null;
            var failAssign = AssignFailure.None;
            for (var i = 0; i < args.Length; i++)
            {
                string Value()
                {
                    if (i + 1 >= args.Length) throw new FormatException(args[i] + " needs a value");
                    return args[++i];
                }

                try
                {
                    switch (args[i])
                    {
                        case "--python":
                            python = Value();
                            break;
                        case "--udid":
                            udid = Value();
                            break;
                        case "--transport":
                            if (!CoreDeviceOptions.TryParseTransport(Value(), out transport)) throw new FormatException("--transport is userspace or tunnel");
                            break;
                        case "--port":
                            port = Number(Value(), "--port");
                            break;
                        case "--test-serve-script":
                            script = Value();
                            break;
                        case "--test-startup-timeout":
                            startup = TimeSpan.FromSeconds(Number(Value(), "--test-startup-timeout"));
                            break;
                        case "--test-grace":
                            grace = TimeSpan.FromSeconds(Number(Value(), "--test-grace"));
                            break;
                        case "--test-fail-assign":
                            var which = Value();
                            if (which == "version-check") failAssign = AssignFailure.VersionCheck;
                            else if (which == "serve-web") failAssign = AssignFailure.ServeWeb;
                            else throw new FormatException("--test-fail-assign is version-check or serve-web");
                            break;
                        default:
                            throw new FormatException("unknown option " + args[i]);
                    }
                }
                catch (FormatException e)
                {
                    error = e.Message;
                    return false;
                }
            }
            if (python == null || udid == null)
            {
                error = "--python and --udid are required";
                return false;
            }
            if (script == null && (startup.HasValue || grace.HasValue || failAssign != AssignFailure.None))
            {
                // these would let a real run cut serve-web's cleanup short
                error = "--test-* options only work together with --test-serve-script";
                return false;
            }
            if (script != null && !CoreDeviceOptions.IsWindowsAbsolutePath(script))
            {
                error = "--test-serve-script needs an absolute path";
                return false;
            }
            try
            {
                settings = new ProbeSettings
                {
                    Options = new CoreDeviceOptions(python, udid, transport, port),
                    TestServeScript = script,
                    TestFailAssign = failAssign,
                };
            }
            catch (ArgumentException e)
            {
                error = e.Message.Split('\n')[0].Split('\r')[0];
                return false;
            }
            if (startup.HasValue) settings.StartupTimeout = startup.Value;
            if (grace.HasValue) settings.Grace = grace.Value;
            if (!System.IO.File.Exists(python))
            {
                error = python + " does not exist; run tools/coredevice/setup.ps1";
                return false;
            }
            return true;
        }

        private static int Number(string text, string option)
        {
            if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value)) throw new FormatException(option + " needs a whole number");
            return value;
        }
    }
}
