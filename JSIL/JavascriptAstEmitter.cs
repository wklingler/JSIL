﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using ICSharpCode.Decompiler.Ast;
using ICSharpCode.Decompiler.ILAst;
using JSIL.Ast;
using JSIL.Internal;
using Mono.Cecil;

namespace JSIL {
    public class JavascriptAstEmitter : JSAstVisitor {
        public readonly JavascriptFormatter Output;

        public readonly TypeSystem TypeSystem;
        public readonly JSILIdentifier JSIL;

        protected readonly Stack<bool> IncludeTypeParens = new Stack<bool>();

        public JavascriptAstEmitter (JavascriptFormatter output, JSILIdentifier jsil, TypeSystem typeSystem) {
            Output = output;
            JSIL = jsil;
            TypeSystem = typeSystem;
            IncludeTypeParens.Push(false);
        }

        protected void CommaSeparatedList (IEnumerable<JSExpression> values, bool withNewlines = false) {
            bool isFirst = true;
            foreach (var value in values) {
                if (!isFirst) {
                    Output.Comma();

                    if (withNewlines)
                        Output.NewLine();
                }

                Visit(value);
                isFirst = false;
            }
        }

        public override void VisitNode (JSNode node) {
            if ((node != null) && !node.IsNull) {
                Console.Error.WriteLine("Cannot emit {0}", node.GetType().Name);
                Output.Identifier("JSIL.UntranslatableNode", true);
                Output.LPar();
                Output.Value(node.GetType().Name);
                Output.RPar();
                Output.Semicolon();
            }

            base.VisitNode(node);
        }

        public void VisitNode (JSBlockStatement block, bool includeBraces = false) {
            if (includeBraces)
                Output.OpenBrace();

            foreach (var stmt in block.Statements)
                Visit(stmt);

            if (includeBraces)
                Output.CloseBrace();
        }

        public void VisitNode (JSVariableDeclarationStatement vars) {
            if (vars.Declarations.Count == 0)
                return;

            Output.Keyword("var");
            Output.Space();

            CommaSeparatedList(vars.Declarations);

            Output.Semicolon();
        }

        public void VisitNode (JSExpressionStatement statement) {
            Visit(statement.Expression);

            if (!statement.IsNull && !statement.Expression.IsNull)
                Output.Semicolon();
        }

        public void VisitNode (JSDotExpression dot) {
            Visit(dot.Target);
            Output.Dot();
            Visit(dot.Member);
        }

        public void VisitNode (JSChangeTypeExpression cte) {
            Visit(cte.Expression);
        }

        public void VisitNode (JSIndexerExpression idx) {
            Visit(idx.Target);
            Output.OpenBracket();
            Visit(idx.Index);
            Output.CloseBracket();
        }

        public void VisitNode (JSIdentifier identifier) {
            Output.Identifier(identifier.Identifier);
        }

        public void VisitNode (JSStringLiteral str) {
            Output.Value(str.Value);
        }

        public void VisitNode (JSVerbatimLiteral verbatim) {
            bool isFirst = true;

            foreach (var line in verbatim.Value.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries)) {
                if (String.IsNullOrWhiteSpace(line))
                    continue;

                if (!isFirst)
                    Output.PlainTextOutput.WriteLine();

                Output.PlainTextOutput.Write(line.Trim());
                isFirst = false;
            }
        }

        public void VisitNode (JSTypeNameLiteral type) {
            Output.Value(type.Value);
        }

        public void VisitNode (JSIntegerLiteral integer) {
            Output.Value(integer.Value);
        }

        public void VisitNode (JSNumberLiteral number) {
            Output.Value(number.Value);
        }

        public void VisitNode (JSBooleanLiteral b) {
            Output.Value(b.Value);
        }

        public void VisitNode (JSEnumLiteral enm) {
            bool isFirst = true;

            foreach (var name in enm.Names) {
                if (!isFirst)
                    Output.Token(" | ");

                Output.Identifier(enm.EnumType);
                Output.Dot();
                Output.Identifier(name);

                isFirst = false;
            }
        }

        public void VisitNode (JSNullLiteral nil) {
            Output.Keyword("null");
        }

        public void VisitNode (JSDefaultValueLiteral defaultValue) {
            if (TypeAnalysis.IsIntegerOrEnum(defaultValue.Value)) {
                Output.Value(0);
            } else if (!defaultValue.Value.IsValueType) {
                Output.Keyword("null");
            } else {
                switch (defaultValue.Value.FullName) {
                    case "System.Nullable`1":
                        Output.Keyword("null");
                        break;
                    case "System.Single":
                    case "System.Double":
                    case "System.Decimal":
                        Output.Value(0.0);
                        break;
                    case "System.Boolean":
                        Output.Keyword("false");
                        break;
                    default:
                        VisitNode(new JSNewExpression(new JSType(defaultValue.Value)));
                        break;
                }
            }
        }

