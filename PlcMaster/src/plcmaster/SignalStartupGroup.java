/*
 * To change this template, choose Tools | Templates
 * and open the template in the editor.
 */
package plcmaster;

/**
 *
 * @author Andrei
 */
public class SignalStartupGroup {
    /** Name of the group. */
    public final String Name;
    /** Minimum interval, milliseconds, between the signals set to be logical '1' */
    public final static long MIN_INTERVAL_MS = 10 * 1000;
    /** Time of last update of signals, milliseconds. */
    public long LastTimeMs = System.currentTimeMillis() - 2*MIN_INTERVAL_MS;
    
    /** Compares the given time with the last time update and minimum interval.
     * Updates the last time update and returns true if the minimum interval is exceeded.
     * 
     * @param TimeNowMs Current time, milliseconds.
     * @return true iff signal update permitted (this round), false otherwise.
     */
    public final boolean TryRegisterUpdate(long TimeNowMs)
    {
        boolean is_permitted = LastTimeMs + MIN_INTERVAL_MS < TimeNowMs;
        if (is_permitted)
        {
            this.LastTimeMs = TimeNowMs;
        }
        return is_permitted;
    }
    
    public SignalStartupGroup(String Name)
    {
        this.Name = Name;
    }
}
