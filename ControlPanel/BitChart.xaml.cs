using System;
using System.Collections.ObjectModel;
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

namespace ControlPanel
{
    /// <summary>
    /// Interaction logic for BitChart.xaml
    /// </summary>
    public partial class BitChart : UserControl
    {
        /// <summary>
        /// One column of data on the chart.
        /// </summary>
        public class BitColumn
        {
            /// <summary>
            /// Timestamp, in DateTime.Ticks.
            /// </summary>
            public long Timestamp;
            /// <summary>
            /// Readings from the PLC, mostly bits. 0=false, 1=true.
            /// </summary>
            public Tuple<bool,int>[] Bits;
        }

        /// <summary>Fill it with data. After updating, call RedrawChart(). </summary>
        public List<BitColumn> Bits = new List<BitColumn>();

        /// <summary>Timestamp corresponding to the beginning of the chart.</summary>
        public DateTime TimestampBegin = DateTime.Now.Subtract(TimeSpan.FromSeconds(180)); // sensible defaults.
        /// <summary>Timestamp corresponding to the end of the chart.</summary>
        public DateTime TimestampEnd = DateTime.Now;

        /// <summary>
        /// Is cursor defined? If not, don't show cursor data.
        /// </summary>
        public bool IsCursorDefined
        {
            get { return (bool)GetValue(IsCursorDefinedProperty); }
            set { SetValue(IsCursorDefinedProperty, value); }
        }
        public static readonly DependencyProperty IsCursorDefinedProperty =
            DependencyProperty.Register("IsCursorDefined", typeof(bool), typeof(BitChart), new PropertyMetadata(false));

        public BitChart()
        {
            InitializeComponent();
            listboxLegend.ItemsSource = _Names;
        }

        /// <summary>
        /// Set new names. Call RedrawChart when done.
        /// </summary>
        /// <param name="Names">New names for the bit lines.</param>
        public void SetNames(IEnumerable<string> Names)
        {
            // 1. New names.
            _Names.Clear();
            foreach (var n in Names)
            {
                _Names.Add(n);
            }

            // 3. New separator lines.
            _RemoveAndClear(_Separators, 0);
            DoubleCollection dashes = new DoubleCollection(new double[] { 5.0, 5.0, });
            for (int i = _Names.Count - 1; i > 0; --i)
            {
                var l = new Line() { Stroke = Brushes.Gray, StrokeDashArray = dashes, };
                canvasPlot.Children.Add(l);
                _Separators.Add(l);
            }
        }

        static readonly Brush FillNormal = new SolidColorBrush(Color.FromRgb(0, 0, 0));
        static readonly Brush FillNormalTail = new SolidColorBrush(Color.FromRgb(60, 60, 60));
        static readonly Brush FillDisconnected = new SolidColorBrush(Color.FromRgb(255, 0, 0));
        static readonly Brush FillDisconnectedTail = new SolidColorBrush(Color.FromRgb(200, 0, 0));
        /// <summary>
        /// Temporary structure used during placing bricks on the chart.
        /// </summary>
        class Plotter
        {
            public long TimestampBegin;
            public long TimestampEnd;
            public long TimestampDelta; // initialize it to TimestampEnd - TimestampBegin
            public double CanvasWidth;
            public double RectTop;
            public double RectBottom;
            public Canvas Canvas;
            public List<Rectangle> Rects;
            public int RectsUsed = 0;

            /// <summary>
            /// Plot the rectangle. Assumes plotting starts from the left and ends at the right.
            /// </summary>
            public void Plot(Tuple<bool, int> Bit, bool IsLast, long Timestamp0, long Timestamp1)
            {
                double rect_left = Timestamp0 * CanvasWidth / TimestampDelta;
                double rect_right = Timestamp1 * CanvasWidth / TimestampDelta;
                if (_PrevBit != null && !IsLast && _PrevBit.Equals(Bit))
                {
                    _PrevRect.Width += rect_right - rect_left;
                }
                else
                {
                    // bit.Item1 ? Brushes.Black : Brushes.Red
                    // bit.Item1 ? Brushes.Gray : Brushes.Pink
                    Rectangle r;
                    if (RectsUsed < Rects.Count)
                    {
                        r = Rects[RectsUsed];
                    }
                    else
                    {
                        r = new Rectangle() { Fill = Brushes.Black };
                        Canvas.Children.Add(r);
                        Rects.Add(r);
                    }
                    r.Fill = IsLast
                        ? (Bit.Item1 ? FillNormalTail : FillDisconnectedTail)
                        : (Bit.Item1 ? FillNormal : FillDisconnected);
                    _SetGeometry(r,
                        rect_left, (Bit.Item2 == 0 ? (RectBottom - (RectBottom - RectTop) * 0.15) : RectTop),
                        rect_right, RectBottom);
                    ++RectsUsed;
                    _PrevRect = r;
                }
                _PrevBit = Bit;
            }

