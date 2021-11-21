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
using System.Windows.Markup;

namespace DeviceEmulator
{
    /// <summary>
    /// Interaction logic for IOModuleControl.xaml
    /// </summary>
    [ContentProperty("Pins")]
    public partial class IOModuleControl : UserControl, IModbusDataStore
    {
        public int Address { get; set; }

        /// <summary>
        /// Groupbox header.
        /// </summary>
        public string Header { get; set; }


        /// <summary>
        /// Is the module connected? Default is true (connected).
        /// </summary>
        public bool? Connected { get; set; }

        public bool IsConnected { get { return Connected == true; } }

        /// <summary>
        /// List of IO pins.
        /// </summary>
        public ObservableCollection<IOPin> Pins
        {
            get { return (ObservableCollection<IOPin>)GetValue(PinsProperty); }
            set { SetValue(PinsProperty, value); }
        }
        public static readonly DependencyProperty PinsProperty =
            DependencyProperty.Register("Pins", typeof(ObservableCollection<IOPin>), typeof(IOModuleControl), new UIPropertyMetadata(null));
        
        public IOModuleControl()
        {
            Pins = new ObservableCollection<IOPin>();
            Connected = true;
            InitializeComponent();
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            DataContext = this;
        }

        private void Button_SetTo1_Click(object sender, RoutedEventArgs e)
        {
            _ActOnSenderPin(sender, (IOPin pin) => { pin.State = 1; });
        }

        private void Button_SetTo0_Click(object sender, RoutedEventArgs e)
        {
            _ActOnSenderPin(sender, (IOPin pin) => { pin.State = 0; });
        }

        private void Button_IncCount_Click(object sender, RoutedEventArgs e)
        {
            _ActOnSenderPin(sender, (IOPin pin) => { pin.Count += 1; });
        }

        private void Button_DecCount_Click(object sender, RoutedEventArgs e)
        {
            _ActOnSenderPin(sender, (IOPin pin) => { pin.Count -= 1; });
        }

        private void _ActOnSenderPin(object sender, Action<IOPin> act)
        {
            var button = sender as Button;
            if (button != null)
            {
                var pin = button.Tag as IOPin;
                if (pin != null)
                {
                    act(pin);
                }
            }
        }

        static int _AlldoneMask(int StartBit, int Count)
        {
            return ((1 << (StartBit + Count)) - 1) - ((1 << StartBit) - 1);
        }
        byte _ReadBits(byte[] dst, int dst_offset, int Address, int Count, bool ExpectInput)
        {
            int done_mask = 0;
            int alldone_mask = _AlldoneMask(Address, Count);
            int nbytes = (Count + 7) / 8;

            // 1. Clear bitarea.
            for (int i = 0; i < nbytes; ++i)
            {
                dst[dst_offset + i] = 0;
            }

            // 2. Copy input.
            foreach (var pin in Pins)
            {
                var ofs = pin.Number - Address;
                if (pin.IsInput == ExpectInput && ofs >= 0 && ofs < Count)
                {
                    int bitval = pin.State << (ofs & 7);
                    dst[dst_offset + ofs/8] |= (byte)bitval;
                    done_mask |= (1 << pin.Number);
                }
            }
            return done_mask == alldone_mask ? (byte)0 : ModbusSlave.EXCEPTION_ILLEGAL_DATA_ADDRESS;
        }

        public byte ReadCoils(byte[] dst, int dst_offset, int CoilAddress, int Count)
        {
            var r = _ReadBits(dst, dst_offset, CoilAddress, Count, false);
            return r;
        }

        public byte ReadDiscreteInputs(byte[] dst, int dst_offset, int InputAddress, int Count)
        {
            var r = _ReadBits(dst, dst_offset, InputAddress, Count, true);
            return r;
        }

        public byte ReadHoldingRegisters(byte[] dst, int dst_offset, int RegisterAddress, int Count)
        {
            int done_mask = 0;
            int alldone_mask = _AlldoneMask(RegisterAddress, Count);
            foreach (var pin in Pins)
            {
                var ofs = pin.Number - RegisterAddress;
                if (pin.IsInput && ofs >= 0 && ofs < Count)
                {
                    ModbusFormat.Bytes_Of_UInt16(dst, dst_offset + 2 * ofs, (ushort)pin.Count);
                    done_mask |= (1 << pin.Number);
                }
            }
            return done_mask == alldone_mask ? (byte)0 : ModbusSlave.EXCEPTION_ILLEGAL_DATA_ADDRESS;
        }

        public byte ReadInputRegisters(byte[] dst, int dst_offset, int RegisterAddress, int Count)
        {
            return ReadHoldingRegisters(dst, dst_offset, RegisterAddress, Count);
        }

        public byte WriteSingleCoil(int CoilAddress, bool Value)
        {
            foreach (var pin in Pins)
            {
                if (!pin.IsInput && pin.Number==CoilAddress)
                {
                    pin.State = Value ? 1 : 0;
                    return 0; // success, it is.
                }
            }
            return ModbusSlave.EXCEPTION_ILLEGAL_DATA_ADDRESS;
        }

        public byte WriteMultipleCoils(int CoilAddress, int Values, int Count)
        {
            int done_mask = 0;
            int alldone_mask = _AlldoneMask(CoilAddress, Count);
            foreach (var pin in Pins)
            {
                var ofs = pin.Number - CoilAddress;
                if (!pin.IsInput && ofs >= 0 && ofs < Count)
                {
                    // ModbusFormat.Bytes_Of_UInt16(dst, dst_offset + 2 * ofs, (ushort)pin.Count);
                    pin.State = (Values >> ofs) & 1;
                    done_mask |= (1 << pin.Number);
                }
            }
            return done_mask == alldone_mask ? (byte)0 : ModbusSlave.EXCEPTION_ILLEGAL_DATA_ADDRESS;
        }

        public byte WriteSingleHoldingRegister(int RegisterAddress, int Value)
        {
            foreach (var pin in Pins)
            {
                if (pin.IsInput && pin.Number==RegisterAddress)
                {
                        pin.Count = Value;
                        return 0; // success, it is.
                }
            }
            return ModbusSlave.EXCEPTION_ILLEGAL_DATA_ADDRESS;
        }
    }
}
