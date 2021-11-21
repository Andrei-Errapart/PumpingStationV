using System;
using System.Collections.Generic;
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

using HandyBox;

namespace ControlPanel
{
    /// <summary>
    /// Interaction logic for BitChartPanel.xaml
    /// </summary>
    public partial class BitChartPanel : UserControl
    {
        /// <summary>
        /// Set it when creating it.
        /// </summary>
        public List<int> SignalIndices;

        public BitChartPanel()
        {
            InitializeComponent();
        }

        public bool IsRunningSelected { get { return radiobuttonRunning.IsChecked == true; } }
        public TimeSpan TimeSpan { get { return timespanpickerPeriod.SelectedTimeSpan; } }
        ControlPanelViewModel _ViewModel { get { return DataContext as ControlPanelViewModel; } }

        /// <summary>
        /// Append signal values to the present chart.
        /// </summary>
        /// <param name="SignalValues"></param>
        /// <param name="Timestamp"></param>
        public void AppendSignalValues(LocalSignalDB.SignalValuesType SignalValues)
        {
            if (!_FirstLoad && IsRunningSelected && TimeSpan.TotalSeconds > 0.0)
            {
                // FIXME: do what?
                var nbits = SignalIndices.Count;
                var bufferbits = new Tuple<bool, int>[_ViewModel.Signals.Count];
                var b = new BitChart.BitColumn() { Bits = new Tuple<bool, int>[nbits], };
                _FillBitColumn(b, bufferbits, SignalValues);

                DateTime timestamp_end = DateTime.Now;
                DateTime timestamp_begin = timestamp_end.Subtract(TimeSpan);

                chart.AppendBitColumn(b, timestamp_begin, timestamp_end);
            }
        }

        /// <summary>
        /// Blindly process signal values for the chart.
        /// </summary>
        /// <param name="SignalValues"></param>
        public void ProcessValidSignalValues(IList<LocalSignalDB.SignalValuesType> SignalValues)
        {
            if (_FirstLoad)
            {
                return;
            }
            DateTime timestamp_end = DateTime.Now;
            DateTime timestamp_begin = timestamp_end.Subtract(TimeSpan);
            _ProcessValidSignalValues(SignalValues, timestamp_begin, timestamp_end);
        }

        /// <summary>
        /// Redraw chart with values fresh from the database...
        /// </summary>
        public void RedrawChart()
        {
            if (_FirstLoad)
            {
                return;
            }

            // 1.Fetch the rows.
            DateTime timestamp_end;
            DateTime timestamp_begin;
            if (IsRunningSelected)
            {
                timestamp_end = DateTime.Now;
                timestamp_begin = timestamp_end.Subtract(TimeSpan);
            }
            else
            {
                timestamp_begin = datetimepickerFrom.Value.Value;
                timestamp_end = timestamp_begin.Add(TimeSpan);
            }
            var rows = _ViewModel.LocalSignalDB.FetchByTimeRangeIncluding(timestamp_begin, timestamp_end);
            _ProcessValidSignalValues(rows, timestamp_begin, timestamp_end);
        }

        public void TimerTick()
        {
            if (!_FirstLoad && IsRunningSelected && TimeSpan.TotalSeconds > 0.0)
            {
                chart.TimestampEnd = DateTime.Now;
                chart.TimestampBegin = chart.TimestampEnd.Subtract(TimeSpan);
                chart.RedrawChart();
            }
        }

        void _ProcessValidSignalValues(
            IList<LocalSignalDB.SignalValuesType> SignalValues,
            DateTime TimestampBegin,
            DateTime TimestampEnd)
        {
            int nrows = SignalValues.Count;

            // 2. Convert them for the BitChart's liking.
            var bits = new BitChart.BitColumn[nrows];
            var nbits = SignalIndices.Count;
            var bufferbits = new Tuple<bool, int>[_ViewModel.Signals.Count];

            if (chart.Bits.Count > nrows)
            {
                chart.Bits.RemoveRange(nrows, chart.Bits.Count - nrows);
            }
            for (int i = 0; i < nrows; ++i)
            {
                BitChart.BitColumn b;
                if (i < chart.Bits.Count)
                {
                    b = chart.Bits[i];
                }
                else
                {
                    b = new BitChart.BitColumn() { Bits = new Tuple<bool, int>[nbits], };
                    chart.Bits.Add(b);
                }
                _FillBitColumn(b, bufferbits, SignalValues[i]);
            }

            // 3. Let the bitchart do the hard work! :D
            chart.TimestampBegin = TimestampBegin;
            chart.TimestampEnd = TimestampEnd;
            chart.RedrawChart();
        }

        bool _FirstLoad = true;
        void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (_FirstLoad)
            {
                datetimepickerFrom.Value = DateTime.Now.Subtract(TimeSpan.FromMinutes(5));
                _FirstLoad = false;
                // Initialize the chart...
                chart.SetNames(from si in SignalIndices select _ViewModel.Signals[si].Description);
                chart.RedrawChart();
            }
        }

        void UserControl_LayoutUpdated(object sender, EventArgs e)
        {
#if (false)
            // NOTE: this caused endless redrawing. How to handle it?
            RedrawChart();
#endif
        }


        void _FillBitColumn(BitChart.BitColumn b, Tuple<bool, int>[] BufferBits, LocalSignalDB.SignalValuesType SignalValues)
        {
            b.Timestamp = SignalValues.Timestamp;
            _ViewModel.PlcConnection.ExtractSignals(BufferBits, SignalValues.Values);
            for (int bit_index = 0; bit_index < SignalIndices.Count; ++bit_index)
            {
                b.Bits[bit_index] = BufferBits[SignalIndices[bit_index]];
            }
        }

        void timespanpickerPeriod_OnTimeSpanChanged(object sender, TimeSpanPicker.TimeSpanChangedEventArgs e)
        {
            if (!_FirstLoad)
            {
                RedrawChart();
            }
        }

        void datetimepickerFrom_ValueChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (!_FirstLoad)
            {
                RedrawChart();
            }
        }

        void radiobuttonRunning_Click(object sender, RoutedEventArgs e)
        {
            if (!_FirstLoad)
            {
                RedrawChart();
            }
        }

        void radiobuttonFrom_Click(object sender, RoutedEventArgs e)
        {
            if (!_FirstLoad)
            {
                RedrawChart();
            }
        }
    }
}
