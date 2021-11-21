using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;

namespace ControlPanel
{
    public class IOSignal : DependencyObject
    {
        public enum TYPE : byte {
            DISCRETE_INPUT,
            COIL,
            INPUT_REGISTER,
            HOLDING_REGISTER
        };

        public static string StringOfType(TYPE Type)
        {
            switch (Type)
            {
                case TYPE.DISCRETE_INPUT:
                    return "input";
                case TYPE.COIL:
                    return "output";
                case TYPE.INPUT_REGISTER:
                    return "input_register";
                case TYPE.HOLDING_REGISTER:
                    return "holding_register";
            }
            return "<unknown>";
        }

        #region READ-ONLY (mostly) PROPERTIES (mostly)
        public int UID { get { return _UID; } }
        private readonly int _UID;
        public int Id { get { return _Id; } }
        private readonly int _Id;
        public string Name { get { return _Name; } }
        private readonly string _Name;
        public bool IsVariable { get { return _IsVariable; } }
        private readonly bool _IsVariable;
        public readonly TYPE Type;
        public bool CanWrite { get { return _CanWrite; } }
        private readonly bool _CanWrite;
        public readonly int BitCount;
        public string Device { get { return _Device; } }
        private readonly string _Device;
        public string IOIndex { get { return _IOIndex; } }
        private readonly string _IOIndex;
        public string Description { get { return _Description; } }
        private readonly string _Description;
        public string Text0 { get { return _Text0; } }
        private readonly string _Text0;
        public string Text1 { get { return _Text1; } }
        private readonly string _Text1;
        #endregion

        #region VALUES SUBJECT TO CHANGE
        /// <summary>
        /// Value of the signal. Use Update to update the value.
        /// </summary>
        public int Value { get { return _Value; } }
        private int _Value;
        /// <summary>
        /// Is the signal connected? Use Update to update the value.
        /// </summary>
        public bool IsConnected { get { return _IsConnected; } }
        private bool _IsConnected;
        #endregion

        #region DEPENDENCY PROPERTIES
        public string DisplayId
        {
            get { return (string)GetValue(DisplayIdProperty); }
            set { SetValue(DisplayIdProperty, value); }
        }
        public static readonly DependencyProperty DisplayIdProperty =
            DependencyProperty.Register("DisplayId", typeof(string), typeof(IOSignal), new PropertyMetadata(""));

        public string DisplayDevicePin
        {
            get { return (string)GetValue(DisplayDevicePinProperty); }
            set { SetValue(DisplayDevicePinProperty, value); }
        }
        public static readonly DependencyProperty DisplayDevicePinProperty =
            DependencyProperty.Register("DisplayDevicePin", typeof(string), typeof(IOSignal), new PropertyMetadata(""));

        public string DisplayValue
        {
            get { return (string)GetValue(DisplayValueProperty); }
            set { SetValue(DisplayValueProperty, value); }
        }
        public static readonly DependencyProperty DisplayValueProperty =
            DependencyProperty.Register("DisplayValue", typeof(string), typeof(IOSignal), new PropertyMetadata(""));

        public bool DisplayIsConnected
        {
            get { return (bool)GetValue(DisplayIsConnectedProperty); }
            set { SetValue(DisplayIsConnectedProperty, value); }
        }
        public static readonly DependencyProperty DisplayIsConnectedProperty =
            DependencyProperty.Register("DisplayIsConnected", typeof(bool), typeof(IOSignal), new PropertyMetadata(false));

        public string DisplayReading
        {
            get { return (string)GetValue(DisplayReadingProperty); }
            set { SetValue(DisplayReadingProperty, value); }
        }
        public static readonly DependencyProperty DisplayReadingProperty =
            DependencyProperty.Register("DisplayReading", typeof(string), typeof(IOSignal), new PropertyMetadata(""));

        /// <summary>
        /// Are we the cause of the blinking?
        /// </summary>
        public bool DisplayIsAlarm
        {
            get { return (bool)GetValue(DisplayIsAlarmProperty); }
            set { SetValue(DisplayIsAlarmProperty, value); }
        }
        public static readonly DependencyProperty DisplayIsAlarmProperty =
            DependencyProperty.Register("DisplayIsAlarm", typeof(bool), typeof(IOSignal), new PropertyMetadata(false));
        #endregion /* DEPENDENCY PROPERTIES */

        /// <summary>
        /// Simple mechanism for generating unique id-s within the application.
        /// </summary>
        static volatile int _NextUID = 1;

        public IOSignal(int Id, string Name, bool IsVariable, TYPE Type, string Device, string IOIndex, string Description, string Text0, string Text1)
        {
            this._UID = _NextUID++;
            this._Id = Id;
            this._Name = Name;
            this._IsVariable = IsVariable;
            this.Type = Type;
            this._CanWrite = Type == TYPE.COIL || Type == TYPE.HOLDING_REGISTER;
            this.BitCount = (Type == TYPE.DISCRETE_INPUT || Type == TYPE.COIL) ? 1 : 16;
            this._Device = Device;
            this._IOIndex = IOIndex;
            this._Description = Description;
            this._Text0 = Text0;
            this._Text1 = Text1;
            this._Value = 0;
            this._IsConnected = false;
            this.DisplayId = Id >= 0 ? Id.ToString() : "";
            this.DisplayDevicePin = Id >= 0
                ? ((this.Device == "0" ? "CPU" : ("R" + this.Device)) + "." + (this.CanWrite ? "DO" : "DI") + this.IOIndex)
                : "variable";
        }

        public void Update(bool IsConnected, int Value)
        {
            this._IsConnected = IsConnected;
            this._Value = Value;
            this.DisplayIsConnected = this.IsConnected;
            this.DisplayValue = this.Value.ToString();
            this.DisplayReading = this.Type == TYPE.COIL || this.Type == TYPE.DISCRETE_INPUT
                ? (this.Value == 0 ? this.Text0 : this.Text1)
                : this.DisplayValue;
        }

        public static TYPE? TypeOfString(string s)
        {
            if (string.Equals(s, "input", StringComparison.CurrentCultureIgnoreCase))
            {
                return TYPE.DISCRETE_INPUT;
            }
            if (string.Equals(s, "output", StringComparison.CurrentCultureIgnoreCase))
            {
                return TYPE.COIL;
            }
            if (string.Equals(s, "input_register", StringComparison.CurrentCultureIgnoreCase))
            {
                return TYPE.INPUT_REGISTER;
            }
            if (string.Equals(s, "holding_register", StringComparison.CurrentCultureIgnoreCase))
            {
                return TYPE.HOLDING_REGISTER;
            }
            return null;
        }

        public override string ToString()
        {
            return "{IOSignal " + Name + "/"  + Id + ": " + Value + "}";
        }
    }
}
