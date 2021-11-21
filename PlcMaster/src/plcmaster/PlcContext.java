/*
 * To change this template, choose Tools | Templates
 * and open the template in the editor.
 */
package plcmaster;

import javax.xml.parsers.SAXParser;
import javax.xml.parsers.SAXParserFactory;
import org.xml.sax.Attributes;
import org.xml.sax.SAXException;
import org.xml.sax.helpers.DefaultHandler;
import java.util.*;
import java.io.*;

import pl.a2s.npe.factory.*;
import communication.*;


/** Configuration of the PLC.
 * 
 * TODO: better name is PlcContext.
 * @author Andrei
 */
public class PlcContext extends DefaultHandler {
    /** Modbus port name. */
    public String ModbusPort = "COM3";
    // Modbus baud rate. */
    public int ModbusBaudrate = 9600;
    /** TCP server port. */
    public int ServerPort = 1555;
    /** TODO: handle versioning correctly. */
    public final int Version = 1;
    /** List of signals present in the configuration file. */
    public final List<IOSignal> Signals = new ArrayList<IOSignal>();
    /** List of devices present in the configuration file. */
    public final List<IODevice> Devices = new ArrayList<IODevice>();
    /** Signals arranged by Id. */
    public final Map<Integer, IOSignal> SignalTable = new HashMap<Integer, IOSignal>();
    /** Signals arranged by Name. */
    public final Map<String, IOSignal> SignalMap = new HashMap<String, IOSignal>();
    /** Startup groups for the signals. */
    public final Map<String, SignalStartupGroup> StartupGroups = new HashMap<String, SignalStartupGroup>();
    /** Local signals specified in the logic program. */
    public final Map<String, IOSignal> LocalSignalMap = new HashMap<String, IOSignal>();
    /** Logic program, to be executed after each device list scan. */
    public final List<LogicStatement> LogicProgram = new ArrayList<LogicStatement>();

    /** Those queries from the client that are to be executed in the main thread.
     * Synchronize on the collection before operations!
     */
    public final Queue<PlcCommunication.MessageToPlc> Queries = new LinkedList<PlcCommunication.MessageToPlc>();
    
    /** Have we been configured to use the Device Emulator instead of native and modbus devices? */
    public final boolean IsEmulated;
    /** Name of the configuration file. */
    public final String Filename;
    
    public final StringBuilder LogicProgramText = new StringBuilder();
    
    /** Get the IOSignal from SignalMap or SignalTable either by name or by signal id.
     * 
     * @param NameOrId
     * @return 
     */
    public IOSignal SignalByNameOrId(String NameOrId)
    {
        IOSignal ios = SignalMap.get(NameOrId);
        if (ios==null)
        {
            try {
                int signal_id = Integer.parseInt(NameOrId);
                ios = SignalTable.get(signal_id);
            } catch (Exception ex)
            {
                // pass.
            }
        }
        return ios;
    }
    
