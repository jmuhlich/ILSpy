// Copyright (c) 2011 AlphaSierraPapa for the SharpDevelop Team
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy of this
// software and associated documentation files (the "Software"), to deal in the Software
// without restriction, including without limitation the rights to use, copy, modify, merge,
// publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons
// to whom the Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all copies or
// substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
// INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR
// PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE
// FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
// OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.Linq;
using ICSharpCode.NRefactory.Utils;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using System.Diagnostics;
using dnSpy.Contracts.Decompiler;

namespace ICSharpCode.Decompiler.ILAst {
	public enum ILAstOptimizationStep
	{
		RemoveVisualBasicCompilerCode,
		RemoveRedundantCode,
		ReduceBranchInstructionSet,
		InlineVariables,
		CopyPropagation,
		ConvertFieldAccessesToPropertyMethodCalls,
		YieldReturn,
		AsyncAwait,
		RemoveKspObfuscations,
		PropertyAccessInstructions,
		SplitToMovableBlocks,
		TypeInference,
		HandlePointerArithmetic,
		SimplifyShortCircuit,
		SimplifyTernaryOperator,
		SimplifyNullCoalescing,
		JoinBasicBlocks,
		SimplifyLogicNot,
		SimplifyShiftOperators,
		TypeConversionSimplifications,
		SimplifyLdObjAndStObj,
		SimplifyCustomShortCircuit,
		SimplifyLiftedOperators,
		TransformArrayInitializers,
		TransformMultidimensionalArrayInitializers,
		TransformObjectInitializers,
		MakeAssignmentExpression,
		IntroducePostIncrement,
		InlineExpressionTreeParameterDeclarations,
		InlineVariables2,
		FindLoops,
		FindConditions,
		FlattenNestedMovableBlocks,
		RemoveEndFinally,
		RemoveRedundantCode2,
		GotoRemoval,
		FixRoslynStaticDelegates,
		FixFilters,
		CreateLoopLocal,
		DuplicateReturns,
		GotoRemoval2,
		ReduceIfNesting,
		InlineVariables3,
		CachedDelegateInitialization,
		IntroduceFixedStatements,
		RecombineVariables,
		TypeInference2,
		RemoveRedundantCode3,
		IntroduceConstants,
		None
	}
	
	public partial class ILAstOptimizer
	{
		int nextLabelIndex = 0;
		
		DecompilerContext context;
		ICorLibTypes corLib;
		ILBlock method;

		// PERF: Cache used lists used in Optimize() instead of creating new ones all the time
		readonly List<ILTryCatchBlock.CatchBlockBase> Optimize_List_CatchBlockBase;
		readonly List<ILTryCatchBlock.CatchBlock> Optimize_List_CatchBlocks;
		readonly List<ILWhileLoop> Optimize_List_ILWhileLoop;
		readonly List<ILBlock> Optimize_List_ILBlock;
		readonly List<ILNode> Optimize_List_ILNode;
		readonly List<ILExpression> Optimize_List_ILExpression;
		readonly List<ILExpression> Optimize_List_ILExpression2;
		readonly Dictionary<ILLabel, int> Optimize_Dict_ILLabel_Int32;
		readonly Dictionary<Local, ILVariable> Optimize_Dict_Local_ILVariable;
		readonly Dictionary<ILLabel, ILNode> Optimize_Dict_ILLabel_ILNode;
		readonly List<KeyValuePair<ILExpression, ILExpression>> Optimize_List_ILExpressionx2;
		bool hasFilters;
		public string CompilerName;

		public ILAstOptimizer()
		{
			this.del_getILInlining = GetILInlining;
			this.Optimize_List_CatchBlockBase = new List<ILTryCatchBlock.CatchBlockBase>();
			this.Optimize_List_CatchBlocks = new List<ILTryCatchBlock.CatchBlock>();
			this.Optimize_List_ILWhileLoop = new List<ILWhileLoop>();
			this.Optimize_List_ILBlock = new List<ILBlock>();
			this.Optimize_List_ILNode = new List<ILNode>();
			this.Optimize_List_ILExpression = new List<ILExpression>();
			this.Optimize_List_ILExpression2 = new List<ILExpression>();
			this.Optimize_Dict_ILLabel_Int32 = new Dictionary<ILLabel, int>();
			this.Optimize_Dict_Local_ILVariable = new Dictionary<Local, ILVariable>();
			this.Optimize_Dict_ILLabel_ILNode = new Dictionary<ILLabel, ILNode>();
			this.Optimize_List_ILExpressionx2 = new List<KeyValuePair<ILExpression, ILExpression>>();
		}

		public void Reset()
		{
			this.context = null;
			this.corLib = null;
			this.method = null;
			this.nextLabelIndex = 0;
			this.Optimize_List_CatchBlockBase.Clear();
			this.Optimize_List_CatchBlocks.Clear();
			this.Optimize_List_ILWhileLoop.Clear();
			this.Optimize_List_ILBlock.Clear();
			this.Optimize_List_ILNode.Clear();
			this.Optimize_List_ILExpression.Clear();
			this.Optimize_List_ILExpression2.Clear();
			this.Optimize_Dict_ILLabel_Int32.Clear();
			this.Optimize_Dict_Local_ILVariable.Clear();
			this.Optimize_Dict_ILLabel_ILNode.Clear();
			this.Optimize_List_ILExpressionx2.Clear();
			hasFilters = false;
			readOnlyPropTempLocalNameCounter = 0;
			tmpLocalCounter = 0;
			CompilerName = null;
		}

		TypeAnalysis GetTypeAnalysis() {
			if (cached_TypeAnalysis == null)
				cached_TypeAnalysis = new TypeAnalysis();
			return cached_TypeAnalysis;
		}
		TypeAnalysis cached_TypeAnalysis;

		SimpleControlFlow GetSimpleControlFlow(DecompilerContext context, ILBlock method)
		{
			if (cached_SimpleControlFlow == null)
				cached_SimpleControlFlow = new SimpleControlFlow(context, method);
			else
				cached_SimpleControlFlow.Initialize(context, method);
			return cached_SimpleControlFlow;
		}
		SimpleControlFlow cached_SimpleControlFlow;

		ILInlining GetILInlining(ILBlock method)
		{
			if (cached_ILInlining == null)
				cached_ILInlining = new ILInlining(context);
			cached_ILInlining.Initialize(method);
			return cached_ILInlining;
		}
		ILInlining GetILInlining(List<ILNode> body, int start, int count) {
			if (cached_ILInlining == null)
				cached_ILInlining = new ILInlining(context);
			cached_ILInlining.Initialize(body, start, count);
			return cached_ILInlining;
		}
		ILInlining cached_ILInlining;

		PatternMatcher GetPatternMatcher(ICorLibTypes corLib)
		{
			if (cached_PatternMatcher == null)
				cached_PatternMatcher = new PatternMatcher(context, corLib);
			else
				cached_PatternMatcher.Initialize(corLib);
			return cached_PatternMatcher;
		}
		PatternMatcher cached_PatternMatcher;

		LoopsAndConditions GetLoopsAndConditions(DecompilerContext context)
		{
			if (cached_LoopsAndConditions == null)
				cached_LoopsAndConditions = new LoopsAndConditions(context);
			else
				cached_LoopsAndConditions.Initialize(context);
			return cached_LoopsAndConditions;
		}
		LoopsAndConditions cached_LoopsAndConditions;

		public void Optimize(DecompilerContext context, ILBlock method, out StateMachineKind stateMachineKind, out MethodDef inlinedMethod, out AsyncMethodDebugInfo asyncInfo, ILAstOptimizationStep abortBeforeStep = ILAstOptimizationStep.None) =>
			Optimize(context, method, null, out stateMachineKind, out inlinedMethod, out asyncInfo, abortBeforeStep);

