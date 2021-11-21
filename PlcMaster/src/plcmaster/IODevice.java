/*
 * To change this template, choose Tools | Templates
 * and open the template in the editor.
 */
package plcmaster;

import pl.a2s.npe.factory.*;

/**
 * IO device, which can be either NPE PLC itself or a Modbus module.
 * The type is determined at the time of construction.
 * @author Andrei
 */
public class IODevice {
    // Modbus address, 0 for the built-in NPE.
    public final int DeviceAddress;
    // One of DISCRETE_INPUT, COIL, INPUT_REGISTER.
    public final IOType Type;
    // Start address of the input/coil/counter.
    public int StartAddress;
    // Number of registers, if any.
    public int Count;
    
    // Last read (or write) time, ms.
    public long LastSyncTimeMs;
    // Was last read successful?
    public boolean IsLastSyncOk;    
    // Data. Discrete inputs and coils are represented as bit fields, counters are stored as is.
    public boolean[] BitData;
    public int[] Data;
    
    public IODevice(int DeviceAddress, IOType Type)
    {
        this.DeviceAddress = DeviceAddress;
        this.Type = Type;
        this.LastSyncTimeMs = 0;
        this.IsLastSyncOk = false;
    }

    public IODevice(int DeviceAddress, HardwareNPE npe, NPEioInterface.DigitalInput[] inputs)
    {
        this.DeviceAddress = DeviceAddress;
        this.Type = IOType.DISCRETE_INPUT;
        this.LastSyncTimeMs = 0;
        _npe = npe;
        _npe_inputs = inputs;
        StartAddress = 0;
        Count = inputs.length;
    }

    public IODevice(int DeviceAddress, HardwareNPE npe, NPEioInterface.DigitalOutput[] outputs)
    {
        this.DeviceAddress = DeviceAddress;
        this.Type = IOType.COIL;
        this.LastSyncTimeMs = 0;
        _npe = npe;
        _npe_outputs = outputs;
        StartAddress = 0;
        Count = outputs.length;
    }
    
    // TODO: implement outputs.
    public void SyncWithModbus(ModbusMaster mm) throws Exception
    {
        switch (Type)
        {
            case DISCRETE_INPUT:
                IsLastSyncOk = false;
                if (BitData==null)
                {
                    BitData = new boolean[Count];
                }
                if (_npe_inputs==null)
                {
                    mm.ReadDiscreteInputs(BitData, DeviceAddress, StartAddress, Count);
                }
                else
                {
                    for (int i=0; i<_npe_inputs.length; ++i)
                    {
                        int x = _npe.inputRead(_npe_inputs[i]);
                        BitData[i] = x!=0;
                    }
                }
                LastSyncTimeMs = System.currentTimeMillis();
                IsLastSyncOk = true;
                break;
            case COIL:
                IsLastSyncOk = false;
                if (BitData==null)
                {
                    boolean[] bit_data = new boolean[Count];
                    _ReadCoilBits(bit_data, mm);
                    BitData = bit_data;
                    _BitDataToBeWritten = new boolean[Count];
                    for (int i=0; i<Count; ++i)
                    {
                        _BitDataToBeWritten[i] = BitData[i];
                    }
                    LastSyncTimeMs = System.currentTimeMillis();
                    IsLastSyncOk = true;
                }
                else
                {
                    boolean is_dirty = false;
                    int coils_to_write = 0; // for Modbus module.
                    // Update our view of the world.
                    _ReadCoilBits(BitData, mm);
                    
                    for (int i=0; i<Count; ++i)
                    {
                        if (BitData[i] != _BitDataToBeWritten[i])
                        {
                            is_dirty = true;
                        }
                        if (_BitDataToBeWritten[i])
                        {
                            coils_to_write |= (1 << i);
                        }
                    }
                    if (is_dirty)
                    {
                        if (_npe_outputs==null)
                        {
                            // modbus module.
                            mm.WriteMultipleCoils(DeviceAddress, StartAddress, coils_to_write, Count);
                        }
                        else
                        {
                            for (int i=0; i<Count; ++i)
                            {
                                if (BitData[i] != _BitDataToBeWritten[i])
                                {
                                    _npe.outputSet(_npe_outputs[i], _BitDataToBeWritten[i] ? 1 : 0);
                                }
                            }
                        }
                    }
                    // By this time, it should be OK.
                    for (int i=0; i<Count; ++i)
                    {
                        BitData[i] = _BitDataToBeWritten[i];
                    }

                    LastSyncTimeMs = System.currentTimeMillis();
                    IsLastSyncOk = true;
                }
                break;
            case INPUT_REGISTER:
                IsLastSyncOk = false;
                if (Data==null)
                {
                    Data = new int[Count];
                    _DataToBeWritten = new int[Count];
                    for (int i=0; i<Count; ++i)
                    {
                        _DataToBeWritten[i] = -1;
                    }
                }
                mm.ReadHoldingRegisters(Data, DeviceAddress, StartAddress, Count);
                for (int i=0; i<Count; ++i)
                {
                    int value = _DataToBeWritten[i];
                    if (value >= 0)
                    {
                        if (value == 0)
                        {
                            // ICPDAS special.
                            mm.ClearHoldingRegister(DeviceAddress, StartAddress + i);
                        }
                        else
                        {
                            mm.WriteHoldingRegister(DeviceAddress, StartAddress + i, value);
                        }
                        _DataToBeWritten[i] = -1;
                    }
                }
                LastSyncTimeMs = System.currentTimeMillis();
                IsLastSyncOk = true;
                break;
            case HOLDING_REGISTER:
                IsLastSyncOk = false;
                if (Data==null)
                {
                    Data = new int[Count];
                }
                mm.ReadHoldingRegisters(Data, DeviceAddress, StartAddress, Count);
                LastSyncTimeMs = System.currentTimeMillis();
                IsLastSyncOk = true;
                break;
        }
    }
    
    /** Write one holding register or coil.
     * NB! Nothing is written until the first round.
     */
    public void Write(int Address, int Value)
    {
        switch (Type)
        {
            case COIL:
                if (_BitDataToBeWritten!=null)
                {
                    _BitDataToBeWritten[Address - StartAddress] = Value!=0;
                }
                break;
            case HOLDING_REGISTER:
                if (_DataToBeWritten!=null)
                {
                    _DataToBeWritten[Address - StartAddress] = Value;
                }
                break;
        }
    }
    
    /** Read coils data from the given modbus device.
     * 
     * @param bit_data
     * @param mm
     * @throws Exception 
     */
    private void _ReadCoilBits(boolean[] bit_data, ModbusMaster mm) throws Exception
    {
        if (_npe_outputs==null)
        {
            mm.ReadCoils(bit_data, DeviceAddress, StartAddress, Count);
        }
        else
        {
            for (int i=0; i<Count; ++i)
            {
                bit_data[i] = _npe.outputRead(_npe_outputs[i]) != 0;
            }
        }
    }
    
    private boolean[] _BitData; // temporary.
    // Is written coil when differs from BitData.
    private boolean[] _BitDataToBeWritten;
    // Is written to holding register when >= 0.
    private int[] _DataToBeWritten;
    private HardwareNPE _npe;
    private NPEioInterface.DigitalOutput[] _npe_outputs;
    private NPEioInterface.DigitalInput[] _npe_inputs;
}
