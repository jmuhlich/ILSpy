﻿// Copyright (c) 2018 Daniel Grunwald
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
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using dnlib.DotNet;
using ICSharpCode.Decompiler.Semantics;
using ICSharpCode.Decompiler.Util;
using CallingConvention = dnlib.DotNet.CallingConvention;

namespace ICSharpCode.Decompiler.TypeSystem.Implementation
{
	sealed class MetadataMethod : IMethod
	{
		readonly MetadataModule module;
		readonly MethodDef handle;

		// eagerly loaded fields:
		readonly MethodAttributes attributes;
		readonly SymbolKind symbolKind;
		readonly ITypeParameter[] typeParameters;
		readonly IHasSemantic accessorOwner;
		public bool IsExtensionMethod { get; }

		// lazy-loaded fields:
		ITypeDefinition declaringType;
		string name;
		IParameter[] parameters;
		IType returnType;

		internal MetadataMethod(MetadataModule module, MethodDef handle)
		{
			Debug.Assert(module != null);
			Debug.Assert(handle != null);
			this.module = module;
			this.handle = handle;
			var metadata = module.metadata;
			this.attributes = handle.Attributes;

			this.symbolKind = SymbolKind.Method;
			var owner = FindOwner(handle);
			if (handle.SemanticsAttributes != 0 && owner != null) {
				this.symbolKind = SymbolKind.Accessor;
				this.accessorOwner = owner;
			} else if ((attributes & (MethodAttributes.SpecialName | MethodAttributes.RTSpecialName)) != 0) {
				string name = this.Name;
				if (name == ".cctor" || name == ".ctor")
					this.symbolKind = SymbolKind.Constructor;
				else if (name.StartsWith("op_", StringComparison.Ordinal))
					this.symbolKind = SymbolKind.Operator;
			}
			this.typeParameters = MetadataTypeParameter.Create(module, this, handle.GenericParameters);
			this.IsExtensionMethod = (attributes & MethodAttributes.Static) == MethodAttributes.Static
									 && (module.TypeSystemOptions & TypeSystemOptions.ExtensionMethods) == TypeSystemOptions.ExtensionMethods
									 && handle.CustomAttributes.HasKnownAttribute(metadata, KnownAttribute.Extension);
		}

		private static IHasSemantic FindOwner(MethodDef handle){
			IHasSemantic owner = null;
			var comparer = MethodEqualityComparer.CompareDeclaringTypes;
			if (handle.IsGetter || handle.IsSetter) {
				foreach (var property in handle.DeclaringType.Properties) {
					if (handle.IsGetter && comparer.Equals(property.GetMethod, handle)) {
						owner = property;
						break;
					}
					if (handle.IsSetter && comparer.Equals(property.SetMethod, handle)) {
						owner = property;
						break;
					}
				}
			}

			if (handle.IsAddOn || handle.IsRemoveOn || handle.IsFire) {
				foreach (var eventDef in handle.DeclaringType.Events) {
					if (handle.IsAddOn && eventDef.AddMethod == handle) {
						owner = eventDef;
						break;
					}
					if (handle.IsRemoveOn && eventDef.RemoveMethod == handle) {
						owner = eventDef;
						break;
					}
					if (handle.IsFire && eventDef.InvokeMethod == handle) {
						owner = eventDef;
						break;
					}
				}
			}

			return owner;
		}

		public dnlib.DotNet.IMDTokenProvider MetadataToken => handle;

		public override string ToString()
		{
			return $"{handle.MDToken.Raw:X8} {DeclaringType?.ReflectionName}.{Name}";
		}

		public string Name {
			get {
				string name = LazyInit.VolatileRead(ref this.name);
				if (name != null)
					return name;
				return LazyInit.GetOrSet(ref this.name, handle.Name.String);
			}
		}

		public IReadOnlyList<ITypeParameter> TypeParameters => typeParameters;
		IReadOnlyList<IType> IMethod.TypeArguments => typeParameters;

