/*
 */
package plcmaster;

import java.util.*;

/**
 * Logical program statement.
 * @author Andrei
 */
public final class LogicStatement {
    public enum TYPE {
        ASSIGNMENT,
        IF,
    }
    
    final PlcContext Context;
    public final TYPE Type;
    public final IOSignal Destination;
    public final LogicExpression ExpressionOrCondition;
    public final List<LogicStatement> IfStatements;
    public final List<LogicStatement> ElseStatements;
    
    
    public LogicStatement(
            PlcContext Context,
            TYPE Type,
            IOSignal Destination, LogicExpression ExpressionOrCondition,
            List<LogicStatement> IfStatements,
            List<LogicStatement> ElseStatements
            )
    {
        this.Context = Context;
        this.Type = Type;
        this.Destination = Destination;
        this.ExpressionOrCondition = ExpressionOrCondition;
        this.IfStatements = IfStatements;
        this.ElseStatements = ElseStatements;
    }
    
    public final void Execute()
    {
        switch (Type)
        {
            case ASSIGNMENT:
                {
                    int value = ExpressionOrCondition.Evaluate();
                    if (Destination != null)
                    {
                        Destination.Set(value);
                    }
                }
                break;
            case IF:
                {
                    int value = ExpressionOrCondition.Evaluate();
                    if (value!=0)
                    {
                        // true!
                        for (LogicStatement ls : IfStatements)
                        {
                            ls.Execute();
                        }
                    }
                    else if (ElseStatements!=null)
                    {
                        // false, maybe.
                        for (LogicStatement ls : ElseStatements)
                        {
                            ls.Execute();
                        }
                    }
                }
                break;
            default:
                PlcMaster.LogLine("LogicStatement.Execute: Unexpected type: " + (Type) + "!");
                break;
        }
    }
}