            Tuple<bool, int> _PrevBit = null;
            Rectangle _PrevRect = null;
        }

        /// <summary>Plot area of the chart.</summary>
        Rect _PlotArea = Rect.Empty;

        public void RedrawChart()
        {
            double ch = canvasPlot.ActualHeight;
            double cw = canvasPlot.ActualWidth;

            const double MIN_HEIGHT = 20.0;
            const double MIN_WIDTH = 20.0;

            textblockBegin.Text = TimestampBegin.ToString(App.TimestampFormatString);
            textblockEnd.Text = TimestampEnd.ToString(App.TimestampFormatString);

            if (ch > MIN_HEIGHT && cw > MIN_WIDTH)
            {
                double n = _Names.Count;

                double margin = 0;

                _PlotArea = new Rect(margin, margin, cw - 2 * margin, ch * n / (n + 1) - 2 * margin);

                // Frame for the chart.
                _SetGeometry(rectFrame, _PlotArea.Left, _PlotArea.Top, _PlotArea.Right, _PlotArea.Bottom);

                // Set up the separators.
                double strip_height = 10;
                if (_Names.Count > 0)
                {
                    strip_height = _PlotArea.Height / n;
                    for (int i = 0; i < _Separators.Count; ++i)
                    {
                        double y = _PlotArea.Top + (i + 1) * strip_height;
                        _SetLineGeometry(_Separators[i], _PlotArea.Left, y, _PlotArea.Right, y);
                    }

                    // Bits! Rectangles!
                    if (_Rectangles == null)
                    {
                        _Rectangles = new List<Rectangle>[_Names.Count];
                        for (int i = 0; i < _Names.Count; ++i)
                        {
                            _Rectangles[i] = new List<Rectangle>();
                        }
                    }
                    for (int row_index = 0; row_index < _Rectangles.Length; ++row_index)
                    {
                        double rect_top = row_index * strip_height;
                        double rect_bottom = rect_top + strip_height;
                        // Work on this stuff, row by row.
                        var plotter = new Plotter()
                        {
                            TimestampBegin = this.TimestampBegin.Ticks,
                            TimestampEnd = this.TimestampEnd.Ticks,
                            CanvasWidth = cw,
                            RectTop = row_index * strip_height,
                            RectBottom = rect_top + strip_height,
                            Canvas = canvasPlot,
                            Rects = _Rectangles[row_index],
                        };
                        plotter.TimestampDelta = plotter.TimestampEnd - plotter.TimestampBegin;
                        for (int column_index = 1; column_index < Bits.Count; ++column_index)
                        {
                            var column = Bits[column_index - 1];
                            var bit = column.Bits[row_index];
                            var ts_1 = column.Timestamp - plotter.TimestampBegin;
                            var ts_2 = Bits[column_index].Timestamp - plotter.TimestampBegin;

                            // Shall we plot a rectangle?
                            plotter.Plot(bit, false, ts_1, ts_2);
                            // _PlotRectangleIfNeeded(rects, ref rects_used, cw, rect_top, rect_bottom, bit, ts_1, ts_2, bit.Item1 ? Brushes.Black : Brushes.Red);
                        }
                        // What about the last one?
                        if (Bits.Count > 0)
                        {
                            var column = Bits[Bits.Count - 1];
                            if (column.Timestamp < plotter.TimestampEnd)
                            {
                                var bit = column.Bits[row_index];
                                var ts_1 = column.Timestamp - plotter.TimestampBegin;
                                var ts_2 = plotter.TimestampEnd - plotter.TimestampBegin;
                                // _PlotRectangleIfNeeded(rects, ref rects_used, cw, rect_top, rect_bottom, bit, ts_1, ts_2, bit.Item1 ? Brushes.Gray : Brushes.Pink);
                                plotter.Plot(bit, true, ts_1, ts_2);
                            }
                        }
                        _RemoveAndClear(plotter.Rects, plotter.RectsUsed);
                    }

                    // Scale.
                    const double text_margin = 2.0;
                    double text_top = ch - strip_height + text_margin;
                    if (textblockBegin.ActualWidth > 0)
                    {
                        _SetPosition(textblockBegin, text_margin, text_top);
                    }
                    if (textblockEnd.ActualWidth > 0)
                    {
                        _SetPosition(textblockEnd, cw - text_margin - textblockEnd.ActualWidth, text_top);
                    }
                }

                _RedrawVerticalCursor();
            }
        }

