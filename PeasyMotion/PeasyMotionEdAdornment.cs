//#define MEASUREEXECTIME

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

namespace PeasyMotion
{ 
    /// <summary>
    /// PeasyMotionEdAdornment places red boxes behind all the "a"s in the editor window
    /// </summary>
    internal sealed class PeasyMotionEdAdornment
    {
        private ITextStructureNavigator textStructureNavigator { get; set; }
        /// <summary>
        /// The layer of the adornment.
        /// </summary>
        private readonly IAdornmentLayer layer;

        /// <summary>
        /// Text view where the adornment is created.
        /// </summary>
        private readonly IWpfTextView view;
        private double emSize;

        private struct Jump
        {
            public SnapshotSpan span;
            public string label;
            public JumpLabelUserControl labelAdornment;
        };
        private struct JumpWord
        {
            public int distanceToCursor;
            public Rect adornmentBounds;
            public SnapshotSpan span;
            public string text;
        };

        private List<Jump> currentJumps = new List<Jump>();

        const string jumpLabelKeyArray = "asdghklqwertyuiopzxcvbnmfj;";

        public PeasyMotionEdAdornment() { // just for listener
        }
        /// <summary>
        /// Initializes a new instance of the <see cref="PeasyMotionEdAdornment"/> class.
        /// </summary>
        /// <param name="view">Text view to create the adornment for</param>
        public PeasyMotionEdAdornment(IWpfTextView view, ITextStructureNavigator textStructNav)
        {
            if (view == null)
            {
                throw new ArgumentNullException(nameof(view));
            }

            this.layer = view.GetAdornmentLayer("PeasyMotionEdAdornment");

            var watch1 = System.Diagnostics.Stopwatch.StartNew();

            this.textStructureNavigator = textStructNav;


            this.view = view;
            this.view.LayoutChanged += this.OnLayoutChanged;

            this.emSize = this.view.FormattedLineSource.DefaultTextProperties.FontRenderingEmSize;

            var jumpWords = new List<JumpWord>();

            int currentTextPos = this.view.TextViewLines.FirstVisibleLine.Start;
            int lastTextPos = this.view.TextViewLines.LastVisibleLine.End;

            var cursorSnapshotPt = this.view.Caret.Position.BufferPosition;
            int cursorIndex = cursorSnapshotPt.Position;
            if ((cursorIndex < currentTextPos) || (cursorIndex > lastTextPos))
            {
                cursorSnapshotPt = this.view.TextSnapshot.GetLineFromPosition(currentTextPos + (lastTextPos - currentTextPos)/2).Start;
                cursorIndex = cursorSnapshotPt.Position;
            }

            // collect words and required properties in visible text
            char prevChar = '\0';
            var startPoint = this.view.TextViewLines.FirstVisibleLine.Start;
            var endPoint = this.view.TextViewLines.LastVisibleLine.End;
            var snapshot = startPoint.Snapshot;
            int lastJumpPos = -1;
            bool prevIsSeparator = Char.IsSeparator(prevChar);
            bool prevIsPunctuation = Char.IsPunctuation(prevChar);
            bool prevIsLetterOrDigit = Char.IsLetterOrDigit(prevChar);
            bool prevIsControl = Char.IsControl(prevChar);
            for (int i = startPoint.Position; i < endPoint.Position; i++)
            {
                var point = new SnapshotPoint(snapshot, i);
                var ch = point.GetChar();
                bool curIsSeparator = Char.IsSeparator(ch);
                bool curIsPunctuation = Char.IsPunctuation(ch);
                bool curIsLetterOrDigit = Char.IsLetterOrDigit(ch);
                bool curIsControl = Char.IsControl(ch);
                if (//TODO: anything faster and simpler ? will regex be faster?
                        (
                            (i==0) || // file start
                            ((prevIsControl || prevIsPunctuation || prevIsSeparator) && curIsLetterOrDigit) || // word begining?
                            ((prevIsLetterOrDigit || prevIsSeparator || prevIsControl || Char.IsWhiteSpace(prevChar)) && curIsPunctuation) // { } [] etc
                        ) 
                         && 
                        ((lastJumpPos+2) < i) // make sure there is a lil bit of space between adornments
                    )
                {
                    SnapshotSpan firstCharSpan = new SnapshotSpan(this.view.TextSnapshot, Span.FromBounds(i, i + 1));
                    Geometry geometry = this.view.TextViewLines.GetTextMarkerGeometry(firstCharSpan);
                    if (geometry != null)
                    {
                        var jw = new JumpWord()
                        {
                            distanceToCursor = Math.Abs(i - cursorIndex),
                            adornmentBounds = geometry.Bounds,
                            span = firstCharSpan,
                            text = null
                        };
                        jumpWords.Add(jw);
                        lastJumpPos = i;
                    }
                }
                prevChar = ch;
                prevIsSeparator = curIsSeparator;
                prevIsPunctuation = curIsPunctuation;
                prevIsLetterOrDigit = curIsLetterOrDigit;
                prevIsControl = curIsControl;
            }
#if false
            for (int i = 0; i < 256; i++) {
                Trace.WriteLine("Char.IsSeparator(" + ((char)i) + " = " + Char.IsLowSurrogate((char)i) );
                Trace.WriteLine("Char.IsControl(" + ((char)i) + " = " + Char.IsControl((char)i) );
                Trace.WriteLine("Char.IsDigit(" + ((char)i) + " = " + Char.IsDigit((char)i) );
                Trace.WriteLine("Char.IsHighSurrogate(" + ((char)i) + " = " + Char.IsHighSurrogate((char)i) );
                Trace.WriteLine("Char.IsLetterOrDigit(" + ((char)i) + " = " + Char.IsLetterOrDigit((char)i) );
                Trace.WriteLine("Char.IsLowSurrogate(" + ((char)i) + " = " + Char.IsLowSurrogate((char)i) );
                Trace.WriteLine("Char.IsNumber(" + ((char)i) + " = " + Char.IsNumber((char)i) );
                Trace.WriteLine("Char.IsPunctuation(" + ((char)i) + " = " + Char.IsPunctuation((char)i) );
                Trace.WriteLine("Char.IsSeparator(" + ((char)i) + " = " + Char.IsSeparator((char)i) );
                Trace.WriteLine("Char.IsSymbol(" + ((char)i) + " = " + Char.IsSymbol((char)i) );
                Trace.WriteLine("-----");
            }
#endif
            /* // too slow
            do
            {
                var word_span = GetNextWord(new SnapshotPoint(this.view.TextSnapshot, currentTextPos));
                if (word_span.HasValue && (!word_span.Value.Contains(cursorSnapshotPt)))
                {
                    var word = this.view.TextSnapshot.GetText(word_span.Value);
                    if (Char.IsLetter(word[0]) || Char.IsNumber(word[0]) )
                    {
                        //Debug.WriteLine(word);
                        int charIndex = word_span.Value.Start;
                        SnapshotSpan firstCharSpan = new SnapshotSpan(this.view.TextSnapshot, Span.FromBounds(charIndex, charIndex + 1));
                        Geometry geometry = this.view.TextViewLines.GetTextMarkerGeometry(firstCharSpan);
                        if (geometry != null)
                        {
                            var jw = new JumpWord()
                            {
                                distanceToCursor = Math.Abs(charIndex - cursorIndex),
                                adornmentBounds = geometry.Bounds,
                                span = firstCharSpan,
                                text = word
                            };
                            jumpWords.Add(jw);
                        }
                    }
                    currentTextPos = word_span.Value.End;
                }
                else
                {
                    currentTextPos++;
                }
            } while (currentTextPos < lastTextPos);
            */

#if MEASUREEXECTIME
            watch1.Stop();
            Trace.WriteLine($"PeasyMotion Adornment find words: {watch1.ElapsedMilliseconds} ms");
            var watch2 = System.Diagnostics.Stopwatch.StartNew();
#endif
            // sort jump words from closest to cursor to farthest
            jumpWords.Sort((a, b) => -a.distanceToCursor.CompareTo(b.distanceToCursor));
#if MEASUREEXECTIME
            watch2.Stop();
            Trace.WriteLine($"PeasyMotion Adornment sort words: {watch2.ElapsedMilliseconds} ms");
            var watch3 = System.Diagnostics.Stopwatch.StartNew();
#endif

            _ = computeGroups(0, jumpWords.Count - 1, jumpLabelKeyArray, null, jumpWords);

#if MEASUREEXECTIME
            watch3.Stop();
            Trace.WriteLine($"PeasyMotion Adornments group&create: {watch3.ElapsedMilliseconds} ms");
#endif
        }

