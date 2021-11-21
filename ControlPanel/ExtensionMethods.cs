using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ControlPanel
{
    public static class ExtensionMethods
    {
        /// <summary>
        /// Find one IOSignal by either name or by Id.
        /// </summary>
        /// <param name="Signals">List of signals to search from.</param>
        /// <param name="NameOrId">Name, or Id to search by.</param>
        /// <returns></returns>
        public static IOSignal SingleOrDefaultByNameOrId(this IEnumerable<IOSignal> Signals, string NameOrId)
        {
            IOSignal r;
            int id;

            r = (from ios in Signals where ios.Name == NameOrId select ios).SingleOrDefault();
            if (r == null && int.TryParse(NameOrId, out id))
            {
                r = (from ios in Signals where ios.Id == id select ios).SingleOrDefault();
            }
            return r;
        }

        /// <summary>
        /// Datetime corresponding to Java's System.currentTimeMillis(). 
        /// </summary>
        /// <param name="SignalValues"></param>
        /// <returns></returns>
        public static DateTime GetTimestamp(this MessageFromPlc.Types.SignalValuesType SignalValues)
        {
            return _DateTimeOfJavaMilliseconds(SignalValues.TimeMs);
        }

        static DateTime _RefTimeJava = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        static DateTime _DateTimeOfJavaMilliseconds(long ms)
        {
            // this two-step process should preserve precision :)
            long minutes = ms / 60000;
            long milliseconds = ms - minutes * 60000;
            var r = _RefTimeJava.AddMinutes(minutes).AddMilliseconds(milliseconds).ToLocalTime();
            return r;
        }

        static long _TicksOfJavaMilliseconds(long ms)
        {
            var r = _DateTimeOfJavaMilliseconds(ms);
            return r.Ticks;
        }

    }
}