		public SymbolKind SymbolKind => symbolKind;
		public bool IsConstructor => symbolKind == SymbolKind.Constructor;
		public bool IsDestructor => symbolKind == SymbolKind.Destructor;
		public bool IsOperator => symbolKind == SymbolKind.Operator;
		public bool IsAccessor => symbolKind == SymbolKind.Accessor;
		public bool HasBody => handle.HasBody;

		public IMember AccessorOwner {
			get {
				if (accessorOwner is null)
					return null;
				if (accessorOwner is PropertyDef propertyDef)
					return module.GetDefinition(propertyDef);
				else if (accessorOwner is EventDef eventDef)
					return module.GetDefinition(eventDef);
				else
					return null;
			}
		}

		#region Signature (ReturnType + Parameters)
		public IReadOnlyList<IParameter> Parameters {
			get {
				var parameters = LazyInit.VolatileRead(ref this.parameters);
				if (parameters != null)
					return parameters;

				var genericContext = new GenericContext(DeclaringType.TypeParameters, this.TypeParameters);
				List<IParameter> param = new List<IParameter>();
				foreach (Parameter par in handle.Parameters) {
					if (par.IsNormalMethodParameter) {
						var deco = par.Type.DecodeSignature(module.TypeProvider, genericContext);
						var parameterType = ApplyAttributeTypeVisitor.ApplyAttributesToType(
							deco, module.Compilation,
							par.ParamDef, module.metadata, module.TypeSystemOptions);
						param.Add(new MetadataParameter(module, this, parameterType, par));
					}
				}

				if (handle.CallingConvention == CallingConvention.VarArg) {
					param.Add(new DefaultParameter(SpecialType.ArgList, name: string.Empty, this));
				}

				return LazyInit.GetOrSet(ref this.parameters, param.ToArray());
			}
		}

		public IType ReturnType {
			get {
				var returnType = LazyInit.VolatileRead(ref this.returnType);
				if (returnType != null)
					return returnType;

				var genericContext = new GenericContext(DeclaringType.TypeParameters, this.TypeParameters);
				var sig = handle.ReturnType.DecodeSignature(module.TypeProvider, genericContext);

				var retType = ApplyAttributeTypeVisitor.ApplyAttributesToType(sig,
					module.Compilation, handle.Parameters.ReturnParameter.ParamDef, module.metadata, module.TypeSystemOptions);

				return LazyInit.GetOrSet(ref this.returnType, retType);
			}
		}
		#endregion

		public bool IsExplicitInterfaceImplementation {
			get {
				if (Name.IndexOf('.') < 0)
					return false;
				return handle.HasOverrides;
			}
		}

		public IEnumerable<IMember> ExplicitlyImplementedInterfaceMembers {
			get {
				foreach (MethodOverride handleOverride in handle.Overrides) {
					yield return module.ResolveMethod(handleOverride.MethodDeclaration,
						new GenericContext(this.DeclaringType.TypeParameters));
				}
			}
		}

		IMember IMember.MemberDefinition => this;
		IMethod IMethod.ReducedFrom => null;
		TypeParameterSubstitution IMember.Substitution => TypeParameterSubstitution.Identity;

		public ITypeDefinition DeclaringTypeDefinition {
			get {
				var declType = LazyInit.VolatileRead(ref this.declaringType);
				if (declType != null) {
					return declType;
				} else {
					return LazyInit.GetOrSet(ref this.declaringType,
						module.GetDefinition(handle.DeclaringType));
				}
			}
		}

		public IType DeclaringType => DeclaringTypeDefinition;

		public IModule ParentModule => module;
		public ICompilation Compilation => module.Compilation;

		#region Attributes
		IType FindInteropType(string name)
		{
			return module.Compilation.FindType(new TopLevelTypeName(
				"System.Runtime.InteropServices", name
			));
		}

