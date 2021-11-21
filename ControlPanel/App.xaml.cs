using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Windows;

using System.ComponentModel;

using CSUtils;
using System.IO;

namespace ControlPanel
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public const string Title = "Pumpla Juhtpaneel";
        public const string TitleOK = "OK - " + Title;
        public const string TitleError = "Viga - " + Title;
        public static string DataDirectory = Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Exe-Project"), "PumpingStationV");
#if (false)
        public static string ConfigurationFilename = Path.Combine(DataDirectory, "ControlPanel.ini");
        public static string SchemeProgramFilename = Path.Combine(DataDirectory, "ControlPanel-Scheme.txt");
#else
        public static string ConfigurationFilename = "ControlPanel.ini";
        public static string SchemeProgramFilename = "ControlPanel-Scheme.txt";
        public static string DatabaseFilename = "ControlPanel-Data.sqlite";
#endif

        public const string TimestampFormatString = "yyyy'-'MM'-'dd' 'HH':'mm':'ss";

        public static new App Current
        {
            get { return Application.Current as App; }
        }

        public Configuration Configuration;
        FileConfig _FileConfig;
        const string _SectionName = "ControlPanel";

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            // 1. Process the command line.
            bool is_in_debug_mode = false;
            foreach (var s in e.Args)
            {
                if (s == "--debug")
                {
                    is_in_debug_mode = true;
                }
            }

            // 2. Create application data directory, if it doesn't exist.
            if (!Directory.Exists(DataDirectory))
            {
                try
                {
                    Directory.CreateDirectory(DataDirectory);
                }
                catch (Exception)
                {
                    // FIXME: what to do?
                }
            }

            // 3. Ensure that there is a configuration, at least some sort.
            try
            {
                _FileConfig = new FileConfig(ConfigurationFilename);
                _FileConfig.Load();
                Configuration = StringDictionary.ToObject<Configuration>(_FileConfig[_SectionName]);
            }
            catch (Exception)
            {
                Configuration = StringDictionary.CreateWithDefaults<Configuration>();
            }
            Configuration.IsDebug = is_in_debug_mode;
        }

        public void Store_Configuration()
        {
            if (_FileConfig == null)
            {
                _FileConfig = new FileConfig(ConfigurationFilename);
            }

            _FileConfig[_SectionName] = StringDictionary.Create(Configuration);
            _FileConfig.Store();
        }

        private void Application_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show(e.Exception.Message, TitleError);
            e.Handled = true;
        }
    }
}
