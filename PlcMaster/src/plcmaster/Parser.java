package plcmaster;

import java.util.*;



public class Parser {
	public static final int _EOF = 0;
	public static final int _string = 1;
	public static final int _intliteral = 2;
	public static final int _ident = 3;
	public static final int maxT = 22;

	static final boolean T = true;
	static final boolean x = false;
	static final int minErrDist = 2;

	public Token t;    // last recognized token
	public Token la;   // lookahead token
	int errDist = minErrDist;
	
	public Scanner scanner;
	public Errors errors;

	public List<LogicStatement> Result;
public PlcContext Context;

public IOSignal _SignalById(int id)
{
	IOSignal r = Context.SignalTable.get(id);
	if (r == null)
	{
		SemErr("Use of undeclared signal id: " + id + "!");
	}
	return r;
}

public IOSignal _SignalByName(String name)
{
	IOSignal r = Context.LocalSignalMap.get(name);
	if (r == null)
	{
		r = Context.SignalMap.get(name);
	}
	if (r == null)
	{
		SemErr("Use of undeclared signal name: " + name + "!");
	}
	return r;
}

public LogicExpression ConcatExpression(LogicExpression expr, LogicExpression.TYPE op, LogicExpression expr2)
{
	LogicExpression r = new LogicExpression(Context, op, expr, expr2, null, -1);
	return r;
}



	public Parser(Scanner scanner) {
		this.scanner = scanner;
		errors = new Errors();
	}

	void SynErr (int n) {
		if (errDist >= minErrDist) errors.SynErr(la.line, la.col, n);
		errDist = 0;
	}

	public void SemErr (String msg) {
		if (errDist >= minErrDist) errors.SemErr(t.line, t.col, msg);
		errDist = 0;
	}
	
	void Get () {
		for (;;) {
			t = la;
			la = scanner.Scan();
			if (la.kind <= maxT) {
				++errDist;
				break;
			}

			la = t;
		}
	}
	
	void Expect (int n) {
		if (la.kind==n) Get(); else { SynErr(n); }
	}
	
	boolean StartOf (int s) {
		return set[s][la.kind];
	}
	
	void ExpectWeak (int n, int follow) {
		if (la.kind == n) Get();
		else {
			SynErr(n);
			while (!StartOf(follow)) Get();
		}
	}
	
	boolean WeakSeparator (int n, int syFol, int repFol) {
		int kind = la.kind;
		if (kind == n) { Get(); return true; }
		else if (StartOf(repFol)) return false;
		else {
			SynErr(n);
			while (!(set[syFol][kind] || set[repFol][kind] || set[0][kind])) {
				Get();
				kind = la.kind;
			}
			return StartOf(syFol);
		}
	}
	
	void LogicProgram() {
		LogicStatement st = null; 
		st = LogicStatement1();
		if (st!=null) { Result.add(st); } 
		while (StartOf(1)) {
			st = LogicStatement1();
			if (st!=null) { Result.add(st); } 
		}
	}

	LogicStatement  LogicStatement1() {
		LogicStatement  st;
		st = null; 
		if (la.kind == 4) {
			VariableStatement();
		} else if (la.kind == 6) {
			st = ConditionalStatement();
		} else if (la.kind == 3 || la.kind == 10) {
			st = AssignmentStatement();
		} else SynErr(23);
		return st;
	}

	void VariableStatement() {
		Expect(4);
		Expect(3);
		Context.LocalSignalMap.put(t.val, new IOSignal(t.val, -1, IOType.COIL, -1, -1, null)); 
		Expect(5);
	}

	LogicStatement  ConditionalStatement() {
		LogicStatement  ls;
		LogicExpression condition = null;
		LogicStatement ls1 = null;
		List<LogicStatement> if_s = new ArrayList<LogicStatement>();
		List<LogicStatement> else_s = null;
		
		Expect(6);
		condition = Expr();
		Expect(7);
		while (StartOf(1)) {
			ls1 = LogicStatement1();
			if (ls1!=null) { if_s.add(ls1); } 
		}
		if (la.kind == 8) {
			Get();
			else_s = new ArrayList<LogicStatement>(); 
			while (StartOf(1)) {
				ls1 = LogicStatement1();
				if (ls1!=null) { else_s.add(ls1); } 
			}
		}
		Expect(9);
		Expect(5);
		ls = new LogicStatement(Context, LogicStatement.TYPE.IF, null, condition, if_s, else_s); 
		return ls;
	}