		public IEnumerable<IAttribute> GetAttributes()
		{
			var b = new AttributeListBuilder(module);

			MethodImplAttributes implAttributes = handle.ImplAttributes & ~MethodImplAttributes.CodeTypeMask;

			#region DllImportAttribute

			var info = handle.ImplMap;
			if ((attributes & MethodAttributes.PinvokeImpl) == MethodAttributes.PinvokeImpl && info != null && info.Module != null) {
				var dllImport = new AttributeBuilder(module, KnownAttribute.DllImport);
				dllImport.AddFixedArg(KnownTypeCode.String, info.Module.Name.String);

				if (info.IsBestFitDisabled)
					dllImport.AddNamedArg("BestFitMapping", KnownTypeCode.Boolean, false);
				if (info.IsBestFitEnabled)
					dllImport.AddNamedArg("BestFitMapping", KnownTypeCode.Boolean, true);

				System.Runtime.InteropServices.CallingConvention callingConvention;
				switch (info.CallConv) {
					case 0:
						Debug.WriteLine($"P/Invoke calling convention not set on: {this}");
						callingConvention = 0;
						break;
					case PInvokeAttributes.CallConvCdecl:
						callingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl;
						break;
					case PInvokeAttributes.CallConvFastcall:
						callingConvention = System.Runtime.InteropServices.CallingConvention.FastCall;
						break;
					case PInvokeAttributes.CallConvStdcall:
						callingConvention = System.Runtime.InteropServices.CallingConvention.StdCall;
						break;
					case PInvokeAttributes.CallConvThiscall:
						callingConvention = System.Runtime.InteropServices.CallingConvention.ThisCall;
						break;
					case PInvokeAttributes.CallConvWinapi:
						callingConvention = System.Runtime.InteropServices.CallingConvention.Winapi;
						break;
					default:
						throw new NotSupportedException("unknown calling convention");
				}
				if (callingConvention != System.Runtime.InteropServices.CallingConvention.Winapi) {
					var callingConventionType = FindInteropType(nameof(System.Runtime.InteropServices.CallingConvention));
					dllImport.AddNamedArg("CallingConvention", callingConventionType, (int)callingConvention);
				}

				CharSet charSet = CharSet.None;
				switch (info.CharSet) {
					case PInvokeAttributes.CharSetAnsi:
						charSet = CharSet.Ansi;
						break;
					case PInvokeAttributes.CharSetAuto:
						charSet = CharSet.Auto;
						break;
					case PInvokeAttributes.CharSetUnicode:
						charSet = CharSet.Unicode;
						break;
				}
				if (charSet != CharSet.None) {
					var charSetType = FindInteropType(nameof(CharSet));
					dllImport.AddNamedArg("CharSet", charSetType, (int)charSet);
				}

				if (!UTF8String.IsNullOrEmpty(info.Name) && info.Name != handle.Name) {
					dllImport.AddNamedArg("EntryPoint", KnownTypeCode.String, info.Name.String);
				}

				if (info.IsNoMangle) {
					dllImport.AddNamedArg("ExactSpelling", KnownTypeCode.Boolean, true);
				}

				if ((implAttributes & MethodImplAttributes.PreserveSig) == MethodImplAttributes.PreserveSig) {
					implAttributes &= ~MethodImplAttributes.PreserveSig;
				} else {
					dllImport.AddNamedArg("PreserveSig", KnownTypeCode.Boolean, true);
				}

				if (info.SupportsLastError)
					dllImport.AddNamedArg("SetLastError", KnownTypeCode.Boolean, true);

				if (info.IsThrowOnUnmappableCharDisabled)
					dllImport.AddNamedArg("ThrowOnUnmappableChar", KnownTypeCode.Boolean, false);
				if (info.IsThrowOnUnmappableCharEnabled)
					dllImport.AddNamedArg("ThrowOnUnmappableChar", KnownTypeCode.Boolean, true);

				b.Add(dllImport.Build());
			}
			#endregion

			#region PreserveSigAttribute
			if (implAttributes == MethodImplAttributes.PreserveSig) {
				b.Add(KnownAttribute.PreserveSig);
				implAttributes = 0;
			}
			#endregion

			#region MethodImplAttribute
			if (implAttributes != 0) {
				b.Add(KnownAttribute.MethodImpl,
					new TopLevelTypeName("System.Runtime.CompilerServices", nameof(MethodImplOptions)),
					(int)implAttributes
				);
			}
			#endregion

			b.Add(handle.CustomAttributes);
			b.AddSecurityAttributes(handle.DeclSecurities);

			return b.Build();
		}
		#endregion

