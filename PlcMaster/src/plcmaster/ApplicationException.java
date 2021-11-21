/*
 */
package plcmaster;

/** Exception to be thrown in the PlcMaster application.
 * Rationale: permits debugging application exceptions separately from other exceptions.
 * 
 * @author Andrei
 */
public class ApplicationException extends  Exception {
    public ApplicationException(String message)
    {
        super(message);
    }
    
    public ApplicationException(Throwable cause)
    {
        super(cause);
    }
}
