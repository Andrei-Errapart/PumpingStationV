using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Xml;

using System.Windows;           // DependencyObject
using System.Windows.Controls;  // UI controls.
// using System.Net.Sockets;
using System.Net;


namespace ControlPanel
{
    /// <summary>
    /// Global data (and helper functions) structure for the ControlPanel.
    /// 
    /// Some of the properties a
    /// </summary>
    public class ControlPanelViewModel : DependencyObject
    {
        #region GUI ELEMENTS
        /// <summary>
        /// Are we connected?
        /// </summary>
        public bool IsConnected
        {
            get { return (bool)GetValue(IsConnectedProperty); }
            set { SetValue(IsConnectedProperty, value); }
        }
        public static readonly DependencyProperty IsConnectedProperty =
            DependencyProperty.Register("IsConnected", typeof(bool), typeof(ControlPanelViewModel), new PropertyMetadata(false, _IsConnectedChanged));

        /// <summary>
        /// Log changes to the system log, as necessary :)
        /// </summary>
        /// <param name="d"></param>
        /// <param name="e"></param>
        private static void _IsConnectedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var vm = d as ControlPanelViewModel;
            var before = (bool)e.OldValue;
            var after = (bool)e.NewValue;
            if (before)
            {
                if (!after)
                {
                    vm.LogLine("Disconnected!");
                }
            }
            else
            {
                if (after)
                {
                    vm.LogLine("Connected!");
                }
            }
        }

        /// <summary>
        /// Signals to be displayed.
        /// </summary>
        public ObservableCollection<IOSignal> Signals
        {
            get { return (ObservableCollection<IOSignal>)GetValue(SignalsProperty); }
            set { SetValue(SignalsProperty, value); }
        }
        public static readonly DependencyProperty SignalsProperty =
            DependencyProperty.Register("Signals", typeof(ObservableCollection<IOSignal>), typeof(ControlPanelViewModel), new PropertyMetadata(null));

        /// <summary>
        /// Synchronization status.
        /// </summary>
        public SynchronizationStatus SynchronizationStatus { get; set; }

        /// <summary>
        /// Details.
        /// 
        /// TODO: We are not supposed to host GUI controls; only data that can be binded to should be here. Shall we use templates?
        /// </summary>
        public TabControl TabGroupDetails;

        /// <summary>
        /// Pump charts, etc.
        /// 
        /// TODO: We are not supposed to host GUI controls; only data that can be binded to should be here. Shall we use templates?
        /// </summary>
        public TabControl TabControlCharts;

        /// <summary>
        /// Canvas displaying all the cool stuff.
        /// 
        /// TODO: We are not supposed to host GUI controls; only data that can be binded to should be here. Shall we use templates?
        /// </summary>
        public Canvas MainCanvas;

        /// <summary>Layers on the main canvas (scheme).</summary>
        public Dictionary<string, SchemeLayer> Layers;

        /// <summary>Table of all signals.</summary>
        public Dictionary<int, IOSignal> SignalTable;
        /// <summary>Dictionary of all signals.</summary>
        public Dictionary<string, IOSignal> SignalDict;

        /// <summary>
        /// Signal groups displayed.
        /// </summary>
        public List<SignalGroupToplevel> Signal_Groups = new List<SignalGroupToplevel>();

        /// <summary>
        /// Buttons on the scheme for the activation of tab items.
        /// </summary>
        List<Button> SchemeButtons = new List<Button>();

        public HistoryControl HistoryControl;
        #endregion // GUI ELEMENTS


        /// <summary>
        /// Local configuration settings.
        /// </summary>
        public Configuration LocalConfiguration { get; set; }

        public delegate void LogLineHandler(string Text);
        /// <summary>
        /// Log a line to the system log. This can be called from any thread.
        /// </summary>
        public LogLineHandler LogLine;

        public PlcConnection PlcConnection;
        /// <summary>
        /// Maintained by the PlcConnection.
        /// </summary>
        public LocalSignalDB LocalSignalDB;

        /// <summary>
        /// Scheme program statements.
        /// </summary>
        public List<SchemeStatement> SchemeStatements = new List<SchemeStatement>();
        /// <summary>
        /// Last signal update time.
        /// </summary>
        public DateTime? LastUpdateTime = null;