        public struct JumpNode
        {
            public int jumpWordIndex;
            public Dictionary<char, JumpNode> childrenNodes;
        };

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
                    if (targetsLeft <= 0)
                    {
                        keyCounts[keyCountsKeys[key]] += targetsLeft;
                        break;
                    }
                    i += 1;

                }
                level += 1;
            }

            double emSize = this.emSize;

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
                    groups[keys0[keyIndex]] = new JumpNode()
                    {
                        jumpWordIndex = wordStartIndex + k,
                        childrenNodes = null
                    };
                    var jw = jumpWords[wordStartIndex + k];
                    string jumpLabel = prefix + keys0[keyIndex];
                    var adornment = new JumpLabelUserControl(jumpLabel, jw.adornmentBounds, emSize);

                    Canvas.SetLeft(adornment, jw.adornmentBounds.Left);
                    Canvas.SetTop(adornment, jw.adornmentBounds.Top);

                    this.layer.AddAdornment(AdornmentPositioningBehavior.TextRelative, jw.span, null, adornment, null);

                    //Debug.WriteLine(jw.text + " -> |" + jumpLabel + "|");
                    var cj = new Jump() { span = jw.span, label = jumpLabel, labelAdornment = adornment };
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

        public static string Reverse( string s )
        {
            char[] charArray = s.ToCharArray();
            Array.Reverse( charArray );
            return new string( charArray );
        }
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
        }

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
            this.currentJumps.Clear();
        }

        internal bool JumpTo(string label)
        {
            int idx = currentJumps.FindIndex(0, j => j.label == label);
            if (-1 < idx)
            {
                var j = currentJumps[idx];
                this.view.Caret.MoveTo(j.span.Start);
                return true;
            } 
            else
            {
                currentJumps.RemoveAll(
                    delegate (Jump j)
                    {
                        bool b = !j.label.StartsWith(label, StringComparison.InvariantCulture);
                        if (b)
                        {
                            this.layer.RemoveAdornment(j.labelAdornment);
                        }
                        return b;
                    }
                );

                foreach(Jump j in currentJumps)
                {
                    j.labelAdornment.UpdateView(j.label.Substring(label.Length));
                }
            }
            return false;
        }
    }
}