		readonly Func<ILBlock, ILInlining> del_getILInlining;
		internal void Optimize(DecompilerContext context, ILBlock method, AutoPropertyProvider autoPropertyProvider, out StateMachineKind stateMachineKind, out MethodDef inlinedMethod, out AsyncMethodDebugInfo asyncInfo, ILAstOptimizationStep abortBeforeStep = ILAstOptimizationStep.None)
		{
			this.context = context;
			this.corLib = context.CurrentMethod.Module.CorLibTypes;
			this.method = method;
			stateMachineKind = StateMachineKind.None;
			inlinedMethod = null;
			asyncInfo = null;

			try {
				if (abortBeforeStep == ILAstOptimizationStep.RemoveVisualBasicCompilerCode) return;
				if (IsVisualBasicModule())
					RemoveVisualBasicCompilerCode(method);

				if (abortBeforeStep == ILAstOptimizationStep.RemoveRedundantCode) return;
				RemoveRedundantCode(context, method, Optimize_List_ILExpression, Optimize_List_ILBlock, Optimize_Dict_ILLabel_Int32);

				if (abortBeforeStep == ILAstOptimizationStep.ReduceBranchInstructionSet) return;
				foreach (ILBlock block in method.GetSelfAndChildrenRecursive<ILBlock>(Optimize_List_ILBlock)) {
					ReduceBranchInstructionSet(block);
				}
				// ReduceBranchInstructionSet runs before inlining because the non-aggressive inlining heuristic
				// looks at which type of instruction consumes the inlined variable.

				if (abortBeforeStep == ILAstOptimizationStep.InlineVariables) return;
				// Works better after simple goto removal because of the following debug pattern: stloc X; br Next; Next:; ldloc X
				ILInlining inlining1 = GetILInlining(method);
				inlining1.InlineAllVariables();

				if (abortBeforeStep == ILAstOptimizationStep.ConvertFieldAccessesToPropertyMethodCalls) return;
				if (context.CurrentMethod.IsConstructor)
					ConvertFieldAccessesToPropertyMethodCalls(method, autoPropertyProvider);

				if (abortBeforeStep == ILAstOptimizationStep.CopyPropagation) return;
				inlining1.CopyPropagation(Optimize_List_ILNode);

				if (abortBeforeStep == ILAstOptimizationStep.YieldReturn) return;
				YieldReturnDecompiler.Run(context, method, autoPropertyProvider, ref stateMachineKind, ref inlinedMethod, ref CompilerName, Optimize_List_ILNode, del_getILInlining, Optimize_List_ILExpression, Optimize_List_ILBlock, Optimize_Dict_ILLabel_Int32);
				var yrd = AsyncDecompiler.RunStep1(context, method, autoPropertyProvider, ref stateMachineKind, ref inlinedMethod, ref CompilerName, Optimize_List_ILExpression, Optimize_List_ILBlock, Optimize_Dict_ILLabel_Int32);

				if (abortBeforeStep == ILAstOptimizationStep.AsyncAwait) return;
				yrd?.RunStep2(context, method, out asyncInfo, Optimize_List_ILExpression, Optimize_List_ILBlock, Optimize_Dict_ILLabel_Int32, Optimize_List_ILNode, del_getILInlining);

				if (abortBeforeStep == ILAstOptimizationStep.RemoveKspObfuscations) return;
				RemoveKspObfuscations(method);

				if (abortBeforeStep == ILAstOptimizationStep.PropertyAccessInstructions) return;
				IntroducePropertyAccessInstructions(method);

				if (abortBeforeStep == ILAstOptimizationStep.SplitToMovableBlocks) return;
				foreach (ILBlock block in method.GetSelfAndChildrenRecursive<ILBlock>(Optimize_List_ILBlock)) {
					SplitToBasicBlocks(block);
				}

				if (abortBeforeStep == ILAstOptimizationStep.TypeInference) return;
				// Types are needed for the ternary operator optimization
				GetTypeAnalysis().Run(context, method);

				if (abortBeforeStep == ILAstOptimizationStep.HandlePointerArithmetic) return;
				HandlePointerArithmetic(method);

				foreach (ILBlock block in method.GetSelfAndChildrenRecursive<ILBlock>(Optimize_List_ILBlock)) {
					bool modified;
					do {
						modified = false;

						if (abortBeforeStep == ILAstOptimizationStep.SimplifyShortCircuit) return;
						modified |= block.RunOptimization(GetSimpleControlFlow(context, method).SimplifyShortCircuit);

						if (abortBeforeStep == ILAstOptimizationStep.SimplifyTernaryOperator) return;
						modified |= block.RunOptimization(GetSimpleControlFlow(context, method).SimplifyTernaryOperator);

						if (abortBeforeStep == ILAstOptimizationStep.SimplifyNullCoalescing) return;
						modified |= block.RunOptimization(GetSimpleControlFlow(context, method).SimplifyNullCoalescing);

						if (abortBeforeStep == ILAstOptimizationStep.JoinBasicBlocks) return;
						modified |= block.RunOptimization(GetSimpleControlFlow(context, method).JoinBasicBlocks);

						if (abortBeforeStep == ILAstOptimizationStep.SimplifyLogicNot) return;
						modified |= block.RunOptimization(SimplifyLogicNot);

						if (abortBeforeStep == ILAstOptimizationStep.SimplifyShiftOperators) return;
						modified |= block.RunOptimization(SimplifyShiftOperators);

						if (abortBeforeStep == ILAstOptimizationStep.TypeConversionSimplifications) return;
						modified |= block.RunOptimization(TypeConversionSimplifications);

						if (abortBeforeStep == ILAstOptimizationStep.SimplifyLdObjAndStObj) return;
						modified |= block.RunOptimization(SimplifyLdObjAndStObj);

						if (abortBeforeStep == ILAstOptimizationStep.SimplifyCustomShortCircuit) return;
						modified |= block.RunOptimization(GetSimpleControlFlow(context, method).SimplifyCustomShortCircuit);

						if (abortBeforeStep == ILAstOptimizationStep.SimplifyLiftedOperators) return;
						modified |= block.RunOptimization(SimplifyLiftedOperators);

						if (abortBeforeStep == ILAstOptimizationStep.TransformArrayInitializers) return;
						modified |= block.RunOptimization(TransformArrayInitializers);

						if (abortBeforeStep == ILAstOptimizationStep.TransformMultidimensionalArrayInitializers) return;
						modified |= block.RunOptimization(TransformMultidimensionalArrayInitializers);

						if (abortBeforeStep == ILAstOptimizationStep.TransformObjectInitializers) return;
						modified |= block.RunOptimization(TransformObjectInitializers);

						if (abortBeforeStep == ILAstOptimizationStep.MakeAssignmentExpression) return;
						if (context.Settings.MakeAssignmentExpressions) {
							modified |= block.RunOptimization(MakeAssignmentExpression);
						}
						modified |= block.RunOptimization(MakeCompoundAssignments);

						if (abortBeforeStep == ILAstOptimizationStep.IntroducePostIncrement) return;
						if (context.Settings.IntroduceIncrementAndDecrement) {
							modified |= block.RunOptimization(IntroducePostIncrement);
						}

						if (abortBeforeStep == ILAstOptimizationStep.InlineExpressionTreeParameterDeclarations) return;
						if (context.Settings.ExpressionTrees) {
							modified |= block.RunOptimization(InlineExpressionTreeParameterDeclarations);
						}

						if (abortBeforeStep == ILAstOptimizationStep.InlineVariables2) return;
						modified |= GetILInlining(method).InlineAllInBlock(block);
						GetILInlining(method).CopyPropagation(Optimize_List_ILNode);

					} while (modified);
				}

				if (abortBeforeStep == ILAstOptimizationStep.FindLoops) return;
				foreach (ILBlock block in method.GetSelfAndChildrenRecursive<ILBlock>(Optimize_List_ILBlock)) {
					GetLoopsAndConditions(context).FindLoops(block);
				}

				if (abortBeforeStep == ILAstOptimizationStep.FindConditions) return;
				foreach (ILBlock block in method.GetSelfAndChildrenRecursive<ILBlock>(Optimize_List_ILBlock)) {
					if (block is ILTryCatchBlock.FilterILBlock)
						hasFilters = true;
					GetLoopsAndConditions(context).FindConditions(block);
				}

				if (abortBeforeStep == ILAstOptimizationStep.FlattenNestedMovableBlocks) return;
				FlattenBasicBlocks(method);

				if (abortBeforeStep == ILAstOptimizationStep.RemoveEndFinally) return;
				RemoveEndFinally(method);

				if (abortBeforeStep == ILAstOptimizationStep.RemoveRedundantCode2) return;
				RemoveRedundantCode(context, method, Optimize_List_ILExpression, Optimize_List_ILBlock, Optimize_Dict_ILLabel_Int32);

				if (abortBeforeStep == ILAstOptimizationStep.GotoRemoval) return;
				GotoRemoval.RemoveGotos(context, method);

				if (abortBeforeStep == ILAstOptimizationStep.FixRoslynStaticDelegates) return;
				FixRoslynStaticDelegates(method);

				if (abortBeforeStep == ILAstOptimizationStep.FixFilters) return;
				FixFilterBlocks(method);

				if (abortBeforeStep == ILAstOptimizationStep.CreateLoopLocal) return;
				CreateLoopLocal(method);

				if (abortBeforeStep == ILAstOptimizationStep.DuplicateReturns) return;
				DuplicateReturnStatements(method);

				if (abortBeforeStep == ILAstOptimizationStep.GotoRemoval2) return;
				GotoRemoval.RemoveGotos(context, method);

				if (abortBeforeStep == ILAstOptimizationStep.ReduceIfNesting) return;
				ReduceIfNesting(method);

				if (abortBeforeStep == ILAstOptimizationStep.InlineVariables3) return;
				// The 2nd inlining pass is necessary because DuplicateReturns and the introduction of ternary operators
				// open up additional inlining possibilities.
				GetILInlining(method).InlineAllVariables();

				if (abortBeforeStep == ILAstOptimizationStep.CachedDelegateInitialization) return;
				if (context.Settings.AnonymousMethods) {
					foreach (ILBlock block in method.GetSelfAndChildrenRecursive<ILBlock>(Optimize_List_ILBlock)) {
						for (int i = 0; i < block.Body.Count; i++) {
							// TODO: Move before loops
							CachedDelegateInitializationWithField(block, ref i);
							CachedDelegateInitializationWithLocal(block, ref i);
						}
					}
				}

				if (abortBeforeStep == ILAstOptimizationStep.IntroduceFixedStatements) return;
				// we need post-order traversal, not pre-order, for "fixed" to work correctly
				foreach (ILBlock block in TreeTraversal.PostOrder<ILNode>(method, n => n.GetChildren()).OfType<ILBlock>()) {
					for (int i = block.Body.Count - 1; i >= 0; i--) {
						// TODO: Move before loops
						if (i < block.Body.Count)
							IntroduceFixedStatements(block, block.Body, i);
					}
				}

				if (abortBeforeStep == ILAstOptimizationStep.RecombineVariables) return;
				RecombineVariables(method);

				if (abortBeforeStep == ILAstOptimizationStep.TypeInference2) return;
				TypeAnalysis.Reset(method, this.Optimize_List_ILExpression);
				GetTypeAnalysis().Run(context, method);

				if (abortBeforeStep == ILAstOptimizationStep.RemoveRedundantCode3) return;
				GotoRemoval.RemoveRedundantCode(method, context);

				if (abortBeforeStep == ILAstOptimizationStep.IntroduceConstants) return;
				IntroduceConstants(method);

				// ReportUnassignedILSpans(method);
			}
			finally {
				this.Optimize_List_CatchBlockBase.Clear();
				this.Optimize_List_CatchBlocks.Clear();
				this.Optimize_List_ILWhileLoop.Clear();
				this.Optimize_List_ILBlock.Clear();
				this.Optimize_List_ILNode.Clear();
				this.Optimize_List_ILExpression.Clear();
				this.Optimize_List_ILExpression2.Clear();
				this.Optimize_Dict_ILLabel_Int32.Clear();
				this.Optimize_Dict_Local_ILVariable.Clear();
				this.Optimize_Dict_ILLabel_ILNode.Clear();
				this.Optimize_List_ILExpressionx2.Clear();
			}
		}

		void IntroduceConstants(ILBlock method) {
			foreach (var block in method.GetSelfAndChildrenRecursive(Optimize_List_ILBlock))
				IntroduceConstantsCore(block);
		}

		static readonly UTF8String nameSystem = "System";
		static bool IsMscorlibSystemClass(TypeDef type, string name) {
			if (type.Namespace != nameSystem)
				return false;
			if (!type.DefinitionAssembly.IsCorLib())
				return false;
			return type.Name == name;
		}

