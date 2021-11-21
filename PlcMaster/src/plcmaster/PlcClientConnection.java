/*
 * To change this template, choose Tools | Templates
 * and open the template in the editor.
 */
package plcmaster;

// import java.util

import com.google.protobuf.ByteString;
import java.io.*;
import java.net.*;
import java.util.*;
import communication.*;

/**
 * Client to the PLC service.
 * 
 * 
 * @author Andrei
 */
public class PlcClientConnection implements Runnable {
    /** Maximum sending period, in milliseconds. Re-sends the last packet, if this timeout is exceeded. */
    public final static int MAX_SENDING_PERIOD_MS = 10 * 1000;
    /** Maximum number of rows in a batch. Queries exceeding this will be served only the newer ones. */
    public final static int MAX_BATCH_SIZE = 100;
    /** Id of the first batch sent. */
    public final static int FIRST_BATCH_ID = 0;
    public final Thread Thread;

    /** Id of the last OOB message sent. */
    public int Last_OOBId_Sent = 0;

    public PlcClientConnection(PlcContext PlcContext, Socket Socket, CircularDB DB) throws Exception
    {
        this.Thread = new Thread(this);
        this._PlcContext = PlcContext;
        this._DB = DB;
        this._Socket = Socket;
        this._Input = Socket.getInputStream();
        this._Output = Socket.getOutputStream();
        
        // 1. Send the configuration file.
        byte[] fin_buffer = FileUtils.ReadFileAsBytes(PlcContext.Filename);
        PlcMaster.LogLine("Sending configuration file (" +  fin_buffer.length + " bytes) to the client " + this.Thread.getName() + ".");

        // 1.1. Message needs to be constructed.
        PlcCommunication.MessageFromPlc.Builder envelope_builder = PlcCommunication.MessageFromPlc.newBuilder();
        envelope_builder.setId(++Last_OOBId_Sent);

        // 1.2. OOBConfiguration
        PlcCommunication.MessageFromPlc.OOBConfigurationType.Builder configuration_builder = PlcCommunication.MessageFromPlc.OOBConfigurationType.newBuilder();
        configuration_builder.setVersion(PlcContext.Version);
        configuration_builder.setConfigurationFile(ByteString.copyFrom(fin_buffer));
        envelope_builder.setOOBConfiguration(configuration_builder);
        
        // 1.3. OOBDatabaseRange
        PlcCommunication.IdRange.Builder idrange_builder = PlcCommunication.IdRange.newBuilder();
        idrange_builder.setTailId(_DB.TailId);
        idrange_builder.setHeadId(_DB.HeadId);
        envelope_builder.setOOBDatabaseRange(idrange_builder);

        // 1.4. Send it!
        PlcCommunication.MessageFromPlc configuration_message = envelope_builder.build();
        configuration_message.writeDelimitedTo(_Output);
        
        // 2. Send the last line, for a memory recall.
        this._CurrentRowId = _DB.HeadId - 1;
        this._DB_Buffer = new ArrayList<byte[]>();
        this._DB_Buffer.add(new byte[this._DB.RowLength]);        
        if (_CurrentRowId>=_DB.TailId && _DB.Fetch(_DB_Buffer, _CurrentRowId, _CurrentRowId+1)>0)
        {
            // _DB.Fetch must have returned 1, then.
            this._UpdateSignalsMessage();
            this._SignalsMessage.writeDelimitedTo(_Output);
            this._SignalsMessageTimeMs = System.currentTimeMillis();
            ++this._CurrentRowId;
        }
        
        // 3. Send first few lines.
        int first_head = Math.min(_DB.TailId + 10, _DB.HeadId);
        if (first_head > _DB.TailId)
        {
            _SendRows(_DB.TailId, first_head, FIRST_BATCH_ID);
        }
    }
    
    @Override
    public void run()
    {       
        try {
            // 3. Run the servicing loop.
            for (;;)
            {
                PlcCommunication.MessageToPlc msg = PlcCommunication.MessageToPlc.parseDelimitedFrom(_Input);
                if (msg == null)
                {
                    PlcMaster.LogLine("PlcClientConnection: peer disconnected, finishing servicing.");
                    break;
                }
                else
                {
                    if (msg.hasId())
                    {
                        if (msg.getQuerySetSignalsCount()>0)
                        {
                            // Dispatch it to the main thread.
                            synchronized (this._PlcContext.Queries)
                            {
                                this._PlcContext.Queries.add(msg);

                            }
                        }
                        if (msg.hasQueryRangeOfRows())
                        {
                            PlcCommunication.IdRange r = msg.getQueryRangeOfRows();
                            // Have we got the right stuff?
                            if (r.hasHeadId() && r.hasTailId())
                            {
                                int tail_id = r.getTailId();
                                int head_id = r.getHeadId();
                                if (head_id - tail_id > MAX_BATCH_SIZE)
                                {
                                    int new_tail_id = head_id - MAX_BATCH_SIZE;
                                    // PlcMaster.LogLine("PlcClientConnection: query for ID-s in the range " + head_id + " ... " + tail_id + " exceeds batch size limit of " + MAX_BATCH_SIZE + ", changing tail to " + new_tail_id + ".");
                                    tail_id = new_tail_id;
                                }
                                else
                                {
                                    // PlcMaster.LogLine("PlcClientConnection: query for ID-s in the range " + head_id + " ... " + tail_id + ".");
                                }
                                
                                _SendRows(tail_id, head_id, msg.getId());
                            }
                        }
                    }
                }
            }
        } catch (Exception ex)
        {
            PlcMaster.LogLine("PlcClientConnection: error: " + ex.getMessage());
        }
        finally {
            // Close the socket.
            try {
                _Socket.close();                    
            } catch (Exception ex)
            {
                PlcMaster.LogLine("Error when closing socket: " + ex);
                // pass.
            }
        }
    }
    