        /// <summary>
        /// Time for the next connection.
        /// </summary>
        public DateTime NextReconnectTime = DateTime.Now;

        public void PostponeReconnect()
        {
            NextReconnectTime = DateTime.Now.AddSeconds(LocalConfiguration.PlcDataTimeout);
        }

        /// <summary>
        /// Connect to the PLC using current settings.
        /// </summary>
        public void ConnectToPlc()
        {
            PostponeReconnect();

            var v = LocalConfiguration.PlcConnection.Split(new char[] { ':' });
            var connect_to = new IPEndPoint(IPAddress.Parse(v[0]), int.Parse(v[1]));
            var current_connection = PlcConnection;
            if (current_connection != null)
            {
                current_connection.Close();
                this.PlcConnection = null;
            }
            this.PlcConnection = new PlcConnection(this, connect_to);
        }



        /// <summary>
        /// Version of configuration.
        /// </summary>
        int ConfigurationVersion = -1;

        void _HandleConfigurationFromPlc(int Version, byte[] ConfigurationFile)
        {
            PostponeReconnect();

            this.ConfigurationVersion = Version;
            using (var ms = new System.IO.MemoryStream(ConfigurationFile))
            {
                List<IOSignal> new_signals = new List<IOSignal>();
                var infopanel_groups = new List<SignalGroup>();
                var charts_groups = new List<SignalGroup>();
                var workhour_groups = new List<SignalGroup>();
                var station_groups = new List<SignalGroup>();
                _ParsePlcConfiguration(new_signals, infopanel_groups, charts_groups, workhour_groups, station_groups, ms);

                // Tables in _SchemeContext need updating.
                Signals.Clear();
                SignalTable.Clear();
                SignalDict.Clear();
                foreach (var ios in new_signals)
                {
                    Signals.Add(ios);
                    if (ios.Name.Length > 0)
                    {
                        SignalDict.Add(ios.Name, ios);
                    }
                    if (ios.Id >= 0)
                    {
                        SignalTable.Add(ios.Id, ios);
                    }
                }

                // 2. Update the info panel TabGroupDetails.
                TabGroupDetails.Items.Clear();
                Signal_Groups.Clear();

                foreach (var g in infopanel_groups)
                {
                    // 1. Toplevel.
                    var gt = new SignalGroupToplevel();
                    gt.GroupName = g.Name;
                    foreach (var s in g.Signals)
                    {
                        gt.DisplaySignals.Add(s.Value);
                    }
                    // 2. Pumps
                    foreach (var gg in g.Groups)
                    {

                        var gpump = new SignalGroupPump(
                            PlcConnection,
                            _FetchByKey(gg.Signals, "REMOTE_CONTROL"),
                            _FetchByKey(gg.Signals, "REMOTE_RUN"),
                            _FetchByKey(gg.Signals, "RUN"),
                            _FetchByKey(gg.Signals, "PROTECT"),
                            _FetchByKey(gg.Signals, "LEAK"),
                            _FetchByKey(gg.Signals, "TERM"),
                            _FetchByKey(gg.Signals, "AUTO"),
                            _FetchByKey(gg.Signals, "RUNNING")
                            ) { PumpName = gg.Name, };
                        gt.AddPump(gpump);
                    }
                    // 3. Add it to the tab control and to the list, too.
                    TabGroupDetails.Items.Add(new TabItem() { Header = g.Name, Content = gt });
                    Signal_Groups.Add(gt);
                }

                // 3. Update charts structure.
                TabControlCharts.Items.Clear();
                foreach (var g in charts_groups)
                {
                    var signal_indices = (from ns in g.Signals select Signals.IndexOf(ns.Value)).ToList();
                    TabControlCharts.Items.Add(new TabItem() { Header = g.Name, Content = new BitChartPanel { DataContext = this, SignalIndices = signal_indices, }, });
                }

                // Clean up the selection mess of the details panels TabControl.
                SetVisibilityOnCorrespondingLayers(TabGroupDetails.Items, false);
                SetVisibilityOnCorrespondingLayers(new object[] { TabGroupDetails.SelectedItem }, true);

                // HistoryControl.Stations
                HistoryControl.Stations.Clear();
                foreach (var g in station_groups)
                {
                    HistoryControl.Stations.Add(g);
                }

                // New buttons on the scheme are needed.
                for (int button_index = SchemeButtons.Count - 1; button_index >= 0; --button_index)
                {
                    MainCanvas.Children.Remove(SchemeButtons[button_index]);
                    SchemeButtons.RemoveAt(button_index);
                }

                // Find the insert position such that selection buttons will be placed below mute buttons.
                int insert_position = 0;
                for (int ch_index=MainCanvas.Children.Count-1; ch_index>=0; --ch_index)
                {
                    var mb = MainCanvas.Children[ch_index] as MuteButton;
                    if (mb == null)
                    {
                        insert_position = ch_index+1;
                        break;
                    }
                }

                // foreach (var si in _Signal_Groups)
                for (int sg_index = 0; sg_index < Signal_Groups.Count; ++sg_index)
                {
                    var si = Signal_Groups[sg_index];
                    var layer_name = si.GroupName + ".Selected";
                    SchemeLayer scheme_layer;
                    if (Layers.TryGetValue(layer_name, out scheme_layer))
                    {
                        // ok, new button!
                        var bounds = scheme_layer.Layer.Bounds;
                        var b = new Button()
                        {
                            Content = "Push ME!",
                            FontSize = 30,
                            Foreground = System.Windows.Media.Brushes.Red,
                            Opacity = 0.0,
                            HorizontalAlignment = HorizontalAlignment.Stretch,
                            VerticalAlignment = VerticalAlignment.Stretch,
                            Width = bounds.Width,
                            Height = bounds.Height,
                        };
                        b.SetValue(Canvas.LeftProperty, bounds.Left);
                        b.SetValue(Canvas.TopProperty, bounds.Top);
                        b.Tag = sg_index;
                        b.Click += _SchemeButton_OnClick;
                        MainCanvas.Children.Insert(insert_position, b);
                        SchemeButtons.Add(b);
                    }
                }

                // 4. Working log update.
                var new_working_times = new ObservableCollection<WorkingTimeCell>();
                foreach (var wg in workhour_groups)
                {
                    foreach (var ks in wg.Signals)
                    {
                        var wt = new WorkingTimeCell()
                        {
                            DisplayName = ks.Key,
                            LastKnownValue = new Tuple<bool,int>(false, 0),
                            SignalIndex = Signals.IndexOf(ks.Value),
                            WorkingTicks = 0,
                        };
                        new_working_times.Add(wt);
                    }
                }
                SynchronizationStatus.WorkingTimes = new_working_times;

                // For now, that's all.
                LogLine(string.Format("Received configuration, {0} signals in total.", new_signals.Count));

                // Probably we'll have to reprocess it...
                if (SynchronizationStatus.IsFinished)
                {
                    OnSynchronizationAttained();
                }
            }
            this.LastUpdateTime = DateTime.Now;
        }

