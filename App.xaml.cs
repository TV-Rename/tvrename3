using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Alphaleonis.Win32.Filesystem;
using JetBrains.Annotations;
using NLog;
using NLog.Config;
using NLog.Targets.Syslog;
using NLog.Targets.Syslog.Settings;
using TVRename.Ipc;
using TVRename.ViewModel;

namespace TVRename
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Defines the entry point of the application.
        /// Checks if the application is already running and if so, performs actions via IPC and exits.
        /// </summary>
        /// <param name="args">The command line arguments.</param>
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            Logger.Info($"TV Rename {Helpers.DisplayVersion} started with args: {string.Join(" ", e.Args)}");
            Logger.Info($"Copyright (C) {DateTime.Now.Year} TV Rename");
            Logger.Info(
                "This program comes with ABSOLUTELY NO WARRANTY; This is free software, and you are welcome to redistribute it under certain conditions");

            // Parse command line arguments
            CommandLineArgs clargs = new CommandLineArgs(new ReadOnlyCollection<string>(e.Args));

            //Application.EnableVisualStyles();
            //Application.SetCompatibleTextRenderingDefault(false);

            AppDomain.CurrentDomain.UnhandledException += GlobalExceptionHandler;

            if (e.Args.Contains("/?", StringComparer.OrdinalIgnoreCase))
            {
                Logger.Info(CommandLineArgs.Helptext());

                //redirect console output to parent process
                //must be before any calls to Console.WriteLine()
                //MS: Never got this to work quite right - seems there is no simple way to output
                //to the command line console if you are a winforms app
                if (NativeMethods.AttachParentConsole())
                {
                    Console.WriteLine(CommandLineArgs.Helptext());
                }
                else
                {
                    Logger.Info("Could not attach to console");
                }

                return;
            }

            // Check if an application instance is already running
            Mutex mutex = new Mutex(true, "TVRename", out bool newInstance);

            if (!newInstance)
            {
                // Already running
                Logger.Warn("An instance is already running");

                // Create an IPC channel to the existing instance
                RemoteClient.Proxy();

                // Transparent proxy to the existing instance
                RemoteClient ipc = new RemoteClient();

                // If already running and no command line arguments then bring instance to the foreground and quit
                if (e.Args.Length == 0)
                {
                    ipc.FocusWindow();

                    return;
                }

                // Send command-line arguments to already running instance
                CommandLineArgs.MissingFolderBehavior previousMissingFolderBehavior = ipc.MissingFolderBehavior;
                bool previousRenameBehavior = ipc.RenameBehavior;

                if (clargs.RenameCheck == false)
                {
                    // Temporarily override behavior for renaming folders
                    ipc.RenameBehavior = false;
                }

                if (clargs.MissingFolder != CommandLineArgs.MissingFolderBehavior.ask)
                {
                    // Temporarily override behavior for missing folders
                    ipc.MissingFolderBehavior = clargs.MissingFolder;
                }

                // TODO: Unify command line handling between here and in UI.cs (ProcessArgs). Just send in clargs via IPC?

                if (clargs.Scan)
                {
                    ipc.Scan();
                }

                if (clargs.QuickScan)
                {
                    ipc.QuickScan();
                }

                if (clargs.RecentScan)
                {
                    ipc.RecentScan();
                }

                if (clargs.DoAll)
                {
                    ipc.ProcessAll();
                }

                if (clargs.Quit)
                {
                    ipc.Quit();
                }

                // TODO: Necessary?
                ipc.RenameBehavior = previousRenameBehavior;
                ipc.MissingFolderBehavior = previousMissingFolderBehavior;

                return;
            }

            try
            {
                Logger.Info("Starting new instance");

                LetsGo(clargs, string.Join(" ", e.Args));

                GC.KeepAlive(mutex);
            }
            catch (Exception ex)
            {
                Logger.Fatal(ex, "Application exiting with error");

                //new ShowException(ex).ShowDialog();

                Environment.Exit(1);
            }

            Logger.Info("Application exiting");
        }

        private void LetsGo(CommandLineArgs clargs, string commandLine) { 

            //initialize the splash screen and set it as the application main window
            TvRenameSplash splashScreen = new TvRenameSplash();
            SplashViewModel vm = new SplashViewModel {Version = Helpers.DisplayVersion};

            this.MainWindow = splashScreen;
            splashScreen.DataContext = vm;

            if (clargs.Hide || !Environment.UserInteractive)
            {
                splashScreen.Visibility = Visibility.Hidden;
            }
            else
            {
                splashScreen.Show();
            }

            // Update splash screen
            vm.Status = "Initializing";

            //because we're not on the UI thread, we need to use the Dispatcher
            //associated with the splash screen to update the progress bar
            vm.Progress = 5;


            //in order to ensure the UI stays responsive, we need to
            //do the work on a different thread
            Task.Factory.StartNew(() =>
            {
                splashScreen.Dispatcher.Invoke(() => vm.Status = "Loading Settings...");
                TVDoc doc = LoadSettings(clargs);

                splashScreen.Dispatcher.Invoke(() => vm.Status = "Setup Logging...");
                if (TVSettings.Instance.mode == TVSettings.BetaMode.BetaToo || TVSettings.Instance.ShareLogs)
                {
                    SetupLogging(commandLine);
                }

                splashScreen.Dispatcher.Invoke(() => vm.Status = "Configuring...");
                ConvertSeriesTimeZones(doc, TheTVDB.Instance);
                // Update RegVersion to bring the WebBrowser up to speed
                RegistryHelper.UpdateBrowserEmulationVersion();

                splashScreen.Dispatcher.Invoke(() => vm.Status = "Create Main Window...");

                //once we're done we need to use the Dispatcher
                //to create and show the main window

                Dispatcher.Invoke(() =>
                {
                    // Show user interface

                    MainWindow ui = new MainWindow(doc, vm, splashScreen.Dispatcher, !clargs.Unattended && !clargs.Hide && Environment.UserInteractive);
                    ui.Title = ui.Title + " " + Helpers.DisplayVersion;

                    // Bind IPC actions to the form, this allows another instance to trigger form actions
                    RemoteClient.Bind(ui, doc);


                    //initialize the main window, set it as the application main window
                    //and close the splash screen
                    this.MainWindow = ui;


                    MainWindow.Show();
                    splashScreen.Close();
                });

            });
        }

        [NotNull]
        private static TVDoc LoadSettings([NotNull] CommandLineArgs clargs)
        {
            bool recover = false;
            string recoverText = string.Empty;

            // Check arguments for forced recover
            if (clargs.ForceRecover)
            {
                recover = true;
                recoverText = "Recover manually requested.";
            }

            SetupCustomSettings(clargs);

            FileInfo tvdbFile = PathManager.TVDBFile;
            FileInfo settingsFile = PathManager.TVDocSettingsFile;
            TVDoc doc;

            do // Loop until files correctly load
            {
                if (recover) // Recovery required, prompt user
                {
/*                    RecoverXml recoveryForm = new RecoverXml(recoverText);

                    if (recoveryForm.ShowDialog() == DialogResult.OK)
                    {
                        tvdbFile = recoveryForm.DbFile;
                        settingsFile = recoveryForm.SettingsFile;
                    }
                    else
                    {
                        Logger.Error("User requested no recovery");
                        throw new TVRenameOperationInterruptedException();
                    }*/
                }

                // Try loading settings file
                doc = new TVDoc(settingsFile, clargs);

                // Try loading TheTVDB cache file
                TheTVDB.Instance.Setup(doc.Library,tvdbFile, PathManager.TVDBFile, clargs);

                if (recover)
                {
                    doc.SetDirty();
                }

                recover = !doc.LoadOk;

                // Continue if correctly loaded
                if (!recover)
                {
                    continue;
                }

                // Set recover message
                recoverText = string.Empty;
                if (!doc.LoadOk && !string.IsNullOrEmpty(doc.LoadErr))
                {
                    recoverText = doc.LoadErr;
                }

                if (!TheTVDB.Instance.LoadOk && !string.IsNullOrEmpty(TheTVDB.Instance.LoadErr))
                {
                    recoverText += $"{Environment.NewLine}{TheTVDB.Instance.LoadErr}";
                }
            } while (recover);

            return doc;
        }

        private static void SetupCustomSettings([NotNull] CommandLineArgs clargs)
        {
            // Check arguments for custom settings path
            if (!string.IsNullOrEmpty(clargs.UserFilePath))
            {
                try
                {
                    PathManager.SetUserDefinedBasePath(clargs.UserFilePath);
                }
                catch (Exception ex)
                {
                    if (!clargs.Unattended && !clargs.Hide && Environment.UserInteractive)
                    {
                        MessageBox.Show($"Error while setting the User-Defined File Path:{Environment.NewLine}{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }

                    Logger.Error(ex, $"Error while setting the User-Defined File Path - EXITING: {clargs.UserFilePath}");

                    Environment.Exit(1);
                }
            }
        }

        private static void ConvertSeriesTimeZones([NotNull] TVDoc doc, TheTVDB tvdb)
        {
            //this is just to convert timezones in the TheTVDB into the TVDOC where they should be:
            //it should only do anything the first time it is run and then be entirely benign
            //can be removed after 1/1/19

            foreach (ShowItem si in doc.Library.GetShowItems())
            {
                string newTimeZone = tvdb.GetSeries(si.TvdbCode)?.TempTimeZone;

                if (string.IsNullOrWhiteSpace(newTimeZone))
                {
                    continue;
                }

                if (newTimeZone == TimeZoneHelper.DefaultTimeZone())
                {
                    continue;
                }

                if (si.ShowTimeZone != TimeZoneHelper.DefaultTimeZone())
                {
                    continue;
                }

                si.ShowTimeZone = newTimeZone;
                doc.SetDirty();
                Logger.Info("Copied timezone:{0} onto series {1}", newTimeZone, si.ShowName);
            }
        }

        private static void GlobalExceptionHandler(object sender, UnhandledExceptionEventArgs args)
        {
        Exception e = (Exception)args.ExceptionObject;
        Logger.Fatal(e, "UNHANDLED ERROR - GLobalExceptionHandler");
        Environment.Exit(1);
        }

        private static void SetupLogging(string commandLine)
        {
            ConfigurationItemFactory.Default.RegisterItemsFromAssembly(Assembly.Load("NLog.Targets.Syslog"));

            LoggingConfiguration config = LogManager.Configuration;

            SyslogTarget syslog = new SyslogTarget
            {
                MessageCreation = { Facility = Facility.Local7 },
                MessageSend =
                {
                    Protocol = ProtocolType.Tcp,
                    Tcp = {Server = "logs7.papertrailapp.com", Port = 13236, Tls = {Enabled = true}}
                }
            };

            config.AddTarget("syslog", syslog);

            syslog.Layout = "| " + Helpers.DisplayVersion + " |${level:uppercase=true}| ${message} ${exception:format=toString,Data}";

            LoggingRule rule = new LoggingRule("*", LogLevel.Error, syslog);
            config.LoggingRules.Add(rule);

            LogManager.Configuration = config;

            Logger.Fatal($"TV Rename {Helpers.DisplayVersion} logging started on {Environment.OSVersion}, {(Environment.Is64BitOperatingSystem ? "64 Bit OS" : "")}, {(Environment.Is64BitProcess ? "64 Bit Process" : "")} {Environment.Version} {(Environment.UserInteractive ? "Interactive" : "")} with args: {commandLine}");
            Logger.Info($"Copyright (C) {DateTime.Now.Year} TV Rename");
            Logger.Info("This program comes with ABSOLUTELY NO WARRANTY; This is free software, and you are welcome to redistribute it under certain conditions");
        }
    }

}

