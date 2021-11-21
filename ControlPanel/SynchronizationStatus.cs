using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;

using System.Windows;
using System.Threading;
using HandyBox; // SetValue<T>, GetValue<T>

namespace ControlPanel
{
    public class SynchronizationStatus : DependencyObject
    {
        /// <summary>
        /// Tail Id of the PLC circular database.
        /// </summary>
        public int RemoteTailId
        {
            get { return this.GetValue<int>(RemoteTailIdProperty, _RemoteTailId); }
            set { this.SetValue<int>(RemoteTailIdProperty, ref _RemoteTailId, value); }
        }
        int _RemoteTailId = 0;
        public static readonly DependencyProperty RemoteTailIdProperty =
            DependencyProperty.Register("RemoteTailId", typeof(int), typeof(SynchronizationStatus), new PropertyMetadata(0));


        /// <summary>
        /// Timestamp of the tail in the PLC circular database.
        /// There is no timestamp for the head, since it serves no use.
        /// </summary>
        public DateTime RemoteTailTimestamp
        {
            get { return this.GetValue<DateTime>(RemoteTailTimestampProperty, _RemoteTailTimestamp); }
            set { this.SetValue<DateTime>(RemoteTailTimestampProperty, ref _RemoteTailTimestamp, value); }
        }
        private DateTime _RemoteTailTimestamp = DateTime.Now;
        public static readonly DependencyProperty RemoteTailTimestampProperty =
            DependencyProperty.Register("RemoteTailTimestamp", typeof(DateTime), typeof(SynchronizationStatus), new PropertyMetadata(DateTime.Now));

        /// <summary>
        /// Head Id of the PLC circular database.
        /// </summary>
        public int RemoteHeadId
        {
            get { return this.GetValue<int>(RemoteHeadIdProperty, _RemoteHeadId); }
            set { this.SetValue<int>(RemoteHeadIdProperty, ref _RemoteHeadId, value); }
        }
        int _RemoteHeadId = 0;
        public static readonly DependencyProperty RemoteHeadIdProperty =
            DependencyProperty.Register("RemoteHeadId", typeof(int), typeof(SynchronizationStatus), new PropertyMetadata(0));
        
        /// <summary>
        /// Points to the next row ID to be fetched. The rows are fetched in increasing order, starting from 0.
        /// When LocalTrackId==RemoteHeadId, all rows have been fetched.
        /// </summary>
        public int LocalTrackId
        {
            get { return this.GetValue<int>(LocalTrackIdProperty, _LocalTrackId); }
            set { this.SetValue<int>(LocalTrackIdProperty, ref _LocalTrackId, value); }
        }
        int _LocalTrackId = 0;
        public static readonly DependencyProperty LocalTrackIdProperty =
            DependencyProperty.Register("LocalTrackId", typeof(int), typeof(SynchronizationStatus), new PropertyMetadata(0));

        public bool IsFinished
        {
            get { return this.GetValue<bool>(IsFinishedProperty, _IsFinished); }
            set { this.SetValue<bool>(IsFinishedProperty, ref _IsFinished, value); }
        }
        private bool _IsFinished = false;
        public static readonly DependencyProperty IsFinishedProperty =
            DependencyProperty.Register("IsFinished", typeof(bool), typeof(SynchronizationStatus), new PropertyMetadata(false));

        /// <summary>Work hours counter is a side-effect of the synchronization.</summary>
        public ObservableCollection<WorkingTimeCell> WorkingTimes
        {
            get { return (ObservableCollection<WorkingTimeCell>)GetValue(WorkingTimesProperty); }
            set { SetValue(WorkingTimesProperty, value); }
        }
        public static readonly DependencyProperty WorkingTimesProperty =
            DependencyProperty.Register("WorkingTimes", typeof(ObservableCollection<WorkingTimeCell>), typeof(SynchronizationStatus), new UIPropertyMetadata(null));

        /// <summary>Pointer to the next TrackId to be used in the Signals table.</summary>
        public int WorkingTimes_TrackId = 0;
        public long WorkingTimes_Timestamp = 0;
    }
}