	LogicStatement  AssignmentStatement() {
		LogicStatement  ls;
		IOSignal dst = null;
		LogicExpression expr = null; 
		if (la.kind == 10) {
			Get();
			if (la.kind == 3) {
				Get();
				dst= _SignalByName(t.val); 
			} else if (la.kind == 2) {
				Get();
				dst = _SignalById(Integer.parseInt(t.val)); 
			} else SynErr(24);
		} else if (la.kind == 3) {
			Get();
		} else SynErr(25);
		dst = _SignalByName(t.val); 
		Expect(11);
		expr = Expr();
		Expect(5);
		if (!dst.Type.CanWrite) {
		SemErr("Cannot assign to read-only " + dst.toString() + "!");
		}
		ls = new LogicStatement(Context, LogicStatement.TYPE.ASSIGNMENT, dst, expr, null, null); 
		return ls;
	}

	LogicExpression  Expr() {
		LogicExpression  exp;
		exp = null;
		LogicExpression exp2 = null;
		LogicExpression.TYPE op = LogicExpression.TYPE.LITERAL_INT; 
		exp = AndExp();
		while (la.kind == 20 || la.kind == 21) {
			op = OrOp();
			exp2 = AndExp();
			exp = ConcatExpression(exp, op, exp2); 
		}
		return exp;
	}

	LogicExpression  Number() {
		LogicExpression  num;
		Expect(2);
		num = new LogicExpression(Context, LogicExpression.TYPE.LITERAL_INT, null, null, null, Integer.parseInt(t.val)); 
		return num;
	}

	LogicExpression  TrueOrFalse() {
		LogicExpression  b;
		b = null; 
		if (la.kind == 12) {
			Get();
			b = new LogicExpression(Context, LogicExpression.TYPE.LITERAL_INT, null, null, null,  1); 
		} else if (la.kind == 13) {
			Get();
			b = new LogicExpression(Context, LogicExpression.TYPE.LITERAL_INT, null, null, null,  0); 
		} else SynErr(26);
		return b;
	}

	LogicExpression  SignalOrIsConnected() {
		LogicExpression  le;
		le = null;
		LogicExpression.TYPE type = LogicExpression.TYPE.UNARY_OP_SIGNAL; 
		if (la.kind == 10 || la.kind == 15) {
			type = SignalOp();
			if (la.kind == 3) {
				Get();
				le = new LogicExpression(Context, type, null, null, _SignalByName(t.val),  -1); 
			} else if (la.kind == 2) {
				Get();
				le = new LogicExpression(Context, type, null, null, _SignalById(Integer.parseInt(t.val)), -1); 
			} else SynErr(27);
		} else if (la.kind == 14) {
			Get();
			if (la.kind == 3) {
				Get();
				le = ConcatExpression(new LogicExpression(Context, LogicExpression.TYPE.UNARY_OP_IS_CONNECTED, null, null, _SignalByName(t.val),  -1), LogicExpression.TYPE.UNARY_OP_NOT, null); 
			} else if (la.kind == 2) {
				Get();
				le = ConcatExpression(new LogicExpression(Context, LogicExpression.TYPE.UNARY_OP_IS_CONNECTED, null, null, _SignalById(Integer.parseInt(t.val)), -1), LogicExpression.TYPE.UNARY_OP_NOT, null); 
			} else SynErr(28);
		} else if (la.kind == 3) {
			Get();
			le = new LogicExpression(Context, type, null, null, _SignalByName(t.val),  -1); 
		} else SynErr(29);
		return le;
	}

	LogicExpression.TYPE  SignalOp() {
		LogicExpression.TYPE  type;
		type = LogicExpression.TYPE.UNARY_OP_SIGNAL; 
		if (la.kind == 10) {
			Get();
			type = LogicExpression.TYPE.UNARY_OP_SIGNAL; 
		} else if (la.kind == 15) {
			Get();
			type = LogicExpression.TYPE.UNARY_OP_IS_CONNECTED; 
		} else SynErr(30);
		return type;
	}

	LogicExpression  AndExp() {
		LogicExpression  exp;
		exp = null;
		LogicExpression exp2 = null;
		LogicExpression.TYPE op = LogicExpression.TYPE.LITERAL_INT; 
		exp = NotExp();
		while (la.kind == 19) {
			op = AndOp();
			exp2 = NotExp();
			exp = ConcatExpression(exp, op, exp2); 
		}
		return exp;
	}

	LogicExpression.TYPE  OrOp() {
		LogicExpression.TYPE  op;
		op = LogicExpression.TYPE.BINARY_OP_OR; 
		if (la.kind == 20) {
			Get();
			op = LogicExpression.TYPE.BINARY_OP_OR; 
		} else if (la.kind == 21) {
			Get();
			op = LogicExpression.TYPE.BINARY_OP_XOR; 
		} else SynErr(31);
		return op;
	}

	LogicExpression  NotExp() {
		LogicExpression  exp;
		exp = null;
		LogicExpression exp2 = null; 
		if (StartOf(2)) {
			exp = Value();
		} else if (la.kind == 16) {
			Get();
			exp2 = Value();
			exp = ConcatExpression(exp2, LogicExpression.TYPE.UNARY_OP_NOT, null); 
		} else SynErr(32);
		return exp;
	}