		void IntroduceConstantsCore(ILBlock block) {
			var list = Optimize_List_ILExpression;
			list.Clear();
			var body = block.Body;
			for (int i = 0; i < body.Count; i++) {
				var expr = body[i] as ILExpression;
				if (expr != null)
					list.Add(expr);
			}
			while (list.Count > 0) {
				var expr = list[list.Count - 1];
				list.RemoveAt(list.Count - 1);
				var args = expr.Arguments;
				for (int i = 0; i < args.Count; i++)
					list.Add(args[i]);

				var type = (expr.InferredType ?? expr.ExpectedType).RemovePinnedAndModifiers();
				if (type == null)
					continue;
				switch (type.ElementType) {
				case ElementType.Char:
					if (expr.Code == ILCode.Ldc_I4) {
						var c = (int)expr.Operand;
						if (c == char.MaxValue && !IsMscorlibSystemClass(context.CurrentMethod.DeclaringType, "Char")) {
							var module = context.CurrentModule;
							var mr = new MemberRefUser(module, "MaxValue", new FieldSig(module.CorLibTypes.Char), module.CorLibTypes.Char.TypeDefOrRef);
							expr.Code = ILCode.Ldsfld;
							expr.Operand = mr;
							expr.Arguments.Clear();
						}
					}
					break;

				case ElementType.I1:
					if (expr.Code == ILCode.Ldc_I4) {
						var c = (int)expr.Operand;
						if ((c == sbyte.MaxValue || c == sbyte.MinValue) && !IsMscorlibSystemClass(context.CurrentMethod.DeclaringType, "SByte")) {
							var module = context.CurrentModule;
							var mr = new MemberRefUser(module, c == sbyte.MaxValue ? "MaxValue" : "MinValue", new FieldSig(module.CorLibTypes.SByte), module.CorLibTypes.SByte.TypeDefOrRef);
							expr.Code = ILCode.Ldsfld;
							expr.Operand = mr;
							expr.Arguments.Clear();
						}
					}
					break;

				case ElementType.U1:
					if (expr.Code == ILCode.Ldc_I4) {
						var c = (int)expr.Operand;
						if (c == byte.MaxValue && !IsMscorlibSystemClass(context.CurrentMethod.DeclaringType, "Byte")) {
							var module = context.CurrentModule;
							var mr = new MemberRefUser(module, "MaxValue", new FieldSig(module.CorLibTypes.Byte), module.CorLibTypes.Byte.TypeDefOrRef);
							expr.Code = ILCode.Ldsfld;
							expr.Operand = mr;
							expr.Arguments.Clear();
						}
					}
					break;

				case ElementType.I2:
					if (expr.Code == ILCode.Ldc_I4) {
						var c = (int)expr.Operand;
						if ((c == short.MaxValue || c == short.MinValue) && !IsMscorlibSystemClass(context.CurrentMethod.DeclaringType, "Int16")) {
							var module = context.CurrentModule;
							var mr = new MemberRefUser(module, c == short.MaxValue ? "MaxValue" : "MinValue", new FieldSig(module.CorLibTypes.Int16), module.CorLibTypes.Int16.TypeDefOrRef);
							expr.Code = ILCode.Ldsfld;
							expr.Operand = mr;
							expr.Arguments.Clear();
						}
					}
					break;

				case ElementType.U2:
					if (expr.Code == ILCode.Ldc_I4) {
						var c = (int)expr.Operand;
						if (c == ushort.MaxValue && !IsMscorlibSystemClass(context.CurrentMethod.DeclaringType, "UInt16")) {
							var module = context.CurrentModule;
							var mr = new MemberRefUser(module, "MaxValue", new FieldSig(module.CorLibTypes.UInt16), module.CorLibTypes.UInt16.TypeDefOrRef);
							expr.Code = ILCode.Ldsfld;
							expr.Operand = mr;
							expr.Arguments.Clear();
						}
					}
					break;

				case ElementType.I4:
					if (expr.Code == ILCode.Ldc_I4) {
						var c = (int)expr.Operand;
						if ((c == int.MaxValue || c == int.MinValue) && !IsMscorlibSystemClass(context.CurrentMethod.DeclaringType, "Int32")) {
							var module = context.CurrentModule;
							var mr = new MemberRefUser(module, c == int.MaxValue ? "MaxValue" : "MinValue", new FieldSig(module.CorLibTypes.Int32), module.CorLibTypes.Int32.TypeDefOrRef);
							expr.Code = ILCode.Ldsfld;
							expr.Operand = mr;
							expr.Arguments.Clear();
						}
					}
					break;

				case ElementType.U4:
					if (expr.Code == ILCode.Ldc_I4) {
						var c = (int)expr.Operand;
						if (c == unchecked((int)uint.MaxValue) && !IsMscorlibSystemClass(context.CurrentMethod.DeclaringType, "UInt32")) {
							var module = context.CurrentModule;
							var mr = new MemberRefUser(module, "MaxValue", new FieldSig(module.CorLibTypes.UInt32), module.CorLibTypes.UInt32.TypeDefOrRef);
							expr.Code = ILCode.Ldsfld;
							expr.Operand = mr;
							expr.Arguments.Clear();
						}
					}
					break;

				case ElementType.I8:
					if (expr.Code == ILCode.Ldc_I8) {
						var c = (long)expr.Operand;
						if ((c == long.MaxValue || c == long.MinValue) && !IsMscorlibSystemClass(context.CurrentMethod.DeclaringType, "Int64")) {
							var module = context.CurrentModule;
							var mr = new MemberRefUser(module, c == long.MaxValue ? "MaxValue" : "MinValue", new FieldSig(module.CorLibTypes.Int64), module.CorLibTypes.Int64.TypeDefOrRef);
							expr.Code = ILCode.Ldsfld;
							expr.Operand = mr;
							expr.Arguments.Clear();
						}
					}
					break;

				case ElementType.U8:
					if (expr.Code == ILCode.Ldc_I8) {
						var c = (long)expr.Operand;
						if (c == unchecked((long)ulong.MaxValue) && !IsMscorlibSystemClass(context.CurrentMethod.DeclaringType, "UInt64")) {
							var module = context.CurrentModule;
							var mr = new MemberRefUser(module, "MaxValue", new FieldSig(module.CorLibTypes.UInt64), module.CorLibTypes.UInt64.TypeDefOrRef);
							expr.Code = ILCode.Ldsfld;
							expr.Operand = mr;
							expr.Arguments.Clear();
						}
					}
					break;

				case ElementType.R4:
					if (expr.Code == ILCode.Ldc_R4) {
						var c = (float)expr.Operand;
						string name;
						if (float.IsNaN(c))
							name = "NaN";
						else if (float.IsPositiveInfinity(c))
							name = "PositiveInfinity";
						else if (float.IsNegativeInfinity(c))
							name = "NegativeInfinity";
						else if (c == float.Epsilon)
							name = "Epsilon";
						else if (c == float.MinValue)
							name = "MinValue";
						else if (c == float.MaxValue)
							name = "MaxValue";
						else
							break;
						if (IsMscorlibSystemClass(context.CurrentMethod.DeclaringType, "Single"))
							break;
						var module = context.CurrentModule;
						var mr = new MemberRefUser(module, name, new FieldSig(module.CorLibTypes.Single), module.CorLibTypes.Single.TypeDefOrRef);
						expr.Code = ILCode.Ldsfld;
						expr.Operand = mr;
						expr.Arguments.Clear();
					}
					break;

				case ElementType.R8:
					if (expr.Code == ILCode.Ldc_R8) {
						var c = (double)expr.Operand;
						string name;
						if (double.IsNaN(c))
							name = "NaN";
						else if (double.IsPositiveInfinity(c))
							name = "PositiveInfinity";
						else if (double.IsNegativeInfinity(c))
							name = "NegativeInfinity";
						else if (c == double.Epsilon)
							name = "Epsilon";
						else if (c == double.MinValue)
							name = "MinValue";
						else if (c == double.MaxValue)
							name = "MaxValue";
						else
							break;
						if (IsMscorlibSystemClass(context.CurrentMethod.DeclaringType, "Double"))
							break;
						var module = context.CurrentModule;
						var mr = new MemberRefUser(module, name, new FieldSig(module.CorLibTypes.Double), module.CorLibTypes.Double.TypeDefOrRef);
						expr.Code = ILCode.Ldsfld;
						expr.Operand = mr;
						expr.Arguments.Clear();
					}
					break;

				case ElementType.ValueType:
					if (expr.Code == ILCode.Ldc_Decimal) {
						var c = (decimal)expr.Operand;
						string name;
						if (c == decimal.MinValue)
							name = "MinValue";
						else if (c == decimal.MaxValue)
							name = "MaxValue";
						else
							break;
						if (IsMscorlibSystemClass(context.CurrentMethod.DeclaringType, "Decimal"))
							break;
						var module = context.CurrentModule;
						var tr = module.CorLibTypes.GetTypeRef("System", "Decimal");
						var mr = new MemberRefUser(module, name, new FieldSig(new ValueTypeSig(tr)), tr);
						expr.Code = ILCode.Ldsfld;
						expr.Operand = mr;
						expr.Arguments.Clear();
					}
					break;
				}
			}
		}

		void CreateLoopLocal(ILBlock method) {
			foreach (var block in method.GetSelfAndChildrenRecursive(Optimize_List_ILWhileLoop))
				CreateLoopLocalCore(block);
		}

		static readonly UTF8String nameMoveNext = new UTF8String("MoveNext");
		void CreateLoopLocalCore(ILWhileLoop block) {
			// 'Current' is passed directly to a method without being stored in a local. This will cause
			// the foreach loop detection in PatternStatementTransform to fail. Solution is to create a
			// local and make sure it doesn't get inlined.
			//
			// loop (call(Enumerator::MoveNext, ldloca(var_0_0A))) {
			//     (....(callgetter(Enumerator::get_Current, ldloca(var_0_0A)))....)
			//     ....
			// }

			IMethod method;
			ILExpression ldloc;
			if (!block.Condition.Match(ILCode.Call, out method, out ldloc) && !block.Condition.Match(ILCode.Callvirt, out method, out ldloc))
				return;
			if (method.Name != nameMoveNext || method.MethodSig.GetParamCount() != 0)
				return;
			ILVariable enumeratorVar;
			if (!ldloc.Match(ILCode.Ldloc, out enumeratorVar) && !ldloc.Match(ILCode.Ldloca, out enumeratorVar))
				return;

			var body = block.BodyBlock?.Body;
			if (body == null || body.Count == 0)
				return;
			var expr = body[0] as ILExpression;
			if (expr == null)
				return;

			// Check if it's a normal store to a local
			// stloc(x, callgetter(Enumerator::get_Current, ldloca(enumeratorVar)))
			ILVariable v;
			ILExpression callgetter;
			if (expr.Match(ILCode.Stloc, out v, out callgetter)) {
				// TODO: Allow castclass, but it's disabled since eg. TransformForeach() won't
				// detect the foreach loop. Castclass is used in eg. foreach (int v in (IList<object>)new object[] { 1 })
				//ILExpression expr2;
				//ITypeDefOrRef type;
				//if (callgetter.Match(ILCode.Castclass, out type, out expr2))
				//	callgetter = expr2;
				if (MatchCallGetterCurrent(callgetter, enumeratorVar))
					return;
			}

			var stack = Optimize_List_ILExpressionx2;
			stack.Clear();
			stack.Add(new KeyValuePair<ILExpression, ILExpression>(expr, null));
			var foundExpr = default(KeyValuePair<ILExpression, ILExpression>);
			while (stack.Count > 0) {
				var info = stack[stack.Count - 1];
				stack.RemoveAt(stack.Count - 1);
				var curr = info.Key;
				if (MatchCallGetterCurrent(curr, enumeratorVar)) {
					// Make sure it's called once in the first expression
					if (foundExpr.Key != null)
						return;
					foundExpr = info;
				}

				var args = curr.Arguments;
				for (int i = 0; i < args.Count; i++)
					stack.Add(new KeyValuePair<ILExpression, ILExpression>(args[i], curr));
			}
			if (foundExpr.Value == null)
				return;
			var parent = foundExpr.Value;
			var child = foundExpr.Key;
			int index = parent.Arguments.IndexOf(child);
			Debug.Assert(index >= 0);
			if (index < 0)
				return;
			var newVar = CreateTempLocal();
			var newStloc = new ILExpression(ILCode.Wrap, null, new ILExpression(ILCode.Stloc, newVar, child));
			body.Insert(0, newStloc);
			parent.Arguments[index] = new ILExpression(ILCode.Ldloc, newVar);
		}

		static bool MatchCallGetterCurrent(ILExpression expr, ILVariable enumeratorVar) {
			IMethod getter;
			ILExpression ldloc;
			if (expr.Match(ILCode.CallGetter, out getter, out ldloc) || expr.Match(ILCode.CallvirtGetter, out getter, out ldloc)) {
				if (getter.Name == name_get_Current && (ldloc.MatchLdloc(enumeratorVar) || ldloc.MatchLdloca(enumeratorVar)))
					return true;
			}
			return false;
		}
		static readonly UTF8String name_get_Current = new UTF8String("get_Current");

		void ConvertFieldAccessesToPropertyMethodCalls(ILBlock method, AutoPropertyProvider autoPropertyProvider) {
			if (autoPropertyProvider == null)
				autoPropertyProvider = new AutoPropertyProvider();
			// The compiler uses method calls in all non-constructor methods
			Debug.Assert(context.CurrentMethod.IsConstructor);
			var info = autoPropertyProvider.GetOrCreate(context.CurrentMethod.DeclaringType);
			foreach (var block in method.GetSelfAndChildrenRecursive(Optimize_List_ILBlock))
				ConvertFieldAccessesToPropertyMethodCallsCore(info, block);
		}

		void ConvertFieldAccessesToPropertyMethodCallsCore(AutoPropertyInfo info, ILBlock block) {
			var body = block.Body;
			var currentType = context.CurrentMethod.DeclaringType;
			for (int i = 0; i < body.Count; i++) {
				var expr = body[i] as ILExpression;
				if (expr == null)
					continue;
				if (expr.Code == ILCode.Stfld || expr.Code == ILCode.Stsfld) {
					var field = (expr.Operand as IField).ResolveFieldWithinSameModule();
					if (field?.DeclaringType != currentType)
						continue;
					var setter = info.TryGetSetter(field);
					// This is never virtual even if the property is virtual
					var code = ILCode.Call;
					if (setter == null) {
						setter = info.TryGetGetter(field);
						if (setter == null)
							continue;
						code = ILCode.CallReadOnlySetter;
					}
					expr.Code = code;
					expr.Operand = setter;
				}
				else if (expr.Code == ILCode.Initobj && expr.Arguments.Count == 1) {
					// initobj(TYPE, ldflda(class C::<Prop1>k__BackingField, ldloc(this)))
					var ldflda = expr.Arguments[0];
					if (ldflda.Code == ILCode.Ldflda || ldflda.Code == ILCode.Ldsflda) {
						var field = (ldflda.Operand as IField).ResolveFieldWithinSameModule();
						if (field?.DeclaringType != currentType)
							continue;
						var setter = info.TryGetSetter(field);
						// This is never virtual even if the property is virtual
						var code = ILCode.Call;
						if (setter == null) {
							setter = info.TryGetGetter(field);
							if (setter == null)
								continue;
							code = ILCode.CallReadOnlySetter;
						}

						if (context.CalculateILSpans)
							expr.ILSpans.AddRange(ldflda.ILSpans);

						var initobjType = expr.Operand;
						var initobjVar = new ILVariable("rop_" + (readOnlyPropTempLocalNameCounter++).ToString()) { GeneratedByDecompiler = true };
						var newInitobj = new ILExpression(ILCode.Initobj, initobjType, new ILExpression(ILCode.Ldloca, initobjVar));
						body.Insert(i++, newInitobj);

						expr.Code = code;
						expr.Operand = setter;
						expr.Arguments.Clear();
						expr.Arguments.AddRange(ldflda.Arguments);
						expr.Arguments.Add(new ILExpression(ILCode.Ldloc, initobjVar));
					}
				}
			}
		}
		int readOnlyPropTempLocalNameCounter;

		void FixRoslynStaticDelegates(ILBlock method) {
			foreach (var block in method.GetSelfAndChildrenRecursive(Optimize_List_ILBlock))
				FixRoslynStaticDelegatesCore(block);
		}

