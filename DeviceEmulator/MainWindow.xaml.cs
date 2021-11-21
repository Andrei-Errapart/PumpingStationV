using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace DeviceEmulator
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        IPAddress _address = IPAddress.Loopback;
        int _port = 1502;
        IPEndPoint _localEP;
        Socket _listener;

        IAsyncResult _listen_result;
        List<ModbusSlave> _modbus_slaves = new List<ModbusSlave>();
        Dictionary<byte, IModbusDataStore> _modbus_datastore = new Dictionary<byte, IModbusDataStore>();

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // _modbus_datastore
            foreach (var ch in grid.Children)
            {
                var io = ch as IOModuleControl;
                if (io != null)
                {
                    _modbus_datastore.Add((byte)io.Address, io);
                }
            }

            try
            {
                // create and start the TCP slave
                _localEP = new IPEndPoint(IPAddress.Loopback, _port);
                _listener = new Socket(_localEP.Address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                _listener.Bind(_localEP);
                _listener.Listen(10);

                _listen_result = _listener.BeginAccept(_SocketAcceptCallback, this);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error");
            }
        }

        static void _SocketAcceptCallback(IAsyncResult ar)
        {
            // Get the socket that handles the client request.
            var self = ar.AsyncState as MainWindow;
            Socket conn = self._listener.EndAccept(ar);


            // Create the state object.
            var ms = new ModbusSlave(conn, self._modbus_datastore, self.Dispatcher);
            self._modbus_slaves.Add(ms);

            // New stuff...
            self._listen_result = self._listener.BeginAccept(_SocketAcceptCallback, self);
        }

        private void Button_Stop_Click(object sender, RoutedEventArgs e)
        {
#if (false)
            var sb = new StringBuilder();
            const string indent = "    ";
            foreach (var ch in grid.Children)
            {
                var box = ch as IOModuleControl;
                if (box != null)
                {
                    int box_addr = box.Address;
                    var src_channel = box_addr == 0 ? "imod" : ("m" + box_addr.ToString());
                    for (int pin_index=0; pin_index<box.Pins.Count; ++pin_index)
                    {
                        var id = box_addr * 100 + pin_index + 1;
                        var pin = box.Pins[pin_index];
                        var is_input = pin.PinName.StartsWith("DI");
                        var param_type = is_input ? "READ" : "WRITE";
                        var src_id = box_addr == 0 ? pin.PinName : (pin_index + 1).ToString();
                        sb.AppendLine(indent + "	<parameter>");
                        sb.AppendLine(indent + "		<id>" + id.ToString() + "</id>");
                        sb.AppendLine(indent + "		<label>" + pin.SignalName + "</label>");
                        sb.AppendLine(indent + "		<description>" + pin.SignalDescription + "</description>");
                        sb.AppendLine(indent + "		<source-channel channel-name=\"" + src_channel + "\" parameter-id=\"" + src_id.ToString() + "\" parameter-type=\"" + param_type + "\"/>");
                        sb.AppendLine(indent + "		<access-channel channel-name=\"eth\" parameter-id=\"" + id.ToString() + "\" parameter-type=\"" + param_type + "\"/>");
                        sb.AppendLine(indent + "	</parameter>");
                    }
                }
            }
            System.Windows.Clipboard.SetText(sb.ToString());
#endif
            if (_listener != null)
            {
                _listener.Close();
                _listener = null;
            }
        }
    }
}