        public void HandleMessageFromPlc(PlcConnection sender, MessageFromPlc message)
        {
            // Messages might be from sources dead long ago.
            if (sender != this.PlcConnection)
            {
                return;
            }

            try
            {
                if (message.HasId)
                {
                    // 1. Any configuration?
                    if (message.HasOOBConfiguration)
                    {
                        var cfg = message.OOBConfiguration;
                        if (cfg.HasVersion && cfg.HasConfigurationFile)
                        {
                            _HandleConfigurationFromPlc(cfg.Version, cfg.ConfigurationFile.ToByteArray());
                        }
                    } // message.HasOOBConfiguration

                    // 2. Any signals?
                    if (message.HasOOBSignalValues)
                    {
                        var oob_signal = message.OOBSignalValues;
                        if (oob_signal.HasVersion && oob_signal.HasTimeMs && oob_signal.HasSignalValues)
                        {
                            PostponeReconnect();
                            // TODO: handle version and time.
                            var encoded_signals = oob_signal.SignalValues.ToByteArray();
                            _Update_Signals(encoded_signals, 0, encoded_signals.Length);
                            this.LastUpdateTime = DateTime.Now;

                            foreach (var st in SchemeStatements)
                            {
                                st.Execute();
                            }

                            // Charts!
                            var signals = new LocalSignalDB.SignalValuesType()
                            {
                                Id = oob_signal.RowId,
                                Version = oob_signal.Version,
                                Timestamp = oob_signal.GetTimestamp().Ticks,
                                Values = encoded_signals,
                            };
                            foreach (var chartpanel in ChartPanels)
                            {
                                chartpanel.AppendSignalValues(signals);
                            }
                            HistoryControl.AppendSignalValues(signals);
                        }
                    } // message.HasOOBSignalValues
                } // message.HasId
            }
            catch (Exception ex)
            {
                // MessageBox.Show(App.TitleError, ex.Message, MessageBoxButton.OK);
                LogLine("_PacketHandler error: " + ex.Message);
                LogLine("Stack trace: " + ex.StackTrace);
            }
        }