    private void _SendRows(int tail_id, int head_id, int response_id) throws Exception
    {
        int batch_size = head_id - tail_id;

        // Batch buffer might be of different length.
        while (_Batch_Buffer.size() < batch_size)
        {
            _Batch_Buffer.add(new byte[_DB.RowLength]);
        }

        // Go ahead with the query.
        int n = _DB.Fetch(_Batch_Buffer, tail_id, head_id);

        // Send the reply packet.
        PlcCommunication.MessageFromPlc.Builder builder = PlcCommunication.MessageFromPlc.newBuilder();
        builder.setId(response_id);
        if (n>=0)
        {
            // PlcMaster.LogLine("PlcClientConnection: debug: sending " + n + " rows!");
            // Build the response.

            for (int i=0; i<n; ++i)
            {
                builder.addDatabaseSignalValues(_SignalValues_Of_DataRow(_BatchSignalsBuilder, tail_id + i, _Batch_Buffer.get(i)));
            }

        }

        PlcCommunication.MessageFromPlc.ResponseType.Builder response_builder = PlcCommunication.MessageFromPlc.ResponseType.newBuilder();
        if (n>=0)
        {
            response_builder.setOK(true);
        }
        else
        {
            response_builder.setOK(false);
            response_builder.setMessage("Invalid range specified: [" + tail_id + " .. " + head_id + ").");
        }
        builder.setResponse(response_builder);
        PlcCommunication.MessageFromPlc reply_packet = builder.build();
                
        synchronized (_Output)
        {
            reply_packet.writeDelimitedTo(_Output);
        }
    }
    
    private final PlcCommunication.MessageFromPlc.Builder _SignalsEnvelopeBuilder = PlcCommunication.MessageFromPlc.newBuilder();
    /** Used from the main thread for OOB messages. */
    private final PlcCommunication.MessageFromPlc.SignalValuesType.Builder _OOBSignalsBuilder = PlcCommunication.MessageFromPlc.SignalValuesType.newBuilder();
    private final PlcCommunication.MessageFromPlc.SignalValuesType.Builder _BatchSignalsBuilder = PlcCommunication.MessageFromPlc.SignalValuesType.newBuilder();
    private PlcCommunication.MessageFromPlc _SignalsMessage = null;
    private long _SignalsMessageTimeMs = 0;
    /** Id of the row fetched from the database. */
    private int _CurrentRowId = 0;
    /** Buffer of one row, corresponding to _CurrentRowId. */
    private final List<byte[]> _DB_Buffer;
    /** Buffer of some rows for batch queries. */
    private final List<byte[]> _Batch_Buffer = new ArrayList<byte[]>();
    
    /** Produce output packets, if any.
     * 
     * @throws Exception 
     */
    public void ProduceOutput() throws Exception
    {
        // 1. Fetch 1 row if needed.
        if (_CurrentRowId != _DB.HeadId && _DB.Fetch(_DB_Buffer, _CurrentRowId, _CurrentRowId+1)>0)
        {
            // _DB.Fetch must have returned 1, then.
            this._UpdateSignalsMessage();
            synchronized (this._Output)
            {
                this._SignalsMessage.writeDelimitedTo(_Output);
            }
            this._SignalsMessageTimeMs = System.currentTimeMillis();
            ++this._CurrentRowId;
        }
        
        // 2. Re-send packet, if needed.
        long t1 = System.currentTimeMillis();
        if (t1 - _SignalsMessageTimeMs > MAX_SENDING_PERIOD_MS && _SignalsMessage!=null)
        {
            synchronized (this._Output)
            {
                this._SignalsMessage.writeDelimitedTo(_Output);
            }
            this._SignalsMessageTimeMs = t1;
        }
    }
    
    /** Update _SignalsMessage (from _SignalsEnvelopeBuilder, _SignalsBuilder) using the newly fetched row in _DB_Buffer.
     * 
     */
    private void _UpdateSignalsMessage()
    {
        // 1. Id in the envelope.
        _SignalsEnvelopeBuilder.setId(++Last_OOBId_Sent);
        
        // 2. Important stuff.
        _SignalsEnvelopeBuilder.setOOBSignalValues(_SignalValues_Of_DataRow(_OOBSignalsBuilder, _CurrentRowId, _DB_Buffer.get(0)));
        
        _SignalsMessage = _SignalsEnvelopeBuilder.build();
    }
    
    private static PlcCommunication.MessageFromPlc.SignalValuesType _SignalValues_Of_DataRow(PlcCommunication.MessageFromPlc.SignalValuesType.Builder builder, int RowId, byte[] row)
    {
        long time_ms = BitUtils.Int64_Of_Bytes_BE(row, CircularRowFormat.TIME_OFFSET);
        builder.setRowId(RowId);
        builder.setVersion(BitUtils.Int32_Of_Bytes_BE(row, CircularRowFormat.VERSION_OFFSET));
        builder.setTimeMs(time_ms);
        builder.setSignalValues(ByteString.copyFrom(row, CircularRowFormat.SIGNALS_OFFSET, row.length - CircularRowFormat.SIGNALS_OFFSET));
        return builder.build();
    }
    
    private final PlcContext _PlcContext;
    private final CircularDB _DB;
    private final Socket _Socket;
    private final InputStream _Input;
    /** Writes can happen both from the main thread and from the socket servicing thread. Use synchronization on this._Output before writing. */
    private final OutputStream _Output;
}
