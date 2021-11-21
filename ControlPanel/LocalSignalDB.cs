using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;

using System.Windows;
using System.Data.SQLite;

namespace ControlPanel
{
    /// <summary>
    /// Access to the local database.
    /// Updates the SynchronizationStatus, mostly.
    /// 
    /// TODO: shall we merge SynchronizationStatus and LocalSignalDB?
    /// </summary>
    public class LocalSignalDB
    {
        /// <summary>
        /// Size of the database batch query.
        /// </summary>
        public const int MAX_BATCH_SIZE = 20;

        /// <summary>
        /// 
        /// </summary>
        public class SignalValuesType {
            /// <summary>
            /// Row Id in the database.
            /// </summary>
            public int Id;
            /// <summary>
            /// Database version, if anyone is interested in that.
            /// </summary>
            public int Version;
            /// <summary>
            /// Timestamp, in .NET Ticks.
            /// </summary>
            public long Timestamp;
            /// <summary>
            /// Raw signal values.
            /// </summary>
            public byte[] Values;
        }

        public LocalSignalDB(ControlPanelViewModel ViewModel, string Filename)
        {
            this._ViewModel = ViewModel;
            this._SynchronizationStatus = ViewModel.SynchronizationStatus;

            // 1. Open the database.
            var con_builder = new SQLiteConnectionStringBuilder();
            con_builder.DataSource = Filename;
            con_builder.LegacyFormat = false;   // No need for the legacy format.
            con_builder.FailIfMissing = false;  // Simply re-create.
            con_builder.DateTimeFormat = SQLiteDateFormats.Ticks;   // Not recommended, but it works best.

            _SqlCon = new SQLiteConnection(con_builder.ConnectionString);
            _SqlCon.Open();

            // 2. Re-create the tables, if needed.
            _ExecuteNonQuery("CREATE TABLE IF NOT EXISTS Signals (Id int primary key not null, Timestamp datetime unique not null, Version int not null, SignalValues blob not null)");
            _ExecuteNonQuery("CREATE TABLE IF NOT EXISTS WorkTimes (Id int primary key not null, Version int not null, SignalId int unique not null, WorkTimeSeconds varchar(250) not null)");
            // Id - row Id.
            // Version - database version Id.
            // SignalId - Index into the Signals database, pointing to the last accounted row.
            // WorkingSeconds - semicolon-separated list of working seconds for each pump defined in the "signal-name=workinghours-in-seconds overhaultime-in-seconds".

            // Have to fetch the last known WorkingHours.
        }

        /// <summary>
        /// Close the database.
        /// </summary>
        public void Close()
        {
            var sqlcon = _SqlCon;
            _SqlCon = null;
            if (sqlcon != null)
            {
                sqlcon.Close();
            }
        }

