using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Threading;

using System.Net;
using System.Net.Sockets;

namespace ControlPanel
{
    /// <summary>
    /// Persistent connection to the PLC. Reconnects on timeouts, etc. Takes care of the LocalSignalDb, too.
    /// </summary>
    public class PlcConnection
    {
        /// <summary>
        /// Synchronization interval, milliseconds. The lower interval, the faster synchronization.
        /// </summary>
        public const int SYNCHRONIZATION_INTERVAL_MS = 100;

        ControlPanelViewModel _ViewModel;

        public PlcConnection(
            ControlPanelViewModel ViewModel,
            IPEndPoint RemoteEndPoint)
        {
            // 1. Set up the fields.
            this._ViewModel = ViewModel;
            this._RemoteEndPoint = RemoteEndPoint;
            this._TcpClient = null;
            this._TcpClientStream = null;
            this._Thread = new System.Threading.Thread(this._MainLoop);

            // 2. Start the reading thread..
            this._Thread.Start();
        }

        /// <summary>
        /// Write given signals.
        /// </summary>
        /// <param name="SignalValues"></param>
        public void WriteSignals(IList<KeyValuePair<string,int>> SignalValues)
        {
            var builder = MessageToPlc.CreateBuilder();
            builder.Id = _NextRequestId++;

            foreach (var sv in SignalValues)
            {
                var sv_builder = new MessageToPlc.Types.SignalAndValue.Builder();
                sv_builder.Name = sv.Key;
                sv_builder.Value = sv.Value;
                builder.AddQuerySetSignals(sv_builder);
            }

            var query = builder.Build();

            // 3. Down the throat of PLC!
            query.WriteDelimitedTo(_TcpClientStream);
        }

        /// <summary>
        /// Are we connected?
        /// </summary>
        public bool IsConnected { get { return _TcpClient != null && _TcpClient.Client != null && _TcpClient.Client.Connected; } }

        /// <summary>
        /// Close connection.
        /// </summary>
        public void Close()
        {
            // Signal end of work.
            this._RemoteEndPoint = null;
            // Close the tcp client, if any.
            var tcpclient = _TcpClient;
            // signal the reading thread to be closed.
            _TcpClient = null;
            _TcpClientStream = null;
            if (tcpclient != null)
            {
                var socket = tcpclient.Client;
                if (socket != null && socket.Connected)
                {
                    try
                    {
                        tcpclient.Close();
                    }
                    catch (Exception)
                    {
                        // pass.
                    }
                }
            }
            _DispatchConnectionStatus(false);
        }

        /// <summary>
        /// Extract signals from the packet.
        /// </summary>
        /// <param name="Buffer"></param>
        /// <param name="PackedSignals"></param>
        public void ExtractSignals(Tuple<bool, int>[] Buffer, byte[] Packet)
        {
            int byte_index = 0;
            int bit_index = 7;
            int signal_index = 0;
            foreach (var ios in _ViewModel.Signals)
            {
                bool is_connected = _ExtractBit(Packet, ref byte_index, ref bit_index) != 0;
                int value = 0;
                for (int i = 0; i < ios.BitCount; ++i)
                {
                    value = (value << 1) | _ExtractBit(Packet, ref byte_index, ref bit_index);
                }
                Buffer[signal_index] = new Tuple<bool,int>(is_connected, value);

                ++signal_index;
            }
        }

        /// <summary>
        /// Count the bytes needed for the packet buffer.
        /// </summary>
        public int BytesInPacket
        {
            get
            {
                int bits = 0;
                foreach (var ios in _ViewModel.Signals)
                {
                    bits += 1 + ios.BitCount;
                }
                return (bits + 7) / 8;
            }
        }

        static int _ExtractBit(byte[] Packet, ref int ByteIndex, ref int BitIndex)
        {
            int r = (Packet[ByteIndex] >> BitIndex) & 1;
            --BitIndex;
            if (BitIndex < 0)
            {
                BitIndex = 7;
                ++ByteIndex;
            }
            return r;
        }

        // Are we closed? After close, there is no return.
        private bool IsClosed { get { return _RemoteEndPoint == null; } }