		bool FixRoslynStaticDelegatesCore(ILBlock block) {
			// Roslyn C# 6 (Debug and Release builds):
			// arg_20_1 : class [mscorlib]System.Func`2<string, int32> [generated]
			// if (logicnot(stloc(arg_20_1, ldsfld('<>c'::<>9__29_0)))) {
			//     stloc(arg_20_1, stsfld('<>c'::<>9__29_0, newobj(class [mscorlib]System.Func`2<string, int32>::.ctor, ldsfld('<>c'::<>9), ldftn('<>c'::<TestDelegate1>b__29_0))))
			// }
			// else {
			// }
			//
			// or if the local wasn't used in the source code and the compiler optimized it away:
			//
			// if (logicnot(ldsfld:class [mscorlib]System.Func`2<string, int32>[exp:bool]('<>c'::<>9__45_0))) {
			//     stsfld:class [mscorlib]System.Func`2<string, int32>('<>c'::<>9__45_0, newobj:class [mscorlib]System.Func`2<string, int32>(class [mscorlib]System.Func`2<string, int32>::.ctor, ldsfld:'<>c'[exp:object]('<>c'::<>9), ldftn:native int('<>c'::<TestDelegate30>b__45_0)))
			// }
			// else {
			// }
			var body = block.Body;
			bool modified = false;
			for (int i = 0; i < body.Count; i++) {
				var ifNode = body[i] as ILCondition;
				if (ifNode == null)
					continue;
				if (ifNode.TrueBlock == null)
					continue;
				if (ifNode.FalseBlock == null || ifNode.FalseBlock.Body.Count != 0)
					continue;

				IField field, instField;
				FieldDef fd, instFd;
				IMethod method;
				MethodDef md;
				ILExpression stloc, ldsfld, newobj, ldftn;
				ILVariable delVar;
				var cond = ifNode.Condition;
				if (!cond.Match(ILCode.LogicNot, out stloc))
					continue;
				if (stloc.Match(ILCode.Stloc, out delVar, out ldsfld)) {
					if (!ldsfld.Match(ILCode.Ldsfld, out field) || (fd = field.ResolveFieldWithinSameModule()) == null)
						continue;
					// Make sure it's a nested type. We can't check that
					//	field.DeclaringType.DeclaringType == context.CurrentMethod.DeclaringType
					// since that sometimes fails.
					if (field.DeclaringType.DeclaringType == null)
						continue;

					var trueBody = ifNode.TrueBlock.Body;
					if (trueBody.Count != 1)
						continue;
					ILExpression stsfld;
					if (!trueBody[0].MatchStloc(delVar, out stsfld))
						continue;
					if (!stsfld.Match(ILCode.Stsfld, out field, out newobj) || field.ResolveFieldWithinSameModule() != fd)
						continue;
					IMethod delTypeCtor;
					if (!newobj.Match(ILCode.Newobj, out delTypeCtor, out ldsfld, out ldftn))
						continue;
					if (!ldsfld.Match(ILCode.Ldsfld, out instField) || (instFd = instField.ResolveFieldWithinSameModule()) == null)
						continue;
					if (instFd.DeclaringType != fd.DeclaringType)
						continue;
					if (!ldftn.Match(ILCode.Ldftn, out method) || (md = method.ResolveMethodWithinSameModule()) == null)
						continue;
					if (md.DeclaringType != fd.DeclaringType || md.IsStatic)
						continue;

					if (context.CalculateILSpans)
						newobj.ILSpans.AddRange(ifNode.GetSelfAndChildrenRecursiveILSpans().ToArray());
					body[i] = new ILExpression(ILCode.Stloc, delVar, newobj);
					modified = true;
				}
				else if (stloc.Match(ILCode.Ldsfld, out field)) {
					if ((fd = field.ResolveFieldWithinSameModule()) == null)
						continue;
					// Make sure it's a nested type. We can't check that
					//	field.DeclaringType.DeclaringType == context.CurrentMethod.DeclaringType
					// since that sometimes fails.
					if (field.DeclaringType.DeclaringType == null)
						continue;

					var trueBody = ifNode.TrueBlock.Body;
					if (trueBody.Count != 1)
						continue;
					if (!trueBody[0].Match(ILCode.Stsfld, out field, out newobj) || field.ResolveFieldWithinSameModule() != fd)
						continue;
					IMethod delTypeCtor;
					if (!newobj.Match(ILCode.Newobj, out delTypeCtor, out ldsfld, out ldftn))
						continue;
					if (!ldsfld.Match(ILCode.Ldsfld, out instField) || (instFd = instField.ResolveFieldWithinSameModule()) == null)
						continue;
					if (instFd.DeclaringType != fd.DeclaringType)
						continue;
					if (!ldftn.Match(ILCode.Ldftn, out method) || (md = method.ResolveMethodWithinSameModule()) == null)
						continue;
					if (md.DeclaringType != fd.DeclaringType || md.IsStatic)
						continue;

					if (context.CalculateILSpans)
						newobj.ILSpans.AddRange(ifNode.GetSelfAndChildrenRecursiveILSpans().ToArray());
					// Make sure the local isn't removed
					body[i] = new ILExpression(ILCode.Wrap, null, new ILExpression(ILCode.Stloc, CreateTempLocal(), newobj));
					modified = true;
				}
			}
			return modified;
		}

		ILVariable CreateTempLocal() => new ILVariable("_tmp_" + (tmpLocalCounter++).ToString()) { GeneratedByDecompiler = true };
		int tmpLocalCounter;

		bool IsVisualBasicModule() {
			foreach (var asmRef in context.CurrentModule.GetAssemblyRefs()) {
				if (asmRef.Name == nameAssemblyVisualBasic || asmRef.Name == nameAssemblyVisualBasicCore)
					return true;
			}
			// The VB runtime can be embedded if '/vbruntime*' option is used, and if so, the compiler adds
			// attribute "Microsoft.VisualBasic.Embedded" (the name doesn't end in Attribute)
			if (context.CurrentModule.Assembly?.IsDefined(nameAssemblyVisualBasic, nameEmbedded) == true)
				return true;
			if (context.CurrentModule.IsDefined(nameAssemblyVisualBasic, nameEmbedded) == true)
				return true;
			if (context.CurrentModule.Assembly?.Name == nameAssemblyVisualBasic || context.CurrentModule.Assembly?.Name == nameAssemblyVisualBasicCore)
				return true;
			return false;
		}
		static readonly UTF8String nameEmbedded = new UTF8String("Embedded");
		static readonly UTF8String nameAssemblyVisualBasic = new UTF8String("Microsoft.VisualBasic");
		static readonly UTF8String nameAssemblyVisualBasicCore = new UTF8String("Microsoft.VisualBasic.Core");
		static readonly UTF8String nameClearProjectError = new UTF8String("ClearProjectError");
		static readonly UTF8String nameSetProjectError = new UTF8String("SetProjectError");
		static readonly UTF8String nameProjectData = new UTF8String("ProjectData");

		void RemoveVisualBasicCompilerCode(ILBlock method) {
			Debug.Assert(IsVisualBasicModule());
			Optimize_List_CatchBlockBase.Clear();
			// The compiler adds the methods to all catch/filter blocks
			foreach (var block in method.GetSelfAndChildrenRecursive(Optimize_List_CatchBlockBase)) {
				var body = block.Body;
				for (int i = 0; i < body.Count; i++) {
					var expr = body[i] as ILExpression;
					IMethod calledMethod;
					List<ILExpression> args;
					if (!expr.Match(ILCode.Call, out calledMethod, out args))
						continue;
					var declType = calledMethod.DeclaringType;
					if (declType.Name != nameProjectData || declType.Namespace != "Microsoft.VisualBasic.CompilerServices")
						continue;

					// Don't check the assembly name since these methods could also be in the current assembly if vbc.exe's
					// '/vbruntime*' option was used.
					//if (declType.DefinitionAssembly?.Name != nameAssemblyVisualBasic)
					//	continue;

					if (args.Count == 0) {
						if (calledMethod.Name != nameClearProjectError)
							continue;
						expr.Code = ILCode.Nop;
						expr.Operand = null;
					}
					else if (args.Count == 1 || args.Count == 2) {
						if (calledMethod.Name != nameSetProjectError)
							continue;
						if (context.CalculateILSpans) {
							foreach (var arg in args)
								expr.ILSpans.AddRange(arg.GetSelfAndChildrenRecursiveILSpans());
						}
						expr.Code = ILCode.Nop;
						expr.Operand = null;
						expr.Arguments.Clear();
					}
				}
			}
		}

		void FixFilterBlocks(ILBlock method) {
			if (!hasFilters)
				return;
			Optimize_List_CatchBlocks.Clear();
			foreach (var catchBlock in method.GetSelfAndChildrenRecursive(Optimize_List_CatchBlocks)) {
				if (catchBlock.FilterBlock == null)
					continue;
				ILVariable exVar;
				ITypeDefOrRef exType;
				if (FixFilter(catchBlock, out exVar, out exType)) {
					catchBlock.ExceptionType = exType.ToTypeSig();
					catchBlock.ExceptionVariable = exVar;
					if (catchBlock.ExceptionVariable != null && catchBlock.ExceptionVariable.Type == null)
						catchBlock.ExceptionVariable.Type = catchBlock.ExceptionType;
				}
			}
		}

		bool FixFilter(ILTryCatchBlock.CatchBlock catchBlock, out ILVariable exVar, out ITypeDefOrRef exType) =>
			FixFilterRoslyn(catchBlock, out exVar, out exType) ||
			FixFilterMcs(catchBlock, out exVar, out exType);

