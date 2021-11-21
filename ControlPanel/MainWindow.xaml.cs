using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Xml.Linq;
using System.Text;
using System.Xml;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Markup;
using System.Windows.Threading;

using System.Net.Sockets;
using System.Net;
using System.Threading;
using System.IO.Ports;
using System.Reflection;

namespace ControlPanel
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public ControlPanelViewModel ViewModel { get; set; }
        /// <summary>
        /// 1-second timer for updating the title and driving the blinking.
        /// </summary>
        DispatcherTimer _Timer = null;

        public MainWindow()
        {
            // ViewModel.Dispatcher is defined by the current thread.
            ViewModel = new ControlPanelViewModel()
            {
                Signals = new ObservableCollection<IOSignal>(),
                LocalConfiguration = App.Current.Configuration,
                SynchronizationStatus = new SynchronizationStatus()
                {
                    WorkingTimes = new ObservableCollection<WorkingTimeCell>(),
                },
            };
            CopyOfConfiguration = new Configuration();
            CopyOfConfiguration.CopyFrom(ViewModel.LocalConfiguration);
            this.DataContext = ViewModel;

            InitializeComponent();

            ViewModel.TabGroupDetails = this.tabGroupDetails;
            ViewModel.TabControlCharts = this.tabcontrolCharts;
            ViewModel.MainCanvas = this.mainCanvas;
            ViewModel.HistoryControl = this.historycontrol;
            ViewModel.LogLine = this.textboxLog.AppendLine;
        }

        DrawingGroup _AddDrawingToCanvas(string FilenameInResources)
        {
            var stream_info = Application.GetResourceStream(new Uri("Resources/" + FilenameInResources, UriKind.RelativeOrAbsolute));
            var xmlr = System.Xml.XmlReader.Create(stream_info.Stream);
            var g = XamlReader.Load(xmlr) as DrawingGroup;
            drawingimageScada.Drawing = g;
            var dg = g.Children[0] as DrawingGroup;
            var rc = (dg.ClipGeometry as RectangleGeometry).Rect;
            mainCanvas.Width = rc.Width;
            mainCanvas.Height = rc.Height;
            imageSCADA.Width = rc.Width;
            imageSCADA.Height = rc.Height;
            return g;
        }

        // label -> id
        Dictionary<string, SchemeLayer> _FetchLayerTable(string SvgFilenameInResources, DrawingCollection layers)
        {
            var stream_info = Application.GetResourceStream(new Uri("Resources/" + SvgFilenameInResources, UriKind.RelativeOrAbsolute));
            var svg = XDocument.Load(stream_info.Stream);

            var r = new Dictionary<string, SchemeLayer>();
            var lst = from item in svg.Descendants()
                      let label = (from at1 in item.Attributes() where at1.Name.LocalName == "label" select at1).SingleOrDefault()
                      let id = (from at2 in item.Attributes() where at2.Name.LocalName == "id" select at2).SingleOrDefault()
                      where item.Name.LocalName == "g" && label != null && id!=null
                      select label.Value; //  new KeyValuePair<string, string>(label.Value, id.Value);
            int index = 0;

            foreach (var id in lst)
            {
                if (index < layers.Count)
                {
                    r[id] = new SchemeLayer(id, layers[index] as DrawingGroup);
                }
                ++index;
            }

            return r;
        }

        void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // 1. Open the database. The SqLiteDb is thread-safe anyway, thus we can (and should) open it only once.
            try
            {
                ViewModel.LocalSignalDB = new LocalSignalDB(ViewModel, App.DatabaseFilename);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, App.TitleError);
                App.Current.Shutdown();
            }

            ViewModel.PostponeReconnect();
            Dictionary<string, SchemeLayer> layers = new Dictionary<string,SchemeLayer>();

            // 2. Load the image.
            try
            {
                DrawingGroup bg = _AddDrawingToCanvas("Scheme.xaml");
                layers = _FetchLayerTable("Scheme.svg", (bg.Children[0] as DrawingGroup).Children);

                // Binding as follows:
                // bg.Opacity <= IsConnected ? 1.0 : 0.0;
                var binding = new Binding() {
                    Source = ViewModel,
                    Path = new PropertyPath("IsConnected"),
                    Mode = BindingMode.OneWay,
                    Converter = new CSUtils.SelectOneOfTwo() { ValueWhenTrue = 1.0, ValueWhenFalse = 0.1, },
                };
                BindingOperations.SetBinding(bg, DrawingGroup.OpacityProperty, binding);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error");
            }

            // 3. Create context to work with.
            ViewModel.Layers = layers;
            ViewModel.SignalTable = new Dictionary<int, IOSignal>();
            ViewModel.SignalDict = new Dictionary<string, IOSignal>();

            // 4. Read the program.
            try
            {
                // _SchemeStatements
                textboxLog.AppendLine("Loading scheme program file " + App.SchemeProgramFilename);
                var scanner = new Scanner(App.SchemeProgramFilename);
                var parser = new Parser(scanner);
                var sb = new StringBuilder();
                using (var error_stream = new System.IO.StringWriter(sb))
                {
                    parser.Context = ViewModel;
                    parser.Result = ViewModel.SchemeStatements;
                    parser.errors.errorStream = error_stream;
                    parser.Parse();
                }
                var lines = sb.ToString().Split(new string[] { "\r\n" }, StringSplitOptions.None);
                foreach (var line in lines)
                {
                    if (line.Length > 0)
                    {
                        textboxLog.AppendLine(line);
                    }
                }
                textboxLog.AppendLine("Scheme program loaded, " + ViewModel.SchemeStatements.Count + " statements in total.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, App.TitleError);
            }

            // 5. Connect to the PLC, if possible.
            ViewModel.ConnectToPlc();

            // 6. Start the update timer.
            _Timer = new DispatcherTimer(TimeSpan.FromSeconds(1.0), DispatcherPriority.Normal, _Timer_Tick, Dispatcher);
            _Timer.IsEnabled = true;
        }

        int _Timer_Tick_Count = 0;
        bool _Timer_LastAnyBlinks = false;
        /// <summary>
        /// one-second timer for:
        /// 1. Blinking.
        /// 2. Timeouts.
        /// 3. Chart scrolls.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void _Timer_Tick(object sender, EventArgs e)
        {
            bool any_blinks = false;
            bool sound_a_beep = false;
            string blinking_layer_name = "";
            foreach (var st in ViewModel.SchemeStatements)
            {
                var layer = st.Layer;
                if (st.Type == SchemeStatement.TYPE.BLINK && layer.IsBlinking)
                {
                    layer.IsVisible = (_Timer_Tick_Count & 1) != 0;
                    blinking_layer_name = layer.Name;
                    any_blinks = true;
                    if (st.MuteButton == null || !st.MuteButton.IsMuted)
                    {
                        sound_a_beep = true;
                    }
                }
            }
            ++_Timer_Tick_Count;
            if (sound_a_beep && App.Current.Configuration.IsAudibleAlarmEnabled)
            {
                System.Media.SystemSounds.Beep.Play();
            }

            // If there are blinking layers, attention should be drawn on the corresponding detailed info panel.
            if (any_blinks && !_Timer_LastAnyBlinks)
            {
                var v = blinking_layer_name.Split(new char[] { '.' }, 2);
                var group_name = v[0];
                for (int sg_index = 0; sg_index < ViewModel.Signal_Groups.Count; ++sg_index)
                {
                    if (ViewModel.Signal_Groups[sg_index].GroupName == group_name)
                    {
                        tabGroupDetails.SelectedIndex = sg_index;
                    }
                }
            }
            _Timer_LastAnyBlinks = any_blinks;

            // We have to keep the connection open...
            // Sometimes the connection drops without the socket noticing it; this is detected by using timeouts.
            var t0 = ViewModel.LastUpdateTime;
            var t1 = DateTime.Now;
            if (t0.HasValue)
            {
                TimeSpan diff = t1.Subtract(t0.Value);
                int total_seconds = (int)Math.Round(diff.TotalSeconds);
                this.Title = App.Title + " - Updated " + total_seconds.ToString() + " seconds ago (" + t0.Value.ToString() + ")";
            }
            else
            {
                this.Title = App.Title + " - Updated never!";
            }
            // Is it time for a reconnect?
            var plc_connection = ViewModel.PlcConnection; // avoid deep water.
            if (t1 > ViewModel.NextReconnectTime && plc_connection != null && plc_connection.IsConnected)
            {
                textboxLog.AppendLine("Data receive timeout, reconnecting!");
                try
                {
                    ViewModel.ConnectToPlc();
                }
                catch (Exception ex)
                {
                    ViewModel.LogLine("Reconnect failed: " + ex.Message);
                }
            }

            foreach (var chartpanel in ViewModel.ChartPanels)
            {
                chartpanel.TimerTick();
            }

            ViewModel.HistoryControl.TimerTick();
            ViewModel.TimerTick();
        }

        /// <summary>
        /// Open a connection to the PLC and return whether it succeeded.
        /// </summary>
        /// <param name="PlcConnection">Connection string in the form ip:port.</param>
        /// <returns>true iff connection succeeded, false otherwise.</returns>
        static bool _TestConnectToPlc(string PlcConnection)
        {
            try
            {
                var v = PlcConnection.Split(new char[] { ':' });
                using (var c = new TcpClient(v[0], int.Parse(v[1])))
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, App.TitleError);
                return false;
            }
        }

        #region DISPLAY SIGNALS
        private void Button_SetTo1_Click(object sender, RoutedEventArgs e)
        {
            _WriteSignalToPlcByButton(sender, 1);
        }
        private void Button_SetTo0_Click(object sender, RoutedEventArgs e)
        {
            _WriteSignalToPlcByButton(sender, 0);
        }

        private void _WriteSignalToPlcByButton(object Sender, int Value)
        {
            var button = Sender as Button;
            var signal = button.Tag as IOSignal;
            var new_kv = new KeyValuePair<string,int>(signal.Name.Length==0 ? signal.Id.ToString() : signal.Name, Value);
            ViewModel.PlcConnection.WriteSignals(new KeyValuePair<string,int>[] { new_kv });
        }
        #endregion

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // 1. Signal the thread to close.
            var plc_connection = ViewModel.PlcConnection;
            ViewModel.PlcConnection = null;
            if (plc_connection != null)
            {
                try
                {
                    plc_connection.Close();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, App.TitleError);
                }
            }

            // 2. Close the local database.
            var lsdb = ViewModel.LocalSignalDB;
            ViewModel.LocalSignalDB = null;
            if (lsdb != null)
            {
                try
                {
                    lsdb.Close();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, App.TitleError);
                }
            }

            e.Cancel = false;

            // 3. Configuration needs saving.
            try
            {
                App.Current.Store_Configuration();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, App.TitleError);
            }
        }

        #region Configuration

        /// <summary>
        /// Has the configuration been tested yes/no?
        /// </summary>
        public bool IsConfigurationTested
        {
            get { return (bool)GetValue(IsConfigurationTestedProperty); }
            set { SetValue(IsConfigurationTestedProperty, value); }
        }
        public static readonly DependencyProperty IsConfigurationTestedProperty =
            DependencyProperty.Register("IsConfigurationTested", typeof(bool), typeof(MainWindow), new PropertyMetadata(true));

        /// <summary>
        /// Our new configuration, if any.
        /// </summary>
        public Configuration CopyOfConfiguration
        {
            get { return (Configuration)GetValue(CopyOfConfigurationProperty); }
            set { SetValue(CopyOfConfigurationProperty, value); }
        }
        public static readonly DependencyProperty CopyOfConfigurationProperty =
            DependencyProperty.Register("CopyOfConfiguration", typeof(Configuration), typeof(MainWindow), new PropertyMetadata(null));

        static void _SettingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var self = d as MainWindow;
            if (self!=null)
            {
                self.IsConfigurationTested = false;
            }
        }

        private void Button_Test_Configuration(object sender, RoutedEventArgs e)
        {
            // 1. Have to test the beep :)
            System.Media.SystemSounds.Beep.Play();
            // 2. Try to connect.
            if (_TestConnectToPlc(CopyOfConfiguration.PlcConnection))
            {
                IsConfigurationTested = true;
                MessageBox.Show("Settings are correct!", App.TitleOK);
            }
        }

        private void Button_Apply_Configuration(object sender, RoutedEventArgs e)
        {
            if (IsConfigurationTested)
            {
                try {
                    // 1. Store the configuration.
                    var ap = App.Current;
                    ap.Configuration.CopyFrom(CopyOfConfiguration);
                    ap.Store_Configuration();

                    // 2. Use the configuration.
                    ViewModel.ConnectToPlc();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, App.TitleError);
                }
            }
            else
            {
                MessageBox.Show("Test the configuration first!", App.TitleError);
            }
        }
        #endregion Configuration

        private void TabControl_InfoPanel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ViewModel.SetVisibilityOnCorrespondingLayers(e.AddedItems, true);
            ViewModel.SetVisibilityOnCorrespondingLayers(e.RemovedItems, false);
        }

        PrintDialog _PrintDialog = new PrintDialog()
        {
        };

        static Tuple<DependencyObject, MethodInfo> _FindChildWithPrintMethod(DependencyObject control)
        {
            int n = VisualTreeHelper.GetChildrenCount(control);
            // 1. Try to get the method from children.
            for (int child_index = 0; child_index < n; ++child_index)
            {
                var ch = VisualTreeHelper.GetChild(control, child_index);
                var mtc = ch.GetType().GetMethod("Print");
                if (mtc != null)
                {
                    return new Tuple<DependencyObject, MethodInfo>(ch, mtc);
                }
            }

            // 2. Try to get the print method from grandchildren.
            for (int child_index = 0; child_index < n; ++child_index)
            {
                var ch = VisualTreeHelper.GetChild(control, child_index);
                var r = _FindChildWithPrintMethod(ch);
                if (r != null)
                {
                    return r;
                }
            }

            // 3. Fail.
            return null;
        }

        private void ButtonPrint_Click(object sender, RoutedEventArgs e)
        {
            var ti = tabcontrolMain.SelectedItem as TabItem;
            var tc = ti.Content as UIElement;
            var special_print = _FindChildWithPrintMethod(tc);
            if (_PrintDialog.ShowDialog() == true)
            {
                if (special_print == null)
                {
                    _PrintDialog.PrintVisual(tc, ti.Header.ToString());
                    // MessageBox.Show("Visuaali '" + ti.Header + "' ei saa trükkida!");
                }
                else
                {
                    special_print.Item2.Invoke(special_print.Item1, new object[] { _PrintDialog });
                }
            }
        }
    }
}