        public void VisitNode (JSType type) {
            Output.Identifier(type.Type, IncludeTypeParens.Peek());
        }

        public void VisitNode (JSVariable variable) {
            Output.Identifier(variable.Identifier);

            if (variable.IsReference) {
                Output.Dot();
                Output.Identifier("value");
            }
        }

        public void VisitNode (JSPassByReferenceExpression byref) {
            JSExpression referent;

            if (JSReferenceExpression.TryMaterialize(JSIL, byref.Referent, out referent)) {
                Output.Comment("ref");
                Visit(referent);
            } else {
                Output.Identifier("JSIL.UnmaterializedReference", true);
                Output.LPar();
                Output.RPar();
            }
        }

        public void VisitNode (JSReferenceExpression reference) {
            Visit(reference.Referent);
        }

        public void VisitNode (JSFunctionExpression function) {
            Output.OpenFunction(
                null,
                (o) => {
                    if (o != Output)
                        throw new InvalidOperationException();

                    bool isFirst = true;
                    foreach (var p in function.Parameters) {
                        if (!isFirst)
                            o.Comma();

                        if (p.IsReference)
                            o.Comment("ref");

                        o.Identifier(p.Identifier);

                        isFirst = false;
                    }
                }
            );

            Visit(function.Body);

            Output.CloseBrace(false);
        }

        public void VisitNode (JSSwitchStatement swtch) {
            WriteLabel(swtch);

            Output.Keyword("switch");
            Output.Space();

            Output.LPar();
            Visit(swtch.Condition);
            Output.RPar();
            Output.Space();

            Output.OpenBrace();

            foreach (var c in swtch.Cases) {
                if (c.Values != null) {
                    foreach (var value in c.Values) {
                        Output.Token("case ");
                        Visit(value);
                        Output.Token(": ");
                        Output.NewLine();
                    }
                } else {
                    Output.Token("default: ");
                    Output.NewLine();
                }

                Output.PlainTextFormatter.Indent();
                Visit(c.Body);
                Output.PlainTextFormatter.Unindent();
            }

            Output.CloseBrace();
        }

        protected void WriteLabel (JSStatement stmt) {
            if (String.IsNullOrWhiteSpace(stmt.Label))
                return;

            Output.Identifier(stmt.Label);
            Output.Token(": ");
            Output.NewLine();
        }

        public void VisitNode (JSLabelStatement label) {
            WriteLabel(label);
        }

        public void VisitNode (JSIfStatement ifs) {
            Output.NewLine();
            Output.Keyword("if");
            Output.Space();

            Output.LPar();
            Visit(ifs.Condition);
            Output.RPar();
            Output.Space();

            Output.OpenBrace();
            Visit(ifs.TrueClause);

            JSStatement falseClause = ifs.FalseClause;
            while (falseClause != null) {
                var nestedBlock = falseClause as JSBlockStatement;
                var nestedIf = falseClause as JSIfStatement;
                if ((nestedBlock != null) && (nestedBlock.Statements.Count == 1))
                    nestedIf = nestedBlock.Statements[0] as JSIfStatement;

                if (nestedIf != null) {
                    Output.CloseAndReopenBrace((o) => {
                        if (o != this.Output)
                            throw new InvalidOperationException();

                        o.Keyword("else if");
                        o.Space();
                        o.LPar();
                        Visit(nestedIf.Condition);
                        o.RPar();
                    });

                    Visit(nestedIf.TrueClause);

                    falseClause = nestedIf.FalseClause;
                } else {
                    Output.CloseAndReopenBrace("else");
                    Visit(falseClause);
                    falseClause = null;
                }
            }

            Output.CloseBrace();
        }

        public void VisitNode (JSTryCatchBlock tcb) {
            Output.Keyword("try");
            Output.Space();
            Output.OpenBrace();

            Visit(tcb.Body);

            if (tcb.Catch != null) {
                Output.CloseAndReopenBrace((o) => {
                    if (o != Output)
                        throw new InvalidOperationException();

                    o.Keyword("catch");
                    o.Space();
                    o.LPar();
                    Visit(tcb.CatchVariable);
                    o.RPar();
                });

                Visit(tcb.Catch);
            }

            if (tcb.Finally != null) {
                Output.CloseAndReopenBrace("finally");

                Visit(tcb.Finally);
            }

            Output.CloseBrace();
        }

