/*
 * To change this template, choose Tools | Templates
 * and open the template in the editor.
 */
package plcmaster;

/**
 * Signal exported to the outside world.
 * 
 * Also provides interface to get/set values.
 * In case of bit values, 0=false, 1=true.
 * 
 * Signals can be used as variables when specifying all id-s as zeroes.
 * @author Andrei
 */
public class IOSignal {
    /** Name of the signal, if any present. */
    public final String Name;
    /** Identifier. */
    public final int Id;
    /** Discrete input, coil, etc. */
    public final IOType Type;
    /** Device identifier (1 ... 255, 0=NPE). */
    public final int DeviceAddress;
    /** Device itself, if any. */
    public IODevice Device = null;
    /** IO register index in the device. */
    public final int IOIndex;
    /** Length, in bits. */
    public final int BitLength;
    /** Is it a variable? */
    public final boolean IsVariable;
    /** Startup group, if any. */
    public final SignalStartupGroup StartupGroup;

    /** Internal storage for variables. */
    private int _Value = 0;
    
    public IOSignal (String Name, int Id, IOType Type, int DeviceAddress, int IOIndex, SignalStartupGroup StartupGroup)
    {
        this.Name = Name;
        this.Id = Id;
        this.Type = Type;
        this.DeviceAddress = DeviceAddress;
        this.IOIndex = IOIndex;
        this.BitLength = Type.BitCount;
        this.IsVariable = DeviceAddress < 0;
        this.StartupGroup = StartupGroup;
    }
    
    /** 
     * Get value. Call Device.SyncWithModbus to refresh value.
     * 
     * Discrete inputs and coils: 0=false, 1=true.
     * Input registers and holding registers: 16 bits of data.
     * @return Value of the signal.
     */
    public final int Get()
    {
        if (Device == null)
        {
            return _Value;
        }
        else
        {
            // When there has no connection been made, the Device.BitData or Device.Data might not be set.
            switch (Type)
            {
                case DISCRETE_INPUT:
                    return Device.BitData==null
                            ? 0
                            : (Device.BitData[IOIndex - Device.StartAddress] ? 1 : 0);
                case COIL:
                    return Device.BitData==null
                            ? 0
                            : (Device.BitData[IOIndex - Device.StartAddress] ? 1 : 0);
                case INPUT_REGISTER:
                    return Device.Data==null
                            ? 0
                            : Device.Data[IOIndex - Device.StartAddress];
                case HOLDING_REGISTER:
                    return Device.Data==null
                            ? 0
                            : Device.Data[IOIndex - Device.StartAddress];
                default:
                    return 0;
            }
        }
    }
    
    /** Set the signal in the device. Call Device.SyncWithModbus to actually write it.
     * Note: StartupGroup is NOT used.
     * 
     * @param Value Value to be set.
     */
    public final void Set(int Value)
    {
        if (Device == null)
        {
            _Value = Value;
        }
        else
        {
            Device.Write(IOIndex, Value);
        }
    }

    @Override
    public String toString()
    {
        if (Name.length()>0)
        {
            return "Signal " + Name + " (id:" + Id + ")";
        }
        else
        {
            return "Signal (id:" + Id + ")";
        }
    }
}