	LogicExpression.TYPE  AndOp() {
		LogicExpression.TYPE  op;
		op = LogicExpression.TYPE.BINARY_OP_AND; 
		Expect(19);
		op = LogicExpression.TYPE.BINARY_OP_AND; 
		return op;
	}

	LogicExpression  Value() {
		LogicExpression  exp;
		exp = null; 
		if (la.kind == 12 || la.kind == 13) {
			exp = TrueOrFalse();
		} else if (la.kind == 2) {
			exp = Number();
		} else if (StartOf(3)) {
			exp = SignalOrIsConnected();
		} else if (la.kind == 17) {
			Get();
			exp = Expr();
			Expect(18);
		} else SynErr(33);
		return exp;
	}



	public void Parse() {
		la = new Token();
		la.val = "";		
		Get();
		LogicProgram();
		Expect(0);

	}

	private static final boolean[][] set = {
		{T,x,x,x, x,x,x,x, x,x,x,x, x,x,x,x, x,x,x,x, x,x,x,x},
		{x,x,x,T, T,x,T,x, x,x,T,x, x,x,x,x, x,x,x,x, x,x,x,x},
		{x,x,T,T, x,x,x,x, x,x,T,x, T,T,T,T, x,T,x,x, x,x,x,x},
		{x,x,x,T, x,x,x,x, x,x,T,x, x,x,T,T, x,x,x,x, x,x,x,x}

	};
} // end Parser


class Errors {
	public int count = 0;                                    // number of errors detected
	public java.io.PrintStream errorStream = System.out;     // error messages go to this stream
	public String errMsgFormat = "-- line {0} col {1}: {2}"; // 0=line, 1=column, 2=text
	
	protected void printMsg(int line, int column, String msg) {
		StringBuffer b = new StringBuffer(errMsgFormat);
		int pos = b.indexOf("{0}");
		if (pos >= 0) { b.delete(pos, pos+3); b.insert(pos, line); }
		pos = b.indexOf("{1}");
		if (pos >= 0) { b.delete(pos, pos+3); b.insert(pos, column); }
		pos = b.indexOf("{2}");
		if (pos >= 0) b.replace(pos, pos+3, msg);
		errorStream.println(b.toString());
	}
	
	public void SynErr (int line, int col, int n) {
		String s;
		switch (n) {
			case 0: s = "EOF expected"; break;
			case 1: s = "string expected"; break;
			case 2: s = "intliteral expected"; break;
			case 3: s = "ident expected"; break;
			case 4: s = "\"Var\" expected"; break;
			case 5: s = "\";\" expected"; break;
			case 6: s = "\"If\" expected"; break;
			case 7: s = "\"Then\" expected"; break;
			case 8: s = "\"Else\" expected"; break;
			case 9: s = "\"End\" expected"; break;
			case 10: s = "\"Signal\" expected"; break;
			case 11: s = "\":=\" expected"; break;
			case 12: s = "\"True\" expected"; break;
			case 13: s = "\"False\" expected"; break;
			case 14: s = "\"NotConnected\" expected"; break;
			case 15: s = "\"IsConnected\" expected"; break;
			case 16: s = "\"Not\" expected"; break;
			case 17: s = "\"(\" expected"; break;
			case 18: s = "\")\" expected"; break;
			case 19: s = "\"And\" expected"; break;
			case 20: s = "\"Or\" expected"; break;
			case 21: s = "\"Xor\" expected"; break;
			case 22: s = "??? expected"; break;
			case 23: s = "invalid LogicStatement1"; break;
			case 24: s = "invalid AssignmentStatement"; break;
			case 25: s = "invalid AssignmentStatement"; break;
			case 26: s = "invalid TrueOrFalse"; break;
			case 27: s = "invalid SignalOrIsConnected"; break;
			case 28: s = "invalid SignalOrIsConnected"; break;
			case 29: s = "invalid SignalOrIsConnected"; break;
			case 30: s = "invalid SignalOp"; break;
			case 31: s = "invalid OrOp"; break;
			case 32: s = "invalid NotExp"; break;
			case 33: s = "invalid Value"; break;
			default: s = "error " + n; break;
		}
		printMsg(line, col, s);
		count++;
	}

	public void SemErr (int line, int col, String s) {	
		printMsg(line, col, s);
		count++;
	}
	
	public void SemErr (String s) {
		errorStream.println(s);
		count++;
	}
	
	public void Warning (int line, int col, String s) {	
		printMsg(line, col, s);
	}
	
	public void Warning (String s) {
		errorStream.println(s);
	}
} // Errors


class FatalError extends RuntimeException {
	public static final long serialVersionUID = 1L;
	public FatalError(String s) { super(s); }
}