        public void VisitNode (JSWhileLoop loop) {
            WriteLabel(loop);

            Output.NewLine();
            Output.Keyword("while");
            Output.Space();

            Output.LPar();
            Visit(loop.Condition);
            Output.RPar();
            Output.Space();

            VisitNode((JSBlockStatement)loop, true);
        }

        public void VisitNode (JSReturnExpression ret) {
            Output.Keyword("return");
            Output.Space();

            if (ret.Value != null)
                Visit(ret.Value);
        }

        public void VisitNode (JSThrowExpression ret) {
            Output.Keyword("throw");
            Output.Space();
            Visit(ret.Exception);
        }

        public void VisitNode (JSBreakExpression brk) {
            Output.Keyword("break");
        }

        public void VisitNode (JSContinueExpression cont) {
            Output.Keyword("continue");
        }

        public void VisitNode (JSUnaryOperatorExpression bop) {
            if (!bop.IsPostfix)
                Output.Token(bop.Operator.Token);

            Visit(bop.Expression);

            if (bop.IsPostfix)
                Output.Token(bop.Operator.Token);
        }

        public void VisitNode (JSBinaryOperatorExpression bop) {
            bool parens = !(bop.Operator is JSAssignmentOperator);
            bool needsTruncation = false;

            // We need to perform manual truncation to maintain the semantics of C#'s division operator
            if ((bop.Operator == JSOperator.Divide)) {
                needsTruncation =                     
                    (ILBlockTranslator.IsIntegral(bop.Left.GetExpectedType(TypeSystem)) &&
                    ILBlockTranslator.IsIntegral(bop.Right.GetExpectedType(TypeSystem))) ||
                    ILBlockTranslator.IsIntegral(bop.GetExpectedType(TypeSystem));

                parens |= needsTruncation;
            }

            if (needsTruncation) {
                if (bop.Operator is JSAssignmentOperator)
                    throw new NotImplementedException();

                Output.Identifier("Math.floor", true);
            }

            if (parens)
                Output.LPar();

            Visit(bop.Left);
            Output.Space();
            Output.Token(bop.Operator.Token);
            Output.Space();
            Visit(bop.Right);

            if (parens)
                Output.RPar();
        }

        public void VisitNode (JSTernaryOperatorExpression ternary) {
            Visit(ternary.Condition);

            Output.Token(" ? ");

            Visit(ternary.True);

            Output.Token(" : ");

            Visit(ternary.False);
        }

        public void VisitNode (JSNewExpression newexp) {
            Output.Keyword("new");
            Output.Space();

            IncludeTypeParens.Push(true);
            try {
                Visit(newexp.Type);
            } finally {
                IncludeTypeParens.Pop();
            }

            Output.LPar();
            CommaSeparatedList(newexp.Arguments);
            Output.RPar();
        }

        public void VisitNode (JSPairExpression pair) {
            Visit(pair.Key);
            Output.Token(": ");
            Visit(pair.Value);
        }

        public void VisitNode (JSArrayExpression array) {
            Output.OpenBracket();
            CommaSeparatedList(array.Values);
            Output.CloseBracket();
        }

        public void VisitNode (JSObjectExpression obj) {
            Output.OpenBrace();
            CommaSeparatedList(obj.Values, true);
            Output.CloseBrace();
        }

        protected int CountOfMatchingSubtrees<TNode> (IEnumerable<JSNode> nodes) 
            where TNode : JSNode {
            return (from n in nodes
                    where n.AllChildrenRecursive.OfType<TNode>().FirstOrDefault() != null
                    select n).Count();
        }

        public void VisitNode (JSInvocationExpression invocation) {
            bool needsParens =
                CountOfMatchingSubtrees<JSFunctionExpression>(new[] { invocation.Target }) > 0;

            if (needsParens)
                Output.LPar();

            Visit(invocation.Target);

            if (needsParens)
                Output.RPar();

            Output.LPar();

            bool needLineBreak = 
                ((invocation.Arguments.Count > 1) && 
                (
                    (CountOfMatchingSubtrees<JSFunctionExpression>(invocation.Arguments) > 0) ||
                    (CountOfMatchingSubtrees<JSInvocationExpression>(invocation.Arguments) > 0)
                )) ||
                (invocation.Arguments.Count > 4);

            if (needLineBreak)
                Output.NewLine();

            CommaSeparatedList(invocation.Arguments, needLineBreak);

            if (needLineBreak)
                Output.NewLine();

            Output.RPar();
        }
    }
}