		#region Return type attributes
		public IEnumerable<IAttribute> GetReturnTypeAttributes()
		{
			var b = new AttributeListBuilder(module);
			if (handle.Parameters.ReturnParameter.HasParamDef) {
				b.AddMarshalInfo(handle.Parameters.ReturnParameter.ParamDef.MarshalType);
				b.Add(handle.Parameters.ReturnParameter.ParamDef.CustomAttributes);
			}
			return b.Build();
		}
		#endregion

		public Accessibility Accessibility => GetAccessibility(attributes);

		internal static Accessibility GetAccessibility(MethodAttributes attr)
		{
			switch (attr & MethodAttributes.MemberAccessMask) {
				case MethodAttributes.Public:
					return Accessibility.Public;
				case MethodAttributes.Assembly:
					return Accessibility.Internal;
				case MethodAttributes.Private:
					return Accessibility.Private;
				case MethodAttributes.Family:
					return Accessibility.Protected;
				case MethodAttributes.FamANDAssem:
					return Accessibility.ProtectedAndInternal;
				case MethodAttributes.FamORAssem:
					return Accessibility.ProtectedOrInternal;
				default:
					return Accessibility.None;
			}
		}

		public bool IsStatic => (attributes & MethodAttributes.Static) != 0;
		public bool IsAbstract => (attributes & MethodAttributes.Abstract) != 0;
		public bool IsSealed => (attributes & (MethodAttributes.Abstract | MethodAttributes.Final | MethodAttributes.NewSlot | MethodAttributes.Static)) == MethodAttributes.Final;
		public bool IsVirtual => (attributes & (MethodAttributes.Abstract | MethodAttributes.Virtual | MethodAttributes.NewSlot | MethodAttributes.Final)) == (MethodAttributes.Virtual | MethodAttributes.NewSlot);
		public bool IsOverride => (attributes & (MethodAttributes.NewSlot | MethodAttributes.Virtual)) == MethodAttributes.Virtual;
		public bool IsOverridable
			=> (attributes & (MethodAttributes.Abstract | MethodAttributes.Virtual)) != 0
			&& (attributes & MethodAttributes.Final) == 0;

		public string FullName => $"{DeclaringType?.FullName}.{Name}";
		public string ReflectionName => $"{DeclaringType?.ReflectionName}.{Name}";
		public string Namespace => DeclaringType?.Namespace ?? string.Empty;

		public override bool Equals(object obj)
		{
			if (obj is MetadataMethod m) {
				return handle == m.handle && module.PEFile == m.module.PEFile;
			}
			return false;
		}

		public override int GetHashCode()
		{
			return 0x5a00d671 ^ module.PEFile.GetHashCode() ^ handle.GetHashCode();
		}

		bool IMember.Equals(IMember obj, TypeVisitor typeNormalization)
		{
			return Equals(obj);
		}

		public IMethod Specialize(TypeParameterSubstitution substitution)
		{
			return SpecializedMethod.Create(this, substitution);
		}

		IMember IMember.Specialize(TypeParameterSubstitution substitution)
		{
			return SpecializedMethod.Create(this, substitution);
		}
	}
}