		bool FixFilterRoslyn(ILTryCatchBlock.CatchBlock catchBlock, out ILVariable exVar, out ITypeDefOrRef exType) {
			exVar = null;
			exType = null;

			var filterBlock = catchBlock.FilterBlock;
			if (filterBlock == null)
				return false;

			var body = filterBlock.Body;
			int pos = 0;

			// stloc:SystemException(expr_0C, isinst:SystemException([mscorlib]System.SystemException, arg_07_0))
			if (!TryGetFilterExceptionType(body, ref pos, filterBlock, out exVar, out exType)) {
				if (body.Count == 1) {
					// Release builds (C# 6), no exception variable:
					//		eg.: catch (IOException) when (ShouldThrow(null)) {
					// endfilter(
					//     logicand:bool(
					//         isinst:IOException[exp:bool]([mscorlib]System.IO.IOException, arg_1F9_0),
					//         cgt.un:bool[exp:int32](
					//             <real expr>,
					//             ldc.i4:bool(0)
					//         )
					//     )
					// )

					ILExpression logicand;
					if (!body[0].Match(ILCode.Endfilter, out logicand))
						return false;
					List<ILExpression> logicand_Args;
					if (!logicand.Match(ILCode.LogicAnd, out logicand_Args) || logicand_Args.Count != 2)
						return false;

					ILExpression isInstLdloc;
					if (!logicand_Args[0].Match(ILCode.Isinst, out exType, out isInstLdloc))
						return false;
					if (!isInstLdloc.Match(ILCode.Ldloc, out exVar))
						return false;
					if (exVar != filterBlock.ExceptionVariable)
						return false;

					List<ILExpression> cgt_args;
					if (!logicand_Args[1].Match(ILCode.Cgt_Un, out cgt_args) || cgt_args.Count != 2)
						return false;
					int intVal;
					if (!cgt_args[1].Match(ILCode.Ldc_I4, out intVal) || intVal != 0)
						return false;

					var newCode = cgt_args[0];
					if (context.CalculateILSpans) {
						var ilSpans = filterBlock.GetSelfAndChildrenRecursiveILSpans().ToArray();
						newCode.ILSpans.AddRange(ilSpans);
					}
					body.Clear();
					body.Add(newCode);
					return true;
				}
				else if (body.Count == 2) {
					// Debug builds (C# 6), no exception variable:
					//		eg.: catch (IOException) when (ShouldThrow(null)) {
					// if (logicnot(isinst:IOException[exp:bool]([mscorlib]System.IO.IOException, arg_1F9_0))) {
					//     stloc:int32(arg_46_0, ldc.i4:int32(0))
					// }
					// else {
					//     stloc:bool(
					//         var_3_4E,
					//         <real expr>
					//     )
					//     stloc:int32(arg_46_0, cgt.un:bool[exp:int32](ldloc:bool(var_3_4E), ldc.i4:bool(0)))
					// }
					// endfilter(arg_46_0)

					var ifCond = body[pos] as ILCondition;
					if (ifCond == null || ifCond.TrueBlock == null || ifCond.FalseBlock == null)
						return false;

					var ifCondExpr = ifCond.Condition;
					ILExpression isinst;
					if (!ifCondExpr.Match(ILCode.LogicNot, out isinst))
						return false;
					ILExpression isInstLdloc;
					if (!isinst.Match(ILCode.Isinst, out exType, out isInstLdloc))
						return false;
					if (!isInstLdloc.Match(ILCode.Ldloc, out exVar))
						return false;
					if (exVar != filterBlock.ExceptionVariable)
						return false;
					ILVariable v;

					// Check failing path
					var trueBody = ifCond.TrueBlock.Body;
					ILVariable retVar;
					ILExpression ldci4;
					if (trueBody.Count != 1 || !trueBody[0].Match(ILCode.Stloc, out retVar, out ldci4))
						return false;
					int intVal;
					if (!ldci4.Match(ILCode.Ldc_I4, out intVal) || intVal != 0)
						return false;

					// Check non-failing path
					var falseBody = ifCond.FalseBlock.Body;
					if (falseBody.Count != 2)
						return false;

					ILVariable boolVar;
					ILExpression newCode;
					if (!falseBody[0].Match(ILCode.Stloc, out boolVar, out newCode))
						return false;

					ILExpression cgt;
					if (!falseBody[1].Match(ILCode.Stloc, out v, out cgt) || v != retVar)
						return false;
					List<ILExpression> cgt_args;
					if (!cgt.Match(ILCode.Cgt_Un, out cgt_args) || cgt_args.Count != 2)
						return false;
					if (!cgt_args[0].Match(ILCode.Ldloc, out v) || v != boolVar)
						return false;
					if (!cgt_args[1].Match(ILCode.Ldc_I4, out intVal) || intVal != 0)
						return false;

					ILExpression ldloc;
					if (!body[1].Match(ILCode.Endfilter, out ldloc) || !ldloc.Match(ILCode.Ldloc, out v) || v != retVar)
						return false;

					if (context.CalculateILSpans) {
						var ilSpans = filterBlock.GetSelfAndChildrenRecursiveILSpans().ToArray();
						newCode.ILSpans.AddRange(ilSpans);
					}
					body.Clear();
					body.Add(newCode);
					return true;
				}
				else
					return false;
			}

			if (pos >= body.Count)
				return false;
			if (pos + 1 == body.Count) {
				// endfilter(
				//     logicand:bool(
				//         expr_0C:SystemException[exp:bool],
				//         cgt.un:bool[exp:int32](
				//             <real expr>,
				//             ldc.i4:bool(0)
				//         )
				//     )
				// )
				ILExpression logicand;
				if (!body[pos].Match(ILCode.Endfilter, out logicand))
					return false;
				List<ILExpression> logicand_Args;
				if (!logicand.Match(ILCode.LogicAnd, out logicand_Args) || logicand_Args.Count != 2)
					return false;
				ILVariable v;
				if (!logicand_Args[0].Match(ILCode.Ldloc, out v) || v != exVar)
					return false;
				List<ILExpression> cgt_args;
				if (!logicand_Args[1].Match(ILCode.Cgt_Un, out cgt_args) || cgt_args.Count != 2)
					return false;
				int intVal;
				if (!cgt_args[1].Match(ILCode.Ldc_I4, out intVal) || intVal != 0)
					return false;

				Debug.Assert(body.Count == 2);
				var newCode = cgt_args[0];
				if (context.CalculateILSpans) {
					newCode.ILSpans.AddRange(body[0].GetSelfAndChildrenRecursiveILSpans());// stloc
					newCode.ILSpans.AddRange(body[pos].ILSpans);// endfilter
					newCode.ILSpans.AddRange(logicand.ILSpans);
					newCode.ILSpans.AddRange(logicand_Args[0].GetSelfAndChildrenRecursiveILSpans());
					newCode.ILSpans.AddRange(logicand_Args[1].ILSpans);
					newCode.ILSpans.AddRange(cgt_args[1].GetSelfAndChildrenRecursiveILSpans());// ldc.i4.0
				}
				body.Clear();
				body.Add(newCode);
				return true;
			}
			else {
				// Release builds (C# 6)
				// ---------------------
				// if (logicnot(expr_0C:SystemException[exp:bool])) {
				//     stloc:int32(arg_46_0, ldc.i4:int32(0))
				// }
				// else {
				//     stloc:SystemException(ex2, expr_0C:SystemException)
				//     stloc:int32(
				//         arg_46_0,
				//         cgt.un:bool[exp:int32](
				//             <real expr>,
				//             ldc.i4:bool(0)
				//         )
				//     )
				// }
				// endfilter(arg_46_0)
				//
				// Debug builds (C# 6)
				// -------------------
				// if (logicnot(expr_0C:SystemException[exp:bool])) {
				//     stloc:int32(arg_46_0, ldc.i4:int32(0))
				// }
				// else {
				//     stloc:SystemException(ex2, expr_0C:SystemException)
				//     stloc:bool(
				//         var_3_4E,
				//         <real expr>
				//     )
				//     stloc:int32(arg_46_0, cgt.un:bool[exp:int32](ldloc:bool(var_3_4E), ldc.i4:bool(0)))
				// }
				// endfilter(arg_46_0)

				var ifCond = body[pos] as ILCondition;
				if (ifCond == null || ifCond.TrueBlock == null || ifCond.FalseBlock == null)
					return false;

				var ifCondExpr = ifCond.Condition;
				ILExpression ldloc;
				if (!ifCondExpr.Match(ILCode.LogicNot, out ldloc))
					return false;
				ILVariable v;
				if (!ldloc.Match(ILCode.Ldloc, out v) || v != exVar)
					return false;

				// Check failing path
				var trueBody = ifCond.TrueBlock.Body;
				ILVariable retVar;
				ILExpression ldci4;
				if (trueBody.Count != 1 || !trueBody[0].Match(ILCode.Stloc, out retVar, out ldci4))
					return false;
				int intVal;
				if (!ldci4.Match(ILCode.Ldc_I4, out intVal) || intVal != 0)
					return false;

				// Check non-failing path
				var falseBody = ifCond.FalseBlock.Body;
				if (falseBody.Count < 2)
					return false;
				ILExpression ldloc2;
				ILVariable realExVar;
				if (!falseBody[0].Match(ILCode.Stloc, out realExVar, out ldloc2))
					return false;
				if (!ldloc2.Match(ILCode.Ldloc, out v) || v != exVar)
					return false;

				if (falseBody.Count == 2) {
					// Release build
					//     stloc:int32(
					//         arg_46_0,
					//         cgt.un:bool[exp:int32](
					//             <real expr>,
					//             ldc.i4:bool(0)
					//         )
					//     )
					ILExpression cgt;
					if (!falseBody[1].Match(ILCode.Stloc, out v, out cgt) || v != retVar)
						return false;
					List<ILExpression> cgt_args;
					if (!cgt.Match(ILCode.Cgt_Un, out cgt_args) || cgt_args.Count != 2)
						return false;
					if (!cgt_args[1].Match(ILCode.Ldc_I4, out intVal) || intVal != 0)
						return false;

					if (!body[2].Match(ILCode.Endfilter, out ldloc) || !ldloc.Match(ILCode.Ldloc, out v) || v != retVar)
						return false;

					var newCode = cgt_args[0];
					if (context.CalculateILSpans) {
						var ilSpans = filterBlock.GetSelfAndChildrenRecursiveILSpans().ToArray();
						newCode.ILSpans.AddRange(ilSpans);
					}
					body.Clear();
					body.Add(newCode);
					exVar = realExVar;
					return true;
				}
				else if (falseBody.Count == 3) {
					// Debug build
					//     stloc:bool(
					//         var_3_4E,
					//         <real expr>
					//     )
					//     stloc:int32(arg_46_0, cgt.un:bool[exp:int32](ldloc:bool(var_3_4E), ldc.i4:bool(0)))
					ILVariable boolVar;
					ILExpression newCode;
					if (!falseBody[1].Match(ILCode.Stloc, out boolVar, out newCode))
						return false;

					ILExpression cgt;
					if (!falseBody[2].Match(ILCode.Stloc, out v, out cgt) || v != retVar)
						return false;
					List<ILExpression> cgt_args;
					if (!cgt.Match(ILCode.Cgt_Un, out cgt_args) || cgt_args.Count != 2)
						return false;
					if (!cgt_args[0].Match(ILCode.Ldloc, out v) || v != boolVar)
						return false;
					if (!cgt_args[1].Match(ILCode.Ldc_I4, out intVal) || intVal != 0)
						return false;

					if (!body[2].Match(ILCode.Endfilter, out ldloc) || !ldloc.Match(ILCode.Ldloc, out v) || v != retVar)
						return false;

					if (context.CalculateILSpans) {
						var ilSpans = filterBlock.GetSelfAndChildrenRecursiveILSpans().ToArray();
						newCode.ILSpans.AddRange(ilSpans);
					}
					body.Clear();
					body.Add(newCode);
					exVar = realExVar;
					return true;
				}
				else
					return false;
			}
		}

		static bool TryGetFilterExceptionType(List<ILNode> body, ref int pos, ILTryCatchBlock.FilterILBlock filterBlock, out ILVariable exVar, out ITypeDefOrRef exType) {
			exVar = null;
			exType = null;
			ILExpression isinst;
			if (pos >= body.Count || !body[pos].Match(ILCode.Stloc, out exVar, out isinst))
				return false;
			ILExpression isInstLdloc;
			if (!isinst.Match(ILCode.Isinst, out exType, out isInstLdloc))
				return false;
			ILVariable v;
			if (!isInstLdloc.Match(ILCode.Ldloc, out v))
				return false;
			if (v != filterBlock.ExceptionVariable)
				return false;

			pos++;
			return true;
		}

		// Tested: mcs, Mono 4.6.2
		bool FixFilterMcs(ILTryCatchBlock.CatchBlock catchBlock, out ILVariable exVar, out ITypeDefOrRef exType) {
			exVar = null;
			exType = null;

			var filterBlock = catchBlock.FilterBlock;
			if (filterBlock == null)
				return false;

			var body = filterBlock.Body;
			int pos = 0;

			// filter arg_13_0 {
			//     stloc(var_0_18, isinst([mscorlib]System.SystemException, arg_13_0))
			//     endfilter(logicand(ldloc(var_0_18), <real-expr>))
			// }
			if (TryGetFilterExceptionType(body, ref pos, filterBlock, out exVar, out exType)) {
				if (pos >= body.Count)
					return false;
				ILExpression logicand;
				if (!body[pos++].Match(ILCode.Endfilter, out logicand))
					return false;
				List<ILExpression> args;
				if (!logicand.Match(ILCode.LogicAnd, out args) || args.Count != 2)
					return false;
				if (!args[0].MatchLdloc(exVar))
					return false;
				var newCode = args[1];

				if (context.CalculateILSpans) {
					var ilSpans = filterBlock.GetSelfAndChildrenRecursiveILSpans().ToArray();
					newCode.ILSpans.AddRange(ilSpans);
				}
				body.Clear();
				body.Add(newCode);
				return true;
			}

			// Local isn't used by the catch handler or the filter
			// eg. catch (IOException) when (ShouldThrow(null)) {
			// filter arg_232_0 {
			//     endfilter(logicand(isinst([mscorlib]System.IO.IOException, arg_232_0), <real-expr>))
			// }
			if (body.Count == 1) {
				ILExpression logicand;
				if (!body[pos++].Match(ILCode.Endfilter, out logicand))
					return false;
				List<ILExpression> args;
				if (!logicand.Match(ILCode.LogicAnd, out args) || args.Count != 2)
					return false;
				ILExpression ldloc;
				if (!args[0].Match(ILCode.Isinst, out exType, out ldloc))
					return false;
				if (!ldloc.Match(ILCode.Ldloc, out exVar))
					return false;
				var newCode = args[1];

				if (context.CalculateILSpans) {
					var ilSpans = filterBlock.GetSelfAndChildrenRecursiveILSpans().ToArray();
					newCode.ILSpans.AddRange(ilSpans);
				}
				body.Clear();
				body.Add(newCode);
				return true;
			}

			return false;
		}

