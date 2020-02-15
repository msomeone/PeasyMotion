//***************************************************************************
//
//    Copyright (c) Microsoft Corporation. All rights reserved.
//    This code is licensed under the Visual Studio SDK license terms.
//    THIS CODE IS PROVIDED *AS IS* WITHOUT WARRANTY OF
//    ANY KIND, EITHER EXPRESS OR IMPLIED, INCLUDING ANY
//    IMPLIED WARRANTIES OF FITNESS FOR A PARTICULAR
//    PURPOSE, MERCHANTABILITY, OR NON-INFRINGEMENT.
//
//***************************************************************************

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Utilities;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text;
using System.Windows.Media;

namespace PeasyMotion
{
/*
    internal class ToDoTag : IGlyphTag { }
    internal class ToDoTagger : ITagger<ToDoTag>
    {
        IEnumerable<ITagSpan<ToDoTag>> ITagger<ToDoTag>.GetTags(NormalizedSnapshotSpanCollection spans) { return null; }

        public event EventHandler<SnapshotSpanEventArgs> TagsChanged { add {} remove {} }
    }

    class ToDoClassifier : IClassifier
    {
        internal ToDoClassifier() { }

        public IList<ClassificationSpan> GetClassificationSpans(SnapshotSpan span) => null;// new List<ClassificationSpan>();

        public event EventHandler<ClassificationChangedEventArgs> ClassificationChanged { add {} remove {} }
    }
    [Export(typeof(IClassifierProvider))]
    [ContentType("text")]
    internal class ToDoClassifierProvider : IClassifierProvider
    {
        [Export(typeof(ClassificationTypeDefinition))]
        [Name("PeasyMotionColors")]
        internal ClassificationTypeDefinition ToDoClassificationType = null;

        [Import]
        internal IClassificationTypeRegistryService ClassificationRegistry = null;

        [Import]
        internal IBufferTagAggregatorFactoryService _tagAggregatorFactory = null;

        public IClassifier GetClassifier(ITextBuffer buffer)
        {
            //IClassificationType classificationType = ClassificationRegistry.GetClassificationType("PeasyMotionColors");
            //_ = _tagAggregatorFactory.CreateTagAggregator<ToDoTag>(buffer);
            return null;
        }
    }*/

    /// <summary>
    /// Set the display values for the classification
    /// </summary>
    //[ClassificationType(ClassificationTypeNames = "PeasyMotionColors")]
    //[Order(After = Priority.High)]
    [Export(typeof(EditorFormatDefinition))]
    [Name("PeasyMotionJumplabel")]
    [UserVisible(true)]
    internal sealed class PeasyMotionJumplabelFormatDef : ClassificationFormatDefinition
    {
        public const string FMT_NAME = "PeasyMotionJumplabel";
        public PeasyMotionJumplabelFormatDef()
        {
            DisplayName = "PeasyMotion Jump label 3"; //human readable version of the name
            BackgroundOpacity = 1;
            BackgroundColor = System.Windows.Media.Colors.White; //GeneralOptions.Instance.JumpLabelBackgroundColorMediaColor;
            ForegroundColor = System.Windows.Media.Colors.Black; //GeneralOptions.Instance.JumpLabelForegroundColorMediaColor;
        }
    }
}