        Tuple<bool, int>[] _UnpackBuffer = null;
        /// <summary>
        /// Update the _Signals.
        /// </summary>
        /// <param name="Packet">Source packet.</param>
        /// <param name="Offset">Offset to start with.</param>
        /// <param name="Length">Length of payload, in bytes.</param>
        void _Update_Signals(byte[] Packet, int Offset, int Length)
        {
            if (_UnpackBuffer == null || _UnpackBuffer.Length < Signals.Count)
            {
                _UnpackBuffer = new Tuple<bool, int>[Signals.Count];
            }
            PlcConnection.ExtractSignals(_UnpackBuffer, Packet);
            int signal_index = 0;

            foreach (var ios in Signals)
            {
                var up = _UnpackBuffer[signal_index];
                ios.Update(up.Item1, up.Item2);

                ++signal_index;
            }

            foreach (var sg in Signal_Groups)
            {
                sg.UpdateDisplay();
            }
        }

        /// <summary>
        /// Set visibility on the layers in SchemeContext whose name matches 'ti.Header + ".Selected"', where ti is an TabItem in "items".
        /// </summary>
        /// <param name="items"></param>
        /// <param name="action"></param>
        public void SetVisibilityOnCorrespondingLayers(System.Collections.IList items, bool IsVisible)
        {
            foreach (var i in items)
            {
                var ti = i as TabItem;
                if (ti != null)
                {
                    string header = ti.Header as string;
                    SchemeLayer layer = null;
                    if (header != null && Layers.TryGetValue(header + ".Selected", out layer))
                    {
                        layer.IsVisible = IsVisible;
                    }
                }
            }
        }

