using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;


namespace DeviceEmulator
{
    public class IOPin : DependencyObject
    {
        public int Number { get; set; }
        public bool IsInput { get; set; }
        string _PinName = null;
        public string PinName
        {
            get {
                if (_PinName == null)
                {
                    _PinName = (IsInput ? "DI" : "DO") + Number;
                }
                return _PinName;
            }
        }

        public string SignalName { get; set; }

        public string SignalDescription { get; set; }

        public int State
        {
            get { return (int)GetValue(StateProperty); }
            set { SetValue(StateProperty, value); }
        }
        public static readonly DependencyProperty StateProperty =
            DependencyProperty.Register("State", typeof(int), typeof(IOPin), new PropertyMetadata(0));

        public int Count
        {
            get { return (int)GetValue(CountProperty); }
            set { SetValue(CountProperty, value); }
        }
        public static readonly DependencyProperty CountProperty =
            DependencyProperty.Register("Count", typeof(int), typeof(IOPin), new PropertyMetadata(0));
    }
}
