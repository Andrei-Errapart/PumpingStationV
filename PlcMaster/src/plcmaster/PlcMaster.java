package plcmaster;

import java.net.*;
import java.io.*;
import java.util.*;
import pl.a2s.npe.factory.*;
import gnu.io.*;

import communication.*;


/**
 *
 * @author Andrei
 */
public class PlcMaster {
    /** Maximum write period, in milliseconds. */
    final static int DB_MAX_WRITE_PERIOD_MS = 10*60*1000;
    final static int DB_NUM_ROWS = (3600/1) * 24 * 30 * 3;
    final static int DB_VERSION = 1;
    static int _DB_Row_Length = -1;

    static PlcContext _PlcContext = null;
    
    static HardwareNPE _npe = null;
    static NPEioInterface.DigitalOutput _heartbeat_led = null;
    static NPEioInterface.DigitalOutput _error_led = null;
    
    static SerialPort _port = null;
    static OutputStream _port_out = null;
    static InputStream _port_in = null;
    static ModbusMaster _modbusmaster = null;

    // ---- Emulation ----
    static String _emulator_host = null;
    static int _emulator_port = 0;
    static Socket _emulator_socket = null;
    
    static ServerSocket _ServerSocket = null;
    static Thread _ServerThread = null;
    
    static List<PlcClientConnection> _Connections = new ArrayList<PlcClientConnection>();

    static CircularDB _DB = null;
    
    // Are we finished? Yes/No.
    static volatile boolean _IsFinished = false;