    public PlcContext(String Filename, boolean IsEmulated) throws Exception
    {        
        // 1. Read the file.
	SAXParserFactory factory = SAXParserFactory.newInstance();
	SAXParser saxParser = factory.newSAXParser();
        HardwareNPE npe = null;
        this.IsEmulated = IsEmulated;
        this.Filename = Filename;
        
        // Simply using saxParser.parse(Filename) will lock the file by not closing the file when done reading...
        // This method doesn't have this problem.
        InputStream f_in = new FileInputStream(Filename);
        try {
            saxParser.parse(f_in, this);
        }
        finally {
            f_in.close();
        }
        
        if (!IsEmulated)
        {
            npe = HardwareNPE.getReference();
        }
        
        // 2a. Fill up the missing fields in signals.
        // 2b. Create necessary devices.
        for (IOSignal ios : this.Signals)
        {
            // Leave out the variables.
            if (ios.DeviceAddress<0 || ios.Id<0 || ios.IOIndex<0)
            {
                continue;
            }
            // 1. Get the corresponding device.
            for (IODevice iod : this.Devices)
            {
                if (iod.DeviceAddress == ios.DeviceAddress && iod.Type==ios.Type)
                {
                    ios.Device = iod;
                    break;
                }
            }
            if (ios.Device==null)
            {
                if (!IsEmulated && ios.DeviceAddress==0)
                {
                    // only discrete inputs and coils supported.
                    switch (ios.Type)
                    {
                        case DISCRETE_INPUT:
                        // 8 discrete inputs
                        {
                            NPEioInterface.DigitalInput[] inputs = new NPEioInterface.DigitalInput[] {
                                NPEioInterface.DigitalInput.DIW,
                                NPEioInterface.DigitalInput.DI1,
                                NPEioInterface.DigitalInput.DI2,
                                NPEioInterface.DigitalInput.DI3,
                                NPEioInterface.DigitalInput.DI4,
                                NPEioInterface.DigitalInput.DI5,
                                NPEioInterface.DigitalInput.DI6,
                                NPEioInterface.DigitalInput.DI7,
                            };
                            ios.Device = new IODevice(ios.DeviceAddress, npe, inputs);
                            break;
                        }
                        case COIL:
                        // 6 outputs.
                        {
                            NPEioInterface.DigitalOutput[] outputs = new NPEioInterface.DigitalOutput[] {
                                NPEioInterface.DigitalOutput.PO4,
                                NPEioInterface.DigitalOutput.DO1,
                                NPEioInterface.DigitalOutput.DO2,
                                NPEioInterface.DigitalOutput.PO1,
                                NPEioInterface.DigitalOutput.PO2,
                                NPEioInterface.DigitalOutput.PO3,
                            };
                            ios.Device = new IODevice(ios.DeviceAddress, npe, outputs);
                            break;
                        }
                        case INPUT_REGISTER:
                            break;
                        case HOLDING_REGISTER:
                            break;
                    }
                }
                else
                {
                    ios.Device = new IODevice(ios.DeviceAddress,ios.Type);
                }
                this.Devices.add(ios.Device);
            }
            IODevice dev = ios.Device;
            if (dev.Count == 0)
            {
                dev.StartAddress = ios.IOIndex;
                dev.Count = 1;
            }
            else
            {
                // Extend if necessary.
                if (ios.IOIndex < dev.StartAddress)
                {
                    // extend before
                    dev.Count += (dev.StartAddress - ios.IOIndex);
                    dev.StartAddress = ios.IOIndex;
                }
                else if (ios.IOIndex >= dev.StartAddress +  dev.Count)
                {
                    // extend after.
                    dev.Count = ios.IOIndex - dev.StartAddress + 1;
                }
            }
        }
        
        // 3. Parse the program.
        // System.out.println("Program: " + LogicProgramText.toString());
        // StringReader sr = new StringReader(LogicProgramText.toString());
        StringBufferInputStream sr = new java.io.StringBufferInputStream(LogicProgramText.toString());
        Scanner scanner = new Scanner(sr);
        Parser parser = new Parser(scanner);
        parser.Result = this.LogicProgram;
        parser.Context = this; // we can accept leaking this.
        parser.Parse();
        sr.close();
        if (parser.errors.count>0)
        {
            throw new ApplicationException("Cannot continue in the presence of parsing errors.");
        }
        PlcMaster.LogLine("Logic program: " + LogicProgram.size() + " statements, " + LocalSignalMap.size() + " variables.");
    }