        void _SchemeButton_OnClick(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn != null && btn.Tag != null)
            {
                var index = (int)btn.Tag;
                TabGroupDetails.SelectedIndex = index;
            }
        }

        static IOSignal _FetchByKey(IEnumerable<KeyValuePair<string, IOSignal>> UsedSignals, string Key)
        {
            return (from kv in UsedSignals where kv.Key == Key select kv.Value).SingleOrDefault();
        }

        static int _FetchIntegerAttribute(XmlTextReader reader, string attribute_name)
        {
            return int.Parse(reader.GetAttribute(attribute_name));
        }

        /// <summary>
        /// Parse PLC configuration.
        /// </summary>
        /// <param name="NewSignals"></param>
        /// <param name="Input">Configuration to be parsed.</param>
        static void _ParsePlcConfiguration(
            List<IOSignal> NewSignals,
            List<SignalGroup> InfopanelGroups,
            List<SignalGroup> ChartGroups,
            List<SignalGroup> WorkHoursGroups,
            List<SignalGroup> StationGroups,
            System.IO.Stream Input)
        {
            // List of top-level groups fetched.
            List<SignalGroup> groups = null;
            // Stack of groups during parsing.
            List<SignalGroup> groupstack = new List<SignalGroup>();

            // 1. Parse the input.
            using (var reader = new XmlTextReader(Input))
            {
                while (reader.Read())
                {
                    // 1. Signals.
                    if (reader.NodeType == XmlNodeType.Element && reader.Name == "signal")
                    {
                        IOSignal ios;
                        int ios_id = _FetchIntegerAttribute(reader, "id");
                        string ios_name = reader.GetAttribute("name") ?? "";
                        string ios_type_name = reader.GetAttribute("type");
                        string ios_device = reader.GetAttribute("device") ?? "";
                        string ios_ioindex = reader.GetAttribute("ioindex") ?? "";
                        string ios_description = reader.GetAttribute("description") ?? "";
                        string ios_text0 = reader.GetAttribute("text0") ?? "0";
                        string ios_text1 = reader.GetAttribute("text1") ?? "1";
                        IOSignal.TYPE? ios_type = IOSignal.TypeOfString(ios_type_name);
                        if (ios_type.HasValue)
                        {
                            ios = new IOSignal(ios_id, ios_name, false, ios_type.Value, ios_device, ios_ioindex, ios_description, ios_text0, ios_text1);
                        }
                        else
                        {
                            throw new ApplicationException("PlcConfiguration.startElement: Invalid value for signal type: '" + ios_type_name + "'");
                        }
                        NewSignals.Add(ios);
                    }
                    // 2. Variables
                    else if (reader.NodeType == XmlNodeType.Element && reader.Name == "variable")
                    {
                        // FIXME: do what?
                        IOSignal ios;
                        string ios_name = reader.GetAttribute("name");
                        string ios_type_name = reader.GetAttribute("type");
                        IOSignal.TYPE? ios_type = IOSignal.TypeOfString(ios_type_name);
                        string ios_description = reader.GetAttribute("description") ?? "";
                        string ios_text0 = reader.GetAttribute("text0") ?? "0";
                        string ios_text1 = reader.GetAttribute("text1") ?? "1";
                        if (ios_name == null)
                        {
                            throw new ApplicationException("PlcConfiguration.startElement: Variable without a name detected!");
                        }
                        if (ios_type.HasValue)
                        {
                            ios = new IOSignal(-1, ios_name, true, ios_type.Value, "", "", ios_description, ios_text0, ios_text1);
                        }
                        else
                        {
                            throw new ApplicationException("PlcConfiguration.startElement: Invalid value for variable type: '" + ios_type_name + "'");
                        }
                        NewSignals.Add(ios);
                    }
                    // 3. Groups
                    else if (reader.Name == "group")
                    {
                        if (reader.NodeType == XmlNodeType.Element)
                        {
                            var g = new SignalGroup() { Name = reader.GetAttribute("name"), };
                            var lg = groupstack.LastOrDefault();
                            (lg == null ? groups : lg.Groups).Add(g);
                            groupstack.Add(g);
                        }
                        else if (reader.NodeType == XmlNodeType.EndElement)
                        {
                            // one off from the stack...
                            groupstack.RemoveAt(groupstack.Count - 1);
                        }
                    }
                    else if (reader.Name == "scheme")
                    {
                        switch (reader.NodeType)
                        {
                            case XmlNodeType.Element:
                                {
                                    string stype = reader.GetAttribute("type");
                                    if (stype == "charts")
                                    {
                                        groups = ChartGroups;
                                    }
                                    else if (stype == "infopanel")
                                    {
                                        groups = InfopanelGroups;
                                    }
                                    else if (stype == "workhours")
                                    {
                                        groups = WorkHoursGroups;
                                    }
                                    else if (stype == "stations")
                                    {
                                        groups = StationGroups;
                                    }
                                    else
                                    {
                                        // just don't use it, but don't crack either.
                                        // LogLine("_ParsePlcConfiguration: unknown scheme type '" + stype + "'.");
                                        groups = new List<SignalGroup>();
                                    }
                                }
                                break;
                            case XmlNodeType.EndElement:
                                break;
                        }
                    }
                    // 4. UsedSignals.
                    else if (reader.Name == "usesignal" && reader.NodeType == XmlNodeType.Element)
                    {
                        string ios_key = reader.GetAttribute("key") ?? "";
                        string ios_name = reader.GetAttribute("signal") ?? "";
                        // First, locate signal by name.
                        IOSignal ios = NewSignals.SingleOrDefaultByNameOrId(ios_name);
                        if (ios != null)
                        {
                            groupstack.Last().Signals.Add(new KeyValuePair<string, IOSignal>(ios_key, ios));
                        }
                    }
                    // 5. usedevice
                    else if (reader.Name == "usedevice" && reader.NodeType == XmlNodeType.Element)
                    {
                        string device = reader.GetAttribute("device");
                        if (device != null)
                        {
                            groupstack.Last().Devices.Add(device);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// List of chart panels, usable by LINQ.
        /// </summary>
        public IEnumerable<BitChartPanel> ChartPanels
        {
            get
            {
                foreach (var o in TabControlCharts.Items)
                {
                    var ti = o as TabItem;
                    if (ti != null)
                    {
                        var bcp = ti.Content as BitChartPanel;
                        if (bcp != null)
                        {
                            yield return bcp;
                        }
                    }
                }
                yield break;
            }
        }

        /// <summary>
        /// This is called by LocalSignalDb when the synchronization has been achieved.
        /// </summary>
        public void OnSynchronizationAttained()
        {
            // 1. Optimize by combining the last things.
            bool any_last = false;
            TimeSpan max_last = TimeSpan.FromSeconds(0.0);
            var chartpanels_last = from o in ChartPanels where o.IsRunningSelected select o;
            foreach (var bcp in chartpanels_last)
            {
                var last = bcp.TimeSpan;
                if (last > max_last)
                {
                    max_last = last;
                    any_last = true;
                }
            }

            // 2. Process all the charts which have bit "LAST" set.
            DateTime timestamp_end = DateTime.Now;
            DateTime timestamp_begin = timestamp_end.Subtract(max_last);
            if (any_last && max_last.TotalSeconds>0.0)
            {
                // ok, GOT IT!
                var rows = LocalSignalDB.FetchByTimeRangeIncluding(timestamp_begin, timestamp_end);
                foreach (var ti in chartpanels_last)
                {
                    ti.ProcessValidSignalValues(rows);
                }

                // 2b. Process the remaining ones.
                foreach (var bcp in (from o in ChartPanels where !o.IsRunningSelected select o))
                {
                    bcp.RedrawChart();
                }
            }
            else
            {
                // 2c. Process the remaining ones.
                foreach (var bcp in ChartPanels)
                {
                    bcp.RedrawChart();
                }
            }

            // 3. History processing.
            HistoryControl.ReloadHistory();
        }

        const int WORKTIMES_BATCH_SIZE = 100;

        /// <summary>
        /// Process the 
        /// Called once per second.
        /// </summary>
        public void TimerTick()
        {
            var db = LocalSignalDB;
            // TODO: load the data from the cache.
            if (db != null && SynchronizationStatus.IsFinished)
            {
                try
                {
                    // Get next batch for update, if any.
                    int tail_id = Math.Max(SynchronizationStatus.WorkingTimes_TrackId, SynchronizationStatus.RemoteTailId);
                    int head_id = Math.Min(tail_id + WORKTIMES_BATCH_SIZE, SynchronizationStatus.RemoteHeadId);
                    if (head_id > tail_id)
                    {
                        var rows = db.FetchByIdRange(tail_id, head_id);
                        var n = Signals.Count;
                        var signal_values = new Tuple<bool, int>[n];
                        foreach (var r in rows)
                        {
                            if (r.Id == SynchronizationStatus.WorkingTimes_TrackId || SynchronizationStatus.WorkingTimes_Timestamp==0)
                            {
                                // Update the times.
                                this.PlcConnection.ExtractSignals(signal_values, r.Values);
                                var dticks = r.Timestamp - SynchronizationStatus.WorkingTimes_Timestamp;
                                var dt = TimeSpan.FromTicks(dticks);
                                // Sum up the times if needed.
                                if (SynchronizationStatus.WorkingTimes_Timestamp != 0 && dticks > 0)
                                {
                                    foreach (var wt in SynchronizationStatus.WorkingTimes)
                                    {
                                        var v2 = signal_values[wt.SignalIndex];
                                        if (wt.LastKnownValue.Item1 && wt.LastKnownValue.Item2 != 0 && v2.Item1)
                                        {
                                            wt.WorkingTime = wt.WorkingTime.Add(dt);
                                            wt.WorkingTicks += dticks;
                                        }
                                    }
                                }
                                foreach (var wt in SynchronizationStatus.WorkingTimes)
                                {
                                    wt.LastKnownValue = signal_values[wt.SignalIndex];
                                }
                                SynchronizationStatus.WorkingTimes_TrackId = r.Id + 1;
                                SynchronizationStatus.WorkingTimes_Timestamp = r.Timestamp;
                            }
                            else
                            {
                                break;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogLine("Error when updating working hours: " + ex.Message);
                }
            }
        }
    } // class ControlPanelViewModel
} // namespace ControlPanel