        /// <summary>
        /// Received some of the requested data...
        /// </summary>
        /// <param name="iar"></param>
        private void _MainLoop()
        {
            try
            {
                // Main loop. stuff.
                while (!IsClosed)
                {
                    var client = new TcpClient();

                    _DispatchConnectionStatus(false);
                    try
                    {
                        client.Connect(this._RemoteEndPoint);
                    }
                    catch (Exception)
                    {
                        // TODO: shall we log it?
                        System.Threading.Thread.Sleep(100);
                        continue;
                    }

                    var stream = client.GetStream();
                    this._TcpClient = client;
                    this._TcpClientStream = stream;

                    _DispatchConnectionStatus(true);

                    bool read_ok = true;
                    while (!IsClosed && read_ok)
                    {
                        try
                        {
                            var message = MessageFromPlc.ParseDelimitedFrom(stream);
                            if (IsClosed)
                            {
                                read_ok = false;
                            }
                            else
                            {
                                var db = _ViewModel.LocalSignalDB;
                                if (db != null)
                                {
                                    bool time_to_fetch_a_batch = false;
                                    // 1. Hijack database stuff.
                                    try
                                    {
                                        if (message.HasOOBDatabaseRange)
                                        {
                                            db.HandlePlcDatabaseRange(message.OOBDatabaseRange);
                                            time_to_fetch_a_batch = _ViewModel.LocalConfiguration.IsSynchronizationEnabled;
                                        }
                                        if (message.HasOOBSignalValues && _ViewModel.LocalConfiguration.IsSynchronizationEnabled)
                                        {
                                            db.HandleSignalValues(message.OOBSignalValues);
                                        }

                                        if (message.DatabaseSignalValuesCount > 0 && _ViewModel.LocalConfiguration.IsSynchronizationEnabled)
                                        {
                                            db.HandleSignalValues(message.DatabaseSignalValuesList);
                                            time_to_fetch_a_batch = true;
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        db = null;
                                        _ViewModel.LocalSignalDB = null;
                                        var msg = ex.Message;
                                        // ok, cannot work with the database!
                                        _ViewModel.Dispatcher.BeginInvoke(new Action(() => {
                                            System.Windows.MessageBox.Show("LocalSignalDB error: " + msg, App.TitleError);
                                        }));
                                    }

                                    // TODO: handle timeouts.
                                    if (time_to_fetch_a_batch && db != null)
                                    {
                                        System.Threading.Thread.Sleep(SYNCHRONIZATION_INTERVAL_MS);
                                        var range = db.NextBatch();
                                        if (range != null)
                                        {
                                            var builder = MessageToPlc.CreateBuilder();
                                            builder.Id = _NextRequestId++;
                                            builder.SetQueryRangeOfRows(range);
                                            var query = builder.Build();
                                            query.WriteDelimitedTo(_TcpClientStream);
                                            _LastBatchQuerySent = query;
                                        }
                                    }
                                }
                                // 2. Send it to the main thread for processing, if needed.
                                if (message.HasOOBConfiguration || message.HasOOBSignalValues)
                                {
                                    _ViewModel.Dispatcher.BeginInvoke(new Action(() => { _ViewModel.HandleMessageFromPlc(this, message); }));
                                }
                            }
                        }
                        catch (Exception)
                        {
                            // Most probably the peer disconnected.
                            // TODO: shall we log it?
                            System.Threading.Thread.Sleep(100);
                            read_ok = false;
                            continue;
                        }
                    }

                    if (!IsClosed)
                    {
                        // Don't reconnect in a tight loop.
                        System.Threading.Thread.Sleep(100);
                    }
                }
                // it's all over. Nothing to do, anymore.
            }
            catch (Exception)
            {
                // TODO: should the exception be logged?
                // pass them this round...
            }
        }

        /// <summary>
        /// Endpoint we are connected to. If null, no longer connect!
        /// </summary>
        volatile IPEndPoint _RemoteEndPoint;
        /// <summary>
        /// Socket we are connected on.
        /// </summary>
        volatile TcpClient _TcpClient;
        /// <summary>
        /// Network stream of the _TcpClient.
        /// </summary>
        volatile NetworkStream _TcpClientStream;
        /// <summary>
        /// Connection thread, if any.
        /// </summary>
        System.Threading.Thread _Thread;
        /// <summary>
        /// Id of the next request.
        /// </summary>
        volatile int _NextRequestId = 1;
        /// <summary>
        /// Last batch query sent to the PLC, if any.
        /// </summary>
        MessageToPlc _LastBatchQuerySent = null;

        void _DispatchConnectionStatus(bool IsConnected)
        {
            if (System.Threading.Thread.CurrentThread == _ViewModel.Dispatcher.Thread)
            {
                _ViewModel.IsConnected = IsConnected;
            }
            else
            {
                _ViewModel.Dispatcher.BeginInvoke(new Action(() => { _ViewModel.IsConnected = IsConnected; } ));
            }
        }
    }
}
