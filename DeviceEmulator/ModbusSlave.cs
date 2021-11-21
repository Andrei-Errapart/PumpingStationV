using System;
using System.Collections.Generic;
//using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Windows.Threading;

namespace DeviceEmulator
{
    public interface IModbusDataStore
    {
        bool IsConnected { get; }
        byte ReadCoils(byte[] dst, int dst_offset, int CoilAddress, int Count);
        byte ReadDiscreteInputs(byte[] dst, int dst_offset, int InputAddress, int Count);
        byte ReadHoldingRegisters(byte[] dst, int dst_offset, int RegisterAddress, int Count);
        byte ReadInputRegisters(byte[] dst, int dst_offset, int RegisterAddress, int Count);
        byte WriteSingleCoil(int CoilAddress, bool Value);
        byte WriteMultipleCoils(int CoilAddress, int Values, int Count);
        byte WriteSingleHoldingRegister(int RegisterAddress, int Value);
    };

    // Supports requests:
    // Read coil.
    // Write coil.
    // Read holding register.
    // Read discrete input.
    //
    // Invalid address and checksum errors are silently discarded.
    public class ModbusSlave : IDisposable
    {
        // Supported functions only.
        public const byte FUNCTION_READ_COILS = 1;
        public const byte FUNCTION_READ_DISCRETE_INPUTS = 2;
        public const byte FUNCTION_READ_HOLDING_REGISTERS = 3;
        public const byte FUNCTION_READ_INPUT_REGISTERS = 4;
        public const byte FUNCTION_WRITE_SINGLE_COIL = 5;
        public const byte FUNCTION_WRITE_MULTIPLE_COILS = 15;

        public const byte EXCEPTION_ILLEGAL_FUNCTION = 1;
        public const byte EXCEPTION_ILLEGAL_DATA_ADDRESS = 2;
        public const byte EXCEPTION_ILLEGAL_DATA_VALUE = 3;
        // public const byte EXCEPTION_ = ;

        // Discards previous input when read completes later than timeout.
        public TimeSpan ReadTimeout = TimeSpan.FromSeconds(5.0);

        public ModbusSlave(Socket connection, IDictionary<byte, IModbusDataStore> data_store, Dispatcher Dispatcher)
        {
            _connection = connection;
            _data_store = data_store;
            _GuiDispatcher = Dispatcher;
            _ReadCallbackF = new AsyncCallback(_ReadCallback);
            _WriteCallbackF = new AsyncCallback(_WriteCallback);
            _read_start_time = DateTime.Now;
            _connection.BeginReceive(_read_buffer, 0, _read_buffer.Length, 0, _ReadCallbackF, this);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool FreeManagedResources)
        {
            if (FreeManagedResources)
            {
                if (_connection != null)
                {
                    try
                    {
                        _connection.Close();
                    }
                    finally
                    {
                        _connection = null;
                    }
                }
            }
        }

        /// <summary>
        /// Is connection still open?
        /// </summary>
        public bool IsOk
        {
            get
            {
                return _connection != null && _connection_exception==null;
            }
        }

        /// <summary>
        /// Reason for the closing, if any.
        /// </summary>
        public Exception ConnectionException
        {
            get
            {
                return _connection_exception;
            }
        }

        #region PRIVATE PARTS
        Socket _connection;
        Exception _connection_exception = null;
        byte[] _read_buffer = new byte[512];
        int _read_size = 0;
        DateTime _read_start_time;
        IDictionary<byte, IModbusDataStore> _data_store;
        System.Windows.Threading.Dispatcher _GuiDispatcher;
        AsyncCallback _ReadCallbackF;
        AsyncCallback _WriteCallbackF;