    boolean _xml_in_program = false;
    @Override
    public void startElement(String uri, String localName,String qName, 
            Attributes atts) throws SAXException
    {
        if (qName.equalsIgnoreCase("modbus"))
        {
            ModbusPort = atts.getValue("port");
            ModbusBaudrate = _FetchIntegerAttribute(atts, "modbus", "baudrate");
        }
        else if (qName.equalsIgnoreCase("server"))
        {
            ServerPort = _FetchIntegerAttribute(atts, "server", "port");
        }
        else if (qName.equalsIgnoreCase("signal"))
        {
            IOSignal ios; // = new IOSignal();
            int ios_id = _FetchIntegerAttribute(atts, "signal", "id");
            int ios_DeviceAddress = _FetchIntegerAttribute(atts, "signal", "device");
            int ios_IOIndex = _FetchIntegerAttribute(atts, "signal", "ioindex");
            String ios_type_name = atts.getValue("type");
            IOType ios_type = IOType.OfString(ios_type_name);
            String ios_name = atts.getValue("name");
            String ios_startup_group_name = atts.getValue("startupgroup");
            SignalStartupGroup signal_startup_group = null;
            if (ios_name == null)
            {
                ios_name = "";
            }
            if (ios_type == null)
            {
                throw new SAXException("PlcContext.startElement: Invalid value for signal type: '" + ios_type_name+"'");
            }
            else
            {
                // Have to check the startupgroup, too.
                if (ios_startup_group_name!=null && ios_startup_group_name.length()>0)
                {
                    signal_startup_group = StartupGroups.get(ios_startup_group_name);
                    if (signal_startup_group == null)
                    {
                        signal_startup_group = new SignalStartupGroup(ios_startup_group_name);
                        StartupGroups.put(ios_startup_group_name, signal_startup_group);
                    }
                }
                ios = new IOSignal(ios_name, ios_id, ios_type, ios_DeviceAddress, ios_IOIndex, signal_startup_group);
            }
            Signals.add(ios);
            _CheckDuplicateId(ios);
            SignalTable.put(ios_id, ios);            
            if (ios_name.length()>0)
            {
                _CheckDuplicateName(ios);
                SignalMap.put(ios_name, ios);
            }
        } else if (qName.equalsIgnoreCase("variable"))
        {
            IOSignal ios; // = new IOSignal();
            String ios_type_name = atts.getValue("type");
            IOType ios_type = IOType.OfString(ios_type_name);
            String ios_name = atts.getValue("name");
            String ios_value = atts.getValue("value");
            if (ios_name == null || ios_name.length()==0)
            {
                throw new SAXException("PlcContext.startElement: Variable name missing!");
            }
            if (ios_type == null)
            {
                throw new SAXException("PlcContext.startElement: Invalid value for variable type: '" + ios_type_name+"'");
            }
            else
            {
                ios = new IOSignal(ios_name, -1, ios_type, -1, -1, null);
            }
            if (ios_value!=null)
            {
                int value = Integer.parseInt(ios_value);
                ios.Set(value);
            }
            // Variables go only to SignalMap and list.
            Signals.add(ios);
            _CheckDuplicateName(ios);
            SignalMap.put(ios_name, ios);
        } else if (qName.equalsIgnoreCase("program"))
        {
            this._xml_in_program = true;
        }
    }
    
    @Override
    public void endElement (String uri, String localName, String qName) throws SAXException
    {
        if (qName.equalsIgnoreCase("program"))
        {
            this._xml_in_program = false;
        }
    }

    void _CheckDuplicateId(IOSignal ios1) throws SAXException
    {
        IOSignal ios2 = SignalTable.get(ios1.Id);
        if (ios2!=null)
        {
            throw new SAXException("Duplicate id in configuration file: " + ios1.Id);
        }
    }
    
    void _CheckDuplicateName(IOSignal ios1) throws SAXException
    {
        if (ios1.Name!=null && ios1.Name.length()>0)
        {
            IOSignal ios2 = SignalMap.get(ios1.Name);
            if (ios2!=null)
            {
                throw new SAXException("Duplicate name in configuration file: " + ios1.Name);
            }
        }
    }
    
    @Override
    public void characters (char ch[], int start, int length) throws SAXException
    {
        if (this._xml_in_program)
        {
            this.LogicProgramText.append(ch, start, length);
        }
    }

    @Override
    public void ignorableWhitespace (char ch[], int start, int length) throws SAXException
    {
        if (this._xml_in_program)
        {
            this.LogicProgramText.append(ch, start, length);
        }
    }

    static int _FetchIntegerAttribute(Attributes atts, String element_name, String attr_name) throws SAXException
    {
        // 1. Get the attribute.
        String s = atts.getValue(attr_name);
        if (s == null)
        {
            throw new SAXException("PlcContext.startElement: Expected tag " + element_name + "." + attr_name+ " to be present, but it is missing.");
        }
        // 2. Parse the string as integer.
        int r = 0;
        try
        {
            r = Integer.parseInt(s);
        }
        catch (Exception ex)
        {
            throw new SAXException("PlcContext.startElement: Expected tag " + element_name + "." + attr_name+ " to have valid integer value, but got '" + s + "'.");
        }
        return r;
    }
}