		/// <summary>
		/// Removes redundatant Br, Nop, Dup, Pop
		/// Ignore arguments of 'leave'
		/// </summary>
		/// <param name="method"></param>
		internal static void RemoveRedundantCode(DecompilerContext context, ILBlock method, List<ILExpression> listExpr, List<ILBlock> listBlock, Dictionary<ILLabel, int> labelRefCount)
		{
			labelRefCount.Clear();
			foreach (var e in method.GetSelfAndChildrenRecursive<ILExpression>(listExpr, e => e.IsBranch())) {
				foreach (var target in e.GetBranchTargets())
					labelRefCount[target] = labelRefCount.GetOrDefault(target) + 1;
			}
			
			foreach(ILBlock block in method.GetSelfAndChildrenRecursive<ILBlock>(listBlock)) {
				List<ILNode> body = block.Body;
				List<ILNode> newBody = new List<ILNode>(body.Count);
				for (int i = 0; i < body.Count; i++) {
					ILLabel target;
					ILExpression popExpr;
					if (body[i].Match(ILCode.Br, out target) && i+1 < body.Count && body[i+1] == target) {
						ILNode prev = newBody.Count > 0 ? newBody[newBody.Count - 1] : null;
						ILNode label = null;
						ILNode br = body[i];
						// Ignore the branch
						if (labelRefCount[target] == 1) {
							label = body[i + 1];
							i++;  // Ignore the label as well
						}
						if (context.CalculateILSpans) {
							ILNode next = i + 1 < body.Count ? body[i + 1] : null;
							Utils.AddILSpansTryPreviousFirst(br, prev, next, block);
							if (label != null)
								Utils.AddILSpansTryPreviousFirst(label, prev, next, block);
						}
					} else if (body[i].Match(ILCode.Nop)){
						// Ignore nop
						if (context.CalculateILSpans)
							Utils.NopMergeILSpans(block, newBody, i);
					} else if (body[i].Match(ILCode.Pop, out popExpr)) {
						ILVariable v;
						if (!popExpr.Match(ILCode.Ldloc, out v))
							throw new Exception("Pop should have just ldloc at this stage");
						if (context.CalculateILSpans) {
							// Best effort to move the ILSpan to previous statement
							ILVariable prevVar;
							ILExpression prevExpr;
							if (i - 1 >= 0 && body[i - 1].Match(ILCode.Stloc, out prevVar, out prevExpr) && prevVar == v)
								prevExpr.ILSpans.AddRange(((ILExpression)body[i]).ILSpans);
							else
								Utils.AddILSpansTryPreviousFirst(newBody, body, i, block);
						}
						// Ignore pop
					} else {
						ILLabel label = body[i] as ILLabel;
						if (label != null) {
							if (labelRefCount.GetOrDefault(label) > 0)
								newBody.Add(label);
							else if (context.CalculateILSpans)
								Utils.LabelMergeILSpans(block, newBody, i);
						} else {
							newBody.Add(body[i]);
						}
					}
				}
				block.Body = newBody;
			}
			
			// Ignore arguments of 'leave'
			foreach (ILExpression expr in method.GetSelfAndChildrenRecursive<ILExpression>(listExpr, e => e.Code == ILCode.Leave)) {
				if (expr.Arguments.Any(arg => !arg.Match(ILCode.Ldloc)))
					throw new Exception("Leave should have just ldloc at this stage");
				if (context.CalculateILSpans) {
					foreach (var arg in expr.Arguments)
						arg.AddSelfAndChildrenRecursiveILSpans(expr.ILSpans);
				}
				expr.Arguments.Clear();
			}
			
			// 'dup' removal
			foreach (ILExpression expr in method.GetSelfAndChildrenRecursive<ILExpression>(listExpr)) {
				for (int i = 0; i < expr.Arguments.Count; i++) {
					ILExpression child;
					if (expr.Arguments[i].Match(ILCode.Dup, out child)) {
						if (context.CalculateILSpans) {
							long index = 0;
							bool done = false;
							var argTmp = expr.Arguments[i];
							for (;;) {
								var b = argTmp.GetAllILSpans(ref index, ref done);
								if (done)
									break;
								child.ILSpans.Add(b);
							}
						}
						expr.Arguments[i] = child;
					}
				}
			}
		}

		/// <summary>
		/// Reduces the branch codes to just br and brtrue.
		/// Moves ILSpans to the branch argument
		/// </summary>
		void ReduceBranchInstructionSet(ILBlock block)
		{
			for (int i = 0; i < block.Body.Count; i++) {
				ILExpression expr = block.Body[i] as ILExpression;
				if (expr != null && expr.Prefixes == null) {
					ILCode op;
					switch(expr.Code) {
						case ILCode.Switch:
						case ILCode.Brtrue:
							if (context.CalculateILSpans) {
								expr.Arguments.Single().ILSpans.AddRange(expr.ILSpans);
								expr.ILSpans.Clear();
							}
							continue;
						case ILCode.Brfalse:  op = ILCode.LogicNot; break;
						case ILCode.Beq:      op = ILCode.Ceq; break;
						case ILCode.Bne_Un:   op = ILCode.Cne; break;
						case ILCode.Bgt:      op = ILCode.Cgt; break;
						case ILCode.Bgt_Un:   op = ILCode.Cgt_Un; break;
						case ILCode.Ble:      op = ILCode.Cle; break;
						case ILCode.Ble_Un:   op = ILCode.Cle_Un; break;
						case ILCode.Blt:      op = ILCode.Clt; break;
						case ILCode.Blt_Un:   op = ILCode.Clt_Un; break;
						case ILCode.Bge:	  op = ILCode.Cge; break;
						case ILCode.Bge_Un:   op = ILCode.Cge_Un; break;
						default:
							continue;
					}
					var newExpr = new ILExpression(op, null, expr.Arguments);
					block.Body[i] = new ILExpression(ILCode.Brtrue, expr.Operand, newExpr);
					if (context.CalculateILSpans)
						newExpr.ILSpans.AddRange(expr.ILSpans);
				}
			}
		}

		/// <summary>
		/// Strips simple dead-code obfuscations seen in KSP game code.
		/// </summary>
		void RemoveKspObfuscations(ILBlock block)
		{
			var toDrop = new HashSet<ILNode>();

			/// Never-taken infinite switch loop
			///   ldc.i4.?      ; (A) Immediate value must be != 0
			///   switch (-10)  ; (B) The single target is the previous ldc
			for (int i = 0; i < block.Body.Count - 1; i++) {
				// At this step in the decompilation we're looking for a Label followed by a
				// Switch. The Ldc will be inlined as the Switch's sole Argument.
				var label = block.Body[i] as ILLabel;
				if (label == null) { continue; }
				ILLabel[] targets;
				ILExpression arg;
				if (!block.Body[i+1].Match(ILCode.Switch, out targets, out arg)) { continue; }
				int v;
				if (!arg.Match(ILCode.Ldc_I4, out v) || v == 0) { continue; } // (A)
				if (targets.Count() != 1 || targets[0] != label) { continue; } // (B)
				// Drop Switch.
				toDrop.Add(block.Body[i+1]);
			}

			/// Dead ldtoken
			///   ldc.i4.1      ; (A) Immediate value is always 1
			///   brtrue.s +6   ; (B) This jumps just past the pop
			///   ldtoken ?     ; Operand could be anything but seems to always be the current method
			///   pop           ; Balances the stack in theory even though it would never be executed
			for (int i = 0; i < block.Body.Count - 3; i++) {
				// We're looking for Brtrue, Stloc, Label. The Ldc is inlined as the Brtrue's
				// Argument, and The Ldtoken has become the Stloc's Argument. The final Pop
				// was optimized out in a previous pass.
				ILLabel target;
				ILVariable locVar; // Ignored, just needed for the pattern match.
				ILExpression branchArg, storeArg;
				if (!block.Body[i].Match(ILCode.Brtrue, out target, out branchArg)) { continue; }
				if (!block.Body[i+1].Match(ILCode.Stloc, out locVar, out storeArg)) { continue; }
				var label = block.Body[i+2] as ILLabel;
				if (label == null) { continue; }
				int v;
				if (!branchArg.Match(ILCode.Ldc_I4, out v) || v != 1) { continue; } // (A)
				if (!storeArg.Match(ILCode.Ldtoken)) { continue; }
				if (target != label) { continue; } // (B)
				// Drop Brtrue and Stloc.
				toDrop.Add(block.Body[i]);
				toDrop.Add(block.Body[i+1]);
			}

			if (toDrop.Count > 0) {
				var newBody = new List<ILNode>(block.Body.Count - toDrop.Count);
				foreach (var node in block.Body) {
					if (!toDrop.Contains(node)) {
						newBody.Add(node);
					}
				}
				block.Body = newBody;
			}
		}

		/// <summary>
		/// Converts call and callvirt instructions that read/write properties into CallGetter/CallSetter instructions.
		/// 
		/// CallGetter/CallSetter is used to allow the ILAst to represent "while ((SomeProperty = value) != null)".
		/// 
		/// Also simplifies 'newobj(SomeDelegate, target, ldvirtftn(F, target))' to 'newobj(SomeDelegate, target, ldvirtftn(F))'
		/// </summary>
		void IntroducePropertyAccessInstructions(ILNode node)
		{
			ILExpression parentExpr = node as ILExpression;
			if (parentExpr != null) {
				for (int i = 0; i < parentExpr.Arguments.Count; i++) {
					ILExpression expr = parentExpr.Arguments[i];
					IntroducePropertyAccessInstructions(expr);
					IntroducePropertyAccessInstructions(expr, parentExpr, i);
				}
			} else {
				foreach (ILNode child in node.GetChildren()) {
					IntroducePropertyAccessInstructions(child);
					ILExpression expr = child as ILExpression;
					if (expr != null) {
						IntroducePropertyAccessInstructions(expr, null, -1);
					}
				}
			}
		}
		
		void IntroducePropertyAccessInstructions(ILExpression expr, ILExpression parentExpr, int posInParent)
		{
			if (expr.Code == ILCode.Call || expr.Code == ILCode.Callvirt) {
				IMethod method = (IMethod)expr.Operand;
				var declArrayType = (method.DeclaringType as TypeSpec)?.TypeSig.RemovePinnedAndModifiers() as ArraySigBase;
				if (declArrayType != null) {
					switch (method.Name) {
						case "Get":
							expr.Code = ILCode.CallGetter;
							break;
						case "Set":
							expr.Code = ILCode.CallSetter;
							break;
						case "Address":
							ByRefSig brt = method.MethodSig.GetRetType() as ByRefSig;
							if (brt != null) {
								IMethod getMethod = new MemberRefUser(method.Module, "Get", method.MethodSig?.Clone(), declArrayType.ToTypeDefOrRef());
								if (getMethod.MethodSig != null)
									getMethod.MethodSig.RetType = declArrayType.Next;
								expr.Operand = getMethod;
							}
							expr.Code = ILCode.CallGetter;
							if (parentExpr != null) {
								parentExpr.Arguments[posInParent] = new ILExpression(ILCode.AddressOf, null, expr);
							}
							break;
					}
				} else if (expr.Arguments.Count == 1 &&
						method.Name == name_get_HasValue &&
						method.MethodSig.GetParamCount() == 0 &&
						method.MethodSig.GetRetType().RemovePinnedAndModifiers().GetElementType() == ElementType.Boolean &&
						IsSystemNullable(method.DeclaringType)) {
					expr.Code = ILCode.Cnotnull;
					expr.Operand = null;
					expr.Prefixes = null;
				} else {
					MethodDef methodDef = method.Resolve();
					if (methodDef?.IsGetter ?? method.Name.StartsWith("get_"))
						expr.Code = (expr.Code == ILCode.Call) ? ILCode.CallGetter : ILCode.CallvirtGetter;
					else if (methodDef?.IsSetter ?? method.Name.StartsWith("set_"))
						expr.Code = (expr.Code == ILCode.Call) ? ILCode.CallSetter : ILCode.CallvirtSetter;
				}
			} else if (expr.Code == ILCode.Newobj && expr.Arguments.Count == 2) {
				// Might be 'newobj(SomeDelegate, target, ldvirtftn(F, target))'.
				ILVariable target;
				if (expr.Arguments[0].Match(ILCode.Ldloc, out target)
					&& expr.Arguments[1].Code == ILCode.Ldvirtftn
					&& expr.Arguments[1].Arguments.Count == 1
					&& expr.Arguments[1].Arguments[0].MatchLdloc(target))
				{
					// Remove the 'target' argument from the ldvirtftn instruction.
					// It's not needed in the translation to C#, and needs to be eliminated so that the target expression
					// can be inlined.
					if (context.CalculateILSpans)
						expr.Arguments[1].Arguments[0].AddSelfAndChildrenRecursiveILSpans(expr.Arguments[1].ILSpans);
					expr.Arguments[1].Arguments.Clear();
				}
			}
		}
		static readonly UTF8String name_get_HasValue = new UTF8String("get_HasValue");

		static bool IsSystemNullable(ITypeDefOrRef tdr) => ((tdr as TypeSpec)?.TypeSig as GenericInstSig)?.GenericType.IsSystemNullable() == true;