        /// <summary>
        /// Write signals to the file log. Only packets differing from the previous one are considered for writing.
        /// </summary>
        /// <param name="Filename"></param>
        /// <param name="BeginTime"></param>
        /// <param name="EndTime"></param>
        public void SaveToFile(string Filename, DateTime BeginTime, DateTime EndTime)
        {
            using (var tw = new System.IO.StreamWriter(Filename))
            {
                // 1. Header.
                tw.Write("Id\tKellaaeg\t");
                foreach (var ios in _ViewModel.Signals)
                {
                    tw.Write(ios.Name);
                    tw.Write('\t');
                }
                tw.WriteLine();

                // 2. Copy over the data....
                using (var cmd_query = new SQLiteCommand("SELECT SignalValues, Id, Timestamp FROM Signals WHERE Timestamp>=(?) And Timestamp<(?) Order By Timestamp Asc", _SqlCon))
                {
                    // 2.1. SQL parameters.
                    var param_begin = new SQLiteParameter(System.Data.DbType.Int64);
                    param_begin.Value = BeginTime.Ticks;
                    cmd_query.Parameters.Add(param_begin);

                    var param_end = new SQLiteParameter(System.Data.DbType.Int64);
                    param_end.Value = EndTime.Ticks;
                    cmd_query.Parameters.Add(param_end);

                    // 2.2. Count the bytes needed for a buffer.
                    var SignalCount = _ViewModel.Signals.Count;
                    var UnpackedSignals = new Tuple<bool, int>[SignalCount];
                    var ByteCount = _ViewModel.PlcConnection.BytesInPacket;
                    var Packet = new byte[ByteCount];
                    byte[] LastPacket = null;
                    using (var reader = cmd_query.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            // 3. Packet.
                            reader.GetBytes(0, 0, Packet, 0, ByteCount);
                            bool is_different = false; // is the packet different from the last one?
                            if (LastPacket == null)
                            {
                                LastPacket = new byte[ByteCount];
                                is_different = true;
                            }
                            else
                            {
                                for (int i=0; i<ByteCount; ++i)
                                {
                                    if (LastPacket[i]!=Packet[i])
                                    {
                                        is_different = true;
                                        break;
                                    }
                                }
                            }

                            if (is_different)
                            {
                                // 1. Id.
                                int id = reader.GetInt32(1);
                                tw.Write(id.ToString() + "\t");

                                // 2. Datetime.
                                DateTime dt = reader.GetDateTime(2);
                                tw.Write(dt.ToString() + "\t");


                                _ViewModel.PlcConnection.ExtractSignals(UnpackedSignals, Packet);
                                for (int i = 0; i < SignalCount; ++i)
                                {
                                    var kv = UnpackedSignals[i];
                                    if (kv.Item1)
                                    {
                                        tw.Write(kv.Item2.ToString() + "\t");
                                    }
                                    else
                                    {
                                        tw.Write("x\t");
                                    }
                                }
                                tw.WriteLine();
                            }

                            for (int i = 0; i < ByteCount; ++i)
                            {
                                LastPacket[i] = Packet[i];
                            }
                        }
                    }
                }
            }
        }

        // 1. Query for the first item, preceeding the rest.
        SQLiteCommand _FetchByTimeRangeCommandFirst = null;
        SQLiteParameter _FetchByTimeRangeParamFirstTime = new SQLiteParameter(System.Data.DbType.Int64);

        // 2. Query for the main batch.
        SQLiteCommand _FetchByTimeRangeCommand = null;
        SQLiteParameter _FetchByTimeRangeParamTimeBegin = new SQLiteParameter(System.Data.DbType.Int64);
        SQLiteParameter _FetchByTimeRangeParamTimeEnd = new SQLiteParameter(System.Data.DbType.Int64);

        // 3. Query for the tail.
        SQLiteCommand _FetchByTimeRangeCommandLast = null;
        SQLiteParameter _FetchByTimeRangeParamLastTime = new SQLiteParameter(System.Data.DbType.Int64);

        /// <summary>
        /// Utility function for fetching rows.
        /// </summary>
        /// <param name="command"></param>
        static void _FetchRowsAsSignalValuesType(List<SignalValuesType> Destination, SQLiteCommand Command)
        {
            using (var reader = Command.ExecuteReader())
            {
                while (reader.Read())
                {
                    var e = new SignalValuesType()
                    {
                        Id = reader.GetInt32(0),
                        Version = reader.GetInt32(1),
                        Timestamp = reader.GetInt64(2),
                        Values = reader.GetValue(3) as byte[],
                    };

                    Destination.Add(e);
                }
            }
        }