        int _ProcessReceivedData(int processed_sofar)
        {
            // 1. Address, valid?
            if (processed_sofar < _read_size)
            {
                int max_size = _read_size - processed_sofar;
                byte device_address = _read_buffer[processed_sofar];
                IModbusDataStore data_store = null;
                _data_store.TryGetValue(device_address, out data_store); // we'll skip unknown addresses later on.
                if (data_store != null && !data_store.IsConnected)
                {
                    data_store = null;
                }
                // Function, valid?
                // 2 = sizeof(address) + sizeof(function)
                // 2 = sizeof(checksum)
                if (max_size>=4)
                {
                    byte function_code = _read_buffer[processed_sofar + 1];
                    int expected_size = 0;
                    switch (function_code)
                    {
                        case FUNCTION_READ_COILS:
                            expected_size = 8;
                            if (max_size >= expected_size)
                            {
                                if (data_store != null && _IsChecksumOK(_read_buffer, processed_sofar, expected_size))
                                {
                                    int input_address = ModbusFormat.UInt16_Of_Bytes(_read_buffer, processed_sofar + 2);
                                    int input_count = ModbusFormat.UInt16_Of_Bytes(_read_buffer, processed_sofar + 4);
                                    int byte_count = (input_count + 7) / 8;
                                    var reply_size = 5 + byte_count;
                                    var wb = _GetFreeWriteBuffer(reply_size);
                                    var dsr = (byte)_GuiDispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Normal, new Func<byte>(
                                        () =>
                                        {
                                            var r = data_store.ReadCoils(wb.Buffer, 3, input_address, input_count);
                                            return r;
                                        })
                                        );
                                    if (dsr == 0)
                                    {
                                        // GOOD!
                                        // _SendReply(_read_buffer, processed_sofar, expected_size);
                                        wb.Buffer[0] = device_address;
                                        wb.Buffer[1] = function_code;
                                        wb.Buffer[2] = (byte)(byte_count);
                                        int wb_crc = ModbusFormat.CRC16(wb.Buffer, 0, reply_size - 2);
                                        ModbusFormat.Bytes_Of_UInt16(wb.Buffer, reply_size - 2, (ushort)wb_crc);
                                        _Write(wb);
                                    }
                                    else
                                    {
                                        _SendErrorReply(device_address, function_code, dsr);
                                    }
                                }
                                return processed_sofar + expected_size;
                            }
                            break;
                        case FUNCTION_READ_DISCRETE_INPUTS:
                            expected_size = 8;
                            if (max_size >= expected_size)
                            {
                                if (data_store!=null && _IsChecksumOK(_read_buffer, processed_sofar, expected_size))
                                {
                                    int input_address = ModbusFormat.UInt16_Of_Bytes(_read_buffer, processed_sofar + 2);
                                    int input_count = ModbusFormat.UInt16_Of_Bytes(_read_buffer, processed_sofar + 4);
                                    int byte_count = (input_count + 7) / 8;
                                    var reply_size = 5 + byte_count;
                                    var wb = _GetFreeWriteBuffer(reply_size);
                                    var dsr = (byte)_GuiDispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Normal, new Func<byte>(
                                        () =>
                                        {
                                            var r = data_store.ReadDiscreteInputs(wb.Buffer, 3, input_address, input_count);
                                            return r;
                                        })
                                        );
                                    if (dsr == 0)
                                    {
                                        // GOOD!
                                        // _SendReply(_read_buffer, processed_sofar, expected_size);
                                        wb.Buffer[0] = device_address;
                                        wb.Buffer[1] = function_code;
                                        wb.Buffer[2] = (byte)(byte_count);
                                        int wb_crc = ModbusFormat.CRC16(wb.Buffer, 0, reply_size - 2);
                                        ModbusFormat.Bytes_Of_UInt16(wb.Buffer, reply_size - 2, (ushort)wb_crc);
                                        _Write(wb);
                                    }
                                    else
                                    {
                                        _SendErrorReply(device_address, function_code, dsr);
                                    }
                                }
                                return processed_sofar + expected_size;
                            }
                            break;
                        case FUNCTION_READ_HOLDING_REGISTERS:
                            expected_size = 8;
                            if (max_size >= expected_size)
                            {
                                if (data_store!=null && _IsChecksumOK(_read_buffer, processed_sofar, expected_size))
                                {
                                    int register_address = ModbusFormat.UInt16_Of_Bytes(_read_buffer, processed_sofar + 2);
                                    int register_count = ModbusFormat.UInt16_Of_Bytes(_read_buffer, processed_sofar + 4);
                                    var reply_size = 5 + register_count*2;
                                    var wb = _GetFreeWriteBuffer(reply_size);
                                    var dsr = (byte)_GuiDispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Normal, new Func<byte>(
                                        () =>
                                        {
                                            var r = data_store.ReadHoldingRegisters(wb.Buffer, 3, register_address, register_count);
                                            return r;
                                        })
                                        );
                                    if (dsr == 0)
                                    {
                                        // GOOD!
                                        // _SendReply(_read_buffer, processed_sofar, expected_size);
                                        wb.Buffer[0] = device_address;
                                        wb.Buffer[1] = function_code;
                                        wb.Buffer[2] = (byte)(register_count * 2);
                                        int wb_crc = ModbusFormat.CRC16(wb.Buffer, 0, reply_size - 2);
                                        ModbusFormat.Bytes_Of_UInt16(wb.Buffer, reply_size - 2, (ushort)wb_crc);
                                        _Write(wb);
                                    }
                                    else
                                    {
                                        _SendErrorReply(device_address, function_code, dsr);
                                    }
                                }
                                return processed_sofar + expected_size;
                            }
                            break;
                        case FUNCTION_READ_INPUT_REGISTERS:
                            break;
                        case FUNCTION_WRITE_SINGLE_COIL:
                            expected_size = 8;
                            if (max_size >= expected_size)
                            {
                                if (data_store!=null && _IsChecksumOK(_read_buffer, processed_sofar, expected_size))
                                {
                                    int coil_address = ModbusFormat.UInt16_Of_Bytes(_read_buffer, processed_sofar + 2);
                                    int value = ModbusFormat.UInt16_Of_Bytes(_read_buffer, processed_sofar + 4);
                                    if (value == 0 || value == 0xFF00)
                                    {
                                        var dsr = (byte)_GuiDispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Normal, new Func<byte>(
                                            () =>
                                            {
                                                var r = (coil_address & 0x200) == 0
                                                    ? data_store.WriteSingleCoil(coil_address, value != 0)
                                                    : data_store.WriteSingleHoldingRegister(coil_address & ~0x200, 0);
                                                return r;
                                            })
                                            );
                                        if (dsr == 0)
                                        {
                                            // GOOD!
                                            _SendReply(_read_buffer, processed_sofar, expected_size);
                                        }
                                        else
                                        {
                                            _SendErrorReply(device_address, function_code, dsr);
                                        }
                                    }
                                    else
                                    {
                                        _SendErrorReply(device_address, function_code, EXCEPTION_ILLEGAL_DATA_VALUE);
                                    }
                                }
                                return processed_sofar + expected_size;
                            }
                            break;
                        case FUNCTION_WRITE_MULTIPLE_COILS:
                            if (max_size >= 10)
                            {
                                int coil_address = ModbusFormat.UInt16_Of_Bytes(_read_buffer, processed_sofar + 2);
                                int coil_count = ModbusFormat.UInt16_Of_Bytes(_read_buffer, processed_sofar + 4);
                                int byte_count = _read_buffer[processed_sofar + 6];
                                expected_size = 9 + byte_count;
                                if (max_size >= expected_size)
                                {
                                    if (data_store != null && _IsChecksumOK(_read_buffer, processed_sofar, expected_size))
                                    {
                                        int value = 0;
                                        for (int byteOffset = 0; byteOffset < byte_count; ++byteOffset)
                                        {
                                            value = value | (_read_buffer[processed_sofar + 7 + byteOffset] << 8 * byteOffset);
                                        }
                                        var dsr = (byte)_GuiDispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Normal, new Func<byte>(
                                            () =>
                                            {
                                                var r = data_store.WriteMultipleCoils(coil_address, value, coil_count);
                                                return r;
                                            })
                                            );
                                        if (dsr == 0)
                                        {
                                            // GOOD!
                                            _SendSuccessReply(device_address, function_code, coil_address, coil_count);
                                        }
                                        else
                                        {
                                            _SendErrorReply(device_address, function_code, dsr);
                                        }

                                    }
                                    return processed_sofar + expected_size;
                                }
                            }
                            break;
                        default:
                            // TODO: what to do?
                            // spoil all the input!
                            return _read_size;
                    }
                }
            }

            return processed_sofar;
        }

        void _DispatchResponse(Func<byte> qfunc, Action<byte> rfunc)
        {
            var r = (byte)_GuiDispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Normal, qfunc);
        }

        static byte[] _Splice(byte[] Buffer, int Offset, int Size)
        {
            var r = new byte[Size];
            Array.Copy(Buffer, Offset, r, 0, Size);
            return r;
        }

        void _SendErrorReply(byte DeviceAddress, byte FunctionCode, byte ExceptionCode)
        {
            // 1. address.
            // 2. error code.
            // 3. exception code.
            // 4. checksum 2 bytes.
            const int size = 5;
            var wb = _GetFreeWriteBuffer(size);
            wb.Buffer[0] = DeviceAddress;
            wb.Buffer[1] = (byte)(FunctionCode | 0x80);
            wb.Buffer[2] = ExceptionCode;
            int crc = ModbusFormat.CRC16(wb.Buffer, 0, size-2);
            ModbusFormat.Bytes_Of_UInt16(wb.Buffer, size-2, (ushort)crc);
            _Write(wb);
        }

        void _SendSuccessReply(byte DeviceAddress, byte FunctionCode, int RegisterOffset, int RegisterCount)
        {
            // 1. address.
            // 2. function code.
            // 3. register offset 2 bytes.
            // 4. register count 2 bytes.
            // 4. checksum 2 bytes.
            const int size = 8;
            var wb = _GetFreeWriteBuffer(size);
            wb.Buffer[0] = DeviceAddress;
            wb.Buffer[1] = FunctionCode;
            ModbusFormat.Bytes_Of_UInt16(wb.Buffer, 2, (ushort)RegisterOffset);
            ModbusFormat.Bytes_Of_UInt16(wb.Buffer, 4, (ushort)RegisterCount);
            int crc = ModbusFormat.CRC16(wb.Buffer, 0, size-2);
            ModbusFormat.Bytes_Of_UInt16(wb.Buffer, size-2, (ushort)crc);
            _Write(wb);
        }

        void _SendReply(byte[] Buffer, int Offset, int Size)
        {
            var wb = _GetFreeWriteBuffer(Size);
            Array.Copy(Buffer, Offset, wb.Buffer, 0, Size);
            _Write(wb);
        }

        static void _ReadCallback(IAsyncResult ar)
        {
            // What have we got?
            var self = ar.AsyncState as ModbusSlave;
            int this_round = -1;
            try
            {
                this_round = self._connection.EndReceive(ar);
            }
            catch (SocketException ex)
            {
                // TODO: how to detect disconnection without exception?
                self._CloseConnection(ex);
                return;
            }
            if (this_round > 0)
            {
                // 1. Read timeout should result in discarding previous input.
                DateTime now = DateTime.Now;
                if (now.Subtract(self._read_start_time) > self.ReadTimeout)
                {
                    // Have to discard previous input.
                    if (self._read_size > 0)
                    {
                        Array.Copy(self._read_buffer, self._read_size, self._read_buffer, 0, this_round);
                    }
                    self._read_size = this_round;
                }
                else
                {
                    self._read_size += this_round;
                }

                // 2. Process remaining stuff.
                int processed_sofar = 0;
                int next_processed_sofar = 0;
                do
                {
                    processed_sofar = next_processed_sofar;
                    next_processed_sofar = self._ProcessReceivedData(processed_sofar);
                } while (processed_sofar != next_processed_sofar);
                processed_sofar = next_processed_sofar;
                int remaining = self._read_size - processed_sofar;
                if (remaining > 0 && processed_sofar>0)
                {
                    // Have to remain at the beginning, simple & stupid.
                    // src src_offset dst dst_offset length
                    Array.Copy(self._read_buffer, processed_sofar, self._read_buffer, 0, remaining);
                }
                self._read_size = remaining;
                if (self._read_size >= self._read_buffer.Length)
                {
                    // OVERFLOW! Revert to zero.
                    self._read_size = 0;
                }

                self._read_start_time = now;
                try
                {
                    self._connection.BeginReceive(
                        self._read_buffer, self._read_size, self._read_buffer.Length - self._read_size,
                        0, self._ReadCallbackF, self);
                }
                catch (SocketException ex)
                {
                    self._CloseConnection(ex);
                }
            }
            else
            {
                self._CloseConnection(null);
            }
        }

        void _CloseConnection(SocketException ex)
        {
            if (_connection != null)
            {
                _connection.Close();
                _connection = null;
            }
            _connection_exception = ex;
        }
        /// <summary>
        /// Verify packet checksum.
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <param name="size">Size of packet, including two checksum bytes.</param>
        /// <returns></returns>
        bool _IsChecksumOK(byte[] buffer, int offset, int size)
        {
            int crc_expected = ModbusFormat.CRC16(buffer, offset, size - 2);
            int crc_read = ModbusFormat.UInt16_Of_Bytes(buffer, offset + size - 2);
            return crc_read == crc_expected;
        }
        #endregion // PRIVATE PARTS

        #region ASYNC SENDING
        class WriteBuffer
        {
            // Write buffer itself.
            public byte[] Buffer;
            // Offset in the data (used when sending).
            public int Offset;
            // Number of bytes to send.
            public int Size;
            // Is the buffer free for public use?
            public bool IsFree;

            public static void _WriteCallback(IAsyncResult ar)
            {
                var self = ar.AsyncState as WriteBuffer;
                self.IsFree = true;
            }
        }

        List<WriteBuffer> _write_buffers = new List<WriteBuffer>();
        Queue<WriteBuffer> _send_queue = new Queue<WriteBuffer>();

        WriteBuffer _GetFreeWriteBuffer(int Size)
        {
            int alloc_size = 16;
            while (alloc_size < Size)
            {
                alloc_size *= 2;
            }
            WriteBuffer last_free = null;
            foreach (var wb in _write_buffers)
            {
                if (wb.IsFree)
                {
                    if (wb.Buffer.Length >= Size)
                    {
                        wb.Offset = 0;
                        wb.Size = Size;
                        return wb;
                    }

                    last_free = wb;
                }
            }
            if (last_free != null)
            {
                last_free.Buffer = new byte[alloc_size];
                last_free.Offset = 0;
                last_free.Size = Size;
                return last_free;
            }
            WriteBuffer new_wb = new WriteBuffer() { Buffer = new byte[alloc_size], Offset=0, Size=Size, IsFree = true };
            _write_buffers.Add(new_wb);
            return new_wb;
        }

        // Either write immediately or prepare the sending queue.
        void _Write(WriteBuffer wb)
        {
            // Now we are commited.
            wb.IsFree = false;
            if (_send_queue.Count == 0)
            {
                _send_queue.Enqueue(wb);
                var x = _connection.BeginSend(wb.Buffer, 0, wb.Size, 0, _WriteCallbackF, wb);
            }
            else
            {
                _send_queue.Enqueue(wb);
            }
        }

        void _WriteCallback(IAsyncResult ar)
        {
            var wb = ar.AsyncState as WriteBuffer;
            int this_round = -1;
            try
            {
                this_round = _connection.EndSend(ar);
            }
            catch (SocketException ex)
            {
                _CloseConnection(ex);
                return;
            }

            if (this_round < 0)
            {
                // We are doomed, still.
                _CloseConnection(null);
            }
            else
            {
                wb.Offset += this_round;
                wb.Size -= this_round;
                if (wb.Size == 0)
                {
                    _send_queue.Dequeue();
                    wb.IsFree = true;
                    // wb = _send_queue.
                    wb = _send_queue.Count > 0 ? _send_queue.Peek() : null;
                }
                if (wb != null)
                {
                    _connection.BeginSend(wb.Buffer, 0, wb.Size, 0, _WriteCallbackF, wb);
                }
            }
        }

        #endregion
    }
}