    /**
     * @param args the command line arguments
     */
    public static void main(String[] args) {
        try {
            // 1. Check the command line.
            boolean args_is_emulated = false;
            for (int argsIndex=0; argsIndex<args.length; ++argsIndex) {
                String args_i = args[argsIndex];
                if (args_i.equals("--help")) {
                    _PrintUsage();
                    return;
                } else if (args_i.equals("--emulator")) {
                    if (argsIndex+1 < args.length)
                    {
                        // ip:port
                        ++argsIndex;
                        String[] v = args[argsIndex].split(":", 2);
                        if (v.length == 2) {
                            try {
                                _emulator_host = v[0];
                                _emulator_port = Integer.parseInt(v[1]);
                                args_is_emulated = true;
                            } catch (Exception ex)
                            {
                                PlcMaster.LogLine("Invalid argument to --emulator:" + ex.getMessage());
                                _PrintUsage();
                                return;
                            }
                        } else {
                            PlcMaster.LogLine("Invalid argument to --emulator.");
                            _PrintUsage();
                            return;
                        }
                    } else {
                        PlcMaster.LogLine("Argument missing for --emulator.");
                        _PrintUsage();
                        return;
                    }
                }
            }

            // 2. Read the configuration file.
            String ConfigurationFilename = "PlcMaster.xml";
            PlcMaster.LogLine("Reading configuration file '" + ConfigurationFilename + "'");
            _PlcContext = new PlcContext(ConfigurationFilename, args_is_emulated);
            PlcMaster.LogLine("Finished reading configuration file, " + _PlcContext.Devices.size() + " devices, " + _PlcContext.Signals.size() +" signals.");
            
            // 2. Open the database.
            String database_filename = "PlcMaster.db";
            try {
                _DB_Row_Length = CircularRowFormat.Count_Bytes_Needed(_PlcContext.Signals);
                PlcMaster.LogLine("Opening circular database '" + database_filename + "', " + DB_NUM_ROWS + " rows (" + _DB_Row_Length + " bytes each).");
                _DB = new CircularDB(database_filename, DB_VERSION, _DB_Row_Length, DB_NUM_ROWS);
                PlcMaster.LogLine("Database opened, total length is " + _DB.FileLength + " bytes.");
                
                // Have to restore the variables into a perfect working order!
                byte[] last_row_buffer = new byte[_DB_Row_Length];
                List<byte[]> last_rows = new ArrayList<byte[]>();
                last_rows.add(last_row_buffer);
                if (_DB.HeadId > _DB.TailId && _DB.Fetch(last_rows, _DB.HeadId-1, _DB.HeadId)>0)
                {
                    PlcMaster.LogLine("Restoring variables...");
                    CircularRowFormat.RestoreVariables(_PlcContext.Signals, last_row_buffer);
                }
            } catch (Exception ex)
            {
                PlcMaster.LogLine("Database error: " + ex);
                PlcMaster.LogLine("Database error: " + ex.getMessage());
                ex.printStackTrace();
                return;
            }
                      
            // 3. Start the MODBUS stuff...
            if (_PlcContext.IsEmulated) {
                PlcMaster.LogLine("Connecting to " + _emulator_host + ":" + _emulator_port + "...");
                _emulator_socket = new Socket(_emulator_host, _emulator_port);
                _port_in = _emulator_socket.getInputStream();
                _port_out = _emulator_socket.getOutputStream();
            } else {
                // 2. Open the hardware, if any.
                PlcMaster.LogLine("Using NPE hardware resources...");
                _npe = HardwareNPE.getReference();
                _heartbeat_led = NPEioInterface.DigitalOutput.USER_LED;
                _error_led = NPEioInterface.DigitalOutput.STATUS_LED;
                PlcMaster.LogLine("ModBus on port: " + _PlcContext.ModbusPort);

                CommPortIdentifier _port_id = CommPortIdentifier.getPortIdentifier(_PlcContext.ModbusPort);
                _port = (SerialPort) _port_id.open("PlcMaster", 5000);
                _port.setSerialPortParams(_PlcContext.ModbusBaudrate, SerialPort.DATABITS_8, SerialPort.STOPBITS_1, SerialPort.PARITY_NONE);
                _port.setFlowControlMode(SerialPort.FLOWCONTROL_NONE);
                _port_in = _port.getInputStream();
                _port_out = _port.getOutputStream();
            }
            
            // 4. Start the TCP server stuff.
            
            _ServerSocket = new ServerSocket(_PlcContext.ServerPort, 10);
            
            Runnable server = new Runnable() {
              @Override
              public void run()
              {
                  while (!_IsFinished)
                  {
                      try {
                          Socket conn = _ServerSocket.accept();
                          // getHostName prints garbage on iMod :(
                          PlcMaster.LogLine("Connection received from " + conn.getInetAddress().getHostAddress());
                          PlcClientConnection client_connection = new PlcClientConnection(_PlcContext, conn, _DB);
                          client_connection.Thread.start();
                          _Connections.add(client_connection);
                      } catch (Exception ex)
                      {
                          PlcMaster.LogLine("ServerSocket.accept: error: " + ex.getMessage());
                      }
                  }
              }
            };
            _ServerThread = new Thread(server);
            _ServerThread.start();

            // 5. Main loop.
            _modbusmaster = new ModbusMaster(_port_in, _port_out);
            byte[] RowBuffer = new byte[_DB_Row_Length];
            // Previous row.
            byte[] PreviousRowBuffer = new byte[_DB_Row_Length];
            long PreviousWriteTimeMs = 0;
            
            for (_IsFinished = false; !_IsFinished;)
            {
                boolean modbus_comm_ok = true;
                for (IODevice dev : _PlcContext.Devices)
                {
                    try
                    {
                        Thread.sleep(10);
                        dev.SyncWithModbus(_modbusmaster);
                    }
                    catch (SocketException sex)
                    {
                        PlcMaster.LogLine("PlcMaster: tcp/ip error, will not reconnect (" + sex.getMessage()+").");
                        _IsFinished = true;
                        break;
                    }
                    catch (Exception ex)
                    {
                        PlcMaster.LogLine("PlcMaster: Error in communicating with device " + dev.DeviceAddress + ": " + ex.getMessage());
                        modbus_comm_ok = false;
                    }
                }
                // long t1 = System.currentTimeMillis();
                // System.out.println("Modbus refresh done in " + (t1-t0) + " ms.");
                if (_npe!=null)
                {
                    // invert the heartbeat LED...
                    int next_r = _npe.outputRead(_heartbeat_led)==0 ? 1 : 0;
                    _npe.outputSet(_heartbeat_led, next_r);                    
                    _npe.outputSet(_error_led, modbus_comm_ok ? 0 : (1-next_r));
                }
                
                // Have to write foundings to the database, too.
                CircularRowFormat.Encode(RowBuffer, 0, _PlcContext.Signals, _PlcContext.Version);
                boolean this_row_is_different = false;
                for (int i=CircularRowFormat.SIGNALS_OFFSET; i<RowBuffer.length; ++i)
                {
                    if (RowBuffer[i]!=PreviousRowBuffer[i])
                    {
                        this_row_is_different = true;
                        break;
                    }
                }
                long t1 = System.currentTimeMillis();
                boolean max_period_exceeded = PreviousWriteTimeMs!=0 && t1-PreviousWriteTimeMs > DB_MAX_WRITE_PERIOD_MS;
                if (this_row_is_different || PreviousWriteTimeMs==0 || max_period_exceeded)
                {
                    if (max_period_exceeded)
                    {
                        PlcMaster.LogLine("Tick.");
                    }
                    try {
                        _DB.Add(RowBuffer);
                        PreviousWriteTimeMs = System.currentTimeMillis();
                    }
                    catch (Exception ex)
                    {
                        PlcMaster.LogLine("Error adding row to the database:" + ex.getMessage());
                    }
                }
                System.arraycopy(RowBuffer, 0, PreviousRowBuffer, 0, RowBuffer.length);
                
                // Process the connection. There might be dead ones, too.
                for (int i=_Connections.size()-1; i>=0; --i)
                {
                    PlcClientConnection conn = _Connections.get(i);
                    if (conn.Thread.isAlive())
                    {
                        try {
                            conn.ProduceOutput();
                        }
                        catch (Exception ex)
                        {
                            PlcMaster.LogLine("Error producing output for connection " + conn.Thread.getName() + ": " + ex);
                            PlcMaster.LogLine("Removing connection " + conn.Thread.getName() + ".");
                            _Connections.remove(i);
                        }
                    }
                    else
                    {
                        PlcMaster.LogLine("Removing connection " + conn.Thread.getName() + ".");
                        _Connections.remove(i);
                    }
                }
                
                Thread.sleep(300);
                
                // Programs might want to be interested if there were any queries.
                PlcCommunication.MessageToPlc qp;
                do {
                    synchronized (_PlcContext.Queries)
                    {
                            qp = _PlcContext.Queries.poll();
                    }
                    if (qp!=null && qp.hasId())
                    {
                        int nsignals_to_set = qp.getQuerySetSignalsCount();
                        for (int signal_index=0; signal_index<nsignals_to_set; ++signal_index)
                        {
                            PlcCommunication.MessageToPlc.SignalAndValue sv = qp.getQuerySetSignals(signal_index);
                            if (sv.hasName() && sv.hasValue())
                            {
                                String signal_name = sv.getName();
                                int signal_value = sv.getValue();
                                IOSignal ios = _PlcContext.SignalByNameOrId(signal_name);
                                if (ios == null)
                                {
                                    PlcMaster.LogLine("Query was to set signal " + signal_name + " to " + signal_value + ", but this signal is unknown to us!");
                                }
                                else
                                {
                                    PlcMaster.LogLine(signal_name + " := " + signal_value);
                                    ios.Set(signal_value);
                                }
                            }
                        }
                    }
                } while (qp!=null);
                
                // Finally, execute the logic program!
                for (LogicStatement ls : _PlcContext.LogicProgram)
                {
                    ls.Execute();
                }
            }
            /*
            int regcount = 4;
            int[] regs = new int[16];
            boolean[] inputs = new boolean[4];
            boolean[] coils = new boolean[4];
            byte device_id = 2;
            
            long t0 = System.currentTimeMillis();
            // _modbusmaster.WriteHoldingRegister(device_id, 1, 3);
            _modbusmaster.ClearHoldingRegister(device_id, 1);
            System.out.println("Holding register 1 cleared.");
            Thread.sleep(10);
            for (int i=0; i<10; ++i)
            {
                // _modbusmaster.WriteSingleCoil(device_id, 0, (i & 1)==0);
                _modbusmaster.WriteMultipleCoils(device_id, 0, (i & 1)==0 ? 0x55 : 0xAA, 8);
                Thread.sleep(10);
                _modbusmaster.ReadHoldingRegisters(regs, device_id, 0, regcount);
                Thread.sleep(10);
                _modbusmaster.ReadDiscreteInputs(inputs, device_id, 0, inputs.length);
                Thread.sleep(10);
                _modbusmaster.ReadCoils(coils, device_id, 0, coils.length);
                Thread.sleep(10);
                System.out.println("Holding registers: " + regs[0] + ", " + regs[1] + ", " + regs[2] + ", " + regs[3]);
                System.out.println("Discrete inputs: " + inputs[0] + ", " + inputs[1] + ", " + inputs[2] + ", " + inputs[3]);
                System.out.println("Coils in: " + coils[0] + ", " + coils[1] + ", " + coils[2] + ", " + coils[3]);
            }
            long t1 = System.currentTimeMillis();
            System.out.println("Time elapsed for 10 queries: " + (t1-t0) + " ms.");
             */
            /*
            for (int k=0; k<regcount; ++k)
            {
                System.out.println("<" + regs[k]);
            }
            for (int k=0; k<inputs.length; ++k)
            {
                System.out.println("<" + inputs[k]);
            }
             */
            
            
            /*
            byte[] query = {  01, 03, 00, 00, 00, 01 };            
            _port_out.write(query);
            _port_out.flush();
            System.out.println("Query sent, reading response...");
            byte[] response = new byte[5];
            int so_far = 0;
            while (so_far<response.length)
            {
                int this_round = _port_in.read(response, so_far, response.length - so_far);
                if (this_round>0)
                {
                    for (int i=0; i<this_round; ++i)
                    {
                        System.out.println(":" + Byte.toString(response[so_far + i]));
                    }
                    so_far += this_round;
                }
            }
            // System.out.println("Response:" + response[0] + ":" + response[1] + ":" + response[2]+":" + response[3]+":" + response[4]);
             */

            /*
            System.out.println("Blinking!");
            blink(dout, 30);
             */
            PlcMaster.LogLine("Finished!");
        } catch (Exception ex)
        {
            PlcMaster.LogLine("PlcMaster error:" + ex.toString());
            PlcMaster.LogLine("PlcMaster error:" + ex.getMessage());
            ex.printStackTrace();
            _CloseIfAny();
        }
        finally
        {
            _CloseIfAny();
        }
    }
    
    private static void _PrintUsage()
    {
        System.out.println("Usage: plcmaster.sh [--emulator host:port] [--help]");
        System.out.println("where");
        System.out.println("\t--emulator: Connect to the given emulator instead of using specified serial port and NPE hardware.");
        System.out.println("\t--help: Print this help.");
    }
    private static void _CloseIfAny()
    {
            if (_port!=null)
            {
                _port.close();
            }
            _port = null;
    }
    private static void blink(NPEioInterface.DigitalOutput out, int nTimes) throws Exception 
    {
      for (int i=0; i<nTimes; ++i)
      {
          _npe.outputSet(out, 0);
          Thread.sleep(100L);
          _npe.outputSet(out, 1);
          Thread.sleep(100L);
      }
    }
    
    private final static java.text.SimpleDateFormat _LogDateFormat = new java.text.SimpleDateFormat("yyyy-MM-dd HH:mm:ss.SSS");
    /**
     * Write line to the log.
     * @param line Line of text to be logged.
     */
    public final static void LogLine(String line)
    {
        Date current = new java.util.Date();
        System.out.print(_LogDateFormat.format(current));
        System.out.print(" ");
        System.out.println(line);
    }
}