        /// <summary>
        /// Fetch some rows according to given time range.
        /// </summary>
        /// <param name="TimeBegin"></param>
        /// <param name="TimeEnd"></param>
        /// <returns></returns>
        public List<SignalValuesType> FetchByTimeRangeIncluding(DateTime TimeBegin, DateTime TimeEnd)
        {
            var r = new List<SignalValuesType>();

            if (_FetchByTimeRangeCommand == null)
            {
                // SELECT ... WHERE
                const string select_where = "SELECT Id, Version, Timestamp, SignalValues FROM Signals WHERE ";
                // 1. Query for the preceeding item, if any.
                _FetchByTimeRangeCommandFirst = new SQLiteCommand(select_where + "Timestamp<(?) Order By Timestamp Desc Limit 1", _SqlCon);
                _FetchByTimeRangeCommandFirst.Parameters.Add(_FetchByTimeRangeParamFirstTime);

                // 2. Query for the main batch.
                _FetchByTimeRangeCommand = new SQLiteCommand(select_where + "Timestamp>=(?) And Timestamp<=(?) Order By Timestamp Asc", _SqlCon);
                _FetchByTimeRangeCommand.Parameters.Add(_FetchByTimeRangeParamTimeBegin);
                _FetchByTimeRangeCommand.Parameters.Add(_FetchByTimeRangeParamTimeEnd);

                // 3. Query for the succeeding item, if any.
                _FetchByTimeRangeCommandLast = new SQLiteCommand(select_where + "Timestamp>(?) Order By Timestamp Asc Limit 1", _SqlCon);
                _FetchByTimeRangeCommandLast.Parameters.Add(_FetchByTimeRangeParamLastTime);
            }
            // Parameters.
            _FetchByTimeRangeParamFirstTime.Value = TimeBegin.Ticks;
            _FetchByTimeRangeParamTimeBegin.Value = TimeBegin.Ticks;
            _FetchByTimeRangeParamTimeEnd.Value = TimeEnd.Ticks;
            _FetchByTimeRangeParamLastTime.Value = TimeEnd.Ticks;

            // Fetch the stuff!
            _FetchRowsAsSignalValuesType(r, _FetchByTimeRangeCommandFirst);
            _FetchRowsAsSignalValuesType(r, _FetchByTimeRangeCommand);
            _FetchRowsAsSignalValuesType(r, _FetchByTimeRangeCommandLast);

            return r;
        }


        SQLiteCommand _FetchByIdRangeCommand = null;

        /// <summary>
        /// Fetch a range of rows, TailId inclusive, HeadId exclusive.
        /// </summary>
        /// <param name="HeadId"></param>
        /// <param name="TailId"></param>
        /// <returns></returns>
        public List<SignalValuesType> FetchByIdRange(int TailId, int HeadId)
        {
            var r = new List<SignalValuesType>();
            if (_FetchByIdRangeCommand == null)
            {
                const string select_where = "SELECT Id, Version, Timestamp, SignalValues FROM Signals WHERE ";
                _FetchByIdRangeCommand = new SQLiteCommand(select_where + "Id>=(?) And Id<(?) Order By Id Asc", _SqlCon);
                _FetchByIdRangeCommand.Parameters.Add(new SQLiteParameter(System.Data.DbType.Int32));
                _FetchByIdRangeCommand.Parameters.Add(new SQLiteParameter(System.Data.DbType.Int32));
            }
            _FetchByIdRangeCommand.Parameters[0].Value = TailId;
            _FetchByIdRangeCommand.Parameters[1].Value = HeadId;

            _FetchRowsAsSignalValuesType(r, _FetchByIdRangeCommand);

            return r;
        }

