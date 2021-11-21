/*
 * To change this template, choose Tools | Templates
 * and open the template in the editor.
 */
package plcmaster;

/**
 *
 * @author Andrei
 */
public final class LogicExpression {
    public enum TYPE {
        LITERAL_INT,
        LITERAL_BOOLEAN,
        UNARY_OP_SIGNAL,
        UNARY_OP_IS_CONNECTED,
        BINARY_OP_AND,
        BINARY_OP_OR,
        BINARY_OP_XOR,
        UNARY_OP_NOT
    }
    
    final PlcContext Context;
    final TYPE Type;
    
    final LogicExpression X1;
    final LogicExpression X2;

    final IOSignal Signal;
    final int LiteralValue;

    public LogicExpression(
        PlcContext Context,
        TYPE Type,
        LogicExpression X1, LogicExpression X2,
        IOSignal Signal,
        int LiteralValue
        )
    {
        this.Context = Context;
        this.Type = Type;
        this.X1 = X1;
        this.X2 = X2;
        this.Signal = Signal;
        this.LiteralValue = LiteralValue;
    }
    
    public final int Evaluate()
    {
        switch (Type)
        {
            case LITERAL_INT:
                return this.LiteralValue;
            case LITERAL_BOOLEAN:
                return this.LiteralValue;
            case UNARY_OP_SIGNAL:
                return Signal == null ? 0 : Signal.Get();
            case UNARY_OP_IS_CONNECTED:
                // Signals with no device are always connected!
                return Signal == null
                        ? 0
                        : (Signal.Device==null ? 1 : (Signal.Device.IsLastSyncOk ? 1 : 0));
            case BINARY_OP_AND:
                return (X1.Evaluate() != 0) && (X2.Evaluate() != 0) ? 1 : 0;
            case BINARY_OP_OR:
                return (X1.Evaluate() != 0) || (X2.Evaluate() != 0) ? 1 : 0;
            case BINARY_OP_XOR:
                return (X1.Evaluate() != 0) ^ (X2.Evaluate() != 0) ? 1 : 0;
            case UNARY_OP_NOT:
                return (X1.Evaluate()!=0) ? 0 : 1;
            default:
                PlcMaster.LogLine("LogicExpression.Evaluate: Unexpected type: " + (Type) + "!");
                return 0;
        }
    }
}
