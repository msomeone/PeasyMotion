#define MEASUREEXECTIME

using System;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Formatting;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Text.Operations;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Collections.Generic;
using System.Windows;
using Microsoft.VisualStudio.Utilities;
using Microsoft.VisualStudio.PlatformUI;

//using System.ComponentModel.Design;
//using System.Globalization;
//using System.Threading;
//using System.Windows.Forms;
//using EnvDTE;
//using EnvDTE80;
//using Microsoft.VisualStudio.Text.Classification;
//using Microsoft.VisualStudio.ComponentModelHost;
//using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio;
//using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
//using Microsoft.VisualStudio.Text.Editor;
//using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.TextManager.Interop;
//using Task = System.Threading.Tasks.Task;
//using Microsoft.VisualStudio.Text;
//using Microsoft.VisualStudio.Text.Formatting;
//using Microsoft.VisualStudio.Imaging;
using System.Runtime.InteropServices;

namespace PeasyMotion
{ 
    struct JumpToResult
    {
        public JumpToResult(
                int currentCursorPosition = -1,
                SnapshotSpan jumpLabelSpan = new SnapshotSpan(),
                IVsWindowFrame windowFrame = null
            )
        {
            this.currentCursorPosition = currentCursorPosition;
            this.jumpLabelSpan = jumpLabelSpan;
            this.windowFrame = windowFrame;
        }
        public int currentCursorPosition {get;}
        public SnapshotSpan jumpLabelSpan {get;} // contains Span of finally selected label 
        public IVsWindowFrame windowFrame {get;}
    };

    public enum JumpMode {
        InvalidMode,
        WordJump,
        SelectTextJump,
        LineJumpToWordBegining,
        LineJumpToWordEnding,
        VisibleDocuments
    }


    /// <summary>
    /// PeasyMotionEdAdornment places red boxes behind all the "a"s in the editor window
    /// </summary>
    internal sealed class PeasyMotionEdAdornment
    {
        private ITextStructureNavigator textStructureNavigator{ get; set; }
            /// <summary>
            /// The layer of the adornment.
            /// </summary>
        private readonly IAdornmentLayer layer;

        /// <summary>
        /// Text view where the adornment is created.
        /// </summary>
        public IVsTextView vsTextView;
        public IWpfTextView view;
        private VsSettings vsSettings;
        private JumpLabelUserControl.CachedSetupParams jumpLabelCachedSetupParams = new JumpLabelUserControl.CachedSetupParams();

        private readonly struct Jump
        {
            public Jump(
                    SnapshotSpan span,
                    string label,
                    JumpLabelUserControl labelAdornment,
                    IVsWindowFrame windowFrame,
                    IVsTextView windowPrimaryTextView,
                    string vanillaTabCaption
                ) 
            {
                this.span = span;
                this.label = label;
                this.labelAdornment = labelAdornment;
                this.windowFrame = windowFrame;
                this.windowPrimaryTextView = windowPrimaryTextView;
                this.vanillaTabCaption = vanillaTabCaption;
            }

            public SnapshotSpan span {get; }
            public string label {get; }
            public JumpLabelUserControl labelAdornment {get; }
            public IVsWindowFrame windowFrame { get; }
            public IVsTextView windowPrimaryTextView { get; }
            public string vanillaTabCaption { get; }
        };

        private readonly struct JumpWord
        {
            public JumpWord( 
                    int distanceToCursor,
                    Rect adornmentBounds,
                    SnapshotSpan span,
                    string text,
                    IVsWindowFrame windowFrame,
                    IVsTextView windowPrimaryTextView,
                    string vanillaTabCaption
                )
            {
                this.distanceToCursor = distanceToCursor;
                this.adornmentBounds = adornmentBounds;
                this.span = span;
                this.text  = text;
                this.windowFrame = windowFrame;
                this.windowPrimaryTextView = windowPrimaryTextView;
                this.vanillaTabCaption = vanillaTabCaption;
            }

            public int distanceToCursor {get; }
            public Rect adornmentBounds {get; }
            public SnapshotSpan span {get; }
            public string text {get; }
            public IVsWindowFrame windowFrame {get; }
            public IVsTextView windowPrimaryTextView { get; }
            public string vanillaTabCaption { get; }
        };

        private List<Jump> currentJumps = new List<Jump>();
        public bool anyJumpsAvailable() => currentJumps.Count > 0;