		/// <summary>
		/// Group input into a set of blocks that can be later arbitraliby schufled.
		/// The method adds necessary branches to make control flow between blocks
		/// explicit and thus order independent.
		/// </summary>
		void SplitToBasicBlocks(ILBlock block)
		{
			List<ILNode> basicBlocks = new List<ILNode>();
			
			ILLabel entryLabel = block.Body.FirstOrDefault() as ILLabel ?? new ILLabel() { Name = "Block_" + (nextLabelIndex++).ToString() };
			ILBasicBlock basicBlock = new ILBasicBlock();
			basicBlocks.Add(basicBlock);
			basicBlock.Body.Add(entryLabel);
			block.EntryGoto = new ILExpression(ILCode.Br, entryLabel);
			
			if (block.Body.Count > 0) {
				if (block.Body[0] != entryLabel)
					basicBlock.Body.Add(block.Body[0]);
				
				for (int i = 1; i < block.Body.Count; i++) {
					ILNode lastNode = block.Body[i - 1];
					ILNode currNode = block.Body[i];
					
					// Start a new basic block if necessary
					if (currNode is ILLabel ||
						currNode is ILTryCatchBlock || // Counts as label
						lastNode.IsConditionalControlFlow() ||
						lastNode.IsUnconditionalControlFlow())
					{
						// Try to reuse the label
						ILLabel label = currNode as ILLabel ?? new ILLabel() { Name = "Block_" + (nextLabelIndex++).ToString() };
						
						// Terminate the last block
						if (!lastNode.IsUnconditionalControlFlow()) {
							// Explicit branch from one block to other
							basicBlock.Body.Add(new ILExpression(ILCode.Br, label));
						}
						
						// Start the new block
						basicBlock = new ILBasicBlock();
						basicBlocks.Add(basicBlock);
						basicBlock.Body.Add(label);
						
						// Add the node to the basic block
						if (currNode != label)
							basicBlock.Body.Add(currNode);
					} else {
						basicBlock.Body.Add(currNode);
					}
				}
			}
			
			block.Body = basicBlocks;
			return;
		}

		void DuplicateReturnStatements(ILBlock method) {
			this.Optimize_Dict_ILLabel_ILNode.Clear();

			foreach (ILBlock block in method.GetSelfAndChildrenRecursive(Optimize_List_ILBlock)) {
				var body = block.Body;
				for (int i = 0; i < body.Count - 1; i++) {
					ILLabel curr = body[i] as ILLabel;
					if (curr != null)
						Optimize_Dict_ILLabel_ILNode[curr] = body[i + 1];
				}
			}

			foreach (ILBlock block in Optimize_List_ILBlock) {
				var body = block.Body;
				for (int i = 0; i < body.Count; i++) {
					var expr = body[i] as ILExpression;
					if (expr == null)
						continue;
					if (expr.Code != ILCode.Br && expr.Code != ILCode.Leave)
						continue;
					var targetLabel = (ILLabel)expr.Operand;
					for (;;) {
						ILNode node;
						if (!Optimize_Dict_ILLabel_ILNode.TryGetValue(targetLabel, out node))
							break;
						var lbl = node as ILLabel;
						if (lbl == null)
							break;
						targetLabel = lbl;
					}

					ILNode target;
					List<ILExpression> retArgs;
					if (Optimize_Dict_ILLabel_ILNode.TryGetValue(targetLabel, out target)) {
						if (target.Match(ILCode.Ret, out retArgs)) {
							ILVariable locVar;
							object constValue;
							if (retArgs.Count == 0)
								body[i] = new ILExpression(ILCode.Ret, null).WithILSpansFrom(context.CalculateILSpans, body[i]);
							else if (retArgs[0].Match(ILCode.Ldloc, out locVar)) {
								ILExpression retExpr;
								ILVariable v;
								if (i > 0 && body[i - 1].Match(ILCode.Stloc, out v, out retExpr) && v == locVar) {
									var newExpr = new ILExpression(ILCode.Ret, null, retExpr);
									if (context.CalculateILSpans) {
										newExpr.ILSpans.AddRange(body[i - 1].ILSpans);
										body[i].AddSelfAndChildrenRecursiveILSpans(newExpr.ILSpans);
									}
									body[i - 1] = newExpr;
									body.RemoveAt(i);
									i--;
								}
								else
									body[i] = new ILExpression(ILCode.Ret, null, new ILExpression(ILCode.Ldloc, locVar)).WithILSpansFrom(context.CalculateILSpans, body[i]);
							}
							else if (retArgs[0].Match(ILCode.Ldc_I4, out constValue))
								body[i] = new ILExpression(ILCode.Ret, null, new ILExpression(ILCode.Ldc_I4, constValue)).WithILSpansFrom(context.CalculateILSpans, body[i]);
						}
					}
					else {
						if (method.Body.Count > 0 && method.Body.Last() == targetLabel) {
							// It exits the main method - so it is same as return;
							body[i] = new ILExpression(ILCode.Ret, null).WithILSpansFrom(context.CalculateILSpans, body[i]);
						}
					}
				}
			}
		}

		/// <summary>
		/// Flattens all nested basic blocks, except the the top level 'node' argument
		/// </summary>
		void FlattenBasicBlocks(ILNode node)
		{
			ILBlock block = node as ILBlock;
			if (block != null) {
				ILBasicBlock prevChildAsBB = null;
				List<ILNode> flatBody = new List<ILNode>();
				foreach (ILNode child in block.GetChildren()) {
					FlattenBasicBlocks(child);
					ILBasicBlock childAsBB = child as ILBasicBlock;
					if (childAsBB != null) {
						if (!(childAsBB.Body.FirstOrDefault() is ILLabel))
							throw new Exception("Basic block has to start with a label. \n" + childAsBB.ToString());
						if (childAsBB.Body.LastOrDefault() is ILExpression && !childAsBB.Body.LastOrDefault().IsUnconditionalControlFlow())
							throw new Exception("Basic block has to end with unconditional control flow. \n" + childAsBB.ToString());
						if (context.CalculateILSpans) {
							if (flatBody.Count > 0)
								flatBody[flatBody.Count - 1].EndILSpans.AddRange(childAsBB.ILSpans);
							else
								block.ILSpans.AddRange(childAsBB.ILSpans);
						}
						foreach (var c in childAsBB.GetChildren())
							flatBody.Add(c);
						prevChildAsBB = childAsBB;
					} else {
						flatBody.Add(child);
						if (context.CalculateILSpans && prevChildAsBB != null)
							child.ILSpans.AddRange(prevChildAsBB.EndILSpans);
						prevChildAsBB = null;
					}
				}
				block.EntryGoto = null;
				block.Body = flatBody;
				if (context.CalculateILSpans && prevChildAsBB != null)
					block.EndILSpans.AddRange(prevChildAsBB.EndILSpans);
			} else if (node is ILExpression) {
				// Optimization - no need to check expressions
			} else if (node != null) {
				// Recursively find all ILBlocks
				foreach(ILNode child in node.GetChildren()) {
					FlattenBasicBlocks(child);
				}
			}
		}
		
		/// <summary>
		/// Replace endfinally with jump to the end of the finally block
		/// </summary>
		void RemoveEndFinally(ILBlock method)
		{
			// Go thought the list in reverse so that we do the nested blocks first
			var list = method.GetSelfAndChildrenRecursive<ILTryCatchBlock>(tc => tc.FinallyBlock != null);
			for (int j = list.Count - 1; j >= 0; j--) {
				var tryCatch = list[j];
				ILLabel label = new ILLabel() { Name = "EndFinally_" + (nextLabelIndex++).ToString() };
				tryCatch.FinallyBlock.Body.Add(label);
				foreach(var block in tryCatch.FinallyBlock.GetSelfAndChildrenRecursive<ILBlock>(Optimize_List_ILBlock)) {
					for (int i = 0; i < block.Body.Count; i++) {
						if (block.Body[i].Match(ILCode.Endfinally)) {
							block.Body[i] = new ILExpression(ILCode.Br, label).WithILSpansFrom(context.CalculateILSpans, block.Body[i]);
						}
					}
				}
			}
		}
		
		/// <summary>
		/// Reduce the nesting of conditions.
		/// It should be done on flat data that already had most gotos removed
		/// </summary>
		void ReduceIfNesting(ILNode node)
		{
			var list = Optimize_List_ILNode;
			list.Clear();
			list.Add(node);
			while (list.Count > 0) {
				node = list[list.Count - 1];
				list.RemoveAt(list.Count - 1);

				ILBlock block = node as ILBlock;
				if (block != null) {
					for (int i = 0; i < block.Body.Count; i++) {
						ILCondition cond = block.Body[i] as ILCondition;
						if (cond != null) {
							bool trueExits = cond.TrueBlock.Body.LastOrDefault().IsUnconditionalControlFlow();
							bool falseExits = cond.FalseBlock.Body.LastOrDefault().IsUnconditionalControlFlow();

							if (trueExits) {
								// Move the false block after the condition
								block.Body.InsertRange(i + 1, cond.FalseBlock.GetChildren());
								cond.FalseBlock = new ILBlock(CodeBracesRangeFlags.ConditionalBraces);
							}
							else if (falseExits) {
								// Move the true block after the condition
								block.Body.InsertRange(i + 1, cond.TrueBlock.GetChildren());
								cond.TrueBlock = new ILBlock(CodeBracesRangeFlags.ConditionalBraces);
							}

							// Eliminate empty true block
							if (!cond.TrueBlock.HasChildren && cond.FalseBlock.HasChildren) {
								// Swap bodies
								ILBlock tmp = cond.TrueBlock;
								cond.TrueBlock = cond.FalseBlock;
								cond.FalseBlock = tmp;
								cond.Condition = new ILExpression(ILCode.LogicNot, null, cond.Condition);
							}
						}
					}
				}

				// We are changing the number of blocks so we use plain old recursion to get all blocks
				foreach (ILNode child in node.GetChildren()) {
					if (child != null && !(child is ILExpression))
						list.Add(child);//TODO: For same order, need to reverse it
				}
			}
		}
		
		void RecombineVariables(ILBlock method)
		{
			// Recombine variables that were split when the ILAst was created
			// This ensures that a single IL variable is a single C# variable (gets assigned only one name)
			// The DeclareVariables transformation might then split up the C# variable again if it is used indendently in two separate scopes.
			Optimize_Dict_Local_ILVariable.Clear();
			ReplaceVariables(
				method,
				delegate(ILVariable v) {
					if (v.OriginalVariable == null)
						return v;
					ILVariable combinedVariable;
					if (!Optimize_Dict_Local_ILVariable.TryGetValue(v.OriginalVariable, out combinedVariable)) {
						Optimize_Dict_Local_ILVariable.Add(v.OriginalVariable, v);
						combinedVariable = v;
					}
					return combinedVariable;
				});
		}

		void HandlePointerArithmetic(ILNode method)
		{
			foreach (ILExpression expr in method.GetSelfAndChildrenRecursive<ILExpression>(this.Optimize_List_ILExpression2)) {
				List<ILExpression> args = expr.Arguments;
				switch (expr.Code) {
					case ILCode.Localloc:
					{
						// Disable unstable code. Will throw when type is void* because it gets
						// changed to a byte*.
#if false
						PtrSig type = expr.InferredType as PtrSig;
						if (type != null) {
							ILExpression arg0 = args[0];
							ILExpression expr2 = expr;
							DivideOrMultiplyBySize(ref expr2, ref arg0, type.Next, true);
							// expr shouldn't change
							if (expr2 != expr)
								throw new InvalidOperationException();
							args[0] = arg0;
						}
#endif
						break;
					}
					case ILCode.Add:
					case ILCode.Add_Ovf:
					case ILCode.Add_Ovf_Un:
					{
						ILExpression arg0 = args[0];
						ILExpression arg1 = args[1];
						if (expr.InferredType is PtrSig) {
							if (arg0.ExpectedType is PtrSig) {
								DivideOrMultiplyBySize(ref arg0, ref arg1, ((PtrSig)expr.InferredType).Next, true);
							} else if (arg1.ExpectedType is PtrSig)
								DivideOrMultiplyBySize(ref arg1, ref arg0, ((PtrSig)expr.InferredType).Next, true);
						}
						args[0] = arg0;
						args[1] = arg1;
						break;
					}
					case ILCode.Sub:
					case ILCode.Sub_Ovf:
					case ILCode.Sub_Ovf_Un:
					{
						ILExpression arg0 = args[0];
						ILExpression arg1 = args[1];
						if (expr.InferredType is PtrSig) {
							if (arg0.ExpectedType is PtrSig && !(arg1.InferredType is PtrSig))
								DivideOrMultiplyBySize(ref arg0, ref arg1, ((PtrSig)expr.InferredType).Next, true);
						}
						args[0] = arg0;
						args[1] = arg1;
						break;
					}
					case ILCode.Conv_I8:
					{
						ILExpression arg0 = args[0];
						// conv.i8(div:intptr(p0 - p1))
						if (arg0.Code == ILCode.Div && arg0.InferredType.RemovePinnedAndModifiers().GetElementType() == ElementType.I)
						{
							ILExpression dividend = arg0.Arguments[0];
							if (dividend.InferredType.RemovePinnedAndModifiers().GetElementType() == ElementType.I &&
								(dividend.Code == ILCode.Sub || dividend.Code == ILCode.Sub_Ovf || dividend.Code == ILCode.Sub_Ovf_Un))
							{
								PtrSig pointerType0 = dividend.Arguments[0].InferredType as PtrSig;
								PtrSig pointerType1 = dividend.Arguments[1].InferredType as PtrSig;

								if (pointerType0 != null && pointerType1 != null) {
									if (pointerType0.Next.RemovePinnedAndModifiers().GetElementType() == ElementType.Void ||
										!new SigComparer().Equals(pointerType0.Next, pointerType1.Next)) {
										pointerType0 = pointerType1 = new PtrSig(corLib.Byte);
										dividend.Arguments[0] = Cast(dividend.Arguments[0], pointerType0);
										dividend.Arguments[1] = Cast(dividend.Arguments[1], pointerType1);
									}

									DivideOrMultiplyBySize(ref dividend, ref arg0, pointerType0.Next, false);
									// dividend shouldn't change
									if (args[0].Arguments[0] != dividend)
										throw new InvalidOperationException();
								}
							}
						}
						args[0] = arg0;
						break;
					}
				}
			}
		}