        /// <summary>
        /// Append new column and redraw the chart, if necessary.
        /// </summary>
        /// <param name="b"></param>
        /// <param name="TimestampBegin"></param>
        /// <param name="TimestampEnd"></param>
        public void AppendBitColumn(BitColumn b, DateTime TimestampBegin, DateTime TimestampEnd)
        {
            this.TimestampBegin = TimestampBegin;
            this.TimestampEnd = TimestampEnd;

            long timestamp_begin = TimestampBegin.Ticks;
            long timestamp_end = TimestampEnd.Ticks;

            // 1. Remove excess from the beginning.
            int nbefore = (from column in Bits where column.Timestamp<timestamp_begin select column).Count();
            if (nbefore > 1)
            {
                Bits.RemoveRange(0, nbefore - 1);
            }

            // 2. Add stuff to the end, if needed.
            if (Bits.Count > 0 && Bits.Last().Timestamp != b.Timestamp)
            {
                Bits.Add(b);
            }

            RedrawChart();
        }

        void canvasPlot_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            RedrawChart();
        }

        void canvasPlot_LayoutUpdated(object sender, EventArgs e)
        {
#if (false)
            // NOTE: this caused endless redrawing. How to handle it?
            RedrawChart();
#endif
        }

        static void _SetGeometry(FrameworkElement e, double left, double top, double right, double bottom)
        {
            Canvas.SetLeft(e, left);
            Canvas.SetTop(e, top);
            if (right > left)
            {
                e.Width = right - left;
            }
            if (bottom > top)
            {
                e.Height = bottom - top;
            }
        }

        static void _SetLineGeometry(Line l, double left, double top, double right, double bottom)
        {
            l.X1 = left;
            l.Y1 = top;
            l.X2 = right;
            l.Y2 = bottom;
        }

        static void _SetPosition(FrameworkElement e, double left, double top)
        {
            Canvas.SetLeft(e, left);
            Canvas.SetTop(e, top);
        }

        void _RemoveAndClear<T>(IList<T> elements, int StartIndex) where T : UIElement
        {
            for (int i = elements.Count-1; i >= StartIndex; --i)
            {
                canvasPlot.Children.Remove(elements[i]);
                elements.RemoveAt(i);
            }
        }

        /// <summary>Horizontal dashed separator lines.</summary>
        List<Line> _Separators = new List<Line>();
        /// <summary>List of names in the legend.</summary>
        ObservableCollection<string> _Names = new ObservableCollection<string>();
        /// <summary>Building blocks of the chart. 1=tall block, 0=shallow block</summary>
        List<Rectangle>[] _Rectangles = null;

        #region VERTICAL CURSOR STUFF
        void _RedrawVerticalCursor()
        {
            // Coerced x-coordinate.
            double x = Math.Min(Math.Max(_PlotArea.Left, _LastMousePos.X), _PlotArea.Right);

            // Timestamp.
            long timestamp_begin = TimestampBegin.Ticks;
            long timestamp_end = TimestampEnd.Ticks;
            long offset = (long)((x - _PlotArea.Left) / _PlotArea.Width * (timestamp_end - timestamp_begin));
            long timestamp = timestamp_begin + offset;
            textblockVerticalCursorTimestamp.Text = (new DateTime(timestamp)).ToString(App.TimestampFormatString);

            lineVerticalCursor.X1 = x;
            lineVerticalCursor.Y1 = _PlotArea.Top;
            lineVerticalCursor.X2 = x;
            lineVerticalCursor.Y2 = _PlotArea.Bottom;
        }

        Point _LastMousePos = new Point(0, 0);
        void canvasPlot_MouseMove(object sender, MouseEventArgs e)
        {
            // yee!
            _LastMousePos = e.GetPosition(canvasPlot);
            _RedrawVerticalCursor();
            IsCursorDefined = true;
        }
        #endregion
    }
}