        const string jumpLabelKeyArray = "asdghklqwertyuiopzxcvbnmfj;";

        private JumpMode jumpMode = JumpMode.InvalidMode;
        public JumpMode CurrentJumpMode { get { return jumpMode; } }

        public PeasyMotionEdAdornment() { // just for listener
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PeasyMotionEdAdornment"/> class.
        /// </summary>
        /// <param name="view">Text view to create the adornment for</param>
        public PeasyMotionEdAdornment(IVsTextView vsTextView, IWpfTextView view, ITextStructureNavigator textStructNav, JumpMode jumpMode_)
        {
            jumpMode = jumpMode_;

            var jumpLabelAssignmentAlgorithm = GeneralOptions.Instance.jumpLabelAssignmentAlgorithm;
            var caretPositionSensivity = Math.Min(Int32.MaxValue >> 2, Math.Abs(GeneralOptions.Instance.caretPositionSensivity));

            this.layer = view.GetAdornmentLayer("PeasyMotionEdAdornment");

            var watch1 = System.Diagnostics.Stopwatch.StartNew();

            this.textStructureNavigator = textStructNav;

            this.vsTextView = vsTextView;
            this.view = view;
            this.view.LayoutChanged += this.OnLayoutChanged;

            this.vsSettings = VsSettings.GetOrCreate(view);
            // subscribe to fmt updates, so user can tune color faster if PeasyMotion was invoked
            this.vsSettings.PropertyChanged += this.OnFormattingPropertyChanged;

            this.jumpLabelCachedSetupParams.fontRenderingEmSize = this.view.FormattedLineSource.DefaultTextProperties.FontRenderingEmSize;
            this.jumpLabelCachedSetupParams.typeface = this.view.FormattedLineSource.DefaultTextProperties.Typeface;
            this.jumpLabelCachedSetupParams.labelFg = this.vsSettings.JumpLabelFirstMotionForegroundColor;
            this.jumpLabelCachedSetupParams.labelBg = this.vsSettings.JumpLabelFirstMotionBackgroundColor;
            this.jumpLabelCachedSetupParams.labelFinalMotionFg = this.vsSettings.JumpLabelFinalMotionForegroundColor;
            this.jumpLabelCachedSetupParams.labelFinalMotionBg = this.vsSettings.JumpLabelFinalMotionBackgroundColor;
            this.jumpLabelCachedSetupParams.Freeze();

            var jumpWords = new List<JumpWord>();

            int currentTextPos = this.view.TextViewLines.FirstVisibleLine.Start;
            int lastTextPos = this.view.TextViewLines.LastVisibleLine.End;
            int currentLineStartTextPos = currentTextPos; // used for line word b/e jump mode
            int currentLineEndTextPos = currentTextPos; // used for line word b/e jump mode

            var cursorSnapshotPt = this.view.Caret.Position.BufferPosition;
            int cursorIndex = 0;
            bool lineJumpToWordBeginOrEnd_isActive = (jumpMode == JumpMode.LineJumpToWordBegining) || (jumpMode == JumpMode.LineJumpToWordEnding);
            if ((JumpLabelAssignmentAlgorithm.CaretRelative == jumpLabelAssignmentAlgorithm) || lineJumpToWordBeginOrEnd_isActive)
            {
                cursorIndex = cursorSnapshotPt.Position;
                if ((cursorIndex < currentTextPos) || (cursorIndex > lastTextPos))
                {
                    cursorSnapshotPt = this.view.TextSnapshot.GetLineFromPosition(currentTextPos + (lastTextPos - currentTextPos) / 2).Start;
                    cursorIndex = cursorSnapshotPt.Position;
                }
                // override text range for line jump mode:
                if (lineJumpToWordBeginOrEnd_isActive) {
                    var currentLine = this.view.TextSnapshot.GetLineFromPosition(cursorIndex);
                    currentLineStartTextPos = currentTextPos = currentLine.Start;
                    currentLineEndTextPos = lastTextPos = currentLine.End;
                }

                // bin caret to virtual segments accroding to sensivity option, with sensivity=0 does nothing
                int dc = caretPositionSensivity + 1;
                cursorIndex = (cursorIndex / dc) * dc + (dc / 2);
            }

            // collect words and required properties in visible text
            char prevChar = '\0';
            var startPoint = this.view.TextViewLines.FirstVisibleLine.Start;
            var endPoint = this.view.TextViewLines.LastVisibleLine.End;
            if (lineJumpToWordBeginOrEnd_isActive) {
                startPoint = new SnapshotPoint(startPoint.Snapshot, currentLineStartTextPos);
                endPoint = new SnapshotPoint(endPoint.Snapshot, currentLineEndTextPos);
            }

            var snapshot = startPoint.Snapshot;
            int lastJumpPos = -100;
            bool prevIsSeparator = Char.IsSeparator(prevChar);
            bool prevIsPunctuation = Char.IsPunctuation(prevChar);
            bool prevIsLetterOrDigit = Char.IsLetterOrDigit(prevChar);
            bool prevIsControl = Char.IsControl(prevChar);
            SnapshotPoint currentPoint = new SnapshotPoint(snapshot, startPoint.Position);
            SnapshotPoint nextPoint = currentPoint; 
            int i = startPoint.Position;
            int lastPosition = Math.Max(endPoint.Position-1, 0);
            if (startPoint.Position == lastPosition) {
                i = lastPosition + 2; // just skip the loop. noob way :D 
            }
            if (jumpMode == JumpMode.VisibleDocuments) {
                i = lastPosition + 2; //TODO: DECIDE LATER IF WE GONA SPLIT  jumpWord list fill into 
                // two functions - with adornmnents from textview and with smth else (doc tabs)
                // for now, just skip loop after cheking jumpMode is DocTabs
            }
            for (; i <= lastPosition; i++)
            {
                var ch = currentPoint.GetChar();
                nextPoint = new SnapshotPoint(snapshot, Math.Min(i+1, lastPosition));
                var nextCh = nextPoint.GetChar();
                bool curIsSeparator = Char.IsSeparator(ch);
                bool curIsPunctuation = Char.IsPunctuation(ch);
                bool curIsLetterOrDigit = Char.IsLetterOrDigit(ch);
                bool curIsControl = Char.IsControl(ch);
                bool nextIsControl = Char.IsControl(nextCh);

                bool candidateLabel = false;
                //TODO: anything faster and simpler ? will regex be faster? maybe symbols 
                // LUT with BITS (IsSep,IsPunct, etc as bits in INT record of LUT?)
                switch (jumpMode) {
                case JumpMode.LineJumpToWordBegining:
                    candidateLabel = curIsLetterOrDigit && !prevIsLetterOrDigit;
                    break;
                case JumpMode.LineJumpToWordEnding:
                    {
                        bool nextIsLetterOrDigit = Char.IsLetterOrDigit(nextCh);
                        candidateLabel = curIsLetterOrDigit && !nextIsLetterOrDigit;
                    }
                    break;
                default:
                    candidateLabel = (curIsLetterOrDigit && !prevIsLetterOrDigit) ||
                                     (!curIsControl && nextIsControl) ||
                                     (!prevIsLetterOrDigit && curIsControl && nextIsControl);
                    break;
                }
                candidateLabel = candidateLabel && (prevIsControl||((lastJumpPos + 2) < i));// make sure there is a lil bit of space between adornments

                if (candidateLabel)
                {
                    SnapshotSpan firstCharSpan = new SnapshotSpan(this.view.TextSnapshot, Span.FromBounds(i, i + 1));
                    Geometry geometry = this.view.TextViewLines.GetTextMarkerGeometry(firstCharSpan);
                    if (geometry != null)
                    {
                        var jw = new JumpWord(
                            distanceToCursor : Math.Abs(i - cursorIndex),
                            adornmentBounds : geometry.Bounds,
                            span : firstCharSpan,
                            text : null,
                            windowFrame : null,
                            windowPrimaryTextView : null,
                            vanillaTabCaption : null);
                        jumpWords.Add(jw);
                        lastJumpPos = i;
                    }
                }
                prevChar = ch;
                prevIsSeparator = curIsSeparator;
                prevIsPunctuation = curIsPunctuation;
                prevIsLetterOrDigit = curIsLetterOrDigit;
                prevIsControl = curIsControl;

                currentPoint = nextPoint;
            }
#if MEASUREEXECTIME
            watch1.Stop();
            Trace.WriteLine($"PeasyMotion Adornment find words: {watch1.ElapsedMilliseconds} ms");
            var watch2 = System.Diagnostics.Stopwatch.StartNew();
#endif

            if (jumpMode == JumpMode.VisibleDocuments) {
                //TODO: extract FN
#if MEASUREEXECTIME
                var watch2_0 = System.Diagnostics.Stopwatch.StartNew();
#endif
                //var vsUIShell4 = Package.GetGlobalService(typeof(SVsUIShell)) as IVsUIShell4;
                //var wfs = vsUIShell4.GetDocumentWindowFrames(__WindowFrameTypeFlags.WINDOWFRAMETYPE_Document).GetValueOrDefault();
                var vsUIShell = Package.GetGlobalService(typeof(SVsUIShell)) as IVsUIShell;
                var wfs = vsUIShell.GetDocumentWindowFrames().GetValueOrDefault();
                //TODO: separate property for Tab Label assignment Algo selection???
                var currentWindowFrame = this.vsTextView.GetWindowFrame().GetValueOrDefault(null);
                        
                Rect emptyRect = new Rect();
                SnapshotSpan emptySpan = new SnapshotSpan();
                Trace.WriteLine($"GetDocumentWindowFrames returned {wfs.Count} window frames");
                int wfi = 0; // there is no easy way to determine document tab coordinates T_T
                foreach(var wf in wfs) {
                    //if (VSConstants.S_OK == wf.GetProperty((int)VsFramePropID.OverrideCaption, out var c)) 
                    //wf.GetProperty((int)VsFramePropID.OverrideCaption, out var oovcap); 
                    //wf.GetProperty((int)VsFramePropID.EditorCaption, out var oecap);
                    //wf.GetProperty((int)VsFramePropID.OwnerCaption, out var oocap);
                    wf.GetProperty((int)VsFramePropID.Caption, out var oce);
                    string ce = (string)oce;
                    //string ecap = (string)oecap;
                    //string ocap = (string)oocap;
                    //string ovcap = (string)oovcap;
                    //var _ = wf.IsOnScreen(out int isonscreen_int);  // unreliable
                    //var inOnScreen = isonscreen_int != 0; // unreliable
                    //var isVisible = VSConstants.S_OK == wf.IsVisible(); // unreliable
                    //int x = -9, y = -9, px = -9, py = -9;
                    //wf.GetFramePos(null, out Guid _, out x, out y, out px, out py);
                    //if (wf is IVsWindowFrame4 wf4) { wf4.GetWindowScreenRect(out x, out y, out px, out py); }
                    //Trace.WriteLine($"WindowFrame[{wfi++}] Current={currentWindowFrame==wf} Caption={ce} OwnerCaption={ocap} "+
                    //    $"EditorCaption={ecap} OverrideCaption={ovcap} "+
                    //    $"IsOnScreen={inOnScreen} IsVisible={isVisible} {x} {y} {px} {py}");
                    if (currentWindowFrame != wf)
                    {
                        //if (c == null) 
                        {
                            //if (VSConstants.S_OK == wf.GetProperty((int)VsFramePropID.Caption, out var ce)) 
                            {
                                var cw = wf.GetCodeWindow().GetValueOrDefault(null);
                                IVsTextView wptv = wf.GetCodeWindow().GetValueOrDefault(null) ? .GetPrimaryView().GetValueOrDefault(null);
                                        
                                var distToCurrentDocument = 
                                    Math.Abs(wfi); //TODO: HOW TO SETUP AN INDEX?!?!?!?!?
                                var jw = new JumpWord(
                                    distanceToCursor : distToCurrentDocument,
                                    adornmentBounds : emptyRect,
                                    span : emptySpan,
                                    text : null,
                                    windowFrame : wf,
                                    windowPrimaryTextView : wptv,
                                    vanillaTabCaption : ce
                                );
                                jumpWords.Add(jw);
                            }
                        } 
                    }
                    //var cw = wf.GetCodeWindow(); //cwi = null;
                    //if (cw.TryGetValue(out IVsCodeWindow cwi)) { //cwi?.SetBaseEditorCaption(new string[]{ (P++).ToString() }); }
                }
#if MEASUREEXECTIME
            //TODO: MEASURE DOC TAB LABEL ASSIGNMENT SPEED
            watch2_0.Stop();
            Trace.WriteLine($"PeasyMotion Adornment find visible document tabs: {watch2_0.ElapsedMilliseconds} ms");
#endif
            }


            if (JumpLabelAssignmentAlgorithm.CaretRelative == jumpLabelAssignmentAlgorithm)
            {
                // sort jump words from closest to cursor to farthest
                jumpWords.Sort((a, b) => -a.distanceToCursor.CompareTo(b.distanceToCursor));
            }
#if MEASUREEXECTIME
            watch2.Stop();
            Trace.WriteLine($"PeasyMotion Adornment sort words: {watch2.ElapsedMilliseconds} ms");
            var watch3 = System.Diagnostics.Stopwatch.StartNew();
#endif

            _ = computeGroups(0, jumpWords.Count - 1, jumpLabelKeyArray, null, jumpWords);

#if MEASUREEXECTIME
            watch3.Stop();
            Trace.WriteLine($"PeasyMotion Adornments create: {adornmentCreateStopwatch?.ElapsedMilliseconds} ms");
            adornmentCreateStopwatch = null;
            Trace.WriteLine($"PeasyMotion Adornments UI Elem create: {createAdornmentUIElem?.ElapsedMilliseconds} ms");
            createAdornmentUIElem = null;
            Trace.WriteLine($"PeasyMotion Adornments group&create: {watch3?.ElapsedMilliseconds} ms");
            Trace.WriteLine($"PeasyMotion Adornment total jump labels - {jumpWords?.Count}");
#endif

            if (jumpMode == JumpMode.VisibleDocuments) {
#if MEASUREEXECTIME
                var setCaptionTiming = System.Diagnostics.Stopwatch.StartNew();
#endif
                foreach(var jump in currentJumps) {
                    jump.windowFrame.SetDocumentWindowFrameCaptionWithLabel(
                        jump.windowPrimaryTextView, jump.vanillaTabCaption, jump.label);
                }
#if MEASUREEXECTIME
                setCaptionTiming.Stop();
                Trace.WriteLine($"PeasyMotion document tabs set caption: {setCaptionTiming?.ElapsedMilliseconds} ms");
#endif
            }
        }

        ~PeasyMotionEdAdornment()
        {
            if (view != null) {
                this.vsSettings.PropertyChanged -= this.OnFormattingPropertyChanged;
            }
        }
        
        public static string getDocumentTabCaptionWithLabel(string originalCaption, string jumpLabel) {
            //TODO: MAYBE PROVIDE AN OPTION TO CONFIGURE LABEL TEXT DECORATION???
            //return $"[{jumpLabel}]{originalCaption}";
            //return $"[{jumpLabel.ToUpper()}]{originalCaption.Substring(jumpLabel.Length+2)}"; // 2 <<= for [ and] chars
            //return $"{jumpLabel.ToUpper()} |{originalCaption.Substring(jumpLabel.Length+2)}"; // 3 <<= for ' | ' chars
            return $"{jumpLabel} |{originalCaption.Substring(jumpLabel.Length+2)}"; // 2 <<= for '| ' chars
        }

        public void Dispose()
        {
            this.vsSettings.PropertyChanged -= this.OnFormattingPropertyChanged;
        }

        public void OnFormattingPropertyChanged(object o, System.ComponentModel.PropertyChangedEventArgs prop)
        {
            var val = vsSettings[prop.PropertyName];
            switch (prop.PropertyName)
            {
            case nameof(VsSettings.JumpLabelFirstMotionForegroundColor):
                {
                    var brush = val as SolidColorBrush;
                    foreach(var j in currentJumps) { 
                        if ((j.labelAdornment != null) && ((j.labelAdornment.Content as string).Length > 1)) {
                            j.labelAdornment.Foreground = brush;
                        }
                    }
                }
                break;
            case nameof(VsSettings.JumpLabelFirstMotionBackgroundColor):
                {
                    var brush = val as SolidColorBrush;
                    foreach(var j in currentJumps) {
                        if ((j.labelAdornment != null) && ((j.labelAdornment.Content as string).Length > 1)) {
                            j.labelAdornment.Background = brush;
                        }
                    }
                }
                break;
            case nameof(VsSettings.JumpLabelFinalMotionForegroundColor):
                {
                    var brush = val as SolidColorBrush;
                    foreach(var j in currentJumps) {
                        if ((j.labelAdornment != null) && ((j.labelAdornment.Content as string).Length == 1)) {
                            j.labelAdornment.Foreground = brush;
                        }
                    }
                }
                break;
            case nameof(VsSettings.JumpLabelFinalMotionBackgroundColor):
                {
                    var brush = val as SolidColorBrush;
                    foreach(var j in currentJumps) {
                        if ((j.labelAdornment != null) && ((j.labelAdornment.Content as string).Length == 1)) {
                            j.labelAdornment.Background = brush;
                        }
                    }
                }
                break;
            }
        }

        public struct JumpNode
        {
            public int jumpWordIndex;
            public Dictionary<char, JumpNode> childrenNodes;
        };

#if MEASUREEXECTIME
        private Stopwatch adornmentCreateStopwatch = null;
        private Stopwatch createAdornmentUIElem = null;
#endif

        private Dictionary<char, JumpNode> computeGroups(int wordStartIndex, int wordEndIndex, string keys0, string prefix, List<JumpWord> jumpWords)
        { 
            // SC-Tree algorithm from vim-easymotion script with minor changes
            var wordCount = wordEndIndex - wordStartIndex + 1;
            var keyCount = keys0.Length;

            Dictionary<char, JumpNode> groups = new Dictionary<char, JumpNode>();

            var keys = Reverse(keys0);

            var keyCounts = new int[keyCount];
            var keyCountsKeys = new Dictionary<char, int>(keyCount);
            var j = 0;
            foreach(char key in keys)
            {
                keyCounts[j] = 0;
                keyCountsKeys[key] = j;
                j++;
            }

            var targetsLeft = wordCount;
            var level = 0;
            var i = 0;

            while (targetsLeft > 0)
            {
                var childrenCount = level == 0 ? 1 : keyCount - 1;
                foreach(char key in keys)
                {
                    keyCounts[keyCountsKeys[key]] += childrenCount;
                    targetsLeft -= childrenCount;
                    if (targetsLeft <= 0) {
                        keyCounts[keyCountsKeys[key]] += targetsLeft;
                        break;
                    }
                    i += 1;

                }
                level += 1;
            }

            var k = 0;
            var keyIndex = 0;
            foreach (int KeyCount2 in keyCounts)
            {
                if (KeyCount2 > 1)
                {
                    groups[keys0[keyIndex]] = new JumpNode()
                    {
                        jumpWordIndex = -1,
                        childrenNodes = computeGroups(wordStartIndex + k, wordStartIndex + k + KeyCount2 - 1 - 1, keys0, 
                            prefix!=null ? (prefix + keys0[keyIndex]) : ""+keys0[keyIndex], jumpWords )
                    };
                }
                else if (KeyCount2 == 1)
                {
                    groups[keys0[keyIndex]] = new JumpNode() {
                        jumpWordIndex = wordStartIndex + k,
                        childrenNodes = null
                    };
                    var jw = jumpWords[wordStartIndex + k];
                    string jumpLabel = prefix + keys0[keyIndex];

                    JumpLabelUserControl adornment = null;
                    if (jw.windowFrame == null)  // winframe=null => regular textview navigation.
                    {
#if MEASUREEXECTIME
                        if (createAdornmentUIElem == null) {
                            createAdornmentUIElem = Stopwatch.StartNew();
                        } else {
                            createAdornmentUIElem.Start();
                        }
#endif
                        adornment = JumpLabelUserControl.GetFreeUserControl();
                        adornment.setup(jumpLabel, jw.adornmentBounds, this.jumpLabelCachedSetupParams);

#if MEASUREEXECTIME
                        createAdornmentUIElem.Stop();
#endif

#if MEASUREEXECTIME
                        if (adornmentCreateStopwatch == null) {
                            adornmentCreateStopwatch = Stopwatch.StartNew();
                        } else {
                            adornmentCreateStopwatch.Start();
                        }
#endif
                        this.layer.AddAdornment(AdornmentPositioningBehavior.TextRelative, jw.span, null, adornment, JumpLabelAdornmentRemovedCallback);
#if MEASUREEXECTIME
                        adornmentCreateStopwatch.Stop();
#endif
                    }

                    //Debug.WriteLine(jw.text + " -> |" + jumpLabel + "|");
                    var cj = new Jump(
                        span : jw.span, 
                        label : jumpLabel, 
                        labelAdornment : adornment, 
                        windowFrame : jw.windowFrame,
                        vanillaTabCaption : jw.vanillaTabCaption,
                        windowPrimaryTextView : jw.windowPrimaryTextView
                    );
                    currentJumps.Add(cj);
                }
                else
                {
                    continue;
                }
                keyIndex += 1;
                k += KeyCount2;
            }

            return groups;
        }

        public void JumpLabelAdornmentRemovedCallback(object _, UIElement element)
        {
            JumpLabelUserControl.ReleaseUserControl(element as JumpLabelUserControl);
        }

        public static string Reverse( string s )
        {
            char[] charArray = s.ToCharArray();
            Array.Reverse( charArray );
            return new string( charArray );
        }
        /*
        internal SnapshotSpan? GetNextWord(SnapshotPoint position)
        {
            var word = this.textStructureNavigator.GetExtentOfWord(position);
            while (!word.IsSignificant && !word.Span.IsEmpty)
            {
                SnapshotSpan previousWordSpan = word.Span;
                word = this.textStructureNavigator.GetExtentOfWord(word.Span.End);
                if (word.Span == previousWordSpan)
                {
                    return null;
                }
            }

            return word.IsSignificant ? new SnapshotSpan?(word.Span) : null;
        }*/

        /// <summary>
        /// Handles whenever the text displayed in the view changes by adding the adornment to any reformatted lines
        /// </summary>
        /// <remarks><para>This event is raised whenever the rendered text displayed in the <see cref="ITextView"/> changes.</para>
        /// <para>It is raised whenever the view does a layout (which happens when DisplayTextLineContainingBufferPosition is called or in response to text or classification changes).</para>
        /// <para>It is also raised whenever the view scrolls horizontally or when its size changes.</para>
        /// </remarks>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        internal void OnLayoutChanged(object sender, TextViewLayoutChangedEventArgs e)
        {
        }

        internal void Reset()
        {
            this.layer.RemoveAllAdornments();
            foreach (var j in this.currentJumps) {
                j.windowFrame?.RemoveJumpLabelFromDocumentWindowFrameCaption(j.windowPrimaryTextView, j.vanillaTabCaption);
            }
            this.currentJumps.Clear();
        }

        internal bool NoLabelsLeft() => (this.currentJumps.Count == 0);

        // returns true if jump succeded
        internal bool JumpTo(string label, out JumpToResult jumpToResult) // returns null if jump motion is not finished yet
        {
            int idx = currentJumps.FindIndex(0, j => j.label == label);
            if (-1 < idx)
            {
                jumpToResult = new JumpToResult(
                    currentCursorPosition : this.view.Caret.Position.BufferPosition.Position, 
                    jumpLabelSpan : currentJumps[idx].span,
                    windowFrame : currentJumps[idx].windowFrame
                );
                return true;
            } 
            else
            {
                if (jumpMode != JumpMode.VisibleDocuments) { // keep all labeled document tabs
                    currentJumps.RemoveAll(
                        delegate (Jump j)
                        {
                            bool b = !j.label.StartsWith(label, StringComparison.InvariantCulture);
                            if (b) {
                                if (null != j.labelAdornment) {
                                    this.layer.RemoveAdornment(j.labelAdornment);
                                }
                                //j.windowFrame?.RemoveJumpLabelFromDocumentWindowFrameCaption();
                            }
                    
                            return b;
                        }
                    );
                }

#if MEASUREEXECTIME
                var timing1 = System.Diagnostics.Stopwatch.StartNew();
#endif
                foreach(Jump j in currentJumps)
                {
                    var labelRemainingMotionSubstr = j.label.Substring(label.Length);
                    j.labelAdornment?.UpdateView(labelRemainingMotionSubstr, this.jumpLabelCachedSetupParams);
                    //TODO: Stabilize tab caption, keeping it same width as before!!!!!! 
                    //      Possible with fixed width fonts?
                    if (labelRemainingMotionSubstr.Length > 0) {
                        // replace parts of document caption with label & decor, trying to 
                        //preserve same tab caption width!
                        j.windowFrame?.SetDocumentWindowFrameCaptionWithLabel(
                            j.windowPrimaryTextView, 
                            j.vanillaTabCaption,
                            labelRemainingMotionSubstr + new string('_', j.label.Length-label.Length));
                    }
                }
#if MEASUREEXECTIME
                timing1.Stop();
                Trace.WriteLine($"PeasyMotion document tabs update caption: {timing1?.ElapsedMilliseconds} ms");
#endif
            }
            jumpToResult = new JumpToResult();
            return false;
        }
    }
}