		ILExpression UnwrapIntPtrCast(ILExpression expr)
		{
			if (expr.Code != ILCode.Conv_I && expr.Code != ILCode.Conv_U)
				return expr;

			ILExpression arg = expr.Arguments[0];
			switch (arg.InferredType.GetElementType()) {
				case ElementType.U1:
				case ElementType.I1:
				case ElementType.U2:
				case ElementType.I2:
				case ElementType.U4:
				case ElementType.I4:
				case ElementType.U8:
				case ElementType.I8:
					if (context.CalculateILSpans)
						arg.ILSpans.AddRange(expr.ILSpans);
					return arg;
			}

			return expr;
		}

		static ILExpression Cast(ILExpression expr, TypeSig type)
		{
			return new ILExpression(ILCode.Castclass, type.ToTypeDefOrRef(), expr)
			{
				InferredType = type,
				ExpectedType = type
			};
		}

		void DivideOrMultiplyBySize(ref ILExpression pointerExpr, ref ILExpression adjustmentExpr, TypeSig elementType, bool divide)
		{
			adjustmentExpr = UnwrapIntPtrCast(adjustmentExpr);

			ILExpression sizeOfExpression;
			switch (TypeAnalysis.GetInformationAmount(elementType)) {
				case 0: // System.Void
					pointerExpr = Cast(pointerExpr, new PtrSig(corLib.Byte));
					goto case 1;
				case 1:
				case 8:
					sizeOfExpression = new ILExpression(ILCode.Ldc_I4, 1);
					break;
				case 16:
					sizeOfExpression = new ILExpression(ILCode.Ldc_I4, 2);
					break;
				case 32:
					sizeOfExpression = new ILExpression(ILCode.Ldc_I4, 4);
					break;
				case 64:
					sizeOfExpression = new ILExpression(ILCode.Ldc_I4, 8);
					break;
				default:
					sizeOfExpression = new ILExpression(ILCode.Sizeof, elementType.ToTypeDefOrRef());
					break;
			}

			if (divide && (adjustmentExpr.Code == ILCode.Mul || adjustmentExpr.Code == ILCode.Mul_Ovf || adjustmentExpr.Code == ILCode.Mul_Ovf_Un) ||
				!divide && (adjustmentExpr.Code == ILCode.Div || adjustmentExpr.Code == ILCode.Div_Un)) {
				ILExpression mulArg = adjustmentExpr.Arguments[1];
				if (mulArg.Code == sizeOfExpression.Code && sizeOfExpression.Operand.Equals(mulArg.Operand)) {
					var arg = adjustmentExpr.Arguments[0];
					if (context.CalculateILSpans) {
						arg.ILSpans.AddRange(adjustmentExpr.ILSpans);
						mulArg.AddSelfAndChildrenRecursiveILSpans(arg.ILSpans);
					}
					adjustmentExpr = UnwrapIntPtrCast(arg);
					return;
				}
			}

			if (adjustmentExpr.Code == sizeOfExpression.Code) {
				if (sizeOfExpression.Operand.Equals(adjustmentExpr.Operand)) {
					adjustmentExpr = new ILExpression(ILCode.Ldc_I4, 1).WithILSpansFrom(context.CalculateILSpans, adjustmentExpr);
					return;
				}

				if (adjustmentExpr.Code == ILCode.Ldc_I4) {
					int offsetInBytes = (int)adjustmentExpr.Operand;
					int elementSize = (int)sizeOfExpression.Operand;

					if (offsetInBytes % elementSize != 0) {
						pointerExpr = Cast(pointerExpr, new PtrSig(corLib.Byte));
						return;
					}

					adjustmentExpr.Operand = offsetInBytes / elementSize;
					return;
				}
			}

			if (!(sizeOfExpression.Code == ILCode.Ldc_I4 && (int)sizeOfExpression.Operand == 1))
				adjustmentExpr = new ILExpression(divide ? ILCode.Div_Un : ILCode.Mul, null, adjustmentExpr, sizeOfExpression);
		}
		
		public static void ReplaceVariables(ILNode node, Func<ILVariable, ILVariable> variableMapping)
		{
			ILExpression expr = node as ILExpression;
			if (expr != null) {
				ILVariable v = expr.Operand as ILVariable;
				if (v != null)
					expr.Operand = variableMapping(v);
				foreach (ILExpression child in expr.Arguments)
					ReplaceVariables(child, variableMapping);
			} else {
				var catchBlock = node as ILTryCatchBlock.CatchBlockBase;
				if (catchBlock != null && catchBlock.ExceptionVariable != null) {
					catchBlock.ExceptionVariable = variableMapping(catchBlock.ExceptionVariable);
				}
				
				foreach (ILNode child in node.GetChildren())
					ReplaceVariables(child, variableMapping);
			}
		}
	}
	
	public static class ILAstOptimizerExtensionMethods
	{
		/// <summary>
		/// Perform one pass of a given optimization on this block.
		/// This block must consist of only basicblocks.
		/// </summary>
		public static bool RunOptimization(this ILBlock block, Func<List<ILNode>, ILBasicBlock, int, bool> optimization)
		{
			bool modified = false;
			List<ILNode> body = block.Body;
			for (int i = body.Count - 1; i >= 0; i--) {
				if (i < body.Count && optimization(body, (ILBasicBlock)body[i], i)) {
					modified = true;
				}
			}
			return modified;
		}
		
		public static bool RunOptimization(this ILBlock block, Func<ILBlockBase, List<ILNode>, ILExpression, int, bool> optimization)
		{
			bool modified = false;
			foreach (ILBasicBlock bb in block.Body) {
				for (int i = bb.Body.Count - 1; i >= 0; i--) {
					ILExpression expr = bb.Body.ElementAtOrDefault(i) as ILExpression;
					if (expr != null && optimization(bb, bb.Body, expr, i)) {
						modified = true;
					}
				}
			}
			return modified;
		}
		
		public static bool IsConditionalControlFlow(this ILNode node)
		{
			ILExpression expr = node as ILExpression;
			return expr != null && expr.Code.IsConditionalControlFlow();
		}
		
		public static bool IsUnconditionalControlFlow(this ILNode node)
		{
			ILExpression expr = node as ILExpression;
			return expr != null && expr.Code.IsUnconditionalControlFlow();
		}
		
		/// <summary>
		/// The expression has no effect on the program and can be removed
		/// if its return value is not needed.
		/// </summary>
		public static bool HasNoSideEffects(this ILExpression expr)
		{
			// Remember that if expression can throw an exception, it is a side effect
			
			switch(expr.Code) {
				case ILCode.Ldloc:
				case ILCode.Ldloca:
				case ILCode.Ldstr:
				case ILCode.Ldnull:
				case ILCode.Ldc_I4:
				case ILCode.Ldc_I8:
				case ILCode.Ldc_R4:
				case ILCode.Ldc_R8:
				case ILCode.Ldc_Decimal:
					return true;
				default:
					return false;
			}
		}
		
		public static bool IsStoreToArray(this ILCode code)
		{
			switch (code) {
				case ILCode.Stelem:
				case ILCode.Stelem_I:
				case ILCode.Stelem_I1:
				case ILCode.Stelem_I2:
				case ILCode.Stelem_I4:
				case ILCode.Stelem_I8:
				case ILCode.Stelem_R4:
				case ILCode.Stelem_R8:
				case ILCode.Stelem_Ref:
					return true;
				default:
					return false;
			}
		}
		
		public static bool IsLoadFromArray(this ILCode code)
		{
			switch (code) {
				case ILCode.Ldelem:
				case ILCode.Ldelem_I:
				case ILCode.Ldelem_I1:
				case ILCode.Ldelem_I2:
				case ILCode.Ldelem_I4:
				case ILCode.Ldelem_I8:
				case ILCode.Ldelem_U1:
				case ILCode.Ldelem_U2:
				case ILCode.Ldelem_U4:
				case ILCode.Ldelem_R4:
				case ILCode.Ldelem_R8:
				case ILCode.Ldelem_Ref:
					return true;
				default:
					return false;
			}
		}
		
		/// <summary>
		/// Can the expression be used as a statement in C#?
		/// </summary>
		public static bool CanBeExpressionStatement(this ILExpression expr)
		{
			switch(expr.Code) {
				case ILCode.Call:
				case ILCode.Callvirt:
					// property getters can't be expression statements, but all other method calls can be
					IMethod mr = (IMethod)expr.Operand;
					return !mr.Name.StartsWith("get_", StringComparison.Ordinal);
				case ILCode.CallReadOnlySetter:
				case ILCode.CallSetter:
				case ILCode.CallvirtSetter:
				case ILCode.Newobj:
				case ILCode.Newarr:
				case ILCode.Stloc:
				case ILCode.Stobj:
				case ILCode.Stsfld:
				case ILCode.Stfld:
				case ILCode.Stind_Ref:
				case ILCode.Stelem:
				case ILCode.Stelem_I:
				case ILCode.Stelem_I1:
				case ILCode.Stelem_I2:
				case ILCode.Stelem_I4:
				case ILCode.Stelem_I8:
				case ILCode.Stelem_R4:
				case ILCode.Stelem_R8:
				case ILCode.Stelem_Ref:
					return true;
				default:
					return false;
			}
		}

		public static ILExpression WithILSpansFrom(this ILExpression expr, bool calculateILSpans, ILNode node)
		{
			if (!calculateILSpans)
				return expr;
			long index = 0;
			bool done = false;
			for (;;) {
				var b = node.GetAllILSpans(ref index, ref done);
				if (done)
					break;
				expr.ILSpans.Add(b);
			}
			return expr;
		}
		
		public static ILNode[] RemoveTail(this List<ILNode> body, params ILCode[] codes)
		{
			for (int i = 0; i < codes.Length; i++) {
				if (((ILExpression)body[body.Count - codes.Length + i]).Code != codes[i])
					throw new Exception("Tailing code does not match expected.");
			}
			var list = new ILNode[codes.Length];
			for (int i = 0; i < codes.Length; i++)
				list[i] = body[body.Count - codes.Length + i];
			body.RemoveRange(body.Count - codes.Length, codes.Length);
			return list;
		}
		
		public static V GetOrDefault<K,V>(this Dictionary<K, V> dict, K key)
		{
			V ret;
			dict.TryGetValue(key, out ret);
			return ret;
		}
		
		public static void RemoveOrThrow<T>(this ICollection<T> collection, T item)
		{
			if (!collection.Remove(item))
				throw new Exception("The item was not found in the collection");
		}
		
		public static void RemoveOrThrow<K,V>(this Dictionary<K,V> collection, K key)
		{
			if (!collection.Remove(key))
				throw new Exception("The key was not found in the dictionary");
		}
		
		public static bool ContainsReferenceTo(this ILExpression expr, ILVariable v)
		{
			if (expr.Operand == v)
				return true;
			foreach (var arg in expr.Arguments) {
				if (ContainsReferenceTo(arg, v))
					return true;
			}
			return false;
		}
	}
}