        /// <summary>Set up the bitmask of rows to be requested from the PLC.
        /// This should be the first message to be handled.
        /// </summary>
        /// <param name="IdRange"></param>
        public void HandlePlcDatabaseRange(IdRange IdRange)
        {
            if (IdRange.HasHeadId && IdRange.HasTailId)
            {
                _SynchronizationStatus.RemoteTailId = IdRange.TailId;
                _SynchronizationStatus.RemoteHeadId = IdRange.HeadId;
                int ntotal = _SynchronizationStatus.RemoteHeadId - _SynchronizationStatus.RemoteTailId;

                // true = row is present.
                // false = row is not present.
                _IsRowPresent = new BitArray(ntotal);
                var local_track_id = _SynchronizationStatus.RemoteTailId; // default to start
                _SynchronizationStatus.LocalTrackId = _SynchronizationStatus.RemoteTailId; // default to start.

                int nrows_present = 0;

                // Scan the local database. Keep an eye on SynchronizationStatus.LocalTrackId as well.
                using (var cmd_query = new SQLiteCommand("SELECT Id, Timestamp FROM Signals WHERE Id>=(?) And Id<(?) Order By Id Asc", _SqlCon))
                {
                    // for some reason the following shortcut:
                    // cmd_query.Parameters.Add(new SQLiteParameter(System.Data.DbType.Int32, tail_id));
                    // doesn't work :(
                    var param_tail = new SQLiteParameter(System.Data.DbType.Int32);
                    param_tail.Value = _SynchronizationStatus.RemoteTailId;
                    cmd_query.Parameters.Add(param_tail);

                    var param_head = new SQLiteParameter(System.Data.DbType.Int32);
                    param_head.Value = _SynchronizationStatus.RemoteHeadId;
                    cmd_query.Parameters.Add(param_head);

                    DateTime? last_time = null;

                    using (var reader = cmd_query.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var id = reader.GetInt32(0);
                            int index = id - _SynchronizationStatus.RemoteTailId;
                            if (index >= 0 && index < _IsRowPresent.Length)
                            {
                                _IsRowPresent[index] = true;
                                ++nrows_present;
                            }
                            // SynchronizationStatus.LocalTrackId
                            if (id == local_track_id)
                            {
                                local_track_id = _NextTrackId(local_track_id);
                            }
                            var dt = reader.GetDateTime(1);
                            if (id == _SynchronizationStatus.RemoteTailId)
                            {
                                _SynchronizationStatus.RemoteTailTimestamp = dt;
                            }
                            if (last_time.HasValue && last_time.Value >= dt)
                            {
                                _ViewModel.LogLine("LocalSignalDb: time disorder at id:" + id + " , time: " + dt.Ticks + " => " + dt.ToString() + ", previous time: " + last_time.Value.Ticks + " => " + last_time.Value.ToString() + ".");
                            }
                            last_time = dt;
                        }
                    }
                }

                _SynchronizationStatus.LocalTrackId = local_track_id;
                _SetIsFinished(local_track_id == _SynchronizationStatus.RemoteHeadId);

                _ViewModel.LogLine("LocalSignalDB: PLC DB range is [" + _SynchronizationStatus.RemoteTailId + " .. " + _SynchronizationStatus.RemoteHeadId + "), total " + ntotal + " rows, of which " + nrows_present + " rows are present in the local db.");
                _ViewModel.LogLine("LocalSignalDB: TrackId is " + _SynchronizationStatus.LocalTrackId + ".");
            }
        }

        /// <summary>
        /// And another message.
        /// </summary>
        /// <param name="SignalValues"></param>
        public void HandleSignalValues(IEnumerable<MessageFromPlc.Types.SignalValuesType> ListOfSignalValues)
        {
            // Oops, invalid packet order.
            if (_IsRowPresent == null)
            {
                return;
            }

            var old_track_id = _SynchronizationStatus.LocalTrackId;
            var ordered_signals = from s in ListOfSignalValues where s.HasRowId orderby s.RowId ascending select s;
            var next_track_id = old_track_id;
            using (var transaction = _SqlCon.BeginTransaction())
            {
                foreach (var SignalValues in ordered_signals)
                {
                    _HandleSignalValues(SignalValues, ref next_track_id);
                } // foreach
                transaction.Commit();
            } // begin transaction.

            _SynchronizationStatus.LocalTrackId = next_track_id;
            _SetIsFinished(next_track_id == _SynchronizationStatus.RemoteHeadId);
        }

        public void HandleSignalValues(MessageFromPlc.Types.SignalValuesType SignalValues)
        {
            // Oops, invalid packet order.
            if (_IsRowPresent == null)
            {
                return;
            }

            var old_track_id = _SynchronizationStatus.LocalTrackId;
            var next_track_id = old_track_id;
            using (var transaction = _SqlCon.BeginTransaction())
            {
                _HandleSignalValues(SignalValues, ref next_track_id);
                transaction.Commit();
            } // begin transaction.

            _SynchronizationStatus.LocalTrackId = next_track_id;
            _SetIsFinished(next_track_id == _SynchronizationStatus.RemoteHeadId);
            if (old_track_id != _SynchronizationStatus.LocalTrackId)
            {
                _ViewModel.LogLine("LocalSignalDB: New TrackId is " + _SynchronizationStatus.LocalTrackId + ".");
            }
        }

        /// <summary>
        /// Handle the signal values.
        /// 1. This has to be called in the external SQL transaction.
        /// 2. The LocalTrackId parameter is used for collecting batch of updates to the SynchronizationStatus.LocalTrackId.
        /// </summary>
        /// <param name="SignalValues">Signal values to be handled.</param>
        /// <param name="LocalTrackId">Copy of SynchronizationStatus.LocalTrackId.</param>
        public void _HandleSignalValues(MessageFromPlc.Types.SignalValuesType SignalValues, ref int LocalTrackId)
        {
            // Have we all the fields, which are required in fact?
            if (SignalValues.HasRowId && SignalValues.HasVersion && SignalValues.HasTimeMs && SignalValues.HasSignalValues)
            {
                var id = SignalValues.RowId;

                // 1. Is it the old data in the PLC database?
                int plc_index = id - _SynchronizationStatus.RemoteTailId;
                if (plc_index >= 0 && plc_index < _IsRowPresent.Length)
                {
                    // Do we have to get it?
                    if (!_IsRowPresent[plc_index])
                    {
                        _InsertSignalValues(SignalValues);
                        _IsRowPresent[plc_index] = true;
                    }
                }
                else if (plc_index >= _IsRowPresent.Length)
                {
                    // Case 1: No packets beforehand.
                    // Case 2: 
                    if (_LastOOBRowIdReceived < 0 || id == (_LastOOBRowIdReceived + 1))
                    {
                        if (_LastOOBRowIdReceived < 0 && _IsIdPresent(id))
                        {
                            // Pass this wonderful opportunity.
                            _ViewModel.LogLine("LocalSignalDB: Id " + id + " already present (probably before internet outage?), will not insert.");
                        }
                        else
                        {
                            // insert!
                            _InsertSignalValues(SignalValues);
                            _LastOOBRowIdReceived = id;
                        }
                    }
                    else
                    {
                        if (id != _LastOOBRowIdReceived)
                        {
                            _ViewModel.LogLine("LocalSignalDB: Cannot handle signal values with non-contiguous row id of " + id + ".");
                        }
                    }
                }
                else if (plc_index < 0)
                {
                    _ViewModel.LogLine("LocalSignalDB: Unexpected signal values with prehistoric row id of " + id + ".");
                }
                // TrackId needs tracking in any case.
                if (id == LocalTrackId)
                {
                    LocalTrackId = _NextTrackId(LocalTrackId);
                }
                if (id == _SynchronizationStatus.RemoteTailId)
                {
                    _SynchronizationStatus.RemoteTailTimestamp = SignalValues.GetTimestamp();
                }
            }
            else
            {
                _ViewModel.LogLine("LocalSignalDB: Signal values record has some fields missing: " + SignalValues.ToString());
            }
        }

        /// <summary>
        /// Get the next batch of rows to be fetched.
        /// </summary>
        /// <returns></returns>
        public IdRange NextBatch()
        {
            if (_SynchronizationStatus.LocalTrackId < _SynchronizationStatus.RemoteHeadId)
            {
                int tail_index = _SynchronizationStatus.LocalTrackId - _SynchronizationStatus.RemoteTailId;
                int head_index = tail_index + 1;

                // Fetch up to MAX_BATCH_SIZE continuous blocks.
                while (head_index < _IsRowPresent.Length && (head_index - tail_index) < MAX_BATCH_SIZE && !_IsRowPresent[head_index])
                {
                    ++head_index;
                }

                var rb = IdRange.CreateBuilder();
                rb.SetTailId(tail_index + _SynchronizationStatus.RemoteTailId);
                rb.SetHeadId(head_index + _SynchronizationStatus.RemoteTailId);
                return rb.Build();
            }
            return null;
        }

        SQLiteCommand _FetchWorkingTimesCommand = null;
        public Tuple<int,Dictionary<string, double>> FetchWorkingTimes()
        {
            if (_FetchWorkingTimesCommand == null)
            {
                _FetchWorkingTimesCommand = new SQLiteCommand("SELECT  ", _SqlCon);
            }

#if (false)
            using (var reader = Command.ExecuteReader())
            {
                while (reader.Read())
                {
                    var e = new SignalValuesType()
                    {
                        Id = reader.GetInt32(0),
                        Version = reader.GetInt32(1),
                        Timestamp = reader.GetInt64(2),
                        Values = reader.GetValue(3) as byte[],
                    };

                    Destination.Add(e);
                }
            }

#endif

            var rdict = new Dictionary<string, double>();
            var r = new Tuple<int, Dictionary<string, double>>(3, rdict);

            return r;
        }

        SQLiteCommand _AppendWorkingTimesCommand = null;
        public void AppendWorkingTimes(int SignalId, Dictionary<string, double> WorkingTimes)
        {
        }


        /// <summary>
        /// Advance track id to the next unfetched row.
        /// </summary>
        /// <param name="StartId">ID to start scanning from.</param>
        /// <returns>Next tracking id.</returns>
        int _NextTrackId(int StartId)
        {
            int head_id = _SynchronizationStatus.RemoteHeadId;
            int index = StartId - _SynchronizationStatus.RemoteTailId;

            while (index < _IsRowPresent.Length && _IsRowPresent[index])
            {
                ++index;
            }

            return index + _SynchronizationStatus.RemoteTailId;
        }

        SQLiteCommand _QueryCountCommand = null;
        int _QueryCountByTime(long timestamp)
        {
            // ViewModel.LogLine("_InsertSignalValues, id " + SignalValues.RowId);
            // 1. Shall we lazily create the command?
            if (_QueryCountCommand == null)
            {
                _QueryCountCommand = new SQLiteCommand("SELECT COUNT(*) FROM Signals WHERE Timestamp=(?)", _SqlCon);

                _QueryCountCommand.Parameters.Add(new SQLiteParameter(System.Data.DbType.Int64));
            }
            _QueryCountCommand.Parameters[0].Value = timestamp;
            int count = 0;

            using (var reader = _QueryCountCommand.ExecuteReader())
            {
                while (reader.Read())
                {
                    count = reader.GetInt32(0);
                }
            }
            return count;
        }

        SQLiteCommand _IsIdPresentCommand = null;
        /// <summary>
        /// Is the row with given Id present in our database?
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        bool _IsIdPresent(int id)
        {
            if (_IsIdPresentCommand == null)
            {
                _IsIdPresentCommand = new SQLiteCommand("SELECT COUNT(*) FROM Signals WHERE Id=(?)", _SqlCon);
                _IsIdPresentCommand.Parameters.Add(new SQLiteParameter(System.Data.DbType.Int32));
            }
            _IsIdPresentCommand.Parameters[0].Value = id;

            int count = 0;
            using (var reader = _IsIdPresentCommand.ExecuteReader())
            {
                while (reader.Read())
                {
                    count = reader.GetInt32(0);
                }
            }
            return count>0;
        }

        SQLiteCommand _InsertCommand = null;
        SQLiteParameter _InsertParamId = null;
        SQLiteParameter _InsertParamTimestamp = null;
        SQLiteParameter _InsertParamVersion = null;
        SQLiteParameter _InsertParamValues = null;

        /// <summary>
        /// Insert signal values into the underlying SQLite database...
        /// </summary>
        /// <param name="SignalValues"></param>
        void _InsertSignalValues(MessageFromPlc.Types.SignalValuesType SignalValues)
        {
            // ViewModel.LogLine("_InsertSignalValues, id " + SignalValues.RowId);
            // 1. Shall we lazily create the command?
            if (_InsertCommand == null)
            {
                _InsertCommand = new SQLiteCommand("INSERT INTO Signals(Id, Timestamp, Version, SignalValues) Values((?), (?), (?), (?))", _SqlCon);

                _InsertParamId = new SQLiteParameter(System.Data.DbType.Int32);
                _InsertCommand.Parameters.Add(_InsertParamId);

                _InsertParamTimestamp = new SQLiteParameter(System.Data.DbType.Int64);
                _InsertCommand.Parameters.Add(_InsertParamTimestamp);

                _InsertParamVersion = new SQLiteParameter(System.Data.DbType.Int32);
                _InsertCommand.Parameters.Add(_InsertParamVersion);

                _InsertParamValues = new SQLiteParameter(System.Data.DbType.Binary);
                _InsertCommand.Parameters.Add(_InsertParamValues);

            }

            long original_timestamp = SignalValues.GetTimestamp().Ticks;
            long real_timestamp = original_timestamp;
            // _ViewModel.LogLine("LocalSignalDb: Trying to insert id: " + SignalValues.RowId + ", ts: " + real_timestamp + ".");
            while (_QueryCountByTime(real_timestamp) > 0)
            {
                ++real_timestamp;
            }
            if (original_timestamp != real_timestamp)
            {
                _ViewModel.LogLine("LocalSignalDb: inserting record (id: " + SignalValues.RowId + ", ts: " + original_timestamp + ") matches exisiting timestamp, changing to " + real_timestamp + ".");
            }
            // 2. Set the parameters.
            _InsertParamId.Value = SignalValues.RowId;
            _InsertParamTimestamp.Value = real_timestamp;
            _InsertParamVersion.Value = SignalValues.Version;
            _InsertParamValues.Value = SignalValues.SignalValues.ToByteArray();

            // 3. Go!
            _InsertCommand.ExecuteNonQuery();
        }

        void _SetIsFinished(bool value)
        {
            bool old_value = _SynchronizationStatus.IsFinished;
            _SynchronizationStatus.IsFinished = value;
            if (old_value == false && value == true)
            {
                _ViewModel.Dispatcher.BeginInvoke(new Action(_ViewModel.OnSynchronizationAttained), null);
            }
        }

        /// <summary>
        /// Execute a one-off query.
        /// </summary>
        /// <param name="SqlCommand">SQL command to be executed.</param>
        void _ExecuteNonQuery(string SqlCommand)
        {
            using (var cmd_recreate = new SQLiteCommand(SqlCommand, _SqlCon))
            {
                cmd_recreate.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Local database.
        /// </summary>
        SQLiteConnection _SqlCon;

        readonly ControlPanelViewModel _ViewModel;
        /// <summary>
        /// Shortcut to _ViewModel.SynchronizationStatus.
        /// </summary>
        readonly SynchronizationStatus _SynchronizationStatus;

        /// <summary>
        /// Is row present in the local db?
        /// </summary>
        BitArray _IsRowPresent = null;

        /// <summary>
        /// Id of the last OOB row data received.
        /// </summary>
        int _LastOOBRowIdReceived = -1;
    }
}
