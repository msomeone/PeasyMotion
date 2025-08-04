using System;
using System.Collections.Generic;
using System.Linq;
using PeasyMotion;

public class JumpLabelAlgorithm
{
    private readonly string   allowedKeys;
    private readonly JumpMode jumpMode;
    private readonly int      caretPositionSensivity;
    private readonly string   nCharSearchJumpKeys;
    private const int MinimumDistanceBetweenLabels = 3;
    public JumpLabelAlgorithm(string allowedKeys, JumpMode jumpMode, int caretPositionSensivity,
                              string nCharSearchJumpKeys = null)
    {
        this.allowedKeys            = allowedKeys;
        this.jumpMode               = jumpMode;
        this.caretPositionSensivity = caretPositionSensivity;
        this.nCharSearchJumpKeys    = nCharSearchJumpKeys;
    }

    public List<JumpTarget> CollectJumpTargets(string text, int cursorPosition, int startPosition, int endPosition)
    {
        var jumpTargets = new List<JumpTarget>();
        var lastJumpPos = -100;
        var prevIsLetterOrDigit = false;

        for (var i = startPosition; i <= endPosition && i < text.Length; i++)
        {
            var ch                 = text[i];
            var nextCh             = i < text.Length - 1 ? text[i + 1] : '\0';
            var curIsLetterOrDigit = char.IsLetterOrDigit(ch);
            var candidateLabel     = false;

            switch (jumpMode)
            {
                case JumpMode.LineJumpToWordBegining:
                    candidateLabel = curIsLetterOrDigit && !prevIsLetterOrDigit;
                    break;
                case JumpMode.LineJumpToWordEnding:
                    candidateLabel = curIsLetterOrDigit && !char.IsLetterOrDigit(nextCh);
                    break;
                case JumpMode.LineBeginingJump:
                    candidateLabel = ch == '\n' || ch == '\r' || i == startPosition;
                    break;
                case JumpMode.TwoCharJump:
                    candidateLabel = i < text.Length - 1 && 
                        char.ToLowerInvariant(ch) == nCharSearchJumpKeys[0] &&
                        char.ToLowerInvariant(nextCh) == nCharSearchJumpKeys[1];
                    break;
                case JumpMode.OneCharJump:
                    candidateLabel = char.ToLowerInvariant(ch) == nCharSearchJumpKeys[0];
                    break;
                default:
                    candidateLabel = curIsLetterOrDigit && !prevIsLetterOrDigit;
                    break;
            }

            if (candidateLabel && (lastJumpPos + MinimumDistanceBetweenLabels) < i && i < endPosition)
            {
                var adjustedCursor = (cursorPosition / (caretPositionSensivity + 1)) * (caretPositionSensivity + 1) +
                                     (caretPositionSensivity / 2);
                jumpTargets.Add(new JumpTarget(
                    position: i,
                    text: ch.ToString(),
                    distanceToCursor: Math.Abs(i - adjustedCursor),
                    metadata: null
                ));
                lastJumpPos = i;
            }

            prevIsLetterOrDigit = curIsLetterOrDigit;
        }

        return jumpTargets;
    }

    public List<JumpLabel> AssignLabels(List<JumpTarget> targets)
    {
        var sortedTargets = targets.OrderBy(t => t.DistanceToCursor).ToList();
        var labels        = new List<JumpLabel>();
        ComputeGroups(0, sortedTargets.Count - 1, allowedKeys, "", sortedTargets, labels);
        return labels;
    }

    private void ComputeGroups(int startIndex, int endIndex, string keys, string prefix, List<JumpTarget> targets,
                               List<JumpLabel> labels)
    {
        var wordCount = endIndex - startIndex + 1;
        if (wordCount <= 0) return;

        var keyCounts     = new int[keys.Length];
        var keyCountsKeys = new Dictionary<char, int>();
        for (var j = 0; j < keys.Length; j++)
        {
            keyCounts[j]           = 0;
            keyCountsKeys[keys[j]] = j;
        }

        var targetsLeft = wordCount;
        var level       = 0;
        while (targetsLeft > 0)
        {
            var childrenCount = level == 0 ? 1 : keys.Length - 1;
            foreach (var key in keys)
            {
                keyCounts[keyCountsKeys[key]] += childrenCount;
                targetsLeft                   -= childrenCount;
                if (targetsLeft <= 0)
                {
                    keyCounts[keyCountsKeys[key]] += targetsLeft;
                    break;
                }
            }

            level++;
        }

        var k = 0;
        Array.Reverse(keyCounts);
        for (var i = 0; i < keys.Length; i++)
        {
            var keyCount = keyCounts[i];
            if (keyCount > 1)
            {
                ComputeGroups(startIndex + k, startIndex + k + keyCount - 1, Reverse(keys), prefix + keys[i], targets,
                              labels);
            }
            else if (keyCount == 1)
            {
                labels.Add(new JumpLabel(targets[startIndex + k], prefix + keys[i]));
            }

            k += keyCount;
        }
    }

    public List<JumpLabel> FilterLabels(List<JumpLabel> labels, string input) => labels.Where(l => l.Label.StartsWith(input, StringComparison.InvariantCulture)).ToList();

    private static string Reverse(string s)
    {
        var charArray = s.ToCharArray();
        Array.Reverse(charArray);
        return new string(charArray);
    }
}