using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace ControlPanel
{
    /// <summary>
    /// Interaction logic for GroupPump.xaml
    /// </summary>
    public partial class SignalGroupPump : UserControl
    {
        public string PumpName
        {
            get { return (string)GetValue(PumpNameProperty); }
            set { SetValue(PumpNameProperty, value); }
        }
        public static readonly DependencyProperty PumpNameProperty =
            DependencyProperty.Register("PumpName", typeof(string), typeof(SignalGroupPump), new PropertyMetadata(""));

        public string PumpStatus
        {
            get { return (string)GetValue(PumpStatusProperty); }
            set { SetValue(PumpStatusProperty, value); }
        }
        public static readonly DependencyProperty PumpStatusProperty =
            DependencyProperty.Register("PumpStatus", typeof(string), typeof(SignalGroupPump), new PropertyMetadata(""));

        public string PumpMode
        {
            get { return (string)GetValue(PumpModeProperty); }
            set { SetValue(PumpModeProperty, value); }
        }
        public static readonly DependencyProperty PumpModeProperty =
            DependencyProperty.Register("PumpMode", typeof(string), typeof(SignalGroupPump), new PropertyMetadata(""));

        public string PumpAlarms
        {
            get { return (string)GetValue(PumpAlarmsProperty); }
            set { SetValue(PumpAlarmsProperty, value); }
        }
        public static readonly DependencyProperty PumpAlarmsProperty =
            DependencyProperty.Register("PumpAlarms", typeof(string), typeof(SignalGroupPump), new PropertyMetadata(""));

        readonly PlcConnection _Connection;
        readonly IOSignal _RemoteControl;
        readonly IOSignal _RemoteRun;
        readonly IOSignal _SignalRun;
        readonly IOSignal _ProtectionAlarm;
        readonly IOSignal _LeakAlarm;
        readonly IOSignal _ThermalAlarm;
        readonly IOSignal _ModeAuto;
        readonly IOSignal _Running;

        List<KeyValuePair<string, int>> _Command_Auto = new List<KeyValuePair<string, int>>();
        List<KeyValuePair<string, int>> _Command_Start = new List<KeyValuePair<string, int>>();
        List<KeyValuePair<string, int>> _Command_Stop = new List<KeyValuePair<string, int>>();

        FontWeight _DefaultFontWeight = FontWeights.Normal;
        FontWeight _ActiveFontWeight = FontWeights.Bold;
        List<Button> _CommandButtons = new List<Button>();

        public SignalGroupPump(
            PlcConnection Connection,
            IOSignal RemoteControl,
            IOSignal RemoteRun,
            IOSignal SignalRun,
            IOSignal ProtectionAlarm,
            IOSignal LeakAlarm,
            IOSignal ThermalAlarm,
            IOSignal ModeAuto,
            IOSignal Running
            )
        {
            this._Connection = Connection;
            this._RemoteControl = RemoteControl;
            this._RemoteRun = RemoteRun;
            this._SignalRun = SignalRun;
            this._ProtectionAlarm = ProtectionAlarm;
            this._LeakAlarm = LeakAlarm;
            this._ThermalAlarm = ThermalAlarm;
            this._ModeAuto = ModeAuto;
            this._Running = Running;

            _Command_Auto.Add(new KeyValuePair<string, int>(_RemoteControl.Name, 0));

            _Command_Start.Add(new KeyValuePair<string, int>(_RemoteControl.Name, 1));
            _Command_Start.Add(new KeyValuePair<string, int>(_RemoteRun.Name, 1));

            _Command_Stop.Add(new KeyValuePair<string, int>(_RemoteControl.Name, 1));
            _Command_Stop.Add(new KeyValuePair<string, int>(_RemoteRun.Name, 0));

            InitializeComponent();
        }

        void Control_Loaded(object sender, RoutedEventArgs e)
        {
            _CommandButtons.Add(buttonAuto);
            _CommandButtons.Add(buttonStart);
            _CommandButtons.Add(buttonStop);
            _DefaultFontWeight = buttonAuto.FontWeight;
        }

        void Button_Automatic_Click(object sender, RoutedEventArgs e)
        {
            _Connection.WriteSignals(_Command_Auto);
            _UpdateColours(sender);
        }

        void Button_Start_Click(object sender, RoutedEventArgs e)
        {
            _Connection.WriteSignals(_Command_Start);
            _UpdateColours(sender);
        }

        void Button_Stop_Click(object sender, RoutedEventArgs e)
        {
            _Connection.WriteSignals(_Command_Stop);
            _UpdateColours(sender);
        }

        void _UpdateColours(object sender)
        {
            var sb = sender as Button;
            foreach (var button in _CommandButtons)
            {
                button.FontWeight = button == sb ? _ActiveFontWeight : _DefaultFontWeight;
            }
        }

        StringBuilder _sb = new StringBuilder();
        public void UpdateDisplay()
        {
            PumpStatus = _Running.Value==0 ? "Seisab" : "Pumpab";
            PumpMode = _ModeAuto.Value==0
                            ? "Käsijuhtimisel."
                            : (_RemoteControl.Value==0 ? "Automaatjuhtimisel." : "Operaator.");
            _sb.Clear();
            if (_ProtectionAlarm.Value != 0)
            {
                _sb.Append("Mootorikaitse");
            }
            if (_LeakAlarm != null && _LeakAlarm.Value != 0)
            {
                if (_sb.Length > 0)
                {
                    _sb.Append(", ");
                }
                _sb.Append("Vesi lekib");
            }
            if (_ThermalAlarm != null && _ThermalAlarm.Value == 0)
            {
                if (_sb.Length > 0)
                {
                    _sb.Append(", ");
                }
                _sb.Append("Ülekuumenenud");
            }
            PumpAlarms = _sb.Length == 0 ? "Puuduvad" : (_sb.ToString() + ".");
        }
    }
}
