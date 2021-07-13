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

using System.Collections.Generic;
using System.Threading;
using dnlib.DotNet;
using ICSharpCode.Decompiler;

namespace ICSharpCode.ILSpy.TreeNodes
{
	class DerivedTypesEntryNode : ILSpyTreeNode, IMemberTreeNode
	{
		private readonly TypeDef type;
		private readonly ModuleDef[] assemblies;
		private readonly ThreadingSupport threading;

		public DerivedTypesEntryNode(TypeDef type, ModuleDef[] assemblies)
		{
			this.type = type;
			this.assemblies = assemblies;
			this.LazyLoading = true;
			threading = new ThreadingSupport();
		}

		public override bool ShowExpander
		{
			get { return !type.IsSealed && base.ShowExpander; }
		}

		public override object Text
		{
			get { return this.Language.TypeToString(type, true) + type.MDToken.ToSuffixString(); }
		}

		public override object Icon
		{
			get { return TypeTreeNode.GetIcon(type); }
		}

		public override FilterResult Filter(FilterSettings settings)
		{
			if (settings.ShowApiLevel == ApiVisibility.PublicOnly && !IsPublicAPI)
				return FilterResult.Hidden;
			if (settings.SearchTermMatches(type.Name)) {
				if (type.DeclaringType != null && (settings.ShowApiLevel != ApiVisibility.All || !settings.Language.ShowMember(type)))
					return FilterResult.Hidden;
				else
					return FilterResult.Match;
			} else
				return FilterResult.Recurse;
		}

		public override bool IsPublicAPI {
			get {
				switch (type.Attributes & TypeAttributes.VisibilityMask) {
					case TypeAttributes.Public:
					case TypeAttributes.NestedPublic:
					case TypeAttributes.NestedFamily:
					case TypeAttributes.NestedFamORAssem:
						return true;
					default:
						return false;
				}
			}
		}

		protected override void LoadChildren()
		{
			threading.LoadChildren(this, FetchChildren);
		}

		IEnumerable<ILSpyTreeNode> FetchChildren(CancellationToken ct)
		{
			// FetchChildren() runs on the main thread; but the enumerator will be consumed on a background thread
			return DerivedTypesTreeNode.FindDerivedTypes(type, assemblies, ct);
		}

		public override void ActivateItem(System.Windows.RoutedEventArgs e)
		{
			e.Handled = BaseTypesEntryNode.ActivateItem(this, type);
		}

		public override void Decompile(Language language, ITextOutput output, DecompilationOptions options)
		{
			language.WriteCommentLine(output, language.TypeToString(type, true));
		}

		IMemberRef IMemberTreeNode.Member => type;
	}
}
