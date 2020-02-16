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
    /// <summary>
    // Fake format definition, we do not add any classifiers. All we want to do is add label color option into Fonts And Colors TextEditor category
    // That is why we provide one format Definition.
    /// </summary>
    [Export(typeof(EditorFormatDefinition))]
    [Name("PeasyMotionJumpLabelFirstMotion")]
    [UserVisible(true)]
    internal sealed class JumpLabelFirstMotionFormatDef : ClassificationFormatDefinition
    {
        public const string FMT_NAME = "PeasyMotionJumpLabelFirstMotion";
        public JumpLabelFirstMotionFormatDef()
        {
            DisplayName = "PeasyMotion First Motion Jump label color"; //human readable version of the name
            BackgroundOpacity = 1;
            BackgroundColor = System.Windows.Media.Colors.Black;
            ForegroundColor = System.Windows.Media.Colors.LightGray;
        }
    }

    [Export(typeof(EditorFormatDefinition))]
    [Name("PeasyMotionJumpLabelFinalMotion")]
    [UserVisible(true)]
    internal sealed class JumpLabelFinalMotionFormatDef : ClassificationFormatDefinition
    {
        public const string FMT_NAME = "PeasyMotionJumpLabelFinalMotion";
        public JumpLabelFinalMotionFormatDef()
        {
            DisplayName = "PeasyMotion Final Motion Jump label color"; //human readable version of the name
            BackgroundOpacity = 1;
            BackgroundColor = System.Windows.Media.Colors.LightGray;
            ForegroundColor = System.Windows.Media.Colors.Red;
        }
    }
}
